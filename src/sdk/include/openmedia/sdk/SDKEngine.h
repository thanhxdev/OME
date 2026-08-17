#pragma once

/// @file SDKEngine.h
/// @brief Client-side engine proxy — main entry point for SDK users
/// @since 1.0.0

#include <openmedia/sdk/SDKConfig.h>
#include <openmedia/sdk/SDKPipeline.h>
#include <openmedia/core/ErrorCodes.h>

#include <cstdint>
#include <functional>
#include <memory>
#include <string>

namespace openmedia::sdk {

/// @brief Server connection state
enum class ServerConnectionState : uint32_t {
    Disconnected = 0,
    Connecting,
    Connected,
    Reconnecting,
    Error,
};

/// @brief Client-side engine proxy — main entry point for SDK users
///
/// Manages the connection to OpenMediaServer.exe, auto-launches the server
/// if needed, and provides factory methods for creating pipelines and sources.
///
/// This is the "transparent API" — client code uses the SDK as if calling
/// the engine directly, but all processing happens in the server process.
///
/// @code
/// auto engine = SDKEngine::Create();
/// engine->Connect();
///
/// auto pipeline = engine->CreatePipeline({
///     .name = "broadcast",
///     .width = 1920, .height = 1080
/// });
///
/// auto source = pipeline->OpenSource({.url = "video.mp4"});
/// pipeline->Start();
///
/// // ... wait for processing
///
/// pipeline->Stop();
/// engine->Disconnect();
/// @endcode
class SDKEngine {
public:
    /// @brief Create an SDKEngine instance
    [[nodiscard]] static std::unique_ptr<SDKEngine> Create(const SDKConfig& config = {});

    ~SDKEngine();

    SDKEngine(const SDKEngine&) = delete;
    SDKEngine& operator=(const SDKEngine&) = delete;

    // --- Connection ---

    /// @brief Connect to the server (auto-launches if configured)
    [[nodiscard]] core::VoidResult Connect();

    /// @brief Disconnect from the server
    void Disconnect();

    /// @brief Check if connected
    [[nodiscard]] bool IsConnected() const;

    /// @brief Get connection state
    [[nodiscard]] ServerConnectionState GetConnectionState() const;

    // --- Pipeline Factory ---

    /// @brief Create a new pipeline on the server
    [[nodiscard]] core::Result<std::unique_ptr<SDKPipeline>> CreatePipeline(
        const PipelineConfig& config = {});

    // --- Server Info ---

    /// @brief Get server version string
    [[nodiscard]] core::Result<std::string> GetServerVersion() const;

    /// @brief Check if server is running
    [[nodiscard]] bool IsServerRunning() const;

    /// @brief Request server shutdown
    [[nodiscard]] core::VoidResult RequestShutdown();

    // --- Events ---

    /// @brief Set connection state change callback
    void OnConnectionChanged(std::function<void(ServerConnectionState)> callback);

    // --- Info ---

    /// @brief Get SDK version string
    [[nodiscard]] static std::string GetSDKVersion();

private:
    SDKEngine(const SDKConfig& config);

    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

} // namespace openmedia::sdk
