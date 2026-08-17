#pragma once

#include <openmedia/core/Types.h>
#include <openmedia/core/ErrorCodes.h>
#include <openmedia/core/MediaFrame.h>
#include <memory>
#include <string>

namespace openmedia::gpu {

enum class GPUType {
    DirectX11,
    DirectX12,
    CUDA,
    QuickSync,
    Vulkan,
    OpenCL,
    Unknown
};

/// @brief Interface for GPU context and device management
class IGPUContext {
public:
    virtual ~IGPUContext() = default;

    /// @brief Initialize the GPU context (e.g. create D3D11 device, CUDA context)
    virtual core::VoidResult Initialize() = 0;

    /// @brief Release all resources
    virtual void Shutdown() = 0;

    /// @brief Get the underlying device pointer (e.g. ID3D11Device*, CUcontext)
    virtual void* GetDeviceHandle() = 0;

    /// @brief Get context type
    virtual GPUType GetType() const = 0;
    
    /// @brief Get device name/description
    virtual std::string GetDeviceName() const = 0;

    /// @brief Upload CPU memory to a GPU texture
    virtual core::VoidResult UploadTexture(const uint8_t* data, int width, int height, core::PixelFormat format, void** outTextureHandle) = 0;

    /// @brief Download GPU texture memory to a CPU buffer
    virtual core::VoidResult DownloadTexture(void* textureHandle, uint8_t* outData, int width, int height, core::PixelFormat format) = 0;

    /// @brief Copy texture from source to destination on the same GPU
    virtual core::VoidResult CopyTexture(void* srcHandle, void* dstHandle) = 0;

    /// @brief Free a texture allocated by UploadTexture
    virtual void FreeTexture(void* textureHandle) = 0;

    /// @brief Initialize a graphics pipeline for mixing layers
    virtual core::VoidResult InitMixerPipeline(const std::string& vsPath, const std::string& psPath) = 0;

    /// @brief Execute a mixing step on the GPU: Output = Layer * Opacity over Background
    virtual core::VoidResult ExecuteMixerPipeline(void* bgHandle, void* layerHandle, void* outHandle, float opacity) = 0;
};

} // namespace openmedia::gpu
