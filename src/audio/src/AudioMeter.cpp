#include <openmedia/audio/AudioMeter.h>
#include <cmath>
#include <algorithm>

namespace OpenMedia {
namespace Audio {

AudioMeter::AudioMeter() {
}

AudioMeter::~AudioMeter() {
}

openmedia::core::Result<void> AudioMeter::ProcessSamples(const openmedia::core::MediaFrame* frame) {
    if (!frame) return std::unexpected(openmedia::core::Error::Make(openmedia::core::ErrorCode::InvalidArgument, "Invalid frame pointer"));

    int numChannels = frame->GetChannelCount();
    int numSamples = frame->GetSampleCount();
    const float* audioData = reinterpret_cast<const float*>(frame->GetAudioData());
    
    if (m_channelData.size() != numChannels) {
        m_channelData.resize(numChannels);
        m_channelStates.resize(numChannels);
    }
    
    // Assumes 48kHz for EBU R128 standard coefficients
    const float sampleRate = 48000.0f;
    const float alpha_vu = 1.0f - std::exp(-1.0f / (sampleRate * 0.300f)); // 300ms window
    const float alpha_lufs = 1.0f - std::exp(-1.0f / (sampleRate * 0.400f)); // 400ms window

    // K-weighting coefficients for 48kHz
    const float b0_1 = 1.53512485958697f, b1_1 = -2.69169618940638f, b2_1 = 1.19839281085285f;
    const float a1_1 = -1.69065929318241f, a2_1 = 0.73248077421585f;
    
    const float b0_2 = 1.0f, b1_2 = -2.0f, b2_2 = 1.0f;
    const float a1_2 = -1.99004745483398f, a2_2 = 0.99007225036621f;
    
    for (int ch = 0; ch < numChannels; ++ch) {
        float peak = 0.0f;
        ChannelState& state = m_channelStates[ch];
        
        for (int i = 0; i < numSamples; ++i) {
            float sample = audioData[i * numChannels + ch];
            float absSample = std::abs(sample);
            if (absSample > peak) peak = absSample;
            
            // VU calculation (RMS over 300ms)
            state.ewma_vu += alpha_vu * (sample * sample - state.ewma_vu);
            
            // LUFS K-weighting filter 1 (Pre-filter)
            float y_1 = b0_1 * sample + b1_1 * state.x1_1 + b2_1 * state.x2_1 - a1_1 * state.y1_1 - a2_1 * state.y2_1;
            state.x2_1 = state.x1_1; state.x1_1 = sample;
            state.y2_1 = state.y1_1; state.y1_1 = y_1;
            
            // LUFS K-weighting filter 2 (RLB filter)
            float y_2 = b0_2 * y_1 + b1_2 * state.x1_2 + b2_2 * state.x2_2 - a1_2 * state.y1_2 - a2_2 * state.y2_2;
            state.x2_2 = state.x1_2; state.x1_2 = y_1;
            state.y2_2 = state.y1_2; state.y1_2 = y_2;
            
            // Momentary LUFS calculation (RMS of filtered signal over 400ms)
            state.ewma_lufs += alpha_lufs * (y_2 * y_2 - state.ewma_lufs);
        }
        
        float peak_db = (peak > 0.000001f) ? 20.0f * std::log10(peak) : -100.0f;
        m_channelData[ch].peak_db = std::max(-100.0f, peak_db);
        
        float rms_vu = std::sqrt(std::max(0.0f, state.ewma_vu));
        float vu_db = (rms_vu > 0.000001f) ? 20.0f * std::log10(rms_vu) : -100.0f;
        m_channelData[ch].rms_db = std::max(-100.0f, vu_db);
        
        float rms_lufs = std::max(0.0f, state.ewma_lufs);
        float lufs_val = (rms_lufs > 0.000001f) ? -0.691f + 10.0f * std::log10(rms_lufs) : -100.0f;
        m_channelData[ch].lufs = std::max(-100.0f, lufs_val);
        
        if (peak >= 1.0f) {
            m_channelData[ch].clipping = true;
        }
    }
    
    return {};
}

std::vector<AudioMeterData> AudioMeter::GetChannelData() const {
    return m_channelData;
}

void AudioMeter::Reset() {
    for (auto& data : m_channelData) {
        data.peak_db = -100.0f;
        data.rms_db = -100.0f;
        data.lufs = -100.0f;
        data.clipping = false;
    }
    for (auto& state : m_channelStates) {
        state = ChannelState{};
    }
}

} // namespace Audio
} // namespace OpenMedia
