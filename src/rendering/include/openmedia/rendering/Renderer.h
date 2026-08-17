#pragma once
#include <openmedia/core/ErrorCodes.h>
#include <openmedia/core/MediaFrame.h>
#include <memory>

namespace openmedia::rendering {

enum class ScaleMode {
    Stretch,          // Fit the whole window exactly (stretching, no aspect ratio)
    AspectRatioFit    // Letterbox/Pillarbox to preserve aspect ratio
};

class Renderer {
public:
    virtual ~Renderer() = default;

    virtual core::Result<void> Initialize(void* windowHandle) = 0;
    virtual core::Result<void> Render(const std::shared_ptr<core::MediaFrame>& frame) = 0;
    virtual core::Result<void> Resize(int width, int height) = 0;
    virtual core::Result<void> Shutdown() = 0;
    virtual void SetScaleMode(ScaleMode mode) = 0;
};

} // namespace openmedia::rendering
