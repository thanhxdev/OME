#pragma once

#include "openmedia/plugins/IPlugin.h"
#include <openmedia/core/MediaFrame.h>

namespace openmedia {
namespace plugins {

class IAudioFilter : public IPlugin {
public:
    virtual ~IAudioFilter() = default;

    // Setup filter with configuration parameters (e.g., JSON string)
    virtual bool Setup(const char* configJson) = 0;

    // Apply filter to an audio frame (process samples)
    virtual bool ProcessSamples(core::MediaFrame* inputFrame, core::MediaFrame* outputFrame = nullptr) = 0;
    
    // Get output parameters after setup
    virtual const char* GetOutputParams() const = 0;
};

} // namespace plugins
} // namespace openmedia
