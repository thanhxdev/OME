/// @file ClockSync.cpp
/// @brief Media clock and PTS synchronization implementation

#include <openmedia/core/ClockSync.h>

#include <atomic>
#include <chrono>

namespace openmedia::core {

struct ClockSync::Impl {
    using clock = std::chrono::high_resolution_clock;
    using time_point = clock::time_point;

    std::atomic<bool> running{false};
    std::atomic<bool> paused{false};
    time_point startTime;
    time_point pauseTime;
    std::chrono::microseconds pausedDuration{0};
    Rational timeBase{1, 90000};
    std::atomic<double> speed{1.0};
};

ClockSync::ClockSync() : m_impl(std::make_unique<Impl>()) {}
ClockSync::~ClockSync() = default;

void ClockSync::Start() {
    m_impl->running.store(true, std::memory_order_release);
    m_impl->paused.store(false, std::memory_order_release);
    m_impl->startTime = Impl::clock::now();
    m_impl->pausedDuration = std::chrono::microseconds(0);
}

void ClockSync::Stop() {
    m_impl->running.store(false, std::memory_order_release);
    m_impl->paused.store(false, std::memory_order_release);
}

void ClockSync::Pause() {
    if (m_impl->running.load() && !m_impl->paused.load()) {
        m_impl->paused.store(true, std::memory_order_release);
        m_impl->pauseTime = Impl::clock::now();
    }
}

void ClockSync::Resume() {
    if (m_impl->paused.load()) {
        auto pauseDuration = std::chrono::duration_cast<std::chrono::microseconds>(
            Impl::clock::now() - m_impl->pauseTime);
        m_impl->pausedDuration += pauseDuration;
        m_impl->paused.store(false, std::memory_order_release);
    }
}

void ClockSync::Reset() {
    m_impl->running.store(false, std::memory_order_release);
    m_impl->paused.store(false, std::memory_order_release);
    m_impl->pausedDuration = std::chrono::microseconds(0);
}

void ClockSync::SetTimeBase(Rational timeBase) {
    m_impl->timeBase = timeBase;
}

std::chrono::microseconds ClockSync::GetElapsed() const {
    if (!m_impl->running.load()) return std::chrono::microseconds(0);

    auto now = m_impl->paused.load() ? m_impl->pauseTime : Impl::clock::now();
    auto elapsed = std::chrono::duration_cast<std::chrono::microseconds>(
        now - m_impl->startTime) - m_impl->pausedDuration;

    double speed = m_impl->speed.load(std::memory_order_relaxed);
    return std::chrono::microseconds(
        static_cast<int64_t>(elapsed.count() * speed));
}

int64_t ClockSync::GetCurrentPts() const {
    auto elapsed = GetElapsed();
    double seconds = elapsed.count() / 1'000'000.0;
    double ptsRate = static_cast<double>(m_impl->timeBase.den) / m_impl->timeBase.num;
    return static_cast<int64_t>(seconds * ptsRate);
}

double ClockSync::GetCurrentTimeSeconds() const {
    return GetElapsed().count() / 1'000'000.0;
}

std::chrono::microseconds ClockSync::PtsToWallTime(int64_t pts) const {
    double ptsRate = static_cast<double>(m_impl->timeBase.den) / m_impl->timeBase.num;
    double seconds = pts / ptsRate;
    return std::chrono::microseconds(static_cast<int64_t>(seconds * 1'000'000));
}

std::chrono::microseconds ClockSync::TimeUntilPts(int64_t pts) const {
    auto target = PtsToWallTime(pts);
    auto current = GetElapsed();
    if (target <= current) return std::chrono::microseconds(0);
    return target - current;
}

bool ClockSync::IsRunning() const {
    return m_impl->running.load(std::memory_order_acquire);
}

void ClockSync::SetSpeed(double speed) {
    m_impl->speed.store(speed, std::memory_order_relaxed);
}

double ClockSync::GetSpeed() const {
    return m_impl->speed.load(std::memory_order_relaxed);
}

} // namespace openmedia::core
