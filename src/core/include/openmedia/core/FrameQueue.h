#pragma once

/// @file FrameQueue.h
/// @brief Lock-free concurrent frame queue
/// @since 1.0.0

#include <openmedia/core/MediaFrame.h>

#include <atomic>
#include <chrono>
#include <cstdint>
#include <memory>
#include <optional>

namespace openmedia::core {

/// @brief Lock-free concurrent queue for frame transfer between pipeline nodes
///
/// Wraps a high-performance concurrent queue for producer-consumer pattern.
/// Supports backpressure, timeout-based operations, and statistics tracking.
///
/// @code
/// FrameQueue queue(8);  // 8-frame capacity
/// queue.Push(frame);
/// auto result = queue.Pop(std::chrono::milliseconds(100));
/// @endcode
class FrameQueue {
public:
    /// @brief Queue statistics
    struct Stats {
        uint64_t totalPushed = 0;
        uint64_t totalPopped = 0;
        uint64_t totalDropped = 0;
        uint64_t peakSize = 0;
    };

    /// @brief Create a frame queue
    /// @param capacity Maximum number of frames in the queue
    explicit FrameQueue(uint32_t capacity = 8);
    ~FrameQueue();

    FrameQueue(const FrameQueue&) = delete;
    FrameQueue& operator=(const FrameQueue&) = delete;
    FrameQueue(FrameQueue&&) noexcept;
    FrameQueue& operator=(FrameQueue&&) noexcept;

    /// @brief Push a frame to the queue
    /// @param frame Frame to push
    /// @return true if pushed, false if queue is full
    bool Push(std::shared_ptr<MediaFrame> frame);

    /// @brief Push a frame, dropping oldest if full
    /// @param frame Frame to push
    /// @return true if pushed without drop, false if a frame was dropped
    bool PushWithDrop(std::shared_ptr<MediaFrame> frame);

    /// @brief Pop a frame from the queue
    /// @return Frame if available, nullopt if empty
    [[nodiscard]] std::optional<std::shared_ptr<MediaFrame>> Pop();

    /// @brief Pop with timeout
    /// @param timeout Maximum wait time
    /// @return Frame if available within timeout, nullopt otherwise
    [[nodiscard]] std::optional<std::shared_ptr<MediaFrame>> Pop(
        std::chrono::milliseconds timeout);

    /// @brief Check if queue is empty
    [[nodiscard]] bool IsEmpty() const;

    /// @brief Check if queue is full
    [[nodiscard]] bool IsFull() const;

    /// @brief Get current size (approximate)
    [[nodiscard]] uint32_t Size() const;

    /// @brief Get capacity
    [[nodiscard]] uint32_t Capacity() const;

    /// @brief Clear all frames
    void Clear();

    /// @brief Get queue statistics
    [[nodiscard]] Stats GetStats() const;

    /// @brief Reset statistics
    void ResetStats();

private:
    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

} // namespace openmedia::core
