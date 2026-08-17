#include <gtest/gtest.h>
#include <openmedia/codecs/CodecFactory.h>
#include <openmedia/core/MediaFrame.h>

using namespace openmedia::codecs;
using namespace openmedia::core;

TEST(NVENCTest, EncoderInitialization) {
    auto encoder = CodecFactory::CreateEncoder(VideoCodec::H264_NVENC);
    ASSERT_NE(encoder, nullptr);

    openmedia::codecs::EncoderConfig config;
    config.width = 1920;
    config.height = 1080;
    config.bitrate = 5000000;
    config.fps = 30;
    encoder->Configure(config);
    
    auto result = encoder->Initialize();
    
    // If the system doesn't have an NVIDIA GPU or FFmpeg was built without NVENC support,
    // Initialize() might fail. We check the error code.
    if (!result.has_value()) {
        auto err = result.error();
        if (err.code == ErrorCode::CodecNotFound || err.code == ErrorCode::CodecOpenFailed) {
            GTEST_SKIP() << "NVENC not supported on this system or FFmpeg lacks NVENC support. Skipping test.";
        } else {
            FAIL() << "Unexpected error during NVENC initialization: " << err.message;
        }
    }
    
    EXPECT_EQ(encoder->GetState(), PipelineState::Stopped);
    EXPECT_TRUE(encoder->Start().has_value());
    EXPECT_EQ(encoder->GetState(), PipelineState::Running);
    EXPECT_TRUE(encoder->Stop().has_value());
}

TEST(NVDECTest, DecoderInitialization) {
    auto decoder = CodecFactory::CreateDecoder(VideoCodec::H264_NVENC);
    ASSERT_NE(decoder, nullptr);

    auto result = decoder->Initialize();
    
    if (!result.has_value()) {
        auto err = result.error();
        if (err.code == ErrorCode::CodecNotFound || err.code == ErrorCode::CodecOpenFailed) {
            GTEST_SKIP() << "NVDEC (cuvid) not supported on this system or FFmpeg lacks cuvid support. Skipping test.";
        } else {
            FAIL() << "Unexpected error during NVDEC initialization: " << err.message;
        }
    }
    
    EXPECT_EQ(decoder->GetState(), PipelineState::Stopped);
    EXPECT_TRUE(decoder->Start().has_value());
    EXPECT_EQ(decoder->GetState(), PipelineState::Running);
    EXPECT_TRUE(decoder->Stop().has_value());
}
