#pragma once

#include "openmedia/gpu/GPUContext.h"
#include <string>

// Forward declarations for D3D12
struct ID3D12Device;
struct ID3D12CommandQueue;

namespace openmedia::gpu {

class D3D12Context : public IGPUContext {
public:
    D3D12Context();
    ~D3D12Context() override;

    core::VoidResult Initialize() override;
    void Shutdown() override;

    void* GetDeviceHandle() override;
    GPUType GetType() const override { return GPUType::DirectX12; }
    std::string GetDeviceName() const override;

    core::VoidResult UploadTexture(const uint8_t* data, int width, int height, core::PixelFormat format, void** outTextureHandle) override;
    core::VoidResult DownloadTexture(void* textureHandle, uint8_t* outData, int width, int height, core::PixelFormat format) override;
    core::VoidResult CopyTexture(void* srcHandle, void* dstHandle) override;
    void FreeTexture(void* textureHandle) override;

    core::VoidResult InitMixerPipeline(const std::string& vsPath, const std::string& psPath) override;
    core::VoidResult ExecuteMixerPipeline(void* bgHandle, void* layerHandle, void* outHandle, float opacity) override;

private:
    ID3D12Device* m_device = nullptr;
    ID3D12CommandQueue* m_commandQueue = nullptr;
    
    std::string m_deviceName;
    bool m_initialized = false;
};

} // namespace openmedia::gpu
