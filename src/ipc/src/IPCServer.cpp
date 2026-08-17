/// @file IPCServer.cpp
/// @brief Server-side IPC wrapper implementation

#include <openmedia/ipc/IPCServer.h>
#include <openmedia/core/Logger.h>

#include <atomic>
#include <mutex>
#include <unordered_map>
#include <openmedia/ipc/D3D11SharedTexturePoolPoC.h>

namespace openmedia::ipc {

struct IPCServer::Impl {
    IPCServerConfig config;
    std::atomic<bool> running{false};

    std::unique_ptr<NamedPipeServer> pipeServer;
    std::unique_ptr<SharedMemoryBuffer> sharedMem;

    std::unordered_map<CommandType, CommandHandlerFn> handlers;
    DefaultCommandHandlerFn defaultHandler;
    std::mutex handlersMutex;

    // PoC for D3D11 Shared Texture Pool
    std::unique_ptr<D3D11SharedTexturePoolPoC> texturePool;
    std::thread renderThread;
    std::atomic<bool> runningRenderThread{false};

    // Dynamic video resolution (set by ServerApp after OpenSource)
    uint32_t videoWidth = 1920;
    uint32_t videoHeight = 1080;
};

IPCServer::IPCServer(const IPCServerConfig& config)
    : m_impl(std::make_unique<Impl>()) {
    m_impl->config = config;
    m_impl->pipeServer = std::make_unique<NamedPipeServer>(config.pipeConfig);
    m_impl->sharedMem = std::make_unique<SharedMemoryBuffer>(config.sharedMemConfig, true);
}

IPCServer::~IPCServer() {
    Stop();
}

core::VoidResult IPCServer::Start() {
    if (m_impl->running.load()) {
        return std::unexpected(core::Error{
            core::ErrorCode::InvalidState, "Already running"});
    }

    // Initialize shared memory
    auto shmResult = m_impl->sharedMem->Initialize();
    if (!shmResult) {
        return std::unexpected(shmResult.error());
    }

    // Set message callback — dispatches to registered handlers
    m_impl->pipeServer->SetMessageCallback(
        [this](const MessageHeader& header, const std::vector<uint8_t>& payload) {
            // D3D11 Shared Texture Pool creation
            if (header.commandType == CommandType::ShareD3D11Texture) {
                // Re-create texture pool if resolution changed or first request
                if (!m_impl->texturePool) {
                    m_impl->texturePool = std::make_unique<D3D11SharedTexturePoolPoC>();
                    m_impl->texturePool->Initialize(m_impl->videoWidth, m_impl->videoHeight, DXGI_FORMAT_B8G8R8A8_UNORM);
                }

                struct ShareD3D11TexturePayload {
                    uint64_t ntHandles[2];
                    uint32_t width;
                    uint32_t height;
                    uint32_t bufferCount;
                };

                ShareD3D11TexturePayload respPayload{};
                HANDLE handles[2];
                m_impl->texturePool->GetSharedHandles(handles);
                respPayload.ntHandles[0] = reinterpret_cast<uint64_t>(handles[0]);
                respPayload.ntHandles[1] = reinterpret_cast<uint64_t>(handles[1]);
                respPayload.width = m_impl->videoWidth;
                respPayload.height = m_impl->videoHeight;
                respPayload.bufferCount = 2;

                std::vector<uint8_t> responsePayload(sizeof(ShareD3D11TexturePayload));
                std::memcpy(responsePayload.data(), &respPayload, sizeof(ShareD3D11TexturePayload));

                ResponseHeader response;
                response.sequenceNumber = header.sequenceNumber;
                response.status = ResponseStatus::Success;
                response.payloadSize = static_cast<uint32_t>(responsePayload.size());

                std::ignore = m_impl->pipeServer->SendResponse(header.clientId, response, responsePayload);
                return;
            }

            std::lock_guard lock(m_impl->handlersMutex);
            auto it = m_impl->handlers.find(header.commandType);
            if (it != m_impl->handlers.end()) {
                auto result = it->second(header.clientId, payload);

                // Send response
                ResponseHeader response;
                response.sequenceNumber = header.sequenceNumber;
                std::vector<uint8_t> responsePayload;

                if (result) {
                    response.status = ResponseStatus::Success;
                    responsePayload = std::move(*result);
                } else {
                    response.status = ResponseStatus::Error;
                    response.errorCode = static_cast<uint32_t>(result.error().code);
                }
                response.payloadSize = static_cast<uint32_t>(responsePayload.size());

                std::ignore = m_impl->pipeServer->SendResponse(
                    header.clientId, response, responsePayload);
            } else if (m_impl->defaultHandler) {
                auto result = m_impl->defaultHandler(header.clientId, header.commandType, payload);

                // Send response
                ResponseHeader response;
                response.sequenceNumber = header.sequenceNumber;
                std::vector<uint8_t> responsePayload;

                if (result) {
                    response.status = ResponseStatus::Success;
                    responsePayload = std::move(*result);
                } else {
                    response.status = ResponseStatus::Error;
                    response.errorCode = static_cast<uint32_t>(result.error().code);
                }
                response.payloadSize = static_cast<uint32_t>(responsePayload.size());

                std::ignore = m_impl->pipeServer->SendResponse(
                    header.clientId, response, responsePayload);
            } else {
                core::Logger::SWarn("IPCServer",
                    "No handler for command {} from client {}",
                    CommandTypeToString(header.commandType),
                    header.clientId);

                ResponseHeader response;
                response.sequenceNumber = header.sequenceNumber;
                response.status = ResponseStatus::InvalidCommand;
                std::ignore = m_impl->pipeServer->SendResponse(
                    header.clientId, response, {});
            }
        });

    // Start pipe server
    auto pipeResult = m_impl->pipeServer->StartListening();
    if (!pipeResult) {
        m_impl->sharedMem->Close();
        return std::unexpected(pipeResult.error());
    }

    m_impl->running.store(true);
    core::Logger::SInfo("IPCServer", "Started");
    return {};
}

void IPCServer::Stop() {
    if (!m_impl->running.load()) return;

    if (m_impl->runningRenderThread) {
        m_impl->runningRenderThread = false;
        if (m_impl->renderThread.joinable()) {
            m_impl->renderThread.join();
        }
    }

    m_impl->running.store(false);
    m_impl->pipeServer->StopListening();
    m_impl->sharedMem->Close();
    core::Logger::SInfo("IPCServer", "Stopped");
}

bool IPCServer::IsRunning() const {
    return m_impl->running.load();
}

void IPCServer::RegisterHandler(CommandType type, CommandHandlerFn handler) {
    std::lock_guard lock(m_impl->handlersMutex);
    m_impl->handlers[type] = std::move(handler);
}

void IPCServer::SetDefaultHandler(DefaultCommandHandlerFn handler) {
    std::lock_guard lock(m_impl->handlersMutex);
    m_impl->defaultHandler = std::move(handler);
}

void IPCServer::UnregisterHandler(CommandType type) {
    std::lock_guard lock(m_impl->handlersMutex);
    m_impl->handlers.erase(type);
}

SharedMemoryBuffer* IPCServer::GetSharedMemory() {
    return m_impl->sharedMem.get();
}

D3D11SharedTexturePoolPoC* IPCServer::GetSharedTexturePool() {
    if (!m_impl->texturePool) {
        m_impl->texturePool = std::make_unique<D3D11SharedTexturePoolPoC>();
        m_impl->texturePool->Initialize(m_impl->videoWidth, m_impl->videoHeight, DXGI_FORMAT_B8G8R8A8_UNORM);
    }
    return m_impl->texturePool.get();
}

void IPCServer::SetVideoResolution(uint32_t width, uint32_t height) {
    if (width == 0 || height == 0) return;
    m_impl->videoWidth = width;
    m_impl->videoHeight = height;

    // If texture pool already exists with different dimensions, recreate it
    if (m_impl->texturePool) {
        m_impl->texturePool.reset();
        m_impl->texturePool = std::make_unique<D3D11SharedTexturePoolPoC>();
        m_impl->texturePool->Initialize(width, height, DXGI_FORMAT_B8G8R8A8_UNORM);
    }
}

core::VoidResult IPCServer::NotifyFrameReady(
    uint32_t slotIndex,
    const FrameSlotHeader& metadata) {
    // Build notification message
    MessageHeader header;
    header.commandType = CommandType::FrameReady;

    struct FrameNotification {
        uint32_t slotIndex;
        uint64_t pts;
        uint32_t width;
        uint32_t height;
    };

    FrameNotification notification{slotIndex, metadata.pts,
                                    metadata.width, metadata.height};
    std::vector<uint8_t> payload(sizeof(FrameNotification));
    std::memcpy(payload.data(), &notification, sizeof(FrameNotification));

    return m_impl->pipeServer->Send(header, payload);
}

core::VoidResult IPCServer::BroadcastEvent(
    CommandType eventType,
    const std::vector<uint8_t>& data) {
    MessageHeader header;
    header.commandType = eventType;
    return m_impl->pipeServer->Send(header, data);
}

core::VoidResult IPCServer::SendEvent(
    uint32_t clientId,
    CommandType /*eventType*/,
    const std::vector<uint8_t>& data) {
    ResponseHeader response;
    response.status = ResponseStatus::Success;
    response.payloadSize = static_cast<uint32_t>(data.size());
    return m_impl->pipeServer->SendResponse(clientId, response, data);
}

uint32_t IPCServer::GetClientCount() const {
    return m_impl->pipeServer->GetClientCount();
}

} // namespace openmedia::ipc
