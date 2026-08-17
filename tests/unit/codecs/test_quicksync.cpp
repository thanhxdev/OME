#include <gtest/gtest.h>
#include <openmedia/codecs/FFmpegQSVEncoder.h>
#include <openmedia/codecs/FFmpegQSVDecoder.h>
#include <openmedia/core/MediaFrame.h>

using namespace openmedia::codecs;
using namespace openmedia::core;

TEST(QuickSyncTest, EncoderInitialization) {
    FFmpegQSVEncoder encoder(false); // H264
    EXPECT_EQ(encoder.GetName(), "FFmpegQSVEncoder(H264)");
    EXPECT_EQ(encoder.GetState(), PipelineState::Stopped);

    auto result = encoder.Initialize();
    
    // It's possible that the current machine doesn't have Intel QSV hardware.
    // If Initialize fails due to CodecOpenFailed or CodecNotFound, we skip the test rather than fail it.
    if (!result.has_value()) {
        auto err = result.error();
        if (err.code == ErrorCode::CodecNotFound || err.code == ErrorCode::CodecOpenFailed) {
            GTEST_SKIP() << "Intel QSV hardware/driver not available on this system.";
        }
    }
    
    ASSERT_TRUE(result.has_value());
    
    EXPECT_TRUE(encoder.Start().has_value());
    EXPECT_EQ(encoder.GetState(), PipelineState::Running);
    
    EXPECT_TRUE(encoder.Stop().has_value());
}

TEST(QuickSyncTest, DecoderInitialization) {
    FFmpegQSVDecoder decoder(true); // HEVC
    EXPECT_EQ(decoder.GetName(), "FFmpegQSVDecoder(HEVC)");
    EXPECT_EQ(decoder.GetState(), PipelineState::Stopped);

    auto result = decoder.Initialize();
    
    if (!result.has_value()) {
        auto err = result.error();
        if (err.code == ErrorCode::CodecNotFound || err.code == ErrorCode::CodecOpenFailed) {
            GTEST_SKIP() << "Intel QSV hardware/driver not available on this system.";
        }
    }
    
    ASSERT_TRUE(result.has_value());
    
    EXPECT_TRUE(decoder.Start().has_value());
    EXPECT_EQ(decoder.GetState(), PipelineState::Running);
    
    EXPECT_TRUE(decoder.Stop().has_value());
}
