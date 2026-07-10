/// @file test_media_frame.cpp
/// @brief Unit tests for MediaFrame

#include <openmedia/core/MediaFrame.h>
#include <gtest/gtest.h>

using namespace openmedia::core;

TEST(MediaFrameTest, CreateVideoFrame) {
    auto frame = MediaFrame::CreateVideo(1920, 1080, PixelFormat::NV12);
    ASSERT_NE(frame, nullptr);
    EXPECT_EQ(frame->GetWidth(), 1920u);
    EXPECT_EQ(frame->GetHeight(), 1080u);
    EXPECT_EQ(frame->GetPixelFormat(), PixelFormat::NV12);
    EXPECT_EQ(frame->GetMediaType(), MediaType::Video);
    EXPECT_TRUE(frame->IsValid());
    EXPECT_EQ(frame->GetVideoPlaneCount(), 2u);
}

TEST(MediaFrameTest, CreateBGRAFrame) {
    auto frame = MediaFrame::CreateVideo(640, 480, PixelFormat::BGRA);
    ASSERT_NE(frame, nullptr);
    EXPECT_EQ(frame->GetVideoPlaneCount(), 1u);
    EXPECT_EQ(frame->GetLineSize(0), 640 * 4);
    EXPECT_NE(frame->GetVideoPlane(0), nullptr);
    EXPECT_EQ(frame->GetTotalSize(), 640u * 480u * 4u);
}

TEST(MediaFrameTest, CreateAudioFrame) {
    auto frame = MediaFrame::CreateAudio(1024, 2, SampleFormat::Float32, 48000);
    ASSERT_NE(frame, nullptr);
    EXPECT_EQ(frame->GetSampleCount(), 1024u);
    EXPECT_EQ(frame->GetChannelCount(), 2u);
    EXPECT_EQ(frame->GetSampleRate(), 48000u);
    EXPECT_EQ(frame->GetSampleFormat(), SampleFormat::Float32);
    EXPECT_EQ(frame->GetMediaType(), MediaType::Audio);
    EXPECT_TRUE(frame->IsValid());
}

TEST(MediaFrameTest, TimingProperties) {
    auto frame = MediaFrame::CreateVideo(1920, 1080, PixelFormat::NV12);
    frame->SetPts(90000);
    frame->SetDts(90000);
    frame->SetDuration(3000);

    EXPECT_EQ(frame->GetPts(), 90000);
    EXPECT_EQ(frame->GetDts(), 90000);
    EXPECT_EQ(frame->GetDuration(), 3000);
}

TEST(MediaFrameTest, CloneFrame) {
    auto original = MediaFrame::CreateVideo(320, 240, PixelFormat::BGRA);
    original->SetPts(12345);

    // Write some data
    auto* plane = original->GetVideoPlane(0);
    ASSERT_NE(plane, nullptr);
    plane[0] = 0xFF;
    plane[1] = 0xAB;

    auto clone = original->Clone();
    ASSERT_NE(clone, nullptr);
    EXPECT_EQ(clone->GetWidth(), 320u);
    EXPECT_EQ(clone->GetPts(), 12345);

    auto* clonePlane = clone->GetVideoPlane(0);
    EXPECT_EQ(clonePlane[0], 0xFF);
    EXPECT_EQ(clonePlane[1], 0xAB);

    // Verify deep copy — modifying clone doesn't affect original
    clonePlane[0] = 0x00;
    EXPECT_EQ(plane[0], 0xFF);
}

TEST(MediaFrameTest, GPUHandle) {
    auto frame = MediaFrame::CreateVideo(1920, 1080, PixelFormat::NV12);
    EXPECT_FALSE(frame->IsGPUFrame());
    EXPECT_EQ(frame->GetGPUTextureHandle(), nullptr);

    int fakeHandle = 42;
    frame->SetGPUTextureHandle(&fakeHandle);
    EXPECT_TRUE(frame->IsGPUFrame());
}

TEST(MediaFrameTest, Metadata) {
    auto frame = MediaFrame::CreateVideo(1920, 1080, PixelFormat::NV12);
    auto& meta = frame->GetMetadata();

    meta.Set("codec", "h264");
    meta.SetInt("bitrate", 5000000);

    EXPECT_TRUE(meta.Has("codec"));
    EXPECT_EQ(meta.Get("codec").value(), "h264");
    EXPECT_EQ(meta.GetInt("bitrate").value(), 5000000);
}

TEST(MediaFrameTest, InvalidPlaneAccess) {
    auto frame = MediaFrame::CreateVideo(1920, 1080, PixelFormat::BGRA);
    // BGRA has only 1 plane
    EXPECT_EQ(frame->GetVideoPlane(1), nullptr);
    EXPECT_EQ(frame->GetLineSize(5), 0);
}
