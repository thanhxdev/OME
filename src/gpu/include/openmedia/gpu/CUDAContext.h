#pragma once

/// @file CUDAContext.h
/// @brief CUDA GPU context using NVIDIA Driver API

#include "openmedia/gpu/GPUContext.h"

#ifdef OME_HAS_CUDA
#include <cuda.h>
#endif

namespace openmedia::gpu {

class CUDAContext : public IGPUContext {
public:
    CUDAContext();
    ~CUDAContext() override;

    core::VoidResult Initialize() override;
    void Shutdown() override;

    void* GetDeviceHandle() override;
    GPUType GetType() const override { return GPUType::CUDA; }
    std::string GetDeviceName() const override;

    core::VoidResult UploadTexture(const uint8_t* data, int width, int height, core::PixelFormat format, void** outTextureHandle) override;
    core::VoidResult DownloadTexture(void* textureHandle, uint8_t* outData, int width, int height, core::PixelFormat format) override;
    core::VoidResult CopyTexture(void* srcHandle, void* dstHandle) override;
    void FreeTexture(void* textureHandle) override;

    core::VoidResult InitMixerPipeline(const std::string& /*vsPath*/, const std::string& /*psPath*/) override { return core::VoidResult(); }
    core::VoidResult ExecuteMixerPipeline(void* /*bgHandle*/, void* /*layerHandle*/, void* /*outHandle*/, float /*opacity*/) override { return core::VoidResult(); }

#ifdef OME_HAS_CUDA
    /// @brief Get the raw CUcontext for NVENC/NVDEC integration
    CUcontext GetCUContext() const { return m_cuContext; }

    /// @brief Get CUDA device ordinal
    int GetDeviceId() const { return m_deviceOrdinal; }
#endif

private:
#ifdef OME_HAS_CUDA
    CUdevice m_cuDevice = 0;
    CUcontext m_cuContext = nullptr;
    int m_deviceOrdinal = 0;
#endif
    std::string m_deviceName;
    bool m_initialized = false;
};

} // namespace openmedia::gpu
