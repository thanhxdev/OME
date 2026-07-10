#pragma once

/// @file IMediaObject.h
/// @brief Pure virtual interface for all media pipeline nodes
/// @since 1.0.0

#include <openmedia/core/ErrorCodes.h>
#include <openmedia/core/MediaFrame.h>
#include <openmedia/core/Types.h>

#include <functional>
#include <memory>
#include <string>

namespace openmedia::core {

/// @brief State change callback
using StateChangeCallback = std::function<void(PipelineState oldState, PipelineState newState)>;

/// @brief Error callback
using ErrorCallback = std::function<void(const Error& error)>;

/// @brief Pure virtual interface for all pipeline nodes
///
/// Every source, filter, mixer, encoder, and output implements this interface.
/// Nodes can operate in push or pull mode based on pipeline graph wiring.
class IMediaObject {
public:
    virtual ~IMediaObject() = default;

    /// @brief Get the name/type of this object
    [[nodiscard]] virtual std::string GetName() const = 0;

    /// @brief Get current state
    [[nodiscard]] virtual PipelineState GetState() const = 0;

    /// @brief Initialize the object
    virtual VoidResult Initialize() = 0;

    /// @brief Start processing
    virtual VoidResult Start() = 0;

    /// @brief Stop processing
    virtual VoidResult Stop() = 0;

    /// @brief Pause processing
    virtual VoidResult Pause() { return {}; }

    /// @brief Resume processing
    virtual VoidResult Resume() { return {}; }

    /// @brief Push a frame into this object (push model)
    virtual VoidResult PushFrame(std::shared_ptr<MediaFrame> frame) = 0;

    /// @brief Pull a frame from this object (pull model)
    [[nodiscard]] virtual Result<std::shared_ptr<MediaFrame>> PullFrame() = 0;

    /// @brief Connect this object's output to another object's input
    virtual VoidResult Connect(std::shared_ptr<IMediaObject> downstream) = 0;

    /// @brief Disconnect from downstream
    virtual VoidResult Disconnect() = 0;

    /// @brief Set state change callback
    virtual void OnStateChange(StateChangeCallback callback) = 0;

    /// @brief Set error callback
    virtual void OnError(ErrorCallback callback) = 0;

    /// @brief Flush internal buffers
    virtual void Flush() {}

    /// @brief Reset to initial state
    virtual void Reset() {}
};

} // namespace openmedia::core
