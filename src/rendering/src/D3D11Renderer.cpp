#include "openmedia/rendering/D3D11Renderer.h"
#include <openmedia/core/Logger.h>
#include <d3dcompiler.h>

#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "d3dcompiler.lib")

namespace openmedia::rendering {

D3D11Renderer::D3D11Renderer() {}
D3D11Renderer::~D3D11Renderer() { Shutdown(); }

core::Result<void> D3D11Renderer::Initialize(void* windowHandle) {
    if (!windowHandle) return std::unexpected(openmedia::core::Error::Make(core::ErrorCode::InvalidArgument, "Invalid window handle"));
    m_hwnd = windowHandle;

    DXGI_SWAP_CHAIN_DESC scd = {};
    scd.BufferCount = 2;
    scd.BufferDesc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
    scd.BufferDesc.Width = 0;
    scd.BufferDesc.Height = 0;
    scd.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    scd.OutputWindow = (HWND)windowHandle;
    scd.SampleDesc.Count = 1;
    scd.Windowed = TRUE;
    scd.SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD;

    UINT createDeviceFlags = 0;
#if defined(_DEBUG)
    createDeviceFlags |= D3D11_CREATE_DEVICE_DEBUG;
#endif

    D3D_FEATURE_LEVEL featureLevels[] = { D3D_FEATURE_LEVEL_11_1, D3D_FEATURE_LEVEL_11_0 };
    D3D_FEATURE_LEVEL featureLevel;

    HRESULT hr = D3D11CreateDeviceAndSwapChain(
        nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, createDeviceFlags,
        featureLevels, ARRAYSIZE(featureLevels), D3D11_SDK_VERSION, &scd,
        &m_swapChain, &m_device, &featureLevel, &m_context
    );

    if (FAILED(hr)) {
        return std::unexpected(openmedia::core::Error::Make(core::ErrorCode::Unknown, "D3D11 Initialization failed"));
    }

    Microsoft::WRL::ComPtr<ID3D11Texture2D> pBackBuffer;
    hr = m_swapChain->GetBuffer(0, __uuidof(ID3D11Texture2D), (void**)&pBackBuffer);
    if (FAILED(hr)) return std::unexpected(openmedia::core::Error::Make(core::ErrorCode::Unknown, "GetBuffer failed"));

    hr = m_device->CreateRenderTargetView(pBackBuffer.Get(), nullptr, &m_renderTargetView);
    if (FAILED(hr)) return std::unexpected(openmedia::core::Error::Make(core::ErrorCode::Unknown, "CreateRenderTargetView failed"));

    m_context->OMSetRenderTargets(1, m_renderTargetView.GetAddressOf(), nullptr);

    auto res = InitShaders();
    if (!res.has_value()) return res;

    openmedia::core::Logger::SInfo("OME", "D3D11Renderer initialized");
    return {};
}

core::Result<void> D3D11Renderer::InitShaders() {
    const char* vsCode = R"(
        struct VSOut { float4 pos : SV_POSITION; float2 texCoord : TEXCOORD; };
        VSOut VS_Main(uint vertexID : SV_VertexID) {
            VSOut output;
            output.texCoord = float2((vertexID << 1) & 2, vertexID & 2);
            output.pos = float4(output.texCoord * float2(2.0f, -2.0f) + float2(-1.0f, 1.0f), 0.0f, 1.0f);
            return output;
        }
    )";

    const char* psBGRACode = R"(
        Texture2D tex : register(t0);
        SamplerState samp : register(s0);
        struct VSOut { float4 pos : SV_POSITION; float2 texCoord : TEXCOORD; };
        float4 PS_BGRA(VSOut input) : SV_TARGET {
            return tex.Sample(samp, input.texCoord);
        }
    )";

    const char* psNV12Code = R"(
        Texture2D texY : register(t0);
        Texture2D texUV : register(t1);
        SamplerState samp : register(s0);
        struct VSOut { float4 pos : SV_POSITION; float2 texCoord : TEXCOORD; };
        float4 PS_NV12(VSOut input) : SV_TARGET {
            float y = texY.Sample(samp, input.texCoord).r;
            float2 uv = texUV.Sample(samp, input.texCoord).rg;
            y = 1.1643 * (y - 0.0625);
            float u = uv.r - 0.5;
            float v = uv.g - 0.5;
            float r = y + 1.5958 * v;
            float g = y - 0.39173 * u - 0.81290 * v;
            float b = y + 2.017 * u;
            return float4(r, g, b, 1.0);
        }
    )";

    Microsoft::WRL::ComPtr<ID3DBlob> vsBlob, psBGRABlob, psNV12Blob, errorBlob;
    
    HRESULT hr = D3DCompile(vsCode, strlen(vsCode), nullptr, nullptr, nullptr, "VS_Main", "vs_5_0", 0, 0, &vsBlob, &errorBlob);
    if (FAILED(hr)) return std::unexpected(openmedia::core::Error::Make(core::ErrorCode::Unknown, "Failed to compile VS"));
    m_device->CreateVertexShader(vsBlob->GetBufferPointer(), vsBlob->GetBufferSize(), nullptr, &m_vertexShader);

    hr = D3DCompile(psBGRACode, strlen(psBGRACode), nullptr, nullptr, nullptr, "PS_BGRA", "ps_5_0", 0, 0, &psBGRABlob, &errorBlob);
    if (FAILED(hr)) return std::unexpected(openmedia::core::Error::Make(core::ErrorCode::Unknown, "Failed to compile PS BGRA"));
    m_device->CreatePixelShader(psBGRABlob->GetBufferPointer(), psBGRABlob->GetBufferSize(), nullptr, &m_pixelShaderBGRA);

    hr = D3DCompile(psNV12Code, strlen(psNV12Code), nullptr, nullptr, nullptr, "PS_NV12", "ps_5_0", 0, 0, &psNV12Blob, &errorBlob);
    if (FAILED(hr)) return std::unexpected(openmedia::core::Error::Make(core::ErrorCode::Unknown, "Failed to compile PS NV12"));
    m_device->CreatePixelShader(psNV12Blob->GetBufferPointer(), psNV12Blob->GetBufferSize(), nullptr, &m_pixelShaderNV12);

    D3D11_SAMPLER_DESC sampDesc = {};
    sampDesc.Filter = D3D11_FILTER_MIN_MAG_MIP_LINEAR;
    sampDesc.AddressU = D3D11_TEXTURE_ADDRESS_CLAMP;
    sampDesc.AddressV = D3D11_TEXTURE_ADDRESS_CLAMP;
    sampDesc.AddressW = D3D11_TEXTURE_ADDRESS_CLAMP;
    m_device->CreateSamplerState(&sampDesc, &m_samplerLinear);

    return {};
}

core::Result<void> D3D11Renderer::Render(const std::shared_ptr<core::MediaFrame>& frame) {
    if (!m_context || !m_swapChain || !m_renderTargetView) return std::unexpected(openmedia::core::Error::Make(core::ErrorCode::InvalidState, "Renderer not initialized"));
    
    if (!frame) {
        const float clearColor[4] = { 0.0f, 0.0f, 0.0f, 1.0f };
        m_context->ClearRenderTargetView(m_renderTargetView.Get(), clearColor);
        m_swapChain->Present(1, 0);
        return {};
    }

    int width = frame->GetWidth();
    int height = frame->GetHeight();
    auto format = frame->GetPixelFormat();

    // Recreate textures if dimension or format changes
    if (m_textureWidth != width || m_textureHeight != height || m_currentFormat != format) {
        m_textureY.Reset(); m_srvY.Reset();
        m_textureUV.Reset(); m_srvUV.Reset();
        
        if (format == core::PixelFormat::NV12) {
            D3D11_TEXTURE2D_DESC texDesc = {};
            texDesc.Width = width; texDesc.Height = height; texDesc.MipLevels = 1; texDesc.ArraySize = 1;
            texDesc.Format = DXGI_FORMAT_R8_UNORM; texDesc.SampleDesc.Count = 1; texDesc.Usage = D3D11_USAGE_DYNAMIC;
            texDesc.BindFlags = D3D11_BIND_SHADER_RESOURCE; texDesc.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
            m_device->CreateTexture2D(&texDesc, nullptr, &m_textureY);
            m_device->CreateShaderResourceView(m_textureY.Get(), nullptr, &m_srvY);

            texDesc.Width = width / 2; texDesc.Height = height / 2; texDesc.Format = DXGI_FORMAT_R8G8_UNORM;
            m_device->CreateTexture2D(&texDesc, nullptr, &m_textureUV);
            m_device->CreateShaderResourceView(m_textureUV.Get(), nullptr, &m_srvUV);
        } else if (format == core::PixelFormat::BGRA) {
            D3D11_TEXTURE2D_DESC texDesc = {};
            texDesc.Width = width; texDesc.Height = height; texDesc.MipLevels = 1; texDesc.ArraySize = 1;
            texDesc.Format = DXGI_FORMAT_B8G8R8A8_UNORM; texDesc.SampleDesc.Count = 1; texDesc.Usage = D3D11_USAGE_DYNAMIC;
            texDesc.BindFlags = D3D11_BIND_SHADER_RESOURCE; texDesc.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
            m_device->CreateTexture2D(&texDesc, nullptr, &m_textureY); // Reuse m_textureY for BGRA
            m_device->CreateShaderResourceView(m_textureY.Get(), nullptr, &m_srvY);
        } else {
            return std::unexpected(openmedia::core::Error::Make(core::ErrorCode::NotSupported, "Unsupported PixelFormat for GPU Render"));
        }

        m_textureWidth = width; m_textureHeight = height; m_currentFormat = format;
    }

    // Upload data
    D3D11_MAPPED_SUBRESOURCE mapY, mapUV;
    if (format == core::PixelFormat::NV12) {
        if (SUCCEEDED(m_context->Map(m_textureY.Get(), 0, D3D11_MAP_WRITE_DISCARD, 0, &mapY))) {
            const uint8_t* srcY = frame->GetVideoPlane(0);
            int lsY = frame->GetLineSize(0);
            for (int i = 0; i < height; ++i) memcpy((uint8_t*)mapY.pData + i * mapY.RowPitch, srcY + i * lsY, width);
            m_context->Unmap(m_textureY.Get(), 0);
        }
        if (SUCCEEDED(m_context->Map(m_textureUV.Get(), 0, D3D11_MAP_WRITE_DISCARD, 0, &mapUV))) {
            const uint8_t* srcUV = frame->GetVideoPlane(1);
            int lsUV = frame->GetLineSize(1);
            for (int i = 0; i < height / 2; ++i) memcpy((uint8_t*)mapUV.pData + i * mapUV.RowPitch, srcUV + i * lsUV, width);
            m_context->Unmap(m_textureUV.Get(), 0);
        }
    } else if (format == core::PixelFormat::BGRA) {
        if (SUCCEEDED(m_context->Map(m_textureY.Get(), 0, D3D11_MAP_WRITE_DISCARD, 0, &mapY))) {
            const uint8_t* src = frame->GetVideoPlane(0);
            int ls = frame->GetLineSize(0);
            for (int i = 0; i < height; ++i) memcpy((uint8_t*)mapY.pData + i * mapY.RowPitch, src + i * ls, width * 4);
            m_context->Unmap(m_textureY.Get(), 0);
        }
    }

    // Draw
    m_context->OMSetRenderTargets(1, m_renderTargetView.GetAddressOf(), nullptr);
    
    // Clear backbuffer to black to avoid smearing in letterbox/pillarbox areas
    const float clearColor[4] = { 0.0f, 0.0f, 0.0f, 1.0f };
    m_context->ClearRenderTargetView(m_renderTargetView.Get(), clearColor);

    // Get window client area size
    RECT clientRect;
    GetClientRect((HWND)m_hwnd, &clientRect);
    float clientWidth = (float)(clientRect.right - clientRect.left);
    float clientHeight = (float)(clientRect.bottom - clientRect.top);

    D3D11_VIEWPORT vp = {};
    if (m_scaleMode == ScaleMode::AspectRatioFit) {
        // Calculate aspect ratio preserving viewport
        float videoAspect = (float)width / (float)height;
        float clientAspect = clientWidth / clientHeight;

        float vpWidth, vpHeight, vpX, vpY;
        if (videoAspect > clientAspect) {
            vpWidth = clientWidth;
            vpHeight = clientWidth / videoAspect;
            vpX = 0.0f;
            vpY = (clientHeight - vpHeight) / 2.0f;
        } else {
            vpHeight = clientHeight;
            vpWidth = clientHeight * videoAspect;
            vpX = (clientWidth - vpWidth) / 2.0f;
            vpY = 0.0f;
        }
        vp = { vpX, vpY, vpWidth, vpHeight, 0.0f, 1.0f };
    } else {
        // Stretch mode: fill window exactly, ignoring aspect ratio
        vp = { 0.0f, 0.0f, clientWidth, clientHeight, 0.0f, 1.0f };
    }

    m_context->RSSetViewports(1, &vp);

    m_context->VSSetShader(m_vertexShader.Get(), nullptr, 0);
    if (format == core::PixelFormat::NV12) {
        m_context->PSSetShader(m_pixelShaderNV12.Get(), nullptr, 0);
        ID3D11ShaderResourceView* srvs[] = { m_srvY.Get(), m_srvUV.Get() };
        m_context->PSSetShaderResources(0, 2, srvs);
    } else {
        m_context->PSSetShader(m_pixelShaderBGRA.Get(), nullptr, 0);
        m_context->PSSetShaderResources(0, 1, m_srvY.GetAddressOf());
    }
    m_context->PSSetSamplers(0, 1, m_samplerLinear.GetAddressOf());
    m_context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    m_context->Draw(3, 0);
    m_swapChain->Present(1, 0);

    return {};
}

core::Result<void> D3D11Renderer::Resize(int width, int height) {
    if (!m_swapChain) return std::unexpected(openmedia::core::Error::Make(core::ErrorCode::InvalidState, "Renderer not initialized"));
    m_renderTargetView.Reset();
    m_swapChain->ResizeBuffers(0, width, height, DXGI_FORMAT_UNKNOWN, 0);
    Microsoft::WRL::ComPtr<ID3D11Texture2D> pBackBuffer;
    if (SUCCEEDED(m_swapChain->GetBuffer(0, __uuidof(ID3D11Texture2D), (void**)&pBackBuffer))) {
        m_device->CreateRenderTargetView(pBackBuffer.Get(), nullptr, &m_renderTargetView);
    }
    return {};
}

core::Result<void> D3D11Renderer::Shutdown() {
    m_textureY.Reset(); m_srvY.Reset();
    m_textureUV.Reset(); m_srvUV.Reset();
    m_vertexShader.Reset(); m_pixelShaderNV12.Reset(); m_pixelShaderBGRA.Reset(); m_samplerLinear.Reset();
    m_renderTargetView.Reset(); m_swapChain.Reset(); m_context.Reset(); m_device.Reset();
    return {};
}

void D3D11Renderer::SetScaleMode(ScaleMode mode) {
    m_scaleMode = mode;
}

} // namespace openmedia::rendering

