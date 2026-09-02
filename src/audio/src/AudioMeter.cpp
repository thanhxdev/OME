#include <openmedia/audio/AudioMeter.h>
#include <cmath>
#include <algorithm>
#include <cstring>

namespace OpenMedia {
namespace Audio {

AudioMeter::AudioMeter() {
}

AudioMeter::~AudioMeter() {
}

openmedia::core::Result<void> AudioMeter::ProcessSamples(const openmedia::core::MediaFrame* frame) {
    if (!frame) {
        return std::unexpected(openmedia::core::Error::Make(openmedia::core::ErrorCode::InvalidArgument, "Invalid frame pointer"));
    }

    uint32_t numChannels = frame->GetChannelCount();
    uint32_t numSamples = frame->GetSampleCount();
    if (numChannels == 0 || numSamples == 0) {
        return {};
    }

    auto sampleFormat = frame->GetSampleFormat();
    uint32_t sampleRate = frame->GetSampleRate();
    if (sampleRate == 0) sampleRate = 48000;

    // Collect pointers to channel data
    std::vector<const void*> channelPointers(numChannels, nullptr);
    bool isPlanar = (sampleFormat == openmedia::core::SampleFormat::Float32P ||
                     sampleFormat == openmedia::core::SampleFormat::S16P);

    if (isPlanar) {
        for (uint32_t ch = 0; ch < numChannels; ++ch) {
            channelPointers[ch] = frame->GetAudioData(ch);
        }
    } else {
        const void* baseData = frame->GetAudioData(0);
        for (uint32_t ch = 0; ch < numChannels; ++ch) {
            channelPointers[ch] = baseData;
        }
    }

    return ProcessRaw(channelPointers.data(), numSamples, numChannels, sampleFormat, sampleRate);
}

openmedia::core::Result<void> AudioMeter::ProcessRaw(
    const void* const* channelPointers,
    uint32_t sampleCount,
    uint32_t channelCount,
    openmedia::core::SampleFormat format,
    uint32_t sampleRate)
{
    if (!channelPointers || channelCount == 0 || sampleCount == 0) {
        return std::unexpected(openmedia::core::Error::Make(openmedia::core::ErrorCode::InvalidArgument, "Invalid audio parameters"));
    }

    if (m_channelData.size() != channelCount) {
        m_channelData.resize(channelCount);
        m_channelStates.resize(channelCount);
    }

    float sr = (sampleRate > 0) ? static_cast<float>(sampleRate) : 48000.0f;
    const float alpha_vu = 1.0f - std::exp(-1.0f / (sr * 0.300f));   // 300ms window (VU standard)
    const float alpha_lufs = 1.0f - std::exp(-1.0f / (sr * 0.400f)); // 400ms window (Momentary LUFS)

    // K-weighting coefficients for ITU-R BS.1770-4 (at 48kHz reference)
    const float b0_1 = 1.53512485958697f, b1_1 = -2.69169618940638f, b2_1 = 1.19839281085285f;
    const float a1_1 = -1.69065929318241f, a2_1 = 0.73248077421585f;
    
    const float b0_2 = 1.0f, b1_2 = -2.0f, b2_2 = 1.0f;
    const float a1_2 = -1.99004745483398f, a2_2 = 0.99007225036621f;

    bool isPlanar = (format == openmedia::core::SampleFormat::Float32P ||
                     format == openmedia::core::SampleFormat::S16P);

    for (uint32_t ch = 0; ch < channelCount; ++ch) {
        const void* ptr = channelPointers[ch];
        if (!ptr) continue;

        float peakLinear = 0.0f;
        ChannelState& state = m_channelStates[ch];

        for (uint32_t i = 0; i < sampleCount; ++i) {
            float sample = 0.0f;

            switch (format) {
                case openmedia::core::SampleFormat::Float32: {
                    const float* fData = static_cast<const float*>(ptr);
                    sample = fData[i * channelCount + ch];
                    break;
                }
                case openmedia::core::SampleFormat::Float32P: {
                    const float* fData = static_cast<const float*>(ptr);
                    sample = fData[i];
                    break;
                }
                case openmedia::core::SampleFormat::S16: {
                    const int16_t* sData = static_cast<const int16_t*>(ptr);
                    sample = static_cast<float>(sData[i * channelCount + ch]) / 32768.0f;
                    break;
                }
                case openmedia::core::SampleFormat::S16P: {
                    const int16_t* sData = static_cast<const int16_t*>(ptr);
                    sample = static_cast<float>(sData[i]) / 32768.0f;
                    break;
                }
                case openmedia::core::SampleFormat::S32: {
                    const int32_t* sData = static_cast<const int32_t*>(ptr);
                    sample = static_cast<float>(sData[i * channelCount + ch]) / 2147483648.0f;
                    break;
                }
                case openmedia::core::SampleFormat::Float64: {
                    const double* dData = static_cast<const double*>(ptr);
                    sample = static_cast<float>(dData[i * channelCount + ch]);
                    break;
                }
                default: {
                    // Default fallback to Float32 interleaved
                    const float* fData = static_cast<const float*>(ptr);
                    sample = fData[isPlanar ? i : (i * channelCount + ch)];
                    break;
                }
            }

            float absSample = std::abs(sample);
            if (absSample > peakLinear) {
                peakLinear = absSample;
            }

            // VU Meter calculation (RMS over 300ms window)
            state.ewma_vu += alpha_vu * (sample * sample - state.ewma_vu);

            // LUFS K-weighting Filter 1 (High-shelf pre-filter)
            float y_1 = b0_1 * sample + b1_1 * state.x1_1 + b2_1 * state.x2_1 - a1_1 * state.y1_1 - a2_1 * state.y2_1;
            state.x2_1 = state.x1_1; state.x1_1 = sample;
            state.y2_1 = state.y1_1; state.y1_1 = y_1;

            // LUFS K-weighting Filter 2 (RLB high-pass filter)
            float y_2 = b0_2 * y_1 + b1_2 * state.x1_2 + b2_2 * state.x2_2 - a1_2 * state.y1_2 - a2_2 * state.y2_2;
            state.x2_2 = state.x1_2; state.x1_2 = y_1;
            state.y2_2 = state.y1_2; state.y1_2 = y_2;

            // Momentary LUFS (RMS of K-weighted signal over 400ms)
            state.ewma_lufs += alpha_lufs * (y_2 * y_2 - state.ewma_lufs);
        }

        // Convert Peak to dBFS (-100.0 dB to 0.0 dB)
        float peak_db = (peakLinear > 0.000001f) ? 20.0f * std::log10(peakLinear) : -100.0f;
        m_channelData[ch].peak_db = std::clamp(peak_db, -100.0f, 0.0f);

        // Convert RMS to dBFS
        float rms_vu = std::sqrt(std::max(0.0f, state.ewma_vu));
        float vu_db = (rms_vu > 0.000001f) ? 20.0f * std::log10(rms_vu) : -100.0f;
        m_channelData[ch].rms_db = std::clamp(vu_db, -100.0f, 0.0f);

        // Convert Momentary LUFS
        float rms_lufs = std::max(0.0f, state.ewma_lufs);
        float lufs_val = (rms_lufs > 0.000001f) ? -0.691f + 10.0f * std::log10(rms_lufs) : -100.0f;
        m_channelData[ch].lufs = std::clamp(lufs_val, -100.0f, 0.0f);

        // Clipping flag: true if peak reaches or exceeds full-scale threshold (>= -0.1 dBFS or linear >= 0.988)
        m_channelData[ch].clipping = (peakLinear >= 0.988f);
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
