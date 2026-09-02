#ifndef OPENMEDIA_AUDIO_METER_H
#define OPENMEDIA_AUDIO_METER_H

#include <openmedia/core/IMediaObject.h>
#include <openmedia/core/MediaFrame.h>
#include <openmedia/core/Types.h>
#include <vector>

namespace OpenMedia {
namespace Audio {

struct AudioMeterData {
    float peak_db = -100.0f;
    float rms_db = -100.0f;
    float lufs = -100.0f;
    bool clipping = false;
};

class AudioMeter {
public:
    AudioMeter();
    ~AudioMeter();

    // Process a frame of audio samples and compute meter data
    openmedia::core::Result<void> ProcessSamples(const openmedia::core::MediaFrame* frame);

    // Process raw audio buffer directly (supports Interleaved and Planar)
    openmedia::core::Result<void> ProcessRaw(
        const void* const* channelPointers,
        uint32_t sampleCount,
        uint32_t channelCount,
        openmedia::core::SampleFormat format,
        uint32_t sampleRate);

    // Get the current meter readings for each channel
    std::vector<AudioMeterData> GetChannelData() const;

    // Reset meter statistics
    void Reset();

private:
    std::vector<AudioMeterData> m_channelData;
    
    struct ChannelState {
        float x1_1 = 0.0f, x2_1 = 0.0f, y1_1 = 0.0f, y2_1 = 0.0f; // Pre-filter state
        float x1_2 = 0.0f, x2_2 = 0.0f, y1_2 = 0.0f, y2_2 = 0.0f; // RLB filter state
        float ewma_vu = 0.0f;   // Squared energy moving average for VU (300ms)
        float ewma_lufs = 0.0f; // Squared energy moving average for LUFS (400ms)
    };
    std::vector<ChannelState> m_channelStates;
};

} // namespace Audio
} // namespace OpenMedia

#endif // OPENMEDIA_AUDIO_METER_H

