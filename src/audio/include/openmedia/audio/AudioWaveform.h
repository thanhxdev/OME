#pragma once

#include <memory>
#include <vector>
#include <openmedia/core/MediaFrame.h>

namespace openmedia::audio {

/// @brief Generates waveform data from audio samples for visualization
class AudioWaveform {
public:
    AudioWaveform();
    ~AudioWaveform();

    void SetResolution(int points);

    /// @brief Process frame and extract waveform visualization points
    /// @return A vector of float values [-1.0, 1.0] representing the waveform
    std::vector<float> Process(std::shared_ptr<openmedia::core::MediaFrame> input);

private:
    int m_points = 256;
};

} // namespace openmedia::audio
