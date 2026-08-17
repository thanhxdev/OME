#include "openmedia/gpu/D3D11Context.h"
#include <openmedia/core/Logger.h>
#include <dxgi.h>
#include <d3dcompiler.h>

#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "dxgi.lib")
#pragma comment(lib, "d3dcompiler.lib")

namespace openmedia::gpu {

D3D11Context::D3D11Context() {
}

D3D11Context::~D3D11Context() {
    Shutdown();
}

core::VoidResult D3D11Context::Initialize() {
    if (m_initialized) {
        return {};
    }

    UINT createDeviceFlags = 0;
#if defined(_DEBUG)
    createDeviceFlags |= D3D11_CREATE_DEVICE_DEBUG;
#endif

    D3D_FEATURE_LEVEL featureLevels[] = {
        D3D_FEATURE_LEVEL_11_1,
        D3D_FEATURE_LEVEL_11_0,
    };
    D3D_FEATURE_LEVEL featureLevel;

    HRESULT hr = D3D11CreateDevice(
        nullptr,                    // Default adapter
        D3D_DRIVER_TYPE_HARDWARE,
        0,                          // No software device
        createDeviceFlags,
        featureLevels,
        ARRAYSIZE(featureLevels),
        D3D11_SDK_VERSION,
        &m_device,
        &featureLevel,
        &m_context
    );

    if (FAILED(hr)) {
        return std::unexpected(core::Error::Make(core::ErrorCode::Unknown, "Failed to create D3D11 device"));
    }

    // Get device name
    Microsoft::WRL::ComPtr<IDXGIDevice> dxgiDevice;
    if (SUCCEEDED(m_device.As(&dxgiDevice))) {
        Microsoft::WRL::ComPtr<IDXGIAdapter> adapter;
        if (SUCCEEDED(dxgiDevice->GetAdapter(&adapter))) {
            DXGI_ADAPTER_DESC desc;
            if (SUCCEEDED(adapter->GetDesc(&desc))) {
                char name[128];
                size_t converted = 0;
                wcstombs_s(&converted, name, sizeof(name), desc.Description, _TRUNCATE);
                m_deviceName = name;
            }
        }
    }

    m_initialized = true;
    core::Logger::SInfo("D3D11Context", "Initialized D3D11 Context on GPU: {}", m_deviceName);
    return {};
}

void D3D11Context::Shutdown() {
    if (m_context) {
        m_context->ClearState();
        m_context->Flush();
        m_context.Reset();
    }
    m_device.Reset();
    m_initialized = false;
}

void* D3D11Context::GetDeviceHandle() {
    return m_device.Get();
}

std::string D3D11Context::GetDeviceName() const {
    return m_deviceName;
}

core::VoidResult D3D11Context::UploadTexture(const uint8_t* data, int width, int height, core::PixelFormat format, void** outTextureHandle) {
    if (!m_initialized || !m_device) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "D3D11Context not initialized"));
    }

    if (!data || !outTextureHandle) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidArgument, "Invalid arguments"));
    }

    DXGI_FORMAT d3dFormat;
    UINT bindFlags = D3D11_BIND_SHADER_RESOURCE | D3D11_BIND_RENDER_TARGET;
    int bpp = 1;
    
    // For NV12, we usually upload Y and UV planes separately or use a DXGI_FORMAT_NV12 texture.
    // For simplicity, if we upload a single plane (e.g. Y or UV), we use R8_UNORM or R8G8_UNORM.
    if (format == core::PixelFormat::NV12) {
        // NV12 format requires specific handling or we create two textures (R8 and R8G8).
        // Let's assume this method is called per plane if we use R8/R8G8.
        return std::unexpected(core::Error::Make(core::ErrorCode::NotSupported, "Please upload planes separately for NV12"));
    } else if (format == core::PixelFormat::BGRA) {
        d3dFormat = DXGI_FORMAT_B8G8R8A8_UNORM;
        bpp = 4;
    } else {
        // Assume R8 for Y plane
        d3dFormat = DXGI_FORMAT_R8_UNORM;
        bpp = 1;
    }

    D3D11_TEXTURE2D_DESC desc = {};
    desc.Width = width;
    desc.Height = height;
    desc.MipLevels = 1;
    desc.ArraySize = 1;
    desc.Format = d3dFormat;
    desc.SampleDesc.Count = 1;
    desc.Usage = D3D11_USAGE_DEFAULT;
    desc.BindFlags = bindFlags;
    desc.CPUAccessFlags = 0;

    D3D11_SUBRESOURCE_DATA subData = {};
    subData.pSysMem = data;
    subData.SysMemPitch = width * bpp; // Assumes packed memory

    ID3D11Texture2D* texture = nullptr;
    HRESULT hr = m_device->CreateTexture2D(&desc, &subData, &texture);
    if (FAILED(hr)) {
        return std::unexpected(core::Error::Make(core::ErrorCode::Unknown, "Failed to create D3D11 Texture2D"));
    }

    *outTextureHandle = texture;
    return {};
}

core::VoidResult D3D11Context::DownloadTexture(void* textureHandle, uint8_t* outData, int width, int height, core::PixelFormat format) {
    if (!m_initialized || !m_context || !textureHandle || !outData) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidArgument, "Invalid arguments or context not initialized"));
    }

    ID3D11Texture2D* srcTexture = static_cast<ID3D11Texture2D*>(textureHandle);
    
    // To read back, we need a staging texture
    D3D11_TEXTURE2D_DESC srcDesc;
    srcTexture->GetDesc(&srcDesc);

    D3D11_TEXTURE2D_DESC stagingDesc = srcDesc;
    stagingDesc.Usage = D3D11_USAGE_STAGING;
    stagingDesc.BindFlags = 0;
    stagingDesc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    stagingDesc.MiscFlags = 0;

    Microsoft::WRL::ComPtr<ID3D11Texture2D> stagingTexture;
    HRESULT hr = m_device->CreateTexture2D(&stagingDesc, nullptr, &stagingTexture);
    if (FAILED(hr)) {
        return std::unexpected(core::Error::Make(core::ErrorCode::Unknown, "Failed to create D3D11 staging texture for download"));
    }

    m_context->CopyResource(stagingTexture.Get(), srcTexture);

    D3D11_MAPPED_SUBRESOURCE mapped;
    hr = m_context->Map(stagingTexture.Get(), 0, D3D11_MAP_READ, 0, &mapped);
    if (FAILED(hr)) {
        return std::unexpected(core::Error::Make(core::ErrorCode::Unknown, "Failed to map D3D11 staging texture"));
    }

    int bpp = (format == core::PixelFormat::BGRA) ? 4 : 1;
    for (int y = 0; y < height; ++y) {
        memcpy(outData + y * width * bpp, static_cast<uint8_t*>(mapped.pData) + y * mapped.RowPitch, width * bpp);
    }

    m_context->Unmap(stagingTexture.Get(), 0);
    return {};
}

core::VoidResult D3D11Context::CopyTexture(void* srcHandle, void* dstHandle) {
    if (!m_initialized || !m_context || !srcHandle || !dstHandle) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidArgument, "Invalid arguments or context not initialized"));
    }

    ID3D11Texture2D* srcTexture = static_cast<ID3D11Texture2D*>(srcHandle);
    ID3D11Texture2D* dstTexture = static_cast<ID3D11Texture2D*>(dstHandle);

    m_context->CopyResource(dstTexture, srcTexture);
    return {};
}

void D3D11Context::FreeTexture(void* textureHandle) {
    if (textureHandle) {
        ID3D11Texture2D* tex = static_cast<ID3D11Texture2D*>(textureHandle);
        tex->Release();
    }
}

// Helper struct for Vertex
struct Vertex {
    float x, y, z;
    float u, v;
};

static std::wstring StringToWString(const std::string& str) {
    int size_needed = MultiByteToWideChar(CP_UTF8, 0, &str[0], (int)str.size(), NULL, 0);
    std::wstring wstrTo(size_needed, 0);
    MultiByteToWideChar(CP_UTF8, 0, &str[0], (int)str.size(), &wstrTo[0], size_needed);
    return wstrTo;
}

core::VoidResult D3D11Context::InitMixerPipeline(const std::string& vsPath, const std::string& psPath) {
    if (!m_initialized || !m_device) return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "D3D11Context not initialized"));

    HRESULT hr;
    Microsoft::WRL::ComPtr<ID3DBlob> vsBlob, psBlob, errorBlob;
    
    // Compile VS
    hr = D3DCompileFromFile(StringToWString(vsPath).c_str(), nullptr, D3D_COMPILE_STANDARD_FILE_INCLUDE, "main", "vs_5_0", 0, 0, &vsBlob, &errorBlob);
    if (FAILED(hr)) {
        if (errorBlob) core::Logger::SError("D3D11", "VS Compile Error: {}", (char*)errorBlob->GetBufferPointer());
        return std::unexpected(core::Error::Make(core::ErrorCode::Unknown, "Failed to compile Vertex Shader"));
    }
    hr = m_device->CreateVertexShader(vsBlob->GetBufferPointer(), vsBlob->GetBufferSize(), nullptr, &m_mixerVS);
    if (FAILED(hr)) return std::unexpected(core::Error::Make(core::ErrorCode::Unknown, "Failed to create Vertex Shader"));

    // Compile PS
    hr = D3DCompileFromFile(StringToWString(psPath).c_str(), nullptr, D3D_COMPILE_STANDARD_FILE_INCLUDE, "main", "ps_5_0", 0, 0, &psBlob, &errorBlob);
    if (FAILED(hr)) {
        if (errorBlob) core::Logger::SError("D3D11", "PS Compile Error: {}", (char*)errorBlob->GetBufferPointer());
        return std::unexpected(core::Error::Make(core::ErrorCode::Unknown, "Failed to compile Pixel Shader"));
    }
    hr = m_device->CreatePixelShader(psBlob->GetBufferPointer(), psBlob->GetBufferSize(), nullptr, &m_mixerPS);
    if (FAILED(hr)) return std::unexpected(core::Error::Make(core::ErrorCode::Unknown, "Failed to create Pixel Shader"));

    // Create Input Layout
    D3D11_INPUT_ELEMENT_DESC layout[] = {
        { "POSITION", 0, DXGI_FORMAT_R32G32B32_FLOAT, 0, 0, D3D11_INPUT_PER_VERTEX_DATA, 0 },
        { "TEXCOORD", 0, DXGI_FORMAT_R32G32_FLOAT, 0, 12, D3D11_INPUT_PER_VERTEX_DATA, 0 }
    };
    hr = m_device->CreateInputLayout(layout, ARRAYSIZE(layout), vsBlob->GetBufferPointer(), vsBlob->GetBufferSize(), &m_mixerInputLayout);
    if (FAILED(hr)) return std::unexpected(core::Error::Make(core::ErrorCode::Unknown, "Failed to create Input Layout"));

    // Create Vertex Buffer (Full screen quad)
    Vertex vertices[] = {
        { -1.0f, -1.0f, 0.0f,  0.0f, 1.0f },
        { -1.0f,  1.0f, 0.0f,  0.0f, 0.0f },
        {  1.0f, -1.0f, 0.0f,  1.0f, 1.0f },
        {  1.0f,  1.0f, 0.0f,  1.0f, 0.0f }
    };
    D3D11_BUFFER_DESC bd = {};
    bd.Usage = D3D11_USAGE_DEFAULT;
    bd.ByteWidth = sizeof(vertices);
    bd.BindFlags = D3D11_BIND_VERTEX_BUFFER;
    D3D11_SUBRESOURCE_DATA initData = {};
    initData.pSysMem = vertices;
    hr = m_device->CreateBuffer(&bd, &initData, &m_mixerVertexBuffer);
    if (FAILED(hr)) return std::unexpected(core::Error::Make(core::ErrorCode::Unknown, "Failed to create Vertex Buffer"));

    // Create Constant Buffer
    D3D11_BUFFER_DESC cbd = {};
    cbd.Usage = D3D11_USAGE_DYNAMIC;
    cbd.ByteWidth = 16; // 1 float + 3 padding
    cbd.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
    cbd.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
    hr = m_device->CreateBuffer(&cbd, nullptr, &m_mixerConstantBuffer);
    if (FAILED(hr)) return std::unexpected(core::Error::Make(core::ErrorCode::Unknown, "Failed to create Constant Buffer"));

    // Create Sampler State
    D3D11_SAMPLER_DESC sampDesc = {};
    sampDesc.Filter = D3D11_FILTER_MIN_MAG_MIP_LINEAR;
    sampDesc.AddressU = D3D11_TEXTURE_ADDRESS_CLAMP;
    sampDesc.AddressV = D3D11_TEXTURE_ADDRESS_CLAMP;
    sampDesc.AddressW = D3D11_TEXTURE_ADDRESS_CLAMP;
    sampDesc.ComparisonFunc = D3D11_COMPARISON_NEVER;
    hr = m_device->CreateSamplerState(&sampDesc, &m_mixerSamplerState);
    if (FAILED(hr)) return std::unexpected(core::Error::Make(core::ErrorCode::Unknown, "Failed to create Sampler State"));

    return {};
}

core::VoidResult D3D11Context::ExecuteMixerPipeline(void* bgHandle, void* layerHandle, void* outHandle, float opacity) {
    if (!m_initialized || !m_context) return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "D3D11Context not initialized"));
    if (!m_mixerVS || !m_mixerPS) return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "Mixer pipeline not initialized"));

    ID3D11Texture2D* bgTex = static_cast<ID3D11Texture2D*>(bgHandle);
    ID3D11Texture2D* layerTex = static_cast<ID3D11Texture2D*>(layerHandle);
    ID3D11Texture2D* outTex = static_cast<ID3D11Texture2D*>(outHandle);

    // Create temporary SRVs and RTV (ideally we should cache these)
    Microsoft::WRL::ComPtr<ID3D11ShaderResourceView> bgSRV, layerSRV;
    Microsoft::WRL::ComPtr<ID3D11RenderTargetView> outRTV;

    HRESULT hr;
    hr = m_device->CreateShaderResourceView(bgTex, nullptr, &bgSRV);
    if (FAILED(hr)) return std::unexpected(core::Error::Make(core::ErrorCode::Unknown, "Failed to create SRV for background"));
    hr = m_device->CreateShaderResourceView(layerTex, nullptr, &layerSRV);
    if (FAILED(hr)) return std::unexpected(core::Error::Make(core::ErrorCode::Unknown, "Failed to create SRV for layer"));
    hr = m_device->CreateRenderTargetView(outTex, nullptr, &outRTV);
    if (FAILED(hr)) return std::unexpected(core::Error::Make(core::ErrorCode::Unknown, "Failed to create RTV for output"));

    // Update Constant Buffer
    D3D11_MAPPED_SUBRESOURCE mapped;
    hr = m_context->Map(m_mixerConstantBuffer.Get(), 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped);
    if (SUCCEEDED(hr)) {
        float* data = static_cast<float*>(mapped.pData);
        data[0] = opacity;
        data[1] = 0; data[2] = 0; data[3] = 0;
        m_context->Unmap(m_mixerConstantBuffer.Get(), 0);
    }

    // Set Pipeline State
    m_context->IASetInputLayout(m_mixerInputLayout.Get());
    UINT stride = sizeof(Vertex);
    UINT offset = 0;
    m_context->IASetVertexBuffers(0, 1, m_mixerVertexBuffer.GetAddressOf(), &stride, &offset);
    m_context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLESTRIP);

    m_context->VSSetShader(m_mixerVS.Get(), nullptr, 0);
    m_context->PSSetShader(m_mixerPS.Get(), nullptr, 0);
    m_context->PSSetConstantBuffers(0, 1, m_mixerConstantBuffer.GetAddressOf());

    ID3D11ShaderResourceView* srvs[] = { bgSRV.Get(), layerSRV.Get() };
    m_context->PSSetShaderResources(0, 2, srvs);
    m_context->PSSetSamplers(0, 1, m_mixerSamplerState.GetAddressOf());

    m_context->OMSetRenderTargets(1, outRTV.GetAddressOf(), nullptr);

    // Viewport
    D3D11_TEXTURE2D_DESC desc;
    outTex->GetDesc(&desc);
    D3D11_VIEWPORT vp = {};
    vp.Width = (float)desc.Width;
    vp.Height = (float)desc.Height;
    vp.MinDepth = 0.0f;
    vp.MaxDepth = 1.0f;
    vp.TopLeftX = 0;
    vp.TopLeftY = 0;
    m_context->RSSetViewports(1, &vp);

    // Draw
    m_context->Draw(4, 0);

    // Cleanup Pipeline State
    ID3D11ShaderResourceView* nullSRVs[] = { nullptr, nullptr };
    m_context->PSSetShaderResources(0, 2, nullSRVs);
    m_context->OMSetRenderTargets(0, nullptr, nullptr);

    return {};
}

} // namespace openmedia::gpu
