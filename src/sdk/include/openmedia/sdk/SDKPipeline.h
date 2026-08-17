#pragma once

/// @file SDKPipeline.h
/// @brief Client-side proxy for server-side pipeline operations
/// @since 1.0.0

#include <openmedia/sdk/SDKConfig.h>
#include <openmedia/sdk/SDKSource.h>
#include <openmedia/core/ErrorCodes.h>

#include <cstdint>
#include <functional>
#include <memory>
#include <string>

namespace openmedia::ipc { class IPCClient; }

namespace openmedia::sdk {

/// @brief Pipeline state (mirrors server-side state)
enum class PipelineState : uint32_t {
    Idle = 0,
    Building,
    Ready,
    Running,
    Paused,
    Stopped,
    Error,
};

/// @brief Pipeline statistics
struct PipelineStats {
    double fps = 0.0;
    double bitrateKbps = 0.0;
    uint64_t totalFrames = 0;
    uint64_t droppedFrames = 0;
    double latencyMs = 0.0;
    double cpuUsagePercent = 0.0;
};

/// @brief Client-side proxy for a pipeline running on the server
///
/// Communicates with the server via IPC. The actual media processing
/// happens entirely in the server process.
///
/// @code
/// SDKPipeline pipeline(ipcClient, pipelineId);
/// pipeline.Start();
/// auto stats = pipeline.GetStats();
/// pipeline.Stop();
/// @endcode
class SDKPipeline {
public:
    SDKPipeline(ipc::IPCClient& client, uint32_t pipelineId);
    ~SDKPipeline();

    SDKPipeline(const SDKPipeline&) = delete;
    SDKPipeline& operator=(const SDKPipeline&) = delete;

    // --- Lifecycle ---

    /// @brief Build and validate the pipeline
    [[nodiscard]] core::VoidResult Build();

    /// @brief Start the pipeline
    [[nodiscard]] core::VoidResult Start();

    /// @brief Stop the pipeline
    [[nodiscard]] core::VoidResult Stop();

    /// @brief Pause the pipeline
    [[nodiscard]] core::VoidResult Pause();

    /// @brief Resume the pipeline
    [[nodiscard]] core::VoidResult Resume();

    /// @brief Destroy the pipeline on the server
    void Destroy();

    // --- State ---

    /// @brief Get current pipeline state
    [[nodiscard]] PipelineState GetState() const;

    /// @brief Get pipeline statistics
    [[nodiscard]] core::Result<PipelineStats> GetStats() const;

    /// @brief Get pipeline ID
    [[nodiscard]] uint32_t GetPipelineId() const;

    // --- Source Management ---

    /// @brief Open a source on this pipeline
    [[nodiscard]] core::Result<std::unique_ptr<SDKSource>> OpenSource(const SourceConfig& config);

    // --- Output Management ---

    /// @brief Add an output to this pipeline
    [[nodiscard]] core::Result<uint32_t> AddOutput(const OutputConfig& config);

    /// @brief Remove an output
    [[nodiscard]] core::VoidResult RemoveOutput(uint32_t outputId);

    // --- Callbacks ---

    /// @brief Set state change callback
    void OnStateChanged(std::function<void(PipelineState)> callback);

    /// @brief Set error callback
    void OnError(std::function<void(const core::Error&)> callback);

    /// @brief Set frame processed callback
    void OnFrameProcessed(std::function<void(uint64_t frameNumber)> callback);

private:
    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

} // namespace openmedia::sdk
