/// @file FrameQueue.cpp
/// @brief Lock-free concurrent frame queue implementation

#include <openmedia/core/FrameQueue.h>

#include <condition_variable>
#include <mutex>
#include <queue>

namespace openmedia::core {

struct FrameQueue::Impl {
    std::queue<std::shared_ptr<MediaFrame>> queue;
    uint32_t capacity;
    mutable std::mutex mutex;
    std::condition_variable notEmpty;
    std::condition_variable notFull;

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
    std::lock_guard lock(m_impl->mutex);
    if (m_impl->queue.size() >= m_impl->capacity) {
        return false;
    }
    m_impl->queue.push(std::move(frame));
    m_impl->totalPushed.fetch_add(1, std::memory_order_relaxed);

    auto currentSize = static_cast<uint64_t>(m_impl->queue.size());
    auto peak = m_impl->peakSize.load(std::memory_order_relaxed);
    while (currentSize > peak &&
           !m_impl->peakSize.compare_exchange_weak(peak, currentSize)) {}

    m_impl->notEmpty.notify_one();
    return true;
}

bool FrameQueue::PushWithDrop(std::shared_ptr<MediaFrame> frame) {
    std::lock_guard lock(m_impl->mutex);
    bool dropped = false;
    if (m_impl->queue.size() >= m_impl->capacity) {
        m_impl->queue.pop();  // Drop oldest
        m_impl->totalDropped.fetch_add(1, std::memory_order_relaxed);
        dropped = true;
    }
    m_impl->queue.push(std::move(frame));
    m_impl->totalPushed.fetch_add(1, std::memory_order_relaxed);
    m_impl->notEmpty.notify_one();
    return !dropped;
}

std::optional<std::shared_ptr<MediaFrame>> FrameQueue::Pop() {
    std::lock_guard lock(m_impl->mutex);
    if (m_impl->queue.empty()) {
        return std::nullopt;
    }
    auto frame = std::move(m_impl->queue.front());
    m_impl->queue.pop();
    m_impl->totalPopped.fetch_add(1, std::memory_order_relaxed);
    m_impl->notFull.notify_one();
    return frame;
}

std::optional<std::shared_ptr<MediaFrame>> FrameQueue::Pop(
    std::chrono::milliseconds timeout) {
    std::unique_lock lock(m_impl->mutex);
    if (!m_impl->notEmpty.wait_for(lock, timeout,
        [this] { return !m_impl->queue.empty(); })) {
        return std::nullopt;
    }
    auto frame = std::move(m_impl->queue.front());
    m_impl->queue.pop();
    m_impl->totalPopped.fetch_add(1, std::memory_order_relaxed);
    m_impl->notFull.notify_one();
    return frame;
}

bool FrameQueue::IsEmpty() const {
    std::lock_guard lock(m_impl->mutex);
    return m_impl->queue.empty();
}

bool FrameQueue::IsFull() const {
    std::lock_guard lock(m_impl->mutex);
    return m_impl->queue.size() >= m_impl->capacity;
}

uint32_t FrameQueue::Size() const {
    std::lock_guard lock(m_impl->mutex);
    return static_cast<uint32_t>(m_impl->queue.size());
}

uint32_t FrameQueue::Capacity() const {
    return m_impl->capacity;
}

void FrameQueue::Clear() {
    std::lock_guard lock(m_impl->mutex);
    while (!m_impl->queue.empty()) {
        m_impl->queue.pop();
    }
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
