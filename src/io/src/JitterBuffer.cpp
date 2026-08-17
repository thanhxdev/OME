/// @file JitterBuffer.cpp
#include <openmedia/io/JitterBuffer.h>
#include <mutex>
#include <queue>
#include <chrono>

namespace openmedia::io {

struct BufferedFrame {
    std::shared_ptr<core::MediaFrame> frame;
    std::chrono::steady_clock::time_point arrivalTime;

    // We want the smallest PTS to be at the top of the priority_queue
    bool operator<(const BufferedFrame& other) const {
        return frame->GetPts() > other.frame->GetPts();
    }
};

struct JitterBuffer::Impl {
    uint32_t targetLatencyMs;
    uint32_t maxCapacity;

    std::mutex mutex;
    std::priority_queue<BufferedFrame> queue;

    bool isBuffering = true;
    std::chrono::steady_clock::time_point bufferingStartTime;

    Impl(uint32_t latency, uint32_t capacity)
        : targetLatencyMs(latency), maxCapacity(capacity) {}
};

JitterBuffer::JitterBuffer(uint32_t targetLatencyMs, uint32_t maxCapacity)
    : m_impl(new Impl(targetLatencyMs, maxCapacity)) {}

JitterBuffer::~JitterBuffer() = default;

bool JitterBuffer::Push(std::shared_ptr<core::MediaFrame> frame) {
    if (!frame) return false;

    std::lock_guard lock(m_impl->mutex);

    // If we are at capacity, drop the oldest frame to make room
    // For a priority queue, we can't easily drop the "oldest" without popping the smallest PTS,
    // which is the next one to be played. Dropping the newly arrived one is easier.
    if (m_impl->queue.size() >= m_impl->maxCapacity) {
        return false; // Dropped
    }

    if (m_impl->isBuffering && m_impl->queue.empty()) {
        m_impl->bufferingStartTime = std::chrono::steady_clock::now();
    }

    m_impl->queue.push({std::move(frame), std::chrono::steady_clock::now()});
    return true;
}

std::optional<std::shared_ptr<core::MediaFrame>> JitterBuffer::Pop() {
    std::lock_guard lock(m_impl->mutex);

    if (m_impl->queue.empty()) {
        m_impl->isBuffering = true;
        return std::nullopt;
    }

    if (m_impl->isBuffering) {
        auto now = std::chrono::steady_clock::now();
        auto elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(
            now - m_impl->bufferingStartTime).count();
            
        if (elapsed >= m_impl->targetLatencyMs || m_impl->queue.size() >= m_impl->maxCapacity) {
            m_impl->isBuffering = false;
        } else {
            return std::nullopt;
        }
    }

    auto top = m_impl->queue.top();
    m_impl->queue.pop();
    
    // If the queue becomes empty, we'll enter buffering mode on the next Pop()
    // or we can do it now:
    if (m_impl->queue.empty()) {
        m_impl->isBuffering = true;
    }

    return top.frame;
}

void JitterBuffer::SetTargetLatency(uint32_t ms) {
    std::lock_guard lock(m_impl->mutex);
    m_impl->targetLatencyMs = ms;
}

void JitterBuffer::SetMaxCapacity(uint32_t capacity) {
    std::lock_guard lock(m_impl->mutex);
    m_impl->maxCapacity = capacity;
}

void JitterBuffer::Clear() {
    std::lock_guard lock(m_impl->mutex);
    while (!m_impl->queue.empty()) {
        m_impl->queue.pop();
    }
    m_impl->isBuffering = true;
}

uint32_t JitterBuffer::Size() const {
    std::lock_guard lock(m_impl->mutex);
    return static_cast<uint32_t>(m_impl->queue.size());
}

} // namespace openmedia::io
