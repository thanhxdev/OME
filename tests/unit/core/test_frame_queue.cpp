/// @file test_frame_queue.cpp
/// @brief Unit tests for FrameQueue

#include <openmedia/core/FrameQueue.h>
#include <openmedia/core/MediaFrame.h>
#include <gtest/gtest.h>

#include <thread>

using namespace openmedia::core;

TEST(FrameQueueTest, CreateQueue) {
    FrameQueue queue(8);
    EXPECT_TRUE(queue.IsEmpty());
    EXPECT_FALSE(queue.IsFull());
    EXPECT_EQ(queue.Size(), 0u);
    EXPECT_EQ(queue.Capacity(), 8u);
}

TEST(FrameQueueTest, PushAndPop) {
    FrameQueue queue(4);

    auto frame = MediaFrame::CreateVideo(1920, 1080, PixelFormat::NV12);
    frame->SetPts(1000);

    EXPECT_TRUE(queue.Push(frame));
    EXPECT_EQ(queue.Size(), 1u);
    EXPECT_FALSE(queue.IsEmpty());

    auto popped = queue.Pop();
    ASSERT_TRUE(popped.has_value());
    EXPECT_EQ(popped.value()->GetPts(), 1000);
    EXPECT_TRUE(queue.IsEmpty());
}

TEST(FrameQueueTest, QueueFull) {
    FrameQueue queue(2);

    auto f1 = MediaFrame::CreateVideo(320, 240, PixelFormat::BGRA);
    auto f2 = MediaFrame::CreateVideo(320, 240, PixelFormat::BGRA);
    auto f3 = MediaFrame::CreateVideo(320, 240, PixelFormat::BGRA);

    EXPECT_TRUE(queue.Push(f1));
    EXPECT_TRUE(queue.Push(f2));
    EXPECT_FALSE(queue.Push(f3));  // Full
    EXPECT_TRUE(queue.IsFull());
}

TEST(FrameQueueTest, PushWithDrop) {
    FrameQueue queue(2);

    auto f1 = MediaFrame::CreateVideo(320, 240, PixelFormat::BGRA);
    f1->SetPts(1);
    auto f2 = MediaFrame::CreateVideo(320, 240, PixelFormat::BGRA);
    f2->SetPts(2);
    auto f3 = MediaFrame::CreateVideo(320, 240, PixelFormat::BGRA);
    f3->SetPts(3);

    queue.Push(f1);
    queue.Push(f2);
    EXPECT_FALSE(queue.PushWithDrop(f3));  // Drops f1

    auto stats = queue.GetStats();
    EXPECT_EQ(stats.totalDropped, 1u);

    auto popped = queue.Pop();
    EXPECT_EQ(popped.value()->GetPts(), 2);  // f1 was dropped
}

TEST(FrameQueueTest, PopWithTimeout) {
    FrameQueue queue(4);

    // Pop from empty queue with timeout
    auto result = queue.Pop(std::chrono::milliseconds(50));
    EXPECT_FALSE(result.has_value());
}

TEST(FrameQueueTest, Clear) {
    FrameQueue queue(4);
    queue.Push(MediaFrame::CreateVideo(320, 240, PixelFormat::BGRA));
    queue.Push(MediaFrame::CreateVideo(320, 240, PixelFormat::BGRA));
    EXPECT_EQ(queue.Size(), 2u);

    queue.Clear();
    EXPECT_EQ(queue.Size(), 0u);
    EXPECT_TRUE(queue.IsEmpty());
}

TEST(FrameQueueTest, Statistics) {
    FrameQueue queue(4);

    auto f1 = MediaFrame::CreateVideo(320, 240, PixelFormat::BGRA);
    queue.Push(f1);
    queue.Pop();

    auto stats = queue.GetStats();
    EXPECT_EQ(stats.totalPushed, 1u);
    EXPECT_EQ(stats.totalPopped, 1u);
    EXPECT_EQ(stats.totalDropped, 0u);

    queue.ResetStats();
    stats = queue.GetStats();
    EXPECT_EQ(stats.totalPushed, 0u);
}

TEST(FrameQueueTest, ProducerConsumerThreaded) {
    FrameQueue queue(16);
    constexpr int numFrames = 100;

    std::thread producer([&] {
        for (int i = 0; i < numFrames; ++i) {
            auto frame = MediaFrame::CreateVideo(320, 240, PixelFormat::BGRA);
            frame->SetPts(i);
            while (!queue.Push(frame)) {
                std::this_thread::sleep_for(std::chrono::microseconds(100));
            }
        }
    });

    int received = 0;
    std::thread consumer([&] {
        while (received < numFrames) {
            auto frame = queue.Pop(std::chrono::milliseconds(100));
            if (frame.has_value()) {
                received++;
            }
        }
    });

    producer.join();
    consumer.join();
    EXPECT_EQ(received, numFrames);
}
