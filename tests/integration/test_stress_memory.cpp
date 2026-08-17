#include <gtest/gtest.h>
#include <openmedia/core/Engine.h>
#include <openmedia/core/MediaPipeline.h>
#include <openmedia/core/MediaFrame.h>
#include <openmedia/core/MemoryPool.h>
#include <memory>
#include <thread>
#include <chrono>

using namespace openmedia::core;

TEST(StressMemoryTest, AllocateFramesAndPipelines) {
    auto engine = Engine::Create();
    ASSERT_TRUE(engine != nullptr);

    constexpr int NUM_ITERATIONS = 50;
    constexpr int FRAMES_PER_ITER = 10;

    for (int i = 0; i < NUM_ITERATIONS; ++i) {
        auto pipeline = engine->CreatePipeline();
        
        for (int j = 0; j < FRAMES_PER_ITER; ++j) {
            auto frame = MediaFrame::CreateVideo(1920, 1080, PixelFormat::NV12);
            ASSERT_TRUE(frame != nullptr);
            // Simulate some frame data
            frame->SetPts(j * 33333); 
            // frame will be destroyed when out of scope
        }
        
        // Pipeline destroyed when out of scope
    }
}
