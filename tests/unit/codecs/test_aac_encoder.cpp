#include <gtest/gtest.h>
#include <openmedia/codecs/CodecFactory.h>
#include <openmedia/core/MediaFrame.h>
#include <cmath>

using namespace openmedia::core;
using namespace openmedia::codecs;

TEST(AACEncoderTest, InitializeAndEncode) {
    auto encoder = CodecFactory::CreateAudioEncoder(AudioCodec::AAC);
    ASSERT_TRUE(encoder != nullptr);
    
    // Configure AAC: 48kHz, stereo, 128kbps
    openmedia::codecs::EncoderConfig aacConfig;
    aacConfig.type = openmedia::codecs::CodecType::Audio;
    aacConfig.sampleRate = 48000;
    aacConfig.channels = 2;
    aacConfig.bitrate = 128000;
    ASSERT_TRUE(encoder->Configure(aacConfig).has_value());
    ASSERT_TRUE(encoder->Initialize().has_value());
    ASSERT_TRUE(encoder->Start().has_value());

    // Create a dummy audio frame (float planar, 1024 samples)
    int samples = 1024;
    auto frame = MediaFrame::CreateAudio(samples, 2, SampleFormat::Float32P, 48000);
    frame->SetPts(0);
    
    // Fill with sine wave
    float* ch0 = reinterpret_cast<float*>(frame->GetAudioData(0));
    float* ch1 = reinterpret_cast<float*>(frame->GetAudioData(1));
    for (int i = 0; i < samples; ++i) {
        ch0[i] = static_cast<float>(std::sin(2.0 * 3.14159 * 440.0 * i / 48000.0));
        ch1[i] = ch0[i]; // same for right channel
    }

    auto pushRes = encoder->PushFrame(frame);
    ASSERT_TRUE(pushRes.has_value());

    // Flush
    (void)encoder->PushFrame(nullptr);

    bool gotPacket = false;
    while (true) {
        auto pullRes = encoder->PullFrame();
        if (!pullRes || !pullRes.value()) break;
        gotPacket = true;
        
        auto pkt = pullRes.value();
        ASSERT_GT(pkt->GetPacketSize(), 0);
    }

    ASSERT_TRUE(gotPacket);
    
    (void)encoder->Stop();
}

TEST(OpusEncoderTest, InitializeAndEncode) {
    auto encoder = CodecFactory::CreateAudioEncoder(AudioCodec::Opus);
    ASSERT_TRUE(encoder != nullptr);
    
    // Configure Opus: 48kHz, stereo, 128kbps
    openmedia::codecs::EncoderConfig opusConfig;
    opusConfig.type = openmedia::codecs::CodecType::Audio;
    opusConfig.sampleRate = 48000;
    opusConfig.channels = 2;
    opusConfig.bitrate = 128000;
    ASSERT_TRUE(encoder->Configure(opusConfig).has_value());
    ASSERT_TRUE(encoder->Initialize().has_value());
    ASSERT_TRUE(encoder->Start().has_value());

    int samples = 960; // 20ms at 48kHz
    auto frame = MediaFrame::CreateAudio(samples, 2, SampleFormat::Float32, 48000); // FFmpegOpusEncoder uses FLT
    frame->SetPts(0);
    
    float* audioData = reinterpret_cast<float*>(frame->GetAudioData(0)); // Packed format for FLT, or if planar then 0 and 1
    // Actually, we should just push it
    
    auto pushRes = encoder->PushFrame(frame);
    ASSERT_TRUE(pushRes.has_value());

    // Flush
    (void)encoder->PushFrame(nullptr);

    bool gotPacket = false;
    while (true) {
        auto pullRes = encoder->PullFrame();
        if (!pullRes || !pullRes.value()) break;
        gotPacket = true;
        
        auto pkt = pullRes.value();
        ASSERT_GT(pkt->GetPacketSize(), 0);
    }

    ASSERT_TRUE(gotPacket);
    
    (void)encoder->Stop();
}
