#pragma once

#include "IPlugin.h"
#include <openmedia/core/MediaFrame.h>

namespace openmedia::plugin {

struct VideoFilterParams {
    int width;
    int height;
    int pixelFormat;    // OME pixel format enum
    double frameRate;
    bool gpuEnabled;
};

class IVideoFilter : public IPlugin {
public:
    // Setup filter with input parameters
    virtual bool Setup(const VideoFilterParams& params) = 0;

    // Process a video frame (in-place or allocate new)
    virtual bool ProcessFrame(
        const core::MediaFrame& input,
        core::MediaFrame& output
    ) = 0;

    // GPU variant (optional)
    virtual bool ProcessFrameGPU(
        const void* /*inputTexture*/,
        void* /*outputTexture*/,
        int /*textureFormat*/
    ) { return false; }

    // Get output parameters (may differ from input)
    virtual VideoFilterParams GetOutputParams() const = 0;

    // Reset filter state
    virtual void Reset() = 0;
};

} // namespace openmedia::plugin
