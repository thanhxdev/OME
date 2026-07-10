/// @file test_clock_sync.cpp
/// @brief Unit tests for ClockSync

#include <openmedia/core/ClockSync.h>
#include <gtest/gtest.h>

#include <thread>

using namespace openmedia::core;

TEST(ClockSyncTest, InitialState) {
    ClockSync clock;
    EXPECT_FALSE(clock.IsRunning());
    EXPECT_DOUBLE_EQ(clock.GetCurrentTimeSeconds(), 0.0);
}

TEST(ClockSyncTest, StartAndElapsed) {
    ClockSync clock;
    clock.Start();
    EXPECT_TRUE(clock.IsRunning());

    std::this_thread::sleep_for(std::chrono::milliseconds(50));

    auto elapsed = clock.GetElapsed();
    EXPECT_GT(elapsed.count(), 40'000);  // At least 40ms
    EXPECT_LT(elapsed.count(), 200'000); // Less than 200ms

    clock.Stop();
    EXPECT_FALSE(clock.IsRunning());
}

TEST(ClockSyncTest, PauseResume) {
    ClockSync clock;
    clock.Start();

    std::this_thread::sleep_for(std::chrono::milliseconds(50));

    clock.Pause();
    auto pausedElapsed = clock.GetElapsed();

    std::this_thread::sleep_for(std::chrono::milliseconds(100));

    // Elapsed should not change while paused
    auto stillPausedElapsed = clock.GetElapsed();
    EXPECT_EQ(pausedElapsed.count(), stillPausedElapsed.count());

    clock.Resume();
    std::this_thread::sleep_for(std::chrono::milliseconds(50));

    auto resumedElapsed = clock.GetElapsed();
    EXPECT_GT(resumedElapsed.count(), pausedElapsed.count());

    clock.Stop();
}

TEST(ClockSyncTest, SpeedControl) {
    ClockSync clock;
    clock.SetSpeed(2.0);
    EXPECT_DOUBLE_EQ(clock.GetSpeed(), 2.0);

    clock.Start();
    std::this_thread::sleep_for(std::chrono::milliseconds(50));

    auto elapsed = clock.GetElapsed();
    // At 2x speed, elapsed should be roughly double wall time
    EXPECT_GT(elapsed.count(), 80'000);

    clock.Stop();
}

TEST(ClockSyncTest, PtsConversion) {
    ClockSync clock;
    clock.SetTimeBase({1, 90000});  // 90kHz timebase

    auto wallTime = clock.PtsToWallTime(90000);
    // 90000 ticks at 90kHz = 1 second
    EXPECT_NEAR(wallTime.count(), 1'000'000, 100);  // ~1 second in microseconds
}

TEST(ClockSyncTest, Reset) {
    ClockSync clock;
    clock.Start();
    std::this_thread::sleep_for(std::chrono::milliseconds(50));

    clock.Reset();
    EXPECT_FALSE(clock.IsRunning());
    EXPECT_DOUBLE_EQ(clock.GetCurrentTimeSeconds(), 0.0);
}
