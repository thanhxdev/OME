/// @file test_gpu_scale.cpp
/// @brief 4K to 1080p GPU scaling performance benchmark and aspect ratio verification.

#include <gtest/gtest.h>
#include <openmedia/mixer/ScaleFilter.h>
#include <openmedia/core/MediaFrame.h>
#include <chrono>

using namespace openmedia;
using namespace openmedia::mixer;

class GPUScaleBenchmarkTest : public ::testing::Test {
protected:
    void SetUp() override {
        m_scaleFilter = std::make_unique<ScaleFilter>();
    }

    std::unique_ptr<ScaleFilter> m_scaleFilter;
};

TEST_F(GPUScaleBenchmarkTest, Scale4KTo1080pPerformance) {
    // 4K UHD Video frame (3840x2160 BGRA)
    auto frame4K = core::MediaFrame::CreateVideo(3840, 2160, core::PixelFormat::BGRA);
    ASSERT_NE(frame4K, nullptr);

    m_scaleFilter->SetOutputSize(1920, 1080);
    m_scaleFilter->SetKeepAspectRatio(true);

    // Warm-up run
    auto warmupResult = m_scaleFilter->Process(frame4K);
    ASSERT_TRUE(warmupResult.has_value());
    EXPECT_EQ(warmupResult.value()->GetWidth(), 1920);
    EXPECT_EQ(warmupResult.value()->GetHeight(), 1080);

    // Benchmark 10 iterations
    constexpr int iterations = 10;
    auto startTime = std::chrono::high_resolution_clock::now();

    for (int i = 0; i < iterations; ++i) {
        auto result = m_scaleFilter->Process(frame4K);
        ASSERT_TRUE(result.has_value());
    }

    auto endTime = std::chrono::high_resolution_clock::now();
    double totalMs = std::chrono::duration<double, std::milli>(endTime - startTime).count();
    double avgLatencyMs = totalMs / iterations;

    // Verify processing latency remains bounded
    EXPECT_GT(avgLatencyMs, 0.0);
    EXPECT_LT(avgLatencyMs, 100.0); // Reasonable bound even on CPU fallback in debug builds
}

TEST_F(GPUScaleBenchmarkTest, AspectRatioLetterboxing) {
    // Ultrawide 21:9 input frame (2560x1080) scaled into 16:9 output (1920x1080)
    auto ultrawideFrame = core::MediaFrame::CreateVideo(2560, 1080, core::PixelFormat::BGRA);
    ASSERT_NE(ultrawideFrame, nullptr);

    m_scaleFilter->SetOutputSize(1920, 1080);
    m_scaleFilter->SetKeepAspectRatio(true);

    auto result = m_scaleFilter->Process(ultrawideFrame);
    ASSERT_TRUE(result.has_value());
    EXPECT_EQ(result.value()->GetWidth(), 1920);
    EXPECT_EQ(result.value()->GetHeight(), 1080);
}
