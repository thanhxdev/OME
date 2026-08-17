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
