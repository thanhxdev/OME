#pragma once
#include <openmedia/core/MediaFrame.h>
#include <openmedia/core/ErrorCodes.h>
#include <memory>
#include <openmedia/gpu/GPUContext.h>

namespace openmedia::mixer {

class LumaKey {
public:
    LumaKey();
    ~LumaKey();

    // Set the luminance threshold [0.0 - 1.0]. Values below this are keyed out.
    void SetThreshold(float threshold);
    
    // Set softness for blending [0.0 - 1.0]
    void SetSoftness(float softness);
    
    // Invert the key (key out bright instead of dark)
    void SetInvert(bool invert);

    core::Result<std::shared_ptr<core::MediaFrame>> Process(const std::shared_ptr<core::MediaFrame>& input);
    core::Result<std::shared_ptr<core::MediaFrame>> ProcessGPU(const std::shared_ptr<core::MediaFrame>& input, std::shared_ptr<gpu::IGPUContext> gpuContext);

private:
    float m_threshold;
    float m_softness;
    bool m_invert;
};

} // namespace openmedia::mixer
