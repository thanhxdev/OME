#pragma once
#include <openmedia/core/Types.h>
#include <openmedia/core/ErrorCodes.h>
#include <memory>
#include <string>

namespace openmedia::audio {

class AudioEngine {
public:
    AudioEngine();
    ~AudioEngine();

    core::VoidResult Initialize(int masterSampleRate, core::ChannelLayout masterLayout, core::SampleFormat masterFormat);
    
    int GetMasterSampleRate() const { return m_sampleRate; }
    core::ChannelLayout GetMasterChannelLayout() const { return m_channelLayout; }
    core::SampleFormat GetMasterSampleFormat() const { return m_sampleFormat; }

private:
    int m_sampleRate = 48000;
    core::ChannelLayout m_channelLayout = core::ChannelLayout::Stereo;
    core::SampleFormat m_sampleFormat = core::SampleFormat::Float32;
};

} // namespace openmedia::audio
