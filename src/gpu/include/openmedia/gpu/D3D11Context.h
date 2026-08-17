#pragma once

#include "openmedia/gpu/GPUContext.h"
#include <d3d11.h>
#include <wrl/client.h>

namespace openmedia::gpu {

class D3D11Context : public IGPUContext {
public:
    D3D11Context();
    ~D3D11Context() override;

    core::VoidResult Initialize() override;
    void Shutdown() override;

    void* GetDeviceHandle() override;
    GPUType GetType() const override { return GPUType::DirectX11; }
    std::string GetDeviceName() const override;

    core::VoidResult UploadTexture(const uint8_t* data, int width, int height, core::PixelFormat format, void** outTextureHandle) override;
    core::VoidResult DownloadTexture(void* textureHandle, uint8_t* outData, int width, int height, core::PixelFormat format) override;
    core::VoidResult CopyTexture(void* srcHandle, void* dstHandle) override;
    void FreeTexture(void* textureHandle) override;

    core::VoidResult InitMixerPipeline(const std::string& vsPath, const std::string& psPath) override;
    core::VoidResult ExecuteMixerPipeline(void* bgHandle, void* layerHandle, void* outHandle, float opacity) override;

    // Specific to D3D11
    Microsoft::WRL::ComPtr<ID3D11Device> GetDevice() const { return m_device; }
    Microsoft::WRL::ComPtr<ID3D11DeviceContext> GetDeviceContext() const { return m_context; }

private:
    Microsoft::WRL::ComPtr<ID3D11Device> m_device;
    Microsoft::WRL::ComPtr<ID3D11DeviceContext> m_context;
    
    // Pipeline state
    Microsoft::WRL::ComPtr<ID3D11VertexShader> m_mixerVS;
    Microsoft::WRL::ComPtr<ID3D11PixelShader> m_mixerPS;
    Microsoft::WRL::ComPtr<ID3D11InputLayout> m_mixerInputLayout;
    Microsoft::WRL::ComPtr<ID3D11Buffer> m_mixerVertexBuffer;
    Microsoft::WRL::ComPtr<ID3D11SamplerState> m_mixerSamplerState;
    Microsoft::WRL::ComPtr<ID3D11Buffer> m_mixerConstantBuffer;
    
    std::string m_deviceName;
    bool m_initialized = false;
};

} // namespace openmedia::gpu
