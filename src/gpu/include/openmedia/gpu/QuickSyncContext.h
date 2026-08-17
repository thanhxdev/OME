#pragma once

#include <memory>
#include <openmedia/gpu/GPUContext.h>

namespace openmedia::gpu {

/// @brief GPU Context wrapper for Intel oneVPL (QuickSync)
class QuickSyncContext : public IGPUContext {
public:
    QuickSyncContext();
    virtual ~QuickSyncContext();

    core::VoidResult Initialize() override;
    void Shutdown() override;

    void* GetDeviceHandle() override;
    GPUType GetType() const override { return GPUType::QuickSync; }
    std::string GetDeviceName() const override { return "Intel QuickSync Video"; }

    core::VoidResult UploadTexture(const uint8_t* data, int width, int height, core::PixelFormat format, void** outTextureHandle) override { return {}; }
    core::VoidResult DownloadTexture(void* textureHandle, uint8_t* outData, int width, int height, core::PixelFormat format) override { return {}; }
    core::VoidResult CopyTexture(void* srcHandle, void* dstHandle) override { return {}; }
    void FreeTexture(void* textureHandle) override {}
    
    core::VoidResult InitMixerPipeline(const std::string& vsPath, const std::string& psPath) override { return {}; }
    core::VoidResult ExecuteMixerPipeline(void* bgHandle, void* layerHandle, void* outHandle, float opacity) override { return {}; }

private:
    void* m_vplSession = nullptr;
    int m_deviceIndex = -1;
};

} // namespace openmedia::gpu
