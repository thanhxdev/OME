/// @file NamedPipeServer.cpp
/// @brief Windows Named Pipe server transport implementation

#include <openmedia/ipc/NamedPipeTransport.h>
#include <openmedia/core/Logger.h>

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#include <sddl.h>

#include <atomic>
#include <mutex>
#include <thread>
#include <unordered_map>
#include <vector>

namespace openmedia::ipc {

// --- NamedPipeServer ---

struct NamedPipeServer::Impl {
    NamedPipeConfig config;
    std::atomic<ConnectionState> state{ConnectionState::Disconnected};
    std::atomic<bool> running{false};
    MessageCallback messageCallback;
    std::mutex callbackMutex;

    // Client connections
    struct ClientConnection {
        HANDLE pipe = INVALID_HANDLE_VALUE;
        uint32_t clientId = 0;
        OVERLAPPED overlapped{};
        std::vector<uint8_t> readBuffer;
    };

    std::unordered_map<uint32_t, std::unique_ptr<ClientConnection>> clients;
    std::mutex clientsMutex;
    std::atomic<uint32_t> nextClientId{1};

    // Listener thread
    std::thread listenerThread;
    HANDLE stopEvent = nullptr;

    Impl(const NamedPipeConfig& cfg) : config(cfg) {
        stopEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    }

    ~Impl() {
        if (stopEvent) CloseHandle(stopEvent);
    }

    HANDLE CreatePipeInstance() {
        std::wstring widePipeName(config.pipeName.begin(), config.pipeName.end());

        // Allow Everyone (WD) and ALL APPLICATION PACKAGES (AC) for WinUI 3 Packaged AppContainer support
        SECURITY_ATTRIBUTES sa{};
        sa.nLength = sizeof(sa);
        sa.bInheritHandle = FALSE;
        PSECURITY_DESCRIPTOR pSD = nullptr;
        if (ConvertStringSecurityDescriptorToSecurityDescriptorW(
                L"D:(A;;GA;;;WD)(A;;GA;;;AC)",
                SDDL_REVISION_1,
                &pSD,
                nullptr)) {
            sa.lpSecurityDescriptor = pSD;
        }

        HANDLE hPipe = CreateNamedPipeW(
            widePipeName.c_str(),
            PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED,
            PIPE_TYPE_MESSAGE | PIPE_READMODE_MESSAGE | PIPE_WAIT,
            config.maxInstances,
            config.bufferSize,
            config.bufferSize,
            config.timeoutMs,
            pSD ? &sa : nullptr);

        if (pSD) {
            LocalFree(pSD);
        }

        return hPipe;
    }

    void ListenerLoop() {
        core::Logger::SInfo("NamedPipeServer", "Listener started on {}", config.pipeName);

        while (running.load()) {
            HANDLE pipe = CreatePipeInstance();
            if (pipe == INVALID_HANDLE_VALUE) {
                core::Logger::SError("NamedPipeServer", "Failed to create pipe instance: {}",
                                    GetLastError());
                std::this_thread::sleep_for(std::chrono::milliseconds(100));
                continue;
            }

            OVERLAPPED connectOverlapped{};
            connectOverlapped.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);

            ConnectNamedPipe(pipe, &connectOverlapped);
            DWORD lastError = GetLastError();

            if (lastError == ERROR_IO_PENDING) {
                // Wait for connection or stop signal
                HANDLE events[] = {connectOverlapped.hEvent, stopEvent};
                DWORD waitResult = WaitForMultipleObjects(2, events, FALSE, INFINITE);

                CloseHandle(connectOverlapped.hEvent);

                if (waitResult == WAIT_OBJECT_0 + 1) {
                    // Stop signal
                    DisconnectNamedPipe(pipe);
                    CloseHandle(pipe);
                    break;
                }
            } else if (lastError != ERROR_PIPE_CONNECTED) {
                CloseHandle(connectOverlapped.hEvent);
                CloseHandle(pipe);
                continue;
            } else {
                CloseHandle(connectOverlapped.hEvent);
            }

            // Client connected
            uint32_t clientId = nextClientId.fetch_add(1);
            auto connection = std::make_unique<ClientConnection>();
            connection->pipe = pipe;
            connection->clientId = clientId;
            connection->readBuffer.resize(config.bufferSize);

            core::Logger::SInfo("NamedPipeServer", "Client {} connected", clientId);

            // Start client read thread
            auto* connPtr = connection.get();
            {
                std::lock_guard lock(clientsMutex);
                clients[clientId] = std::move(connection);
            }

            std::thread([this, connPtr, clientId] {
                ClientReadLoop(connPtr, clientId);
            }).detach();
        }

        core::Logger::SInfo("NamedPipeServer", "Listener stopped");
    }

    void ClientReadLoop(ClientConnection* conn, uint32_t clientId) {
        while (running.load()) {
            DWORD bytesRead = 0;
            OVERLAPPED readOverlapped{};
            readOverlapped.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);

            BOOL success = ReadFile(
                conn->pipe,
                conn->readBuffer.data(),
                static_cast<DWORD>(conn->readBuffer.size()),
                nullptr,
                &readOverlapped);

            if (!success && GetLastError() == ERROR_IO_PENDING) {
                HANDLE events[] = {readOverlapped.hEvent, stopEvent};
                DWORD waitResult = WaitForMultipleObjects(2, events, FALSE,
                    config.timeoutMs * 10);

                if (waitResult == WAIT_OBJECT_0) {
                    GetOverlappedResult(conn->pipe, &readOverlapped, &bytesRead, FALSE);
                } else {
                    CloseHandle(readOverlapped.hEvent);
                    break;
                }
            } else if (success) {
                GetOverlappedResult(conn->pipe, &readOverlapped, &bytesRead, FALSE);
            } else {
                CloseHandle(readOverlapped.hEvent);
                break;
            }

            CloseHandle(readOverlapped.hEvent);

            if (bytesRead >= sizeof(MessageHeader)) {
                MessageHeader header;
                std::memcpy(&header, conn->readBuffer.data(), sizeof(MessageHeader));

                if (header.IsValid()) {
                    header.clientId = clientId;
                    std::vector<uint8_t> payload(
                        conn->readBuffer.begin() + sizeof(MessageHeader),
                        conn->readBuffer.begin() + bytesRead);

                    std::lock_guard lock(callbackMutex);
                    if (messageCallback) {
                        messageCallback(header, payload);
                    }
                }
            }
        }

        // Cleanup client
        core::Logger::SInfo("NamedPipeServer", "Client {} disconnected", clientId);
        DisconnectNamedPipe(conn->pipe);
        CloseHandle(conn->pipe);

        std::lock_guard lock(clientsMutex);
        clients.erase(clientId);
    }
};

NamedPipeServer::NamedPipeServer(const NamedPipeConfig& config)
    : m_impl(std::make_unique<Impl>(config)) {}

NamedPipeServer::~NamedPipeServer() {
    StopListening();
}

core::VoidResult NamedPipeServer::Connect() {
    return StartListening();
}

void NamedPipeServer::Disconnect() {
    StopListening();
}

bool NamedPipeServer::IsConnected() const {
    return m_impl->running.load();
}

ConnectionState NamedPipeServer::GetState() const {
    return m_impl->state.load();
}

core::VoidResult NamedPipeServer::Send(
    const MessageHeader& header,
    const std::vector<uint8_t>& payload) {
    // Broadcast to all clients
    std::lock_guard lock(m_impl->clientsMutex);
    for (auto& [id, conn] : m_impl->clients) {
        std::vector<uint8_t> buffer(sizeof(MessageHeader) + payload.size());
        std::memcpy(buffer.data(), &header, sizeof(MessageHeader));
        std::memcpy(buffer.data() + sizeof(MessageHeader), payload.data(), payload.size());

        OVERLAPPED writeOverlapped{};
        writeOverlapped.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        
        DWORD bytesWritten = 0;
        BOOL success = WriteFile(conn->pipe, buffer.data(),
                  static_cast<DWORD>(buffer.size()), nullptr, &writeOverlapped);
        if (!success && GetLastError() == ERROR_IO_PENDING) {
            DWORD waitResult = WaitForSingleObject(writeOverlapped.hEvent, 5);
            if (waitResult == WAIT_OBJECT_0) {
                GetOverlappedResult(conn->pipe, &writeOverlapped, &bytesWritten, FALSE);
            } else {
                CancelIoEx(conn->pipe, &writeOverlapped);
            }
        }
        CloseHandle(writeOverlapped.hEvent);
    }
    return {};
}

core::Result<std::vector<uint8_t>> NamedPipeServer::SendAndReceive(
    const MessageHeader& /*header*/,
    const std::vector<uint8_t>& /*payload*/,
    std::chrono::milliseconds /*timeout*/) {
    return std::unexpected(core::Error{
        core::ErrorCode::NotImplemented,
        "Server does not use SendAndReceive"});
}

void NamedPipeServer::SetMessageCallback(MessageCallback callback) {
    std::lock_guard lock(m_impl->callbackMutex);
    m_impl->messageCallback = std::move(callback);
}

std::string NamedPipeServer::GetChannelName() const {
    return m_impl->config.pipeName;
}

core::VoidResult NamedPipeServer::StartListening() {
    if (m_impl->running.load()) {
        return std::unexpected(core::Error{
            core::ErrorCode::InvalidState,
            "Server already running"});
    }

    m_impl->running.store(true);
    m_impl->state.store(ConnectionState::Connected);
    ResetEvent(m_impl->stopEvent);

    m_impl->listenerThread = std::thread([this] { m_impl->ListenerLoop(); });

    core::Logger::SInfo("NamedPipeServer", "Server started on {}", m_impl->config.pipeName);
    return {};
}

void NamedPipeServer::StopListening() {
    if (!m_impl->running.load()) return;

    m_impl->running.store(false);
    m_impl->state.store(ConnectionState::Disconnected);
    SetEvent(m_impl->stopEvent);

    if (m_impl->listenerThread.joinable()) {
        // Create a dummy connection to unblock ConnectNamedPipe
        std::wstring widePipeName(m_impl->config.pipeName.begin(),
                                  m_impl->config.pipeName.end());
        HANDLE dummy = CreateFileW(widePipeName.c_str(), GENERIC_READ | GENERIC_WRITE,
                                   0, nullptr, OPEN_EXISTING, 0, nullptr);
        if (dummy != INVALID_HANDLE_VALUE) CloseHandle(dummy);

        m_impl->listenerThread.join();
    }

    // Close all client connections
    std::lock_guard lock(m_impl->clientsMutex);
    for (auto& [id, conn] : m_impl->clients) {
        DisconnectNamedPipe(conn->pipe);
        CloseHandle(conn->pipe);
    }
    m_impl->clients.clear();

    core::Logger::SInfo("NamedPipeServer", "Server stopped");
}

uint32_t NamedPipeServer::GetClientCount() const {
    std::lock_guard lock(m_impl->clientsMutex);
    return static_cast<uint32_t>(m_impl->clients.size());
}

core::VoidResult NamedPipeServer::SendResponse(
    uint32_t clientId,
    const ResponseHeader& response,
    const std::vector<uint8_t>& payload) {
    std::lock_guard lock(m_impl->clientsMutex);
    auto it = m_impl->clients.find(clientId);
    if (it == m_impl->clients.end()) {
        return std::unexpected(core::Error{
            core::ErrorCode::NotFound,
            "Client not found: " + std::to_string(clientId)});
    }

    std::vector<uint8_t> buffer(sizeof(ResponseHeader) + payload.size());
    std::memcpy(buffer.data(), &response, sizeof(ResponseHeader));
    std::memcpy(buffer.data() + sizeof(ResponseHeader), payload.data(), payload.size());

    OVERLAPPED writeOverlapped{};
    writeOverlapped.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);

    DWORD bytesWritten = 0;
    BOOL success = WriteFile(it->second->pipe, buffer.data(),
                   static_cast<DWORD>(buffer.size()), nullptr, &writeOverlapped);
    if (!success && GetLastError() == ERROR_IO_PENDING) {
        success = GetOverlappedResult(it->second->pipe, &writeOverlapped, &bytesWritten, TRUE);
    }
    CloseHandle(writeOverlapped.hEvent);

    if (!success) {
        return std::unexpected(core::Error{
            core::ErrorCode::IOError,
            "Failed to write to pipe: " + std::to_string(GetLastError())});
    }

    return {};
}

} // namespace openmedia::ipc
