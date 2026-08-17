#pragma once

/// @file Engine.h
/// @brief Main factory class and application lifecycle
/// @since 1.0.0

#include <openmedia/core/ErrorCodes.h>
#include <openmedia/core/MediaPipeline.h>
#include <openmedia/core/Types.h>

#include <memory>
#include <string>
#include <string_view>
#include <vector>

namespace openmedia::core {

/// @brief Engine version information
struct EngineVersion {
    uint32_t major;
    uint32_t minor;
    uint32_t patch;
    std::string gitVersion;
    std::string buildType;
};

/// @brief Main factory class for OpenMedia SDK
///
/// Entry point for creating pipelines, sources, encoders, and other objects.
/// Manages the engine lifecycle and global resources.
///
/// @code
/// auto engine = Engine::Create();
/// engine->Initialize();
///
/// auto pipeline = engine->CreatePipeline("broadcast");
/// // ... configure pipeline ...
///
/// engine->Run();
/// // ... event loop ...
/// engine->Stop();
/// @endcode
class Engine {
public:
    /// @brief Create a new engine instance
    static std::unique_ptr<Engine> Create();

    ~Engine();
    Engine(const Engine&) = delete;
    Engine& operator=(const Engine&) = delete;

    // --- Lifecycle ---

    /// @brief Initialize the engine and all subsystems
    [[nodiscard]] VoidResult Initialize();

    /// @brief Run the engine event loop (blocking)
    [[nodiscard]] VoidResult Run();

    /// @brief Stop the engine
    [[nodiscard]] VoidResult Stop();

    /// @brief Check if engine is running
    [[nodiscard]] bool IsRunning() const;

    // --- Factory Methods ---

    /// @brief Create a new pipeline
    [[nodiscard]] std::unique_ptr<MediaPipeline> CreatePipeline(
        std::string_view name = "pipeline");

    /// @brief Enumerate available media devices
    [[nodiscard]] std::vector<std::string> EnumerateDevices() const;

    // --- Info ---

    /// @brief Get engine version
    [[nodiscard]] static EngineVersion GetVersion();

    /// @brief Get engine build info string
    [[nodiscard]] static std::string GetBuildInfo();

    /// @brief Get list of active pipelines
    [[nodiscard]] std::vector<MediaPipeline*> GetActivePipelines() const;

    /// @brief Get active pipeline count
    [[nodiscard]] size_t GetActivePipelineCount() const;

private:
    Engine();

    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

} // namespace openmedia::core
