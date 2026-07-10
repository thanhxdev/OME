#pragma once

/// @file ClockSync.h
/// @brief Media clock and PTS synchronization
/// @since 1.0.0

#include <openmedia/core/Types.h>

#include <chrono>
#include <cstdint>
#include <memory>

namespace openmedia::core {

/// @brief Media clock for PTS tracking and wall clock synchronization
///
/// Maintains a mapping between media timestamps (PTS) and system wall clock.
/// Used for live streaming, A/V sync, and playback timing.
class ClockSync {
public:
    ClockSync();
    ~ClockSync();

    ClockSync(const ClockSync&) = delete;
    ClockSync& operator=(const ClockSync&) = delete;

    /// @brief Start the clock
    void Start();

    /// @brief Stop the clock
    void Stop();

    /// @brief Pause the clock
    void Pause();

    /// @brief Resume the clock
    void Resume();

    /// @brief Reset the clock to initial state
    void Reset();

    /// @brief Set the time base for PTS conversion
    void SetTimeBase(Rational timeBase);

    /// @brief Get current media time in the clock's time base
    [[nodiscard]] int64_t GetCurrentPts() const;

    /// @brief Get current media time in seconds
    [[nodiscard]] double GetCurrentTimeSeconds() const;

    /// @brief Get elapsed wall-clock time since start
    [[nodiscard]] std::chrono::microseconds GetElapsed() const;

    /// @brief Map a PTS value to wall-clock time
    [[nodiscard]] std::chrono::microseconds PtsToWallTime(int64_t pts) const;

    /// @brief Calculate wait time to reach a given PTS
    [[nodiscard]] std::chrono::microseconds TimeUntilPts(int64_t pts) const;

    /// @brief Check if clock is running
    [[nodiscard]] bool IsRunning() const;

    /// @brief Set playback speed (1.0 = normal)
    void SetSpeed(double speed);

    /// @brief Get current playback speed
    [[nodiscard]] double GetSpeed() const;

private:
    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

} // namespace openmedia::core
