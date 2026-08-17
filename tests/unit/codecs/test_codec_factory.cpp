#include <gtest/gtest.h>
#include <openmedia/codecs/CodecFactory.h>

using namespace openmedia::codecs;

TEST(CodecFactoryTest, CreateEncoders) {
    auto h264Enc = CodecFactory::CreateEncoder(VideoCodec::H264);
    EXPECT_NE(h264Enc, nullptr);
    EXPECT_EQ(h264Enc->GetName(), "FFmpegH264Encoder");

    auto h265Enc = CodecFactory::CreateEncoder(VideoCodec::H265);
    EXPECT_NE(h265Enc, nullptr);
    EXPECT_EQ(h265Enc->GetName(), "FFmpegH265Encoder");

    auto av1Enc = CodecFactory::CreateEncoder(VideoCodec::AV1);
    EXPECT_NE(av1Enc, nullptr);
    EXPECT_EQ(av1Enc->GetName(), "FFmpegAV1Encoder");

    auto unsupportedVideoEnc = CodecFactory::CreateEncoder(VideoCodec::VP8);
    EXPECT_EQ(unsupportedVideoEnc, nullptr);
}

TEST(CodecFactoryTest, CreateDecoders) {
    auto h264Dec = CodecFactory::CreateDecoder(VideoCodec::H264);
    EXPECT_NE(h264Dec, nullptr);
    EXPECT_EQ(h264Dec->GetName(), "FFmpegH264Decoder");

    auto h265Dec = CodecFactory::CreateDecoder(VideoCodec::H265);
    EXPECT_NE(h265Dec, nullptr);
    EXPECT_EQ(h265Dec->GetName(), "FFmpegH265Decoder");

    auto unsupportedVideoDec = CodecFactory::CreateDecoder(VideoCodec::AV1);
    EXPECT_EQ(unsupportedVideoDec, nullptr);
}

TEST(CodecFactoryTest, CreateAudioEncoders) {
    auto aacEnc = CodecFactory::CreateAudioEncoder(AudioCodec::AAC);
    EXPECT_NE(aacEnc, nullptr);
    EXPECT_EQ(aacEnc->GetName(), "FFmpegAACEncoder");

    auto opusEnc = CodecFactory::CreateAudioEncoder(AudioCodec::Opus);
    EXPECT_NE(opusEnc, nullptr);
    EXPECT_EQ(opusEnc->GetName(), "FFmpegOpusEncoder");

    auto unsupportedAudioEnc = CodecFactory::CreateAudioEncoder(AudioCodec::MP3);
    EXPECT_EQ(unsupportedAudioEnc, nullptr);
}
