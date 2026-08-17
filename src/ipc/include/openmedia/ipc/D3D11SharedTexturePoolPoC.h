#pragma once

#include <d3d11_1.h>
#include <dxgi1_2.h>
#include <wrl/client.h>
#include <cstdint>
#include <array>

namespace openmedia {
namespace ipc {

    // Double buffering constant
    constexpr size_t POOL_SIZE = 2;

    class D3D11SharedTexturePoolPoC {
    public:
        // Prevent copying
        D3D11SharedTexturePoolPoC(const D3D11SharedTexturePoolPoC&) = delete;
        D3D11SharedTexturePoolPoC& operator=(const D3D11SharedTexturePoolPoC&) = delete;

        D3D11SharedTexturePoolPoC();
        ~D3D11SharedTexturePoolPoC();

        // Initialize device and create shared textures for the pool
        bool Initialize(uint32_t width, uint32_t height, DXGI_FORMAT format = DXGI_FORMAT_B8G8R8A8_UNORM);

        // Get the NT Handles for the shared textures (to be sent over IPC)
        void GetSharedHandles(HANDLE handlesOut[POOL_SIZE]) const;

        // Acquire the keyed mutex for writing (Key = 0)
        bool AcquireWriteLock(size_t index, DWORD timeoutMs = INFINITE);

        // Update texture with BGRA data
        void UpdateFrame(size_t index, const uint8_t* bgraData, uint32_t rowPitch);

        // Release the keyed mutex after writing, unlocking for read (Key = 1)
        void ReleaseWriteLock(size_t index);

        // Access the underlying texture
        Microsoft::WRL::ComPtr<ID3D11Texture2D> GetTexture(size_t index) const { return m_textures[index]; }
        Microsoft::WRL::ComPtr<ID3D11DeviceContext> GetContext() const { return m_context; }

    private:
        Microsoft::WRL::ComPtr<ID3D11Device> m_device;
        Microsoft::WRL::ComPtr<ID3D11Device1> m_device1;
        Microsoft::WRL::ComPtr<ID3D11DeviceContext> m_context;

        std::array<Microsoft::WRL::ComPtr<ID3D11Texture2D>, POOL_SIZE> m_textures;
        std::array<Microsoft::WRL::ComPtr<IDXGIResource1>, POOL_SIZE> m_dxgiResources;
        std::array<Microsoft::WRL::ComPtr<IDXGIKeyedMutex>, POOL_SIZE> m_keyedMutexes;
        std::array<HANDLE, POOL_SIZE> m_sharedHandles = {nullptr, nullptr};

        uint32_t m_width = 0;
        uint32_t m_height = 0;
    };

} // namespace ipc
} // namespace openmedia
