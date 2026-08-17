#include <openmedia/audio/AudioEngine.h>

namespace openmedia::audio {

AudioEngine::AudioEngine() {
}

AudioEngine::~AudioEngine() {
}

core::VoidResult AudioEngine::Initialize(int masterSampleRate, core::ChannelLayout masterLayout, core::SampleFormat masterFormat) {
    m_sampleRate = masterSampleRate;
    m_channelLayout = masterLayout;
    m_sampleFormat = masterFormat;
    return {};
}

} // namespace openmedia::audio
