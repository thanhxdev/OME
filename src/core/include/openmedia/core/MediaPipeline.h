#pragma once

/// @file MediaPipeline.h
/// @brief Pipeline builder pattern and state machine
/// @since 1.0.0

#include <openmedia/core/ErrorCodes.h>
#include <openmedia/core/IMediaObject.h>
#include <openmedia/core/Types.h>

#include <functional>
#include <memory>
#include <string>
#include <vector>

namespace openmedia::core {

/// @brief Pipeline builder pattern with state machine
///
/// Constructs a media processing pipeline by connecting sources, filters,
/// mixers, encoders, and outputs. Validates the graph before execution.
///
/// @code
/// auto pipeline = MediaPipeline::Create("my-pipeline");
/// pipeline->SetSource(fileSource)
///         .AddFilter(deinterlace)
///         .SetEncoder(h264Encoder)
///         .AddOutput(fileOutput);
///
/// auto result = pipeline->Build();
/// if (result) pipeline->Start();
/// @endcode
class MediaPipeline {
public:
    /// @brief Create a new pipeline
    /// @param name Human-readable name for debugging
    /// @return Unique pointer to the pipeline
    static std::unique_ptr<MediaPipeline> Create(std::string_view name = "pipeline");

    ~MediaPipeline();
    MediaPipeline(const MediaPipeline&) = delete;
    MediaPipeline& operator=(const MediaPipeline&) = delete;
    MediaPipeline(MediaPipeline&&) noexcept;
    MediaPipeline& operator=(MediaPipeline&&) noexcept;

    // --- Builder Pattern ---

    /// @brief Set the source for this pipeline
    MediaPipeline& SetSource(std::shared_ptr<IMediaObject> source);

    /// @brief Add a filter to the pipeline
    MediaPipeline& AddFilter(std::shared_ptr<IMediaObject> filter);

    /// @brief Set the mixer (multi-input)
    MediaPipeline& SetMixer(std::shared_ptr<IMediaObject> mixer);

    /// @brief Set the encoder
    MediaPipeline& SetEncoder(std::shared_ptr<IMediaObject> encoder);

    /// @brief Add an output
    MediaPipeline& AddOutput(std::shared_ptr<IMediaObject> output);

    // --- Lifecycle ---

    /// @brief Build and validate the pipeline graph
    [[nodiscard]] VoidResult Build();

    /// @brief Start the pipeline
    [[nodiscard]] VoidResult Start();

    /// @brief Stop the pipeline
    [[nodiscard]] VoidResult Stop();

    /// @brief Pause the pipeline
    [[nodiscard]] VoidResult Pause();

    /// @brief Resume the pipeline
    [[nodiscard]] VoidResult Resume();

    // --- State ---

    /// @brief Get current pipeline state
    [[nodiscard]] PipelineState GetState() const;

    /// @brief Get pipeline name
    [[nodiscard]] std::string GetName() const;

    /// @brief Get pipeline ID
    [[nodiscard]] uint64_t GetId() const;

    // --- Callbacks ---

    /// @brief Set state change callback
    void OnStateChange(StateChangeCallback callback);

    /// @brief Set error callback
    void OnError(ErrorCallback callback);

    // --- Query ---

    /// @brief Get all nodes in the pipeline
    [[nodiscard]] std::vector<std::shared_ptr<IMediaObject>> GetNodes() const;

    /// @brief Get node count
    [[nodiscard]] size_t GetNodeCount() const;

private:
    MediaPipeline();

    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

} // namespace openmedia::core
