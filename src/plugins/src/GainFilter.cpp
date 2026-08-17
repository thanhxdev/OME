#include "GainFilter.h"
#include <cstring>

namespace openmedia::plugins {

bool GainFilter::ProcessSamples(
    const openmedia::core::MediaFrame& input,
    openmedia::core::MediaFrame& output)
{
    openmedia::core::AudioFrameInfo info = input.GetAudioInfo();
    
    // Process each channel
    for (int c = 0; c < info.channels; ++c) {
        const float* inSamples = input.GetAudioData(c);
        float* outSamples = output.GetAudioData(c);
        
        if (inSamples && outSamples) {
            for (int i = 0; i < info.sampleCount; ++i) {
                // Apply gain
                outSamples[i] = inSamples[i] * m_gain;
            }
        }
    }

    return true;
}

} // namespace openmedia::plugins

// Standard Plugin Export
extern "C" {
#ifdef _WIN32
    __declspec(dllexport)
#else
    __attribute__((visibility("default")))
#endif
    openmedia::plugin::IPlugin* ome_plugin_create() {
        return new openmedia::plugins::GainFilter();
    }
}
