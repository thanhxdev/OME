#pragma once
#include <openmedia/core/ErrorCodes.h>
#include <openmedia/rendering/Renderer.h>
#include <d3d11.h>
#include <wrl/client.h>

namespace openmedia::rendering {

class D3D11Renderer : public Renderer {
public:
    D3D11Renderer();
    ~D3D11Renderer() override;

    core::Result<void> Initialize(void* windowHandle) override;
    core::Result<void> Render(const std::shared_ptr<core::MediaFrame>& frame) override;
    core::Result<void> Resize(int width, int height) override;
    core::Result<void> Shutdown() override;
    void SetScaleMode(ScaleMode mode) override;

private:
    core::Result<void> InitShaders();

    Microsoft::WRL::ComPtr<ID3D11Device> m_device;
    Microsoft::WRL::ComPtr<ID3D11DeviceContext> m_context;
    Microsoft::WRL::ComPtr<IDXGISwapChain> m_swapChain;
    Microsoft::WRL::ComPtr<ID3D11RenderTargetView> m_renderTargetView;
    
    // Rendering objects
    Microsoft::WRL::ComPtr<ID3D11Texture2D> m_textureY;
    Microsoft::WRL::ComPtr<ID3D11ShaderResourceView> m_srvY;
    Microsoft::WRL::ComPtr<ID3D11Texture2D> m_textureUV;
    Microsoft::WRL::ComPtr<ID3D11ShaderResourceView> m_srvUV;
    
    Microsoft::WRL::ComPtr<ID3D11VertexShader> m_vertexShader;
    Microsoft::WRL::ComPtr<ID3D11PixelShader> m_pixelShaderNV12;
    Microsoft::WRL::ComPtr<ID3D11PixelShader> m_pixelShaderBGRA;
    Microsoft::WRL::ComPtr<ID3D11SamplerState> m_samplerLinear;
    
    int m_textureWidth = 0;
    int m_textureHeight = 0;
    core::PixelFormat m_currentFormat = core::PixelFormat::Unknown;

    void* m_hwnd = nullptr;
    ScaleMode m_scaleMode = ScaleMode::Stretch;
};

} // namespace openmedia::rendering
