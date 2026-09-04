/// @file FrameQueue.cpp
/// @brief Lock-free concurrent frame queue implementation using moodycamel::BlockingConcurrentQueue

#include <openmedia/core/FrameQueue.h>

#if __has_include(<moodycamel/blockingconcurrentqueue.h>)
#include <moodycamel/blockingconcurrentqueue.h>
#elif __has_include(<concurrentqueue/moodycamel/blockingconcurrentqueue.h>)
#include <concurrentqueue/moodycamel/blockingconcurrentqueue.h>
#elif __has_include(<blockingconcurrentqueue.h>)
#include <blockingconcurrentqueue.h>
#endif

#include <algorithm>
#include <atomic>

namespace openmedia::core {

struct FrameQueue::Impl {
    moodycamel::BlockingConcurrentQueue<std::shared_ptr<MediaFrame>> queue;
    uint32_t capacity;
    std::atomic<int32_t> currentSize{0};

    // Stats
    std::atomic<uint64_t> totalPushed{0};
    std::atomic<uint64_t> totalPopped{0};
    std::atomic<uint64_t> totalDropped{0};
    std::atomic<uint64_t> peakSize{0};
};

FrameQueue::FrameQueue(uint32_t capacity)
    : m_impl(std::make_unique<Impl>()) {
    m_impl->capacity = capacity;
}

FrameQueue::~FrameQueue() = default;
FrameQueue::FrameQueue(FrameQueue&&) noexcept = default;
FrameQueue& FrameQueue::operator=(FrameQueue&&) noexcept = default;

bool FrameQueue::Push(std::shared_ptr<MediaFrame> frame) {
    if (!frame) return false;

    int32_t cur = m_impl->currentSize.load(std::memory_order_relaxed);
    while (cur < static_cast<int32_t>(m_impl->capacity)) {
        if (m_impl->currentSize.compare_exchange_weak(cur, cur + 1,
                std::memory_order_acquire, std::memory_order_relaxed)) {
            m_impl->queue.enqueue(std::move(frame));
            m_impl->totalPushed.fetch_add(1, std::memory_order_relaxed);

            auto current = static_cast<uint64_t>(cur + 1);
            auto peak = m_impl->peakSize.load(std::memory_order_relaxed);
            while (current > peak &&
                   !m_impl->peakSize.compare_exchange_weak(peak, current, std::memory_order_relaxed)) {}
            return true;
        }
    }
    return false;
}

bool FrameQueue::PushWithDrop(std::shared_ptr<MediaFrame> frame) {
    if (!frame) return false;

    bool dropped = false;
    while (m_impl->currentSize.load(std::memory_order_relaxed) >= static_cast<int32_t>(m_impl->capacity)) {
        std::shared_ptr<MediaFrame> oldFrame;
        if (m_impl->queue.try_dequeue(oldFrame)) {
            m_impl->currentSize.fetch_sub(1, std::memory_order_release);
            m_impl->totalDropped.fetch_add(1, std::memory_order_relaxed);
            dropped = true;
            break;
        }
    }

    m_impl->currentSize.fetch_add(1, std::memory_order_release);
    m_impl->queue.enqueue(std::move(frame));
    m_impl->totalPushed.fetch_add(1, std::memory_order_relaxed);

    auto current = static_cast<uint64_t>(m_impl->currentSize.load(std::memory_order_relaxed));
    auto peak = m_impl->peakSize.load(std::memory_order_relaxed);
    while (current > peak &&
           !m_impl->peakSize.compare_exchange_weak(peak, current, std::memory_order_relaxed)) {}
    return !dropped;
}

std::optional<std::shared_ptr<MediaFrame>> FrameQueue::Pop() {
    std::shared_ptr<MediaFrame> frame;
    if (m_impl->queue.try_dequeue(frame)) {
        m_impl->currentSize.fetch_sub(1, std::memory_order_release);
        m_impl->totalPopped.fetch_add(1, std::memory_order_relaxed);
        return frame;
    }
    return std::nullopt;
}

std::optional<std::shared_ptr<MediaFrame>> FrameQueue::Pop(
    std::chrono::milliseconds timeout) {
    std::shared_ptr<MediaFrame> frame;
    if (m_impl->queue.wait_dequeue_timed(frame, timeout)) {
        m_impl->currentSize.fetch_sub(1, std::memory_order_release);
        m_impl->totalPopped.fetch_add(1, std::memory_order_relaxed);
        return frame;
    }
    return std::nullopt;
}

bool FrameQueue::IsEmpty() const {
    return m_impl->currentSize.load(std::memory_order_relaxed) <= 0;
}

bool FrameQueue::IsFull() const {
    return m_impl->currentSize.load(std::memory_order_relaxed) >= static_cast<int32_t>(m_impl->capacity);
}

uint32_t FrameQueue::Size() const {
    int32_t sz = m_impl->currentSize.load(std::memory_order_relaxed);
    return (sz > 0) ? static_cast<uint32_t>(sz) : 0;
}

uint32_t FrameQueue::Capacity() const {
    return m_impl->capacity;
}

void FrameQueue::Clear() {
    std::shared_ptr<MediaFrame> frame;
    while (m_impl->queue.try_dequeue(frame)) {
        m_impl->currentSize.fetch_sub(1, std::memory_order_release);
    }
    m_impl->currentSize.store(0, std::memory_order_release);
}

FrameQueue::Stats FrameQueue::GetStats() const {
    return {
        m_impl->totalPushed.load(std::memory_order_relaxed),
        m_impl->totalPopped.load(std::memory_order_relaxed),
        m_impl->totalDropped.load(std::memory_order_relaxed),
        m_impl->peakSize.load(std::memory_order_relaxed),
    };
}

void FrameQueue::ResetStats() {
    m_impl->totalPushed.store(0, std::memory_order_relaxed);
    m_impl->totalPopped.store(0, std::memory_order_relaxed);
    m_impl->totalDropped.store(0, std::memory_order_relaxed);
    m_impl->peakSize.store(0, std::memory_order_relaxed);
}

} // namespace openmedia::core
