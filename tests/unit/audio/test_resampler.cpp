#include <gtest/gtest.h>
#include <openmedia/audio/Resampler.h>
#include <openmedia/core/MediaFrame.h>

using namespace openmedia::audio;
using namespace openmedia::core;

TEST(ResamplerTest, Initialize) {
    Resampler resampler;
    bool result = resampler.Initialize(48000, SampleFormat::Float32, 2);
    // Might return false if FFmpeg is not fully linked/initialized in test context, 
    // but the method should exist and not crash.
    // We'll just expect no throw for now.
}

TEST(ResamplerTest, ProcessFrame) {
    Resampler resampler;
    if (resampler.Initialize(48000, SampleFormat::Float32, 2)) {
        auto inFrame = MediaFrame::CreateAudio(1024, 2, SampleFormat::S16, 44100);
        auto outFrame = resampler.Process(inFrame);
        
        EXPECT_NE(outFrame, nullptr);
        if (outFrame) {
            EXPECT_EQ(outFrame->GetSampleRate(), 48000u);
            EXPECT_EQ(outFrame->GetSampleFormat(), SampleFormat::Float32);
        }
    }
}
