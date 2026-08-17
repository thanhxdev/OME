#include <openmedia/gpu/GPUFrame.h>

namespace openmedia::gpu {

std::shared_ptr<GPUFrame> GPUFrame::Create(
    std::shared_ptr<IGPUContext> context,
    void* textureHandle,
    uint32_t width,
    uint32_t height,
    core::PixelFormat format)
{
    if (!context || !textureHandle) {
        return nullptr;
    }
    
    // We can't use std::make_shared because the constructor is private/protected
    return std::shared_ptr<GPUFrame>(new GPUFrame(context, textureHandle, width, height, format));
}

GPUFrame::GPUFrame(std::shared_ptr<IGPUContext> context, void* textureHandle, uint32_t width, uint32_t height, core::PixelFormat format)
    : m_context(context)
{
    m_gpuTextureHandle = textureHandle;
    m_width = width;
    m_height = height;
    m_pixelFormat = format;
    m_mediaType = core::MediaType::Video;
}

GPUFrame::~GPUFrame()
{
    if (m_context && m_gpuTextureHandle) {
        m_context->FreeTexture(m_gpuTextureHandle);
        m_gpuTextureHandle = nullptr;
    }
}

} // namespace openmedia::gpu
