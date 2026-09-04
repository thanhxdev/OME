/// @file NamedPipeServer.cpp
/// @brief Windows Named Pipe server transport with IOCP asynchronous I/O
/// @since 2.0.0

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
#include <memory>

namespace openmedia::ipc {

// --- NamedPipeServer ---

struct NamedPipeServer::Impl {
    NamedPipeConfig config;
    std::atomic<ConnectionState> state{ConnectionState::Disconnected};
    std::atomic<bool> running{false};
    MessageCallback messageCallback;
    std::mutex callbackMutex;

    // Client connection context for IOCP
    struct ClientConnection {
        HANDLE pipe = INVALID_HANDLE_VALUE;
        uint32_t clientId = 0;
        OVERLAPPED readOverlapped{};
        std::vector<uint8_t> readBuffer;
        std::atomic<bool> closing{false};
    };

    std::unordered_map<uint32_t, std::shared_ptr<ClientConnection>> clients;
    std::mutex clientsMutex;
    std::atomic<uint32_t> nextClientId{1};

    // Windows IOCP handle & workers
    HANDLE iocpHandle = nullptr;
    std::vector<std::thread> iocpWorkers;
    static constexpr size_t NUM_IOCP_WORKERS = 2;

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

    void StartAsyncRead(const std::shared_ptr<ClientConnection>& conn) {
        if (!running.load() || conn->closing.load()) return;

        ZeroMemory(&conn->readOverlapped, sizeof(OVERLAPPED));
        DWORD bytesRead = 0;
        BOOL success = ReadFile(
            conn->pipe,
            conn->readBuffer.data(),
            static_cast<DWORD>(conn->readBuffer.size()),
            &bytesRead,
            &conn->readOverlapped);

        if (!success) {
            DWORD err = GetLastError();
            if (err != ERROR_IO_PENDING) {
                core::Logger::SDebug("NamedPipeServer", "ReadFile error on client {}: {}", conn->clientId, err);
                CloseClient(conn->clientId);
            }
        }
    }

    void CloseClient(uint32_t clientId) {
        std::shared_ptr<ClientConnection> conn;
        {
            std::lock_guard lock(clientsMutex);
            auto it = clients.find(clientId);
            if (it != clients.end()) {
                conn = it->second;
                clients.erase(it);
            }
        }

        if (conn && !conn->closing.exchange(true)) {
            core::Logger::SInfo("NamedPipeServer", "Client {} disconnected (IOCP)", clientId);
            if (conn->pipe != INVALID_HANDLE_VALUE) {
                CancelIoEx(conn->pipe, nullptr);
                DisconnectNamedPipe(conn->pipe);
                CloseHandle(conn->pipe);
                conn->pipe = INVALID_HANDLE_VALUE;
            }
        }
    }

    void IocpWorkerLoop() {
        while (running.load()) {
            DWORD bytesTransferred = 0;
            ULONG_PTR completionKey = 0;
            LPOVERLAPPED pOverlapped = nullptr;

            BOOL ok = GetQueuedCompletionStatus(
                iocpHandle,
                &bytesTransferred,
                &completionKey,
                &pOverlapped,
                250 // Timeout in ms to allow checking running status
            );

            if (!running.load()) break;

            if (!ok) {
                DWORD err = GetLastError();
                if (err == WAIT_TIMEOUT) {
                    continue;
                }
                if (completionKey != 0) {
                    uint32_t clientId = static_cast<uint32_t>(completionKey);
                    CloseClient(clientId);
                }
                continue;
            }

            if (completionKey == 0 || pOverlapped == nullptr) {
                // Exit signal posted
                break;
            }

            uint32_t clientId = static_cast<uint32_t>(completionKey);
            std::shared_ptr<ClientConnection> conn;
            {
                std::lock_guard lock(clientsMutex);
                auto it = clients.find(clientId);
                if (it != clients.end()) {
                    conn = it->second;
                }
            }

            if (!conn || conn->closing.load()) {
                continue;
            }

            if (bytesTransferred >= sizeof(MessageHeader)) {
                MessageHeader header;
                std::memcpy(&header, conn->readBuffer.data(), sizeof(MessageHeader));

                if (header.IsValid()) {
                    header.clientId = clientId;
                    std::vector<uint8_t> payload(
                        conn->readBuffer.begin() + sizeof(MessageHeader),
                        conn->readBuffer.begin() + bytesTransferred);

                    std::lock_guard lock(callbackMutex);
                    if (messageCallback) {
                        messageCallback(header, payload);
                    }
                }
            }

            // Queue next asynchronous read for this client
            StartAsyncRead(conn);
        }
    }

    void ListenerLoop() {
        core::Logger::SInfo("NamedPipeServer", "Listener started on {} (IOCP Mode)", config.pipeName);

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
                HANDLE events[] = {connectOverlapped.hEvent, stopEvent};
                DWORD waitResult = WaitForMultipleObjects(2, events, FALSE, INFINITE);

                CloseHandle(connectOverlapped.hEvent);

                if (waitResult == WAIT_OBJECT_0 + 1 || !running.load()) {
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
            auto connection = std::make_shared<ClientConnection>();
            connection->pipe = pipe;
            connection->clientId = clientId;
            connection->readBuffer.resize(config.bufferSize);

            // Associate pipe with IOCP handle
            if (CreateIoCompletionPort(pipe, iocpHandle, static_cast<ULONG_PTR>(clientId), 0) == nullptr) {
                core::Logger::SError("NamedPipeServer", "Failed to associate pipe with IOCP: {}", GetLastError());
                DisconnectNamedPipe(pipe);
                CloseHandle(pipe);
                continue;
            }

            {
                std::lock_guard lock(clientsMutex);
                clients[clientId] = connection;
            }

            core::Logger::SInfo("NamedPipeServer", "Client {} connected (Associated with IOCP)", clientId);

            // Kick off initial asynchronous read via IOCP
            StartAsyncRead(connection);
        }

        core::Logger::SInfo("NamedPipeServer", "Listener stopped");
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
        if (conn->closing.load() || conn->pipe == INVALID_HANDLE_VALUE) continue;

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

    // 1. Create Windows IOCP handle
    m_impl->iocpHandle = CreateIoCompletionPort(INVALID_HANDLE_VALUE, nullptr, 0, 0);
    if (!m_impl->iocpHandle) {
        return std::unexpected(core::Error{
            core::ErrorCode::IOError,
            "Failed to create IO Completion Port: " + std::to_string(GetLastError())});
    }

    m_impl->running.store(true);
    m_impl->state.store(ConnectionState::Connected);
    ResetEvent(m_impl->stopEvent);

    // 2. Start IOCP worker threads
    for (size_t i = 0; i < Impl::NUM_IOCP_WORKERS; ++i) {
        m_impl->iocpWorkers.emplace_back([this] {
            m_impl->IocpWorkerLoop();
        });
    }

    // 3. Start listener thread
    m_impl->listenerThread = std::thread([this] { m_impl->ListenerLoop(); });

    core::Logger::SInfo("NamedPipeServer", "Server started on {} with {} IOCP workers",
                       m_impl->config.pipeName, m_impl->iocpWorkers.size());
    return {};
}

void NamedPipeServer::StopListening() {
    if (!m_impl->running.load()) return;

    m_impl->running.store(false);
    m_impl->state.store(ConnectionState::Disconnected);
    SetEvent(m_impl->stopEvent);

    // 1. Wake up and unblock listener
    if (m_impl->listenerThread.joinable()) {
        std::wstring widePipeName(m_impl->config.pipeName.begin(),
                                  m_impl->config.pipeName.end());
        HANDLE dummy = CreateFileW(widePipeName.c_str(), GENERIC_READ | GENERIC_WRITE,
                                   0, nullptr, OPEN_EXISTING, 0, nullptr);
        if (dummy != INVALID_HANDLE_VALUE) CloseHandle(dummy);

        m_impl->listenerThread.join();
    }

    // 2. Wake up and join IOCP worker threads
    if (m_impl->iocpHandle) {
        for (size_t i = 0; i < m_impl->iocpWorkers.size(); ++i) {
            PostQueuedCompletionStatus(m_impl->iocpHandle, 0, 0, nullptr);
        }
        for (auto& worker : m_impl->iocpWorkers) {
            if (worker.joinable()) {
                worker.join();
            }
        }
        m_impl->iocpWorkers.clear();
        CloseHandle(m_impl->iocpHandle);
        m_impl->iocpHandle = nullptr;
    }

    // 3. Close all client connections
    std::unordered_map<uint32_t, std::shared_ptr<Impl::ClientConnection>> clientsToClose;
    {
        std::lock_guard lock(m_impl->clientsMutex);
        clientsToClose = std::move(m_impl->clients);
        m_impl->clients.clear();
    }

    for (auto& [id, conn] : clientsToClose) {
        if (conn && conn->pipe != INVALID_HANDLE_VALUE) {
            DisconnectNamedPipe(conn->pipe);
            CloseHandle(conn->pipe);
            conn->pipe = INVALID_HANDLE_VALUE;
        }
    }

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
    std::shared_ptr<Impl::ClientConnection> conn;
    {
        std::lock_guard lock(m_impl->clientsMutex);
        auto it = m_impl->clients.find(clientId);
        if (it == m_impl->clients.end()) {
            return std::unexpected(core::Error{
                core::ErrorCode::NotFound,
                "Client not found: " + std::to_string(clientId)});
        }
        conn = it->second;
    }

    if (conn->closing.load() || conn->pipe == INVALID_HANDLE_VALUE) {
        return std::unexpected(core::Error{
            core::ErrorCode::IOError,
            "Client connection is closed"});
    }

    std::vector<uint8_t> buffer(sizeof(ResponseHeader) + payload.size());
    std::memcpy(buffer.data(), &response, sizeof(ResponseHeader));
    std::memcpy(buffer.data() + sizeof(ResponseHeader), payload.data(), payload.size());

    OVERLAPPED writeOverlapped{};
    writeOverlapped.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);

    DWORD bytesWritten = 0;
    BOOL success = WriteFile(conn->pipe, buffer.data(),
                   static_cast<DWORD>(buffer.size()), nullptr, &writeOverlapped);
    if (!success && GetLastError() == ERROR_IO_PENDING) {
        success = GetOverlappedResult(conn->pipe, &writeOverlapped, &bytesWritten, TRUE);
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
