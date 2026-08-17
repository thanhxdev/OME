#pragma once

/// @file NamedPipeTransport.h
/// @brief Windows Named Pipes transport for commands/responses
/// @since 1.0.0

#include <openmedia/ipc/IPCTransport.h>

#include <memory>
#include <string>

namespace openmedia::ipc {

/// @brief Configuration for Named Pipe transport
struct NamedPipeConfig {
    std::string pipeName = "\\\\.\\pipe\\OpenMediaSDK";
    uint32_t bufferSize = 64 * 1024;    ///< 64KB buffer
    uint32_t maxInstances = 4;          ///< Max concurrent client connections
    uint32_t timeoutMs = 5000;          ///< Connection timeout
    uint32_t heartbeatIntervalMs = 1000;///< Heartbeat interval
    bool asyncIO = true;                ///< Use OVERLAPPED async I/O
};

/// @brief Named Pipe transport — server side
///
/// Listens for incoming client connections and dispatches messages.
/// Supports multiple concurrent client connections with async I/O.
class NamedPipeServer : public IPCTransport {
public:
    explicit NamedPipeServer(const NamedPipeConfig& config = {});
    ~NamedPipeServer() override;

    NamedPipeServer(const NamedPipeServer&) = delete;
    NamedPipeServer& operator=(const NamedPipeServer&) = delete;

    // --- IPCTransport interface ---
    [[nodiscard]] core::VoidResult Connect() override;
    void Disconnect() override;
    [[nodiscard]] bool IsConnected() const override;
    [[nodiscard]] ConnectionState GetState() const override;
    [[nodiscard]] core::VoidResult Send(
        const MessageHeader& header,
        const std::vector<uint8_t>& payload) override;
    [[nodiscard]] core::Result<std::vector<uint8_t>> SendAndReceive(
        const MessageHeader& header,
        const std::vector<uint8_t>& payload,
        std::chrono::milliseconds timeout) override;
    void SetMessageCallback(MessageCallback callback) override;
    [[nodiscard]] std::string GetChannelName() const override;

    /// @brief Start listening for connections
    [[nodiscard]] core::VoidResult StartListening();

    /// @brief Stop listening
    void StopListening();

    /// @brief Get number of connected clients
    [[nodiscard]] uint32_t GetClientCount() const;

    /// @brief Send response to a specific client
    [[nodiscard]] core::VoidResult SendResponse(
        uint32_t clientId,
        const ResponseHeader& response,
        const std::vector<uint8_t>& payload);

private:
    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

/// @brief Named Pipe transport — client side
///
/// Connects to a Named Pipe server and sends commands/receives responses.
class NamedPipeClient : public IPCTransport {
public:
    explicit NamedPipeClient(const NamedPipeConfig& config = {});
    ~NamedPipeClient() override;

    NamedPipeClient(const NamedPipeClient&) = delete;
    NamedPipeClient& operator=(const NamedPipeClient&) = delete;

    // --- IPCTransport interface ---
    [[nodiscard]] core::VoidResult Connect() override;
    void Disconnect() override;
    [[nodiscard]] bool IsConnected() const override;
    [[nodiscard]] ConnectionState GetState() const override;
    [[nodiscard]] core::VoidResult Send(
        const MessageHeader& header,
        const std::vector<uint8_t>& payload) override;
    [[nodiscard]] core::Result<std::vector<uint8_t>> SendAndReceive(
        const MessageHeader& header,
        const std::vector<uint8_t>& payload,
        std::chrono::milliseconds timeout) override;
    void SetMessageCallback(MessageCallback callback) override;
    [[nodiscard]] std::string GetChannelName() const override;

    /// @brief Enable auto-reconnect on disconnection
    void SetAutoReconnect(bool enable, uint32_t maxAttempts = 5,
                          uint32_t delayMs = 2000);

private:
    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

} // namespace openmedia::ipc
