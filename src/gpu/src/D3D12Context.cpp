#include "openmedia/gpu/D3D12Context.h"

#include <d3d12.h>
#include <dxgi1_4.h>
#include <wrl/client.h>

#pragma comment(lib, "d3d12.lib")
#pragma comment(lib, "dxgi.lib")

using Microsoft::WRL::ComPtr;

namespace openmedia::gpu {

D3D12Context::D3D12Context() : m_deviceName("D3D12 Stub Device") {}

D3D12Context::~D3D12Context() {
    Shutdown();
}

core::VoidResult D3D12Context::Initialize() {
    if (m_initialized) return {};

    ComPtr<IDXGIFactory4> factory;
    if (FAILED(CreateDXGIFactory1(IID_PPV_ARGS(&factory)))) {
        return std::unexpected(core::Error::Make(core::ErrorCode::GPUNotAvailable, "Failed to create DXGI Factory"));
    }

    ComPtr<IDXGIAdapter1> hardwareAdapter;
    for (UINT adapterIndex = 0; DXGI_ERROR_NOT_FOUND != factory->EnumAdapters1(adapterIndex, &hardwareAdapter); ++adapterIndex) {
        DXGI_ADAPTER_DESC1 desc;
        hardwareAdapter->GetDesc1(&desc);
        if (desc.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) continue;
        if (SUCCEEDED(D3D12CreateDevice(hardwareAdapter.Get(), D3D_FEATURE_LEVEL_11_0, _uuidof(ID3D12Device), nullptr))) {
            char name[128];
            size_t converted;
            wcstombs_s(&converted, name, sizeof(name), desc.Description, _TRUNCATE);
            m_deviceName = name;
            break;
        }
    }

    if (!hardwareAdapter) {
        return std::unexpected(core::Error::Make(core::ErrorCode::GPUNotAvailable, "Failed to find D3D12 adapter"));
    }

    if (FAILED(D3D12CreateDevice(hardwareAdapter.Get(), D3D_FEATURE_LEVEL_11_0, IID_PPV_ARGS(&m_device)))) {
        return std::unexpected(core::Error::Make(core::ErrorCode::GPUNotAvailable, "Failed to create D3D12 device"));
    }

    D3D12_COMMAND_QUEUE_DESC queueDesc = {};
    queueDesc.Flags = D3D12_COMMAND_QUEUE_FLAG_NONE;
    queueDesc.Type = D3D12_COMMAND_LIST_TYPE_DIRECT;

    if (FAILED(m_device->CreateCommandQueue(&queueDesc, IID_PPV_ARGS(&m_commandQueue)))) {
        return std::unexpected(core::Error::Make(core::ErrorCode::GPUNotAvailable, "Failed to create command queue"));
    }

    m_initialized = true;
    return {};
}

void D3D12Context::Shutdown() {
    if (!m_initialized) return;

    if (m_commandQueue) {
        m_commandQueue->Release();
        m_commandQueue = nullptr;
    }
    if (m_device) {
        m_device->Release();
        m_device = nullptr;
    }

    m_initialized = false;
}

void* D3D12Context::GetDeviceHandle() {
    return m_device;
}

std::string D3D12Context::GetDeviceName() const {
    return m_deviceName;
}

core::VoidResult D3D12Context::UploadTexture(const uint8_t* data, int width, int height, core::PixelFormat format, void** outTextureHandle) {
    return std::unexpected(core::Error::Make(core::ErrorCode::NotImplemented, "D3D12 UploadTexture not implemented"));
}

core::VoidResult D3D12Context::DownloadTexture(void* textureHandle, uint8_t* outData, int width, int height, core::PixelFormat format) {
    return std::unexpected(core::Error::Make(core::ErrorCode::NotImplemented, "D3D12 DownloadTexture not implemented"));
}

core::VoidResult D3D12Context::CopyTexture(void* srcHandle, void* dstHandle) {
    return std::unexpected(core::Error::Make(core::ErrorCode::NotImplemented, "D3D12 CopyTexture not implemented"));
}

void D3D12Context::FreeTexture(void* textureHandle) {
    // Stub
}

core::VoidResult D3D12Context::InitMixerPipeline(const std::string& vsPath, const std::string& psPath) {
    return std::unexpected(core::Error::Make(core::ErrorCode::NotImplemented, "D3D12 InitMixerPipeline not implemented"));
}

core::VoidResult D3D12Context::ExecuteMixerPipeline(void* bgHandle, void* layerHandle, void* outHandle, float opacity) {
    return std::unexpected(core::Error::Make(core::ErrorCode::NotImplemented, "D3D12 ExecuteMixerPipeline not implemented"));
}

} // namespace openmedia::gpu
