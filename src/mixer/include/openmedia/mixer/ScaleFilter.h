#pragma once
#include <openmedia/core/MediaFrame.h>
#include <openmedia/core/ErrorCodes.h>
#include <memory>
#include <openmedia/gpu/GPUContext.h>

struct SwsContext;

namespace openmedia::mixer {

class ScaleFilter {
public:
    ScaleFilter();
    ~ScaleFilter();

    void SetOutputSize(int width, int height);

    core::Result<std::shared_ptr<core::MediaFrame>> Process(const std::shared_ptr<core::MediaFrame>& input);
    core::Result<std::shared_ptr<core::MediaFrame>> ProcessGPU(const std::shared_ptr<core::MediaFrame>& input, std::shared_ptr<gpu::IGPUContext> gpuContext);

private:
    int m_targetWidth;
    int m_targetHeight;
    SwsContext* m_swsCtx;
    int m_lastInW;
    int m_lastInH;
};

} // namespace openmedia::mixer
