#pragma once

/// @file IPCClient.h
/// @brief Client-side IPC wrapper for SDK communication
/// @since 1.0.0

#include <openmedia/ipc/CommandTypes.h>
#include <openmedia/ipc/NamedPipeTransport.h>
#include <openmedia/ipc/SharedMemoryBuffer.h>
#include <openmedia/core/ErrorCodes.h>

#include <chrono>
#include <functional>
#include <memory>
#include <string>
#include <vector>

namespace openmedia::ipc {

/// @brief Client configuration
struct IPCClientConfig {
    NamedPipeConfig pipeConfig;
    SharedMemoryConfig sharedMemConfig;
    bool autoLaunchServer = true;               ///< Auto-launch server if not running
    std::string serverExePath = "OpenMediaServer.exe";
    uint32_t connectionTimeoutMs = 10000;       ///< Connection timeout
    uint32_t maxReconnectAttempts = 5;
    uint32_t reconnectDelayMs = 2000;
};

/// @brief Event callback types
using ConnectionCallback = std::function<void(ConnectionState state)>;
using FrameReadyCallback = std::function<void(uint32_t slotIndex, const FrameSlotHeader& metadata)>;
using EventCallback = std::function<void(CommandType eventType, const std::vector<uint8_t>& data)>;

/// @brief Client-side IPC wrapper
///
/// Provides high-level API for communicating with OpenMediaServer.exe.
/// Handles connection, command sending, shared memory mapping, and events.
///
/// @code
/// IPCClient client;
/// client.Connect();
/// auto result = client.SendCommand(CommandType::CreatePipeline, pipelineConfig);
/// @endcode
class IPCClient {
public:
    explicit IPCClient(const IPCClientConfig& config = {});
    ~IPCClient();

    IPCClient(const IPCClient&) = delete;
    IPCClient& operator=(const IPCClient&) = delete;

    // --- Connection ---

    /// @brief Connect to the server
    [[nodiscard]] core::VoidResult Connect();

    /// @brief Disconnect from server
    void Disconnect();

    /// @brief Check connection status
    [[nodiscard]] bool IsConnected() const;

    /// @brief Get connection state
    [[nodiscard]] ConnectionState GetState() const;

    // --- Commands ---

    /// @brief Send a command and wait for response
    [[nodiscard]] core::Result<std::vector<uint8_t>> SendCommand(
        CommandType type,
        const std::vector<uint8_t>& payload = {},
        std::chrono::milliseconds timeout = std::chrono::milliseconds(5000));

    /// @brief Send a command without waiting for response (fire-and-forget)
    [[nodiscard]] core::VoidResult SendCommandAsync(
        CommandType type,
        const std::vector<uint8_t>& payload = {});

    // --- Shared Memory ---

    /// @brief Map shared memory for frame access
    [[nodiscard]] core::VoidResult MapSharedMemory();

    /// @brief Unmap shared memory
    void UnmapSharedMemory();

    /// @brief Get shared memory buffer (for frame reading)
    [[nodiscard]] SharedMemoryBuffer* GetSharedMemory();

    // --- Callbacks ---

    /// @brief Set connection state change callback
    void OnConnectionChange(ConnectionCallback callback);

    /// @brief Set frame ready callback
    void OnFrameReady(FrameReadyCallback callback);

    /// @brief Set event callback
    void OnEvent(EventCallback callback);

    // --- Server Management ---

    /// @brief Launch server process
    [[nodiscard]] core::VoidResult LaunchServer();

    /// @brief Check if server is running
    [[nodiscard]] bool IsServerRunning() const;

    /// @brief Request server shutdown
    [[nodiscard]] core::VoidResult RequestShutdown();

private:
    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

} // namespace openmedia::ipc
