#pragma once

/// @file JitterBuffer.h
/// @brief A Jitter Buffer to reorder and delay incoming MediaFrames
/// @since 1.0.0

#include <openmedia/core/MediaFrame.h>
#include <memory>
#include <optional>
#include <cstdint>

namespace openmedia::io {

/// @brief Absorbs network latency variations (jitter) and reorders frames by PTS
///
/// Keeps frames for a specified target latency before releasing them.
/// If frames arrive out of order, they are sorted by their PTS value.
class JitterBuffer {
public:
    /// @brief Construct a JitterBuffer
    /// @param targetLatencyMs Target delay in milliseconds to absorb jitter
    /// @param maxCapacity Maximum number of frames the buffer can hold
    explicit JitterBuffer(uint32_t targetLatencyMs = 200, uint32_t maxCapacity = 300);
    ~JitterBuffer();

    JitterBuffer(const JitterBuffer&) = delete;
    JitterBuffer& operator=(const JitterBuffer&) = delete;

    /// @brief Push a frame into the buffer
    /// @param frame The frame to buffer
    /// @return true if pushed, false if capacity exceeded and pushed frame is dropped
    bool Push(std::shared_ptr<core::MediaFrame> frame);

    /// @brief Try to pop a frame from the buffer
    /// @return A frame if one has matured (waited longer than targetLatencyMs), 
    ///         or if max capacity is reached. Returns nullopt otherwise.
    [[nodiscard]] std::optional<std::shared_ptr<core::MediaFrame>> Pop();

    /// @brief Change the target latency
    void SetTargetLatency(uint32_t ms);

    /// @brief Change the max capacity
    void SetMaxCapacity(uint32_t capacity);

    /// @brief Clear the buffer
    void Clear();

    /// @brief Get current frame count in the buffer
    [[nodiscard]] uint32_t Size() const;

private:
    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

} // namespace openmedia::io
