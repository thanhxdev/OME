#include <gtest/gtest.h>
#include <openmedia/audio/AudioMeter.h>
#include <openmedia/core/MediaFrame.h>

using namespace openmedia::core;

TEST(AudioMeterTest, ProcessSilence) {
    OpenMedia::Audio::AudioMeter meter;
    
    auto frame = MediaFrame::CreateAudio(1024, 2, SampleFormat::Float32, 48000);
    // Frame is zeroed out by default
    
    meter.ProcessSamples(frame.get());
    auto stats = meter.GetChannelData();
    
    // Silence should be very low dBFS
    EXPECT_LE(stats[0].peak_db, -90.0f); 
    EXPECT_LE(stats[0].rms_db, -90.0f);
}

TEST(AudioMeterTest, LUFSIntegration) {
    OpenMedia::Audio::AudioMeter meter;
    auto frame = MediaFrame::CreateAudio(48000, 2, SampleFormat::Float32, 48000); // 1 second
    
    meter.ProcessSamples(frame.get());
    auto stats = meter.GetChannelData();
    
    // LUFS for silence is essentially -infinity (-120 or similar)
    EXPECT_LE(stats[0].lufs, -70.0f);
}

TEST(AudioMeterTest, MultiChannel16Channels) {
    OpenMedia::Audio::AudioMeter meter;
    const uint32_t numChannels = 16;
    const uint32_t numSamples = 480; // 10ms at 48kHz
    auto frame = MediaFrame::CreateAudio(numSamples, numChannels, SampleFormat::Float32, 48000);
    
    float* audioData = reinterpret_cast<float*>(frame->GetAudioData(0));
    // Fill Channel 3 with full scale sine
    for (uint32_t i = 0; i < numSamples; ++i) {
        audioData[i * numChannels + 3] = std::sin(2.0f * 3.14159265f * 1000.0f * (static_cast<float>(i) / 48000.0f));
    }
    
    meter.ProcessSamples(frame.get());
    auto stats = meter.GetChannelData();
    
    ASSERT_EQ(stats.size(), numChannels);
    // Channel 3 should have peak close to 0 dBFS and clipping flag
    EXPECT_NEAR(stats[3].peak_db, 0.0f, 0.5f);
    // Other channels should be silence
    EXPECT_LE(stats[0].peak_db, -90.0f);
    EXPECT_LE(stats[15].peak_db, -90.0f);
}

TEST(AudioMeterTest, S16FormatPeakCalculation) {
    OpenMedia::Audio::AudioMeter meter;
    const uint32_t numChannels = 2;
    const uint32_t numSamples = 480;
    
    // Buffer for S16: Left channel at -6dBFS (amplitude 16384), Right channel at -18dBFS (amplitude 4124)
    std::vector<int16_t> s16Buffer(numSamples * numChannels);
    for (uint32_t i = 0; i < numSamples; ++i) {
        s16Buffer[i * 2 + 0] = 16384; // ~ -6.02 dBFS
        s16Buffer[i * 2 + 1] = 4124;  // ~ -18.00 dBFS
    }
    
    const void* channelPtrs[2] = { s16Buffer.data(), s16Buffer.data() };
    auto res = meter.ProcessRaw(channelPtrs, numSamples, numChannels, SampleFormat::S16, 48000);
    EXPECT_TRUE(res.has_value());
    
    auto stats = meter.GetChannelData();
    ASSERT_EQ(stats.size(), 2);
    EXPECT_NEAR(stats[0].peak_db, -6.02f, 0.5f);
    EXPECT_NEAR(stats[1].peak_db, -18.00f, 0.5f);
}

