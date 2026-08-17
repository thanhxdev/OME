#pragma once

#include <memory>
#include <openmedia/core/MediaFrame.h>

namespace openmedia::audio {

/// @brief Audio sample rate and format converter
class Resampler {
public:
    Resampler();
    ~Resampler();

    /// @brief Initialize the resampler with target parameters
    bool Initialize(int targetSampleRate, openmedia::core::SampleFormat targetFormat, int targetChannels);

    /// @brief Process an audio frame, returning a resampled frame
    std::shared_ptr<openmedia::core::MediaFrame> Process(std::shared_ptr<openmedia::core::MediaFrame> input);

private:
    int m_targetSampleRate = 48000;
    openmedia::core::SampleFormat m_targetFormat = openmedia::core::SampleFormat::Float32;
    int m_targetChannels = 2;
    
    // Internal state (e.g. SwrContext pointer)
    void* m_swrCtx = nullptr;
};

} // namespace openmedia::audio
