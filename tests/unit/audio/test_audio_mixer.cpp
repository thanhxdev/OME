#include <gtest/gtest.h>
#include <openmedia/audio/AudioMixer.h>
#include <openmedia/core/MediaFrame.h>

using namespace openmedia::audio;
using namespace openmedia::core;

TEST(AudioMixerTest, Initialize) {
    AudioMixer mixer(nullptr);
    EXPECT_TRUE(mixer.Initialize().has_value());
}

TEST(AudioMixerTest, AddRemoveInput) {
    AudioMixer mixer(nullptr);
    auto result = mixer.Initialize();
    EXPECT_TRUE(result.has_value());

    mixer.AddInput(1);
    mixer.AddInput(2);

    mixer.RemoveInput(1);
    mixer.RemoveInput(1); // Already removed, shouldn't crash
}

TEST(AudioMixerTest, MixSilence) {
    AudioMixer mixer(nullptr);
    auto resultInit = mixer.Initialize();
    EXPECT_TRUE(resultInit.has_value());
    auto resultStart = mixer.Start();
    EXPECT_TRUE(resultStart.has_value());

    mixer.AddInput(1);
    
    // Create an empty (silent) frame
    auto frame = MediaFrame::CreateAudio(1024, 2, SampleFormat::Float32, 48000);
    
    EXPECT_TRUE(mixer.PushFrame(frame, 1).has_value());
}

TEST(AudioMixerTest, Mix3Inputs) {
    AudioMixer mixer(nullptr);
    auto resultInit = mixer.Initialize();
    EXPECT_TRUE(resultInit.has_value());
    auto resultStart = mixer.Start();
    EXPECT_TRUE(resultStart.has_value());

    int input1 = 1;
    int input2 = 2;
    int input3 = 3;

    mixer.AddInput(input1);
    mixer.AddInput(input2);
    mixer.AddInput(input3);

    mixer.SetInputVolume(input1, 0.8f);
    mixer.SetInputPan(input2, -1.0f); // Pan left
    mixer.SetInputSolo(input3, true); // Solo input 3

    auto frame1 = MediaFrame::CreateAudio(1024, 2, SampleFormat::Float32, 48000);
    auto frame2 = MediaFrame::CreateAudio(1024, 2, SampleFormat::Float32, 48000);
    auto frame3 = MediaFrame::CreateAudio(1024, 2, SampleFormat::Float32, 48000);

    EXPECT_TRUE(mixer.PushFrame(frame1, input1).has_value());
    EXPECT_TRUE(mixer.PushFrame(frame2, input2).has_value());
    EXPECT_TRUE(mixer.PushFrame(frame3, input3).has_value());
}
