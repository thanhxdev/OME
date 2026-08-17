#pragma once

#include "IPlugin.h"
#include <cstddef>

namespace openmedia::plugin {

struct AudioFilterParams {
    int sampleRate;
    int channels;
    int sampleFormat;   // float32, int16, etc.
    int samplesPerFrame;
};

class IAudioFilter : public IPlugin {
public:
    virtual bool Setup(const AudioFilterParams& params) = 0;

    // Process audio samples
    virtual bool ProcessSamples(
        const float* input,
        float* output,
        size_t sampleCount,
        int channels
    ) = 0;

    virtual AudioFilterParams GetOutputParams() const = 0;
    virtual void Reset() = 0;
};

} // namespace openmedia::plugin
