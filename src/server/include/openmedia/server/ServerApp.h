#pragma once

/// @file ServerApp.h
/// @brief OpenMediaServer.exe application lifecycle
/// @since 1.0.0

#include <openmedia/core/ErrorCodes.h>
#include <openmedia/ipc/IPCServer.h>
#include <openmedia/command_dispatcher/CommandDispatcher.h>
#include <openmedia/worker_pool/WorkerPool.h>

#include <memory>
#include <string>

namespace openmedia::server {

/// @brief Server configuration
struct ServerConfig {
    std::string configFile;                     ///< Path to server config file
    ipc::IPCServerConfig ipcConfig;             ///< IPC server settings
    worker_pool::WorkerPoolConfig workerConfig; ///< Worker pool settings
    bool enableWatchdog = true;                 ///< Enable watchdog timer
    uint32_t watchdogTimeoutMs = 30000;         ///< Watchdog timeout
    bool enableMetrics = true;                  ///< Enable metrics collection
    bool runAsService = false;                  ///< Run as Windows Service
};

/// @brief Server state
enum class ServerState : uint32_t {
    Stopped = 0,
    Starting,
    Running,
    Stopping,
    Error,
};

/// @brief OpenMediaServer application
///
/// Main entry point for the server process. Manages:
/// - IPC Server (Named Pipes, Shared Memory)
/// - Command Dispatcher (routes commands to handlers)
/// - Worker Pool (executes tasks)
/// - Pipeline management
/// - Plugin hosting
/// - Health monitoring
///
/// @code
/// ServerApp app;
/// auto result = app.Initialize(config);
/// if (result) app.Run();  // Blocks until shutdown
/// @endcode
class ServerApp {
public:
    ServerApp();
    ~ServerApp();

    ServerApp(const ServerApp&) = delete;
    ServerApp& operator=(const ServerApp&) = delete;

    // --- Lifecycle ---

    /// @brief Initialize the server with configuration
    [[nodiscard]] core::VoidResult Initialize(const ServerConfig& config = {});

    /// @brief Run the server event loop (blocking)
    [[nodiscard]] core::VoidResult Run();

    /// @brief Stop the server gracefully
    void Stop();

    /// @brief Request shutdown (can be called from any thread)
    void RequestShutdown();

    // --- State ---

    /// @brief Get current server state
    [[nodiscard]] ServerState GetState() const;

    /// @brief Check if server is running
    [[nodiscard]] bool IsRunning() const;

    // --- Components ---

    /// @brief Get the IPC server
    [[nodiscard]] ipc::IPCServer& GetIPCServer();

    /// @brief Get the command dispatcher
    [[nodiscard]] command_dispatcher::CommandDispatcher& GetDispatcher();

    /// @brief Get the worker pool
    [[nodiscard]] worker_pool::WorkerPool& GetWorkerPool();

    /// @brief Get the pipeline engine manager
    [[nodiscard]] class PipelineEngineManager& GetPipelineManager();

    // --- Info ---

    /// @brief Get server uptime
    [[nodiscard]] std::chrono::seconds GetUptime() const;

    /// @brief Get server version string
    [[nodiscard]] static std::string GetVersion();

private:
    /// @brief Register built-in command handlers
    void RegisterBuiltinHandlers();

    /// @brief Setup signal handlers for graceful shutdown
    void SetupSignalHandlers();

    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

} // namespace openmedia::server
