#pragma once

#include <openmedia/core/MediaFrame.h>
#include <openmedia/gpu/GPUContext.h>
#include <memory>

namespace openmedia::gpu {

/// @brief GPU-resident media frame that manages the lifecycle of a GPU texture.
class GPUFrame : public core::MediaFrame {
public:
    /// @brief Create a GPU frame wrapping an existing texture handle
    /// @param context The GPU context that created this texture (used for cleanup)
    /// @param textureHandle Platform-specific texture handle (e.g. ID3D11Texture2D*)
    /// @param width Width in pixels
    /// @param height Height in pixels
    /// @param format Pixel format of the texture
    static std::shared_ptr<GPUFrame> Create(
        std::shared_ptr<IGPUContext> context,
        void* textureHandle,
        uint32_t width, 
        uint32_t height, 
        core::PixelFormat format);

    ~GPUFrame();

    /// @brief Get the associated GPU context
    std::shared_ptr<IGPUContext> GetContext() const { return m_context; }

private:
    GPUFrame(std::shared_ptr<IGPUContext> context, void* textureHandle, uint32_t width, uint32_t height, core::PixelFormat format);

    std::shared_ptr<IGPUContext> m_context;
};

} // namespace openmedia::gpu
