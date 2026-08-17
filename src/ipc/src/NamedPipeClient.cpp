/// @file NamedPipeClient.cpp
/// @brief Windows Named Pipe client transport implementation

#include <openmedia/ipc/NamedPipeTransport.h>
#include <openmedia/core/Logger.h>

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>

#include <atomic>
#include <condition_variable>
#include <mutex>
#include <thread>

namespace openmedia::ipc {

struct NamedPipeClient::Impl {
    NamedPipeConfig config;
    HANDLE pipe = INVALID_HANDLE_VALUE;
    std::atomic<ConnectionState> state{ConnectionState::Disconnected};
    MessageCallback messageCallback;
    std::mutex callbackMutex;

    // Auto-reconnect
    bool autoReconnect = false;
    uint32_t maxReconnectAttempts = 5;
    uint32_t reconnectDelayMs = 2000;

    // Read thread
    std::thread readThread;
    std::atomic<bool> running{false};
    HANDLE stopEvent = nullptr;

    // Response handling
    std::mutex responseMutex;
    std::condition_variable responseCV;
    std::vector<uint8_t> responseBuffer;
    bool responseReady = false;

    Impl(const NamedPipeConfig& cfg) : config(cfg) {
        stopEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    }

    ~Impl() {
        if (stopEvent) CloseHandle(stopEvent);
    }

    void ReadLoop() {
        std::vector<uint8_t> buffer(config.bufferSize);

        while (running.load()) {
            DWORD bytesRead = 0;
            OVERLAPPED readOverlapped{};
            readOverlapped.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);

            BOOL success = ReadFile(pipe, buffer.data(),
                                    static_cast<DWORD>(buffer.size()),
                                    nullptr, &readOverlapped);

            if (!success && GetLastError() == ERROR_IO_PENDING) {
                HANDLE events[] = {readOverlapped.hEvent, stopEvent};
                DWORD waitResult = WaitForMultipleObjects(2, events, FALSE, INFINITE);

                if (waitResult == WAIT_OBJECT_0) {
                    GetOverlappedResult(pipe, &readOverlapped, &bytesRead, FALSE);
                } else {
                    CloseHandle(readOverlapped.hEvent);
                    break;
                }
            } else if (success) {
                GetOverlappedResult(pipe, &readOverlapped, &bytesRead, FALSE);
            } else {
                CloseHandle(readOverlapped.hEvent);

                // Pipe broken, attempt reconnect
                if (autoReconnect && running.load()) {
                    state.store(ConnectionState::Reconnecting);
                    AttemptReconnect();
                }
                break;
            }

            CloseHandle(readOverlapped.hEvent);

            if (bytesRead == 0) continue;

            // Check if this is a response (ResponseHeader) or message (MessageHeader)
            if (bytesRead >= sizeof(ResponseHeader)) {
                ResponseHeader respHeader;
                std::memcpy(&respHeader, buffer.data(), sizeof(ResponseHeader));

                if (respHeader.IsValid()) {
                    // This is a response to a pending request
                    std::lock_guard lock(responseMutex);
                    responseBuffer.assign(buffer.begin(), buffer.begin() + bytesRead);
                    responseReady = true;
                    responseCV.notify_one();
                    continue;
                }
            }

            if (bytesRead >= sizeof(MessageHeader)) {
                MessageHeader header;
                std::memcpy(&header, buffer.data(), sizeof(MessageHeader));

                if (header.IsValid()) {
                    std::vector<uint8_t> payload(
                        buffer.begin() + sizeof(MessageHeader),
                        buffer.begin() + bytesRead);

                    std::lock_guard lock(callbackMutex);
                    if (messageCallback) {
                        messageCallback(header, payload);
                    }
                }
            }
        }
    }

    void AttemptReconnect() {
        for (uint32_t attempt = 0;
             attempt < maxReconnectAttempts && running.load();
             ++attempt) {
            core::Logger::SInfo("NamedPipeClient", "Reconnect attempt {}/{}",
                               attempt + 1, maxReconnectAttempts);

            std::this_thread::sleep_for(
                std::chrono::milliseconds(reconnectDelayMs));

            if (TryConnect()) {
                state.store(ConnectionState::Connected);
                core::Logger::SInfo("NamedPipeClient", "Reconnected successfully");
                return;
            }
        }

        state.store(ConnectionState::Error);
        core::Logger::SError("NamedPipeClient", "Reconnect failed after {} attempts",
                            maxReconnectAttempts);
    }

    bool TryConnect() {
        std::wstring widePipeName(config.pipeName.begin(), config.pipeName.end());

        pipe = CreateFileW(
            widePipeName.c_str(),
            GENERIC_READ | GENERIC_WRITE,
            0, nullptr, OPEN_EXISTING,
            FILE_FLAG_OVERLAPPED, nullptr);

        if (pipe == INVALID_HANDLE_VALUE) {
            if (GetLastError() == ERROR_PIPE_BUSY) {
                WaitNamedPipeW(widePipeName.c_str(), config.timeoutMs);
                pipe = CreateFileW(widePipeName.c_str(),
                                   GENERIC_READ | GENERIC_WRITE,
                                   0, nullptr, OPEN_EXISTING,
                                   FILE_FLAG_OVERLAPPED, nullptr);
            }
        }

        if (pipe == INVALID_HANDLE_VALUE) return false;

        // Set pipe to message mode
        DWORD mode = PIPE_READMODE_MESSAGE;
        SetNamedPipeHandleState(pipe, &mode, nullptr, nullptr);

        return true;
    }
};

NamedPipeClient::NamedPipeClient(const NamedPipeConfig& config)
    : m_impl(std::make_unique<Impl>(config)) {}

NamedPipeClient::~NamedPipeClient() {
    Disconnect();
}

core::VoidResult NamedPipeClient::Connect() {
    if (m_impl->state.load() == ConnectionState::Connected) {
        return std::unexpected(core::Error{
            core::ErrorCode::InvalidState,
            "Already connected"});
    }

    m_impl->state.store(ConnectionState::Connecting);

    if (!m_impl->TryConnect()) {
        m_impl->state.store(ConnectionState::Error);
        return std::unexpected(core::Error{
            core::ErrorCode::IPCConnectionFailed,
            "Failed to connect to pipe: " + m_impl->config.pipeName});
    }

    m_impl->state.store(ConnectionState::Connected);
    m_impl->running.store(true);
    ResetEvent(m_impl->stopEvent);

    m_impl->readThread = std::thread([this] { m_impl->ReadLoop(); });

    core::Logger::SInfo("NamedPipeClient", "Connected to {}", m_impl->config.pipeName);
    return {};
}

void NamedPipeClient::Disconnect() {
    m_impl->running.store(false);
    SetEvent(m_impl->stopEvent);

    if (m_impl->readThread.joinable()) {
        // Cancel pending I/O
        if (m_impl->pipe != INVALID_HANDLE_VALUE) {
            CancelIoEx(m_impl->pipe, nullptr);
        }
        m_impl->readThread.join();
    }

    if (m_impl->pipe != INVALID_HANDLE_VALUE) {
        CloseHandle(m_impl->pipe);
        m_impl->pipe = INVALID_HANDLE_VALUE;
    }

    m_impl->state.store(ConnectionState::Disconnected);
    core::Logger::SInfo("NamedPipeClient", "Disconnected");
}

bool NamedPipeClient::IsConnected() const {
    return m_impl->state.load() == ConnectionState::Connected;
}

ConnectionState NamedPipeClient::GetState() const {
    return m_impl->state.load();
}

core::VoidResult NamedPipeClient::Send(
    const MessageHeader& header,
    const std::vector<uint8_t>& payload) {
    if (!IsConnected()) {
        return std::unexpected(core::Error{
            core::ErrorCode::InvalidState,
            "Not connected"});
    }

    std::vector<uint8_t> buffer(sizeof(MessageHeader) + payload.size());
    std::memcpy(buffer.data(), &header, sizeof(MessageHeader));
    if (!payload.empty()) {
        std::memcpy(buffer.data() + sizeof(MessageHeader),
                    payload.data(), payload.size());
    }

    OVERLAPPED writeOverlapped{};
    writeOverlapped.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);

    DWORD bytesWritten = 0;
    BOOL success = WriteFile(m_impl->pipe, buffer.data(),
                   static_cast<DWORD>(buffer.size()), nullptr, &writeOverlapped);

    if (!success && GetLastError() == ERROR_IO_PENDING) {
        success = GetOverlappedResult(m_impl->pipe, &writeOverlapped, &bytesWritten, TRUE);
    }
    
    CloseHandle(writeOverlapped.hEvent);

    if (!success) {
        return std::unexpected(core::Error{
            core::ErrorCode::IOError,
            "Write failed: " + std::to_string(GetLastError())});
    }

    return {};
}

core::Result<std::vector<uint8_t>> NamedPipeClient::SendAndReceive(
    const MessageHeader& header,
    const std::vector<uint8_t>& payload,
    std::chrono::milliseconds timeout) {
    // Reset response state
    {
        std::lock_guard lock(m_impl->responseMutex);
        m_impl->responseReady = false;
        m_impl->responseBuffer.clear();
    }

    // Send request
    auto sendResult = Send(header, payload);
    if (!sendResult) return std::unexpected(sendResult.error());

    // Wait for response
    std::unique_lock lock(m_impl->responseMutex);
    if (!m_impl->responseCV.wait_for(lock, timeout,
            [this] { return m_impl->responseReady; })) {
        return std::unexpected(core::Error{
            core::ErrorCode::Timeout,
            "Response timeout after " + std::to_string(timeout.count()) + "ms"});
    }

    return std::move(m_impl->responseBuffer);
}

void NamedPipeClient::SetMessageCallback(MessageCallback callback) {
    std::lock_guard lock(m_impl->callbackMutex);
    m_impl->messageCallback = std::move(callback);
}

std::string NamedPipeClient::GetChannelName() const {
    return m_impl->config.pipeName;
}

void NamedPipeClient::SetAutoReconnect(bool enable, uint32_t maxAttempts,
                                        uint32_t delayMs) {
    m_impl->autoReconnect = enable;
    m_impl->maxReconnectAttempts = maxAttempts;
    m_impl->reconnectDelayMs = delayMs;
}

} // namespace openmedia::ipc
