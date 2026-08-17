#pragma once

/// @file IPCTransport.h
/// @brief Abstract transport interface for IPC communication
/// @since 1.0.0

#include <openmedia/ipc/CommandTypes.h>
#include <openmedia/core/ErrorCodes.h>

#include <chrono>
#include <cstdint>
#include <functional>
#include <memory>
#include <string>
#include <vector>

namespace openmedia::ipc {

/// @brief Callback for received messages
using MessageCallback = std::function<void(const MessageHeader& header, const std::vector<uint8_t>& payload)>;

/// @brief Connection state
enum class ConnectionState {
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Error,
};

/// @brief Abstract transport interface
class IPCTransport {
public:
    virtual ~IPCTransport() = default;

    /// @brief Connect to the other end
    [[nodiscard]] virtual core::VoidResult Connect() = 0;

    /// @brief Disconnect
    virtual void Disconnect() = 0;

    /// @brief Check if connected
    [[nodiscard]] virtual bool IsConnected() const = 0;

    /// @brief Get connection state
    [[nodiscard]] virtual ConnectionState GetState() const = 0;

    /// @brief Send a message (header + payload)
    [[nodiscard]] virtual core::VoidResult Send(
        const MessageHeader& header,
        const std::vector<uint8_t>& payload) = 0;

    /// @brief Send a message and wait for response
    [[nodiscard]] virtual core::Result<std::vector<uint8_t>> SendAndReceive(
        const MessageHeader& header,
        const std::vector<uint8_t>& payload,
        std::chrono::milliseconds timeout = std::chrono::milliseconds(5000)) = 0;

    /// @brief Set callback for incoming messages
    virtual void SetMessageCallback(MessageCallback callback) = 0;

    /// @brief Get the pipe/channel name
    [[nodiscard]] virtual std::string GetChannelName() const = 0;
};

} // namespace openmedia::ipc
