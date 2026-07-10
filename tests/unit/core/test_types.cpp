/// @file test_types.cpp
/// @brief Unit tests for core type definitions

#include <openmedia/core/Types.h>
#include <gtest/gtest.h>

using namespace openmedia::core;

TEST(TypesTest, PixelFormatToString) {
    EXPECT_EQ(PixelFormatToString(PixelFormat::NV12), "NV12");
    EXPECT_EQ(PixelFormatToString(PixelFormat::BGRA), "BGRA");
    EXPECT_EQ(PixelFormatToString(PixelFormat::YUV420P), "YUV420P");
    EXPECT_EQ(PixelFormatToString(PixelFormat::Unknown), "Unknown");
}

TEST(TypesTest, SampleFormatToString) {
    EXPECT_EQ(SampleFormatToString(SampleFormat::Float32), "Float32");
    EXPECT_EQ(SampleFormatToString(SampleFormat::S16), "S16");
    EXPECT_EQ(SampleFormatToString(SampleFormat::Unknown), "Unknown");
}

TEST(TypesTest, BytesPerPixel) {
    EXPECT_EQ(BytesPerPixel(PixelFormat::BGRA), 4u);
    EXPECT_EQ(BytesPerPixel(PixelFormat::RGBA), 4u);
    EXPECT_EQ(BytesPerPixel(PixelFormat::RGB24), 3u);
    EXPECT_EQ(BytesPerPixel(PixelFormat::GRAY8), 1u);
    EXPECT_EQ(BytesPerPixel(PixelFormat::NV12), 1u);
}

TEST(TypesTest, RationalToDouble) {
    Rational r{1, 30};
    EXPECT_NEAR(r.ToDouble(), 1.0 / 30.0, 0.0001);

    Rational zero{0, 1};
    EXPECT_DOUBLE_EQ(zero.ToDouble(), 0.0);

    Rational divByZero{1, 0};
    EXPECT_DOUBLE_EQ(divByZero.ToDouble(), 0.0);
}

TEST(TypesTest, PipelineStateValues) {
    EXPECT_EQ(static_cast<uint32_t>(PipelineState::Idle), 0u);
    EXPECT_NE(PipelineState::Running, PipelineState::Idle);
}
