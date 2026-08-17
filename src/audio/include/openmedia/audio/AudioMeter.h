#ifndef OPENMEDIA_AUDIO_METER_H
#define OPENMEDIA_AUDIO_METER_H

#include <openmedia/core/IMediaObject.h>
#include <openmedia/core/MediaFrame.h>
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

    // Get the current meter readings for each channel
    std::vector<AudioMeterData> GetChannelData() const;

    // Reset meter statistics
    void Reset();

private:
    std::vector<AudioMeterData> m_channelData;
    
    struct ChannelState {
        float x1_1 = 0, x2_1 = 0, y1_1 = 0, y2_1 = 0; // Pre-filter state
        float x1_2 = 0, x2_2 = 0, y1_2 = 0, y2_2 = 0; // RLB filter state
        float ewma_vu = 0;   // Squared energy moving average for VU
        float ewma_lufs = 0; // Squared energy moving average for LUFS
    };
    std::vector<ChannelState> m_channelStates;
};

} // namespace Audio
} // namespace OpenMedia

#endif // OPENMEDIA_AUDIO_METER_H
