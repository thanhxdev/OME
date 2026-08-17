#include <openmedia/ipc/D3D11SharedTexturePoolPoC.h>
#include <iostream>

#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "dxgi.lib")

namespace openmedia {
namespace ipc {

    D3D11SharedTexturePoolPoC::D3D11SharedTexturePoolPoC() {
    }

    D3D11SharedTexturePoolPoC::~D3D11SharedTexturePoolPoC() {
        for (size_t i = 0; i < POOL_SIZE; ++i) {
            if (m_sharedHandles[i]) {
                CloseHandle(m_sharedHandles[i]);
                m_sharedHandles[i] = nullptr;
            }
        }
        m_width = 0;
        m_height = 0;
    }

    bool D3D11SharedTexturePoolPoC::Initialize(uint32_t width, uint32_t height, DXGI_FORMAT format) {
        m_width = width;
        m_height = height;

        // 1. Create Device
        UINT creationFlags = D3D11_CREATE_DEVICE_BGRA_SUPPORT;

        HRESULT hr = D3D11CreateDevice(
            nullptr,                    // Default adapter
            D3D_DRIVER_TYPE_HARDWARE,
            nullptr,
            creationFlags,
            nullptr, 0,                 // Default feature levels
            D3D11_SDK_VERSION,
            &m_device,
            nullptr,
            &m_context
        );

        if (FAILED(hr)) {
            std::cerr << "Failed to create D3D11 Device. HR: " << std::hex << hr << "\n";
            return false;
        }

        hr = m_device.As(&m_device1);
        if (FAILED(hr)) {
            std::cerr << "Failed to query ID3D11Device1. HR: " << std::hex << hr << "\n";
            return false;
        }

        // 2. Create Textures with SHARED flags
        D3D11_TEXTURE2D_DESC desc = {};
        desc.Width = width;
        desc.Height = height;
        desc.MipLevels = 1;
        desc.ArraySize = 1;
        desc.Format = format;
        desc.SampleDesc.Count = 1;
        desc.SampleDesc.Quality = 0;
        desc.Usage = D3D11_USAGE_DEFAULT;
        desc.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
        desc.CPUAccessFlags = 0;
        desc.MiscFlags = D3D11_RESOURCE_MISC_SHARED;

        for (size_t i = 0; i < POOL_SIZE; ++i) {
            hr = m_device1->CreateTexture2D(&desc, nullptr, &m_textures[i]);
            if (FAILED(hr)) {
                std::cerr << "Failed to create shared texture " << i << ". HR: " << std::hex << hr << "\n";
                return false;
            }

            // 3. Get IDXGIResource
            hr = m_textures[i].As(&m_dxgiResources[i]);
            if (FAILED(hr)) {
                std::cerr << "Failed to query IDXGIResource for texture " << i << ". HR: " << std::hex << hr << "\n";
                return false;
            }

            // 4. Get KMT Shared Handle (works cross-process with OpenSharedResource)
            hr = m_dxgiResources[i]->GetSharedHandle(&m_sharedHandles[i]);
            if (FAILED(hr) || m_sharedHandles[i] == nullptr) {
                std::cerr << "Failed to get shared handle for texture " << i << ". HR: " << std::hex << hr << "\n";
                return false;
            }
        }

        return true;
    }

    void D3D11SharedTexturePoolPoC::GetSharedHandles(HANDLE handlesOut[POOL_SIZE]) const {
        for (size_t i = 0; i < POOL_SIZE; ++i) {
            handlesOut[i] = m_sharedHandles[i];
        }
    }

    bool D3D11SharedTexturePoolPoC::AcquireWriteLock(size_t index, DWORD timeoutMs) {
        return true;
    }

    void D3D11SharedTexturePoolPoC::ReleaseWriteLock(size_t index) {
        if (m_context) {
            m_context->Flush();
        }
    }

    void D3D11SharedTexturePoolPoC::UpdateFrame(size_t index, const uint8_t* bgraData, uint32_t rowPitch) {
        if (index >= POOL_SIZE || !m_context || !m_textures[index] || !bgraData) return;
        m_context->UpdateSubresource(m_textures[index].Get(), 0, nullptr, bgraData, rowPitch, 0);
    }

} // namespace ipc
} // namespace openmedia
