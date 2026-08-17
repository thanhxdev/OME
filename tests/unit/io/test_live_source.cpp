#include <gtest/gtest.h>
#include <openmedia/io/JitterBuffer.h>
#include <openmedia/io/LiveSource.h>
#include <thread>
#include <chrono>

using namespace openmedia::io;
using namespace openmedia::core;

TEST(JitterBufferTest, PushAndPopWithReordering) {
    JitterBuffer buffer(100, 10); // 100ms latency, max 10 frames

    auto frame1 = MediaFrame::CreateVideo(1920, 1080, PixelFormat::NV12);
    frame1->SetPts(2000);

    auto frame2 = MediaFrame::CreateVideo(1920, 1080, PixelFormat::NV12);
    frame2->SetPts(1000); // Earlier PTS, arrived later

    EXPECT_TRUE(buffer.Push(frame1));
    EXPECT_TRUE(buffer.Push(frame2));

    EXPECT_EQ(buffer.Size(), 2);

    // Immediate pop should fail because of buffering mode
    auto popped = buffer.Pop();
    EXPECT_FALSE(popped.has_value());

    // Wait for target latency (100ms)
    std::this_thread::sleep_for(std::chrono::milliseconds(110));

    // Pop should succeed now
    popped = buffer.Pop();
    ASSERT_TRUE(popped.has_value());
    // Should get frame2 because it has lower PTS
    EXPECT_EQ(popped.value()->GetPts(), 1000);

    popped = buffer.Pop();
    ASSERT_TRUE(popped.has_value());
    EXPECT_EQ(popped.value()->GetPts(), 2000);

    // Queue should be empty now
    EXPECT_EQ(buffer.Size(), 0);
    popped = buffer.Pop();
    EXPECT_FALSE(popped.has_value());
}

TEST(JitterBufferTest, PushBeyondCapacity) {
    JitterBuffer buffer(1000, 5); // 1s latency, max 5 frames

    for (int i = 0; i < 6; ++i) {
        auto frame = MediaFrame::CreateVideo(1920, 1080, PixelFormat::NV12);
        frame->SetPts(i * 1000);
        bool success = buffer.Push(frame);
        if (i < 5) {
            EXPECT_TRUE(success);
        } else {
            EXPECT_FALSE(success); // 6th frame should be dropped
        }
    }

    EXPECT_EQ(buffer.Size(), 5);

    // Pop should succeed immediately because capacity is reached (triggering early release)
    auto popped = buffer.Pop();
    ASSERT_TRUE(popped.has_value());
    EXPECT_EQ(popped.value()->GetPts(), 0);
}

TEST(LiveSourceTest, StartStopBackgroundThread) {
    LiveSource source;
    // We do not open a URL because we don't have a live server in unit tests,
    // but Start() should safely spin up the background thread and reconnect loop.
    
    EXPECT_EQ(source.GetState(), PipelineState::Stopped);
    
    source.Start();
    EXPECT_EQ(source.GetState(), PipelineState::Running);
    
    // Give thread some time to spin
    std::this_thread::sleep_for(std::chrono::milliseconds(50));
    
    // Test PullFrame when there is no data
    auto frameRes = source.PullFrame();
    EXPECT_FALSE(frameRes.has_value());
    EXPECT_EQ(frameRes.error().code, ErrorCode::WouldBlock);
    
    source.Stop();
    EXPECT_EQ(source.GetState(), PipelineState::Stopped);
}
