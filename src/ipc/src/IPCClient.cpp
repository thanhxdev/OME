/// @file IPCClient.cpp
/// @brief Client-side IPC wrapper implementation

#include <openmedia/ipc/IPCClient.h>
#include <openmedia/core/Logger.h>

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#include <tlhelp32.h>

#include <atomic>
#include <mutex>

namespace openmedia::ipc {

struct IPCClient::Impl {
    IPCClientConfig config;
    std::unique_ptr<NamedPipeClient> pipeClient;
    std::unique_ptr<SharedMemoryBuffer> sharedMem;

    ConnectionCallback connectionCallback;
    FrameReadyCallback frameReadyCallback;
    EventCallback eventCallback;
    std::mutex callbackMutex;

    std::atomic<uint32_t> nextSequence{1};
    HANDLE serverProcess = nullptr;
};

IPCClient::IPCClient(const IPCClientConfig& config)
    : m_impl(std::make_unique<Impl>()) {
    m_impl->config = config;
    m_impl->pipeClient = std::make_unique<NamedPipeClient>(config.pipeConfig);
    m_impl->sharedMem = std::make_unique<SharedMemoryBuffer>(
        config.sharedMemConfig, false);  // client opens, not creates
}

IPCClient::~IPCClient() {
    Disconnect();
}

core::VoidResult IPCClient::Connect() {
    // Auto-launch server if needed
    if (m_impl->config.autoLaunchServer && !IsServerRunning()) {
        auto launchResult = LaunchServer();
        if (!launchResult) {
            return std::unexpected(launchResult.error());
        }
        // Wait for server to start
        std::this_thread::sleep_for(std::chrono::milliseconds(1000));
    }

    // Setup auto-reconnect
    m_impl->pipeClient->SetAutoReconnect(true,
        m_impl->config.maxReconnectAttempts,
        m_impl->config.reconnectDelayMs);

    // Connect to server pipe
    auto connectResult = m_impl->pipeClient->Connect();
    if (!connectResult) {
        return std::unexpected(connectResult.error());
    }

    // Set message callback for events/notifications
    m_impl->pipeClient->SetMessageCallback(
        [this](const MessageHeader& header, const std::vector<uint8_t>& payload) {
            std::lock_guard lock(m_impl->callbackMutex);

            if (header.commandType == CommandType::FrameReady) {
                if (m_impl->frameReadyCallback && payload.size() >= sizeof(uint32_t) * 2) {
                    uint32_t slotIndex;
                    std::memcpy(&slotIndex, payload.data(), sizeof(uint32_t));
                    FrameSlotHeader metadata{};
                    m_impl->frameReadyCallback(slotIndex, metadata);
                }
            } else if (m_impl->eventCallback) {
                m_impl->eventCallback(header.commandType, payload);
            }
        });

    // Send handshake
    auto handshakeResult = SendCommand(CommandType::Handshake);
    if (!handshakeResult) {
        core::Logger::SWarn("IPCClient", "Handshake failed, but connection established");
    }

    core::Logger::SInfo("IPCClient", "Connected to server");
    return {};
}

void IPCClient::Disconnect() {
    UnmapSharedMemory();
    m_impl->pipeClient->Disconnect();

    if (m_impl->serverProcess) {
        CloseHandle(m_impl->serverProcess);
        m_impl->serverProcess = nullptr;
    }
}

bool IPCClient::IsConnected() const {
    return m_impl->pipeClient->IsConnected();
}

ConnectionState IPCClient::GetState() const {
    return m_impl->pipeClient->GetState();
}

core::Result<std::vector<uint8_t>> IPCClient::SendCommand(
    CommandType type,
    const std::vector<uint8_t>& payload,
    std::chrono::milliseconds timeout) {
    if (!IsConnected()) {
        return std::unexpected(core::Error{
            core::ErrorCode::InvalidState, "Not connected"});
    }

    MessageHeader header;
    header.commandType = type;
    header.sequenceNumber = m_impl->nextSequence.fetch_add(1);
    header.payloadSize = static_cast<uint32_t>(payload.size());

    auto now = std::chrono::steady_clock::now().time_since_epoch();
    header.timestamp = std::chrono::duration_cast<std::chrono::microseconds>(now).count();

    auto result = m_impl->pipeClient->SendAndReceive(header, payload, timeout);
    if (!result) {
        return std::unexpected(result.error());
    }

    const auto& buffer = result.value();
    if (buffer.size() < sizeof(ResponseHeader)) {
        return std::unexpected(core::Error{
            core::ErrorCode::IPCProtocolError, "Response too small"});
    }

    ResponseHeader respHeader;
    std::memcpy(&respHeader, buffer.data(), sizeof(ResponseHeader));

    if (respHeader.status != ResponseStatus::Success) {
        std::string errorMsg = "Command failed with error code: " + std::to_string(respHeader.errorCode);
        if (buffer.size() > sizeof(ResponseHeader)) {
            errorMsg = std::string(reinterpret_cast<const char*>(buffer.data() + sizeof(ResponseHeader)), 
                                   buffer.size() - sizeof(ResponseHeader));
        }
        return std::unexpected(core::Error{
            static_cast<core::ErrorCode>(respHeader.errorCode), std::move(errorMsg)});
    }

    return std::vector<uint8_t>(buffer.begin() + sizeof(ResponseHeader), buffer.end());
}

core::VoidResult IPCClient::SendCommandAsync(
    CommandType type,
    const std::vector<uint8_t>& payload) {
    if (!IsConnected()) {
        return std::unexpected(core::Error{
            core::ErrorCode::InvalidState, "Not connected"});
    }

    MessageHeader header;
    header.commandType = type;
    header.sequenceNumber = m_impl->nextSequence.fetch_add(1);
    header.payloadSize = static_cast<uint32_t>(payload.size());

    return m_impl->pipeClient->Send(header, payload);
}

core::VoidResult IPCClient::MapSharedMemory() {
    return m_impl->sharedMem->Initialize();
}

void IPCClient::UnmapSharedMemory() {
    m_impl->sharedMem->Close();
}

SharedMemoryBuffer* IPCClient::GetSharedMemory() {
    return m_impl->sharedMem.get();
}

void IPCClient::OnConnectionChange(ConnectionCallback callback) {
    std::lock_guard lock(m_impl->callbackMutex);
    m_impl->connectionCallback = std::move(callback);
}

void IPCClient::OnFrameReady(FrameReadyCallback callback) {
    std::lock_guard lock(m_impl->callbackMutex);
    m_impl->frameReadyCallback = std::move(callback);
}

void IPCClient::OnEvent(EventCallback callback) {
    std::lock_guard lock(m_impl->callbackMutex);
    m_impl->eventCallback = std::move(callback);
}

core::VoidResult IPCClient::LaunchServer() {
    STARTUPINFOW si{};
    si.cb = sizeof(si);
    PROCESS_INFORMATION pi{};

    std::wstring exePath(m_impl->config.serverExePath.begin(),
                         m_impl->config.serverExePath.end());

    std::wstring cmdLine = exePath + L" --pipe-name " +
        std::wstring(m_impl->config.pipeConfig.pipeName.begin(),
                     m_impl->config.pipeConfig.pipeName.end());

    if (!CreateProcessW(nullptr, cmdLine.data(), nullptr, nullptr,
                         FALSE, 0, nullptr, nullptr, &si, &pi)) {
        return std::unexpected(core::Error{
            core::ErrorCode::IOError,
            "Failed to launch server: " + std::to_string(GetLastError())});
    }

    CloseHandle(pi.hThread);
    m_impl->serverProcess = pi.hProcess;

    core::Logger::SInfo("IPCClient", "Launched server process (PID: {})", pi.dwProcessId);
    return {};
}

bool IPCClient::IsServerRunning() const {
    // Check if server process is already running
    HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snapshot == INVALID_HANDLE_VALUE) return false;

    PROCESSENTRY32W pe{};
    pe.dwSize = sizeof(pe);

    bool found = false;
    if (Process32FirstW(snapshot, &pe)) {
        do {
            if (wcscmp(pe.szExeFile, L"OpenMediaServer.exe") == 0) {
                found = true;
                break;
            }
        } while (Process32NextW(snapshot, &pe));
    }

    CloseHandle(snapshot);
    return found;
}

core::VoidResult IPCClient::RequestShutdown() {
    return SendCommandAsync(CommandType::Shutdown);
}

} // namespace openmedia::ipc
