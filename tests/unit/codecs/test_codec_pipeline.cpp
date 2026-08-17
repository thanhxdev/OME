#include <gtest/gtest.h>
#include <openmedia/codecs/CodecFactory.h>
#include <openmedia/core/MediaFrame.h>

using namespace openmedia::codecs;
using namespace openmedia::core;

TEST(CodecPipelineTest, EncodeDecodeFlow) {
    auto encoder = CodecFactory::CreateEncoder(VideoCodec::H264);
    auto decoder = CodecFactory::CreateDecoder(VideoCodec::H264);

    ASSERT_NE(encoder, nullptr);
    ASSERT_NE(decoder, nullptr);

    auto encoder_downstream = std::dynamic_pointer_cast<IMediaObject>(decoder);
    ASSERT_TRUE(encoder->Connect(encoder_downstream).has_value());

    if (!encoder->Initialize().has_value()) { GTEST_SKIP(); return; }
    if (!decoder->Initialize().has_value()) { GTEST_SKIP(); return; }

    ASSERT_TRUE(encoder->Start().has_value());
    ASSERT_TRUE(decoder->Start().has_value());

    // Push multiple frames to fill encoder/decoder buffers
    bool gotFrame = false;
    for (int i = 0; i < 100; ++i) {
        auto frame = MediaFrame::CreateVideo(1920, 1080, PixelFormat::NV12);
        frame->SetPts(i);
        auto pushResult = encoder->PushFrame(frame);
        ASSERT_TRUE(pushResult.has_value());

        auto pullResult = decoder->PullFrame();
        if (pullResult.has_value()) {
            gotFrame = true;
            auto decodedFrame = pullResult.value();
            EXPECT_NE(decodedFrame, nullptr);
            break;
        }
    }
    
    EXPECT_TRUE(gotFrame) << "Failed to get any decoded frame after pushing 100 frames.";

    (void)encoder->Stop();
    (void)decoder->Stop();
}

TEST(CodecPipelineTest, EncodeDecodeFlowH265) {
    auto encoder = CodecFactory::CreateEncoder(VideoCodec::H265);
    auto decoder = CodecFactory::CreateDecoder(VideoCodec::H265);

    ASSERT_NE(encoder, nullptr);
    ASSERT_NE(decoder, nullptr);

    auto encoder_downstream = std::dynamic_pointer_cast<IMediaObject>(decoder);
    ASSERT_TRUE(encoder->Connect(encoder_downstream).has_value());

    if (!encoder->Initialize().has_value()) { GTEST_SKIP(); return; }
    if (!decoder->Initialize().has_value()) { GTEST_SKIP(); return; }

    ASSERT_TRUE(encoder->Start().has_value());
    ASSERT_TRUE(decoder->Start().has_value());

    bool gotFrame = false;
    for (int i = 0; i < 100; ++i) {
        auto frame = MediaFrame::CreateVideo(1920, 1080, PixelFormat::NV12);
        frame->SetPts(i);
        auto pushResult = encoder->PushFrame(frame);
        ASSERT_TRUE(pushResult.has_value());

        auto pullResult = decoder->PullFrame();
        if (pullResult.has_value()) {
            gotFrame = true;
            break;
        }
    }
    
    EXPECT_TRUE(gotFrame) << "Failed to get any decoded frame after pushing 30 frames (H265).";

    (void)encoder->Stop();
    (void)decoder->Stop();
}

TEST(CodecPipelineTest, EncodeFlowAV1) {
    auto encoder = CodecFactory::CreateEncoder(VideoCodec::AV1);
    ASSERT_NE(encoder, nullptr);
    ASSERT_TRUE(encoder->Initialize().has_value());
    ASSERT_TRUE(encoder->Start().has_value());

    bool gotPacket = false;
    for (int i = 0; i < 30; ++i) {
        auto frame = MediaFrame::CreateVideo(1920, 1080, PixelFormat::NV12);
        frame->SetPts(i);
        ASSERT_TRUE(encoder->PushFrame(frame).has_value());

        auto pullResult = encoder->PullFrame();
        if (pullResult.has_value()) {
            gotPacket = true;
            break;
        }
    }
    
    EXPECT_TRUE(gotPacket) << "Failed to get any encoded packet (AV1).";
    (void)encoder->Stop();
}
