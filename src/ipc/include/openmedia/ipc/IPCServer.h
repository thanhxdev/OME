#pragma once

/// @file IPCServer.h
/// @brief Server-side IPC listener and dispatcher
/// @since 1.0.0

#include <openmedia/ipc/CommandTypes.h>
#include <openmedia/ipc/NamedPipeTransport.h>
#include <openmedia/ipc/SharedMemoryBuffer.h>
#include <openmedia/core/ErrorCodes.h>

#include <functional>
#include <memory>
#include <vector>

namespace openmedia::ipc {

/// @brief Server-side IPC configuration
struct IPCServerConfig {
    NamedPipeConfig pipeConfig;
    SharedMemoryConfig sharedMemConfig;
    uint32_t maxClients = 4;
};

/// @brief Command handler function type
/// Takes command payload, returns response payload
using CommandHandlerFn = std::function<core::Result<std::vector<uint8_t>>(
    uint32_t clientId,
    const std::vector<uint8_t>& payload)>;

using DefaultCommandHandlerFn = std::function<core::Result<std::vector<uint8_t>>(
    uint32_t clientId,
    CommandType type,
    const std::vector<uint8_t>& payload)>;

class D3D11SharedTexturePoolPoC;

/// @brief Server-side IPC listener
///
/// Listens for client connections, receives commands, dispatches to handlers,
/// and manages shared memory for frame transfer.
///
/// @code
/// IPCServer server;
/// server.RegisterHandler(CommandType::CreatePipeline, [](auto clientId, auto& payload) {
///     // handle command
///     return std::vector<uint8_t>{};
/// });
/// server.Start();
/// @endcode
class IPCServer {
public:
    explicit IPCServer(const IPCServerConfig& config = {});
    ~IPCServer();

    IPCServer(const IPCServer&) = delete;
    IPCServer& operator=(const IPCServer&) = delete;

    // --- Lifecycle ---

    /// @brief Start listening for client connections
    [[nodiscard]] core::VoidResult Start();

    /// @brief Stop the server
    void Stop();

    /// @brief Check if server is running
    [[nodiscard]] bool IsRunning() const;

    // --- Command Handling ---

    /// @brief Register a handler for a command type
    void RegisterHandler(CommandType type, CommandHandlerFn handler);

    /// @brief Set a default handler for unknown commands
    void SetDefaultHandler(DefaultCommandHandlerFn handler);

    /// @brief Unregister a handler
    void UnregisterHandler(CommandType type);

    // --- Shared Memory ---

    /// @brief Get shared memory buffer (for frame writing)
    [[nodiscard]] SharedMemoryBuffer* GetSharedMemory();

    [[nodiscard]] D3D11SharedTexturePoolPoC* GetSharedTexturePool();

    /// @brief Set the video resolution for shared texture allocation
    /// Must be called after OpenSource so textures match actual video dimensions
    void SetVideoResolution(uint32_t width, uint32_t height);

    /// @brief Notify clients that a frame is ready
    [[nodiscard]] core::VoidResult NotifyFrameReady(
        uint32_t slotIndex,
        const FrameSlotHeader& metadata);

    // --- Events ---

    /// @brief Send an event to all connected clients
    [[nodiscard]] core::VoidResult BroadcastEvent(
        CommandType eventType,
        const std::vector<uint8_t>& data);

    /// @brief Send an event to a specific client
    [[nodiscard]] core::VoidResult SendEvent(
        uint32_t clientId,
        CommandType eventType,
        const std::vector<uint8_t>& data);

    // --- Info ---

    /// @brief Get number of connected clients
    [[nodiscard]] uint32_t GetClientCount() const;

private:
    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

} // namespace openmedia::ipc
