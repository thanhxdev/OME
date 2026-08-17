#pragma once

#include "openmedia/plugins/IPlugin.h"
#include <openmedia/core/MediaFrame.h>

namespace openmedia {
namespace plugins {

class IVideoFilter : public IPlugin {
public:
    virtual ~IVideoFilter() = default;

    // Setup filter with configuration parameters (e.g., JSON string)
    virtual bool Setup(const char* configJson) = 0;

    // Apply filter to a video frame (CPU processing)
    virtual bool ProcessFrame(core::MediaFrame* inputFrame, core::MediaFrame* outputFrame = nullptr) = 0;
    
    // Apply filter to a video frame (GPU processing)
    virtual bool ProcessFrameGPU(core::MediaFrame* inputFrame, core::MediaFrame* outputFrame = nullptr) = 0;

    // Get output parameters after setup
    virtual const char* GetOutputParams() const = 0;
};

} // namespace plugins
} // namespace openmedia
