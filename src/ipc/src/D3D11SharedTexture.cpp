/// @file D3D11SharedTexture.cpp
/// @brief D3D11 shared texture pool implementation

#include <openmedia/ipc/D3D11SharedTexture.h>
#include <openmedia/core/Logger.h>

#ifdef _WIN32
#include <d3d11.h>
#include <dxgi.h>
#include <wrl/client.h>
#endif

#include <mutex>
#include <vector>

namespace openmedia::ipc {

namespace {
auto& Log() { return core::Logger::Get("ipc.d3d11"); }
}

#ifdef _WIN32
using Microsoft::WRL::ComPtr;
#endif

struct D3D11SharedTexturePool::Impl {
#ifdef _WIN32
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> context;
#endif

    SharedTexturePoolConfig config;
    bool initialized = false;

    struct TextureSlot {
#ifdef _WIN32
        ComPtr<ID3D11Texture2D> texture;
        ComPtr<IDXGIKeyedMutex> keyedMutex;
#endif
        SharedTextureSlot metadata;
    };

    std::vector<TextureSlot> slots;
    mutable std::mutex mutex;
};

D3D11SharedTexturePool::D3D11SharedTexturePool(
    ID3D11Device* device, const SharedTexturePoolConfig& config)
    : m_impl(std::make_unique<Impl>()) {
    m_impl->config = config;
#ifdef _WIN32
    m_impl->device = device;
    if (device) {
        device->GetImmediateContext(m_impl->context.GetAddressOf());
    }
#endif
}

D3D11SharedTexturePool::~D3D11SharedTexturePool() {
    Destroy();
}

core::VoidResult D3D11SharedTexturePool::Initialize() {
#ifdef _WIN32
    std::lock_guard lock(m_impl->mutex);

    if (!m_impl->device) {
        return std::unexpected(core::Error{
            core::ErrorCode::InvalidArgument,
            "D3D11 device is null",
            "D3D11SharedTexturePool"});
    }

    m_impl->slots.resize(m_impl->config.poolSize);

    for (uint32_t i = 0; i < m_impl->config.poolSize; ++i) {
        auto& slot = m_impl->slots[i];

        D3D11_TEXTURE2D_DESC desc = {};
        desc.Width = m_impl->config.textureDesc.width;
        desc.Height = m_impl->config.textureDesc.height;
        desc.MipLevels = 1;
        desc.ArraySize = 1;
        desc.SampleDesc.Count = 1;
        desc.Usage = D3D11_USAGE_DEFAULT;
        desc.BindFlags = D3D11_BIND_SHADER_RESOURCE | D3D11_BIND_RENDER_TARGET;
        desc.MiscFlags = D3D11_RESOURCE_MISC_SHARED_KEYEDMUTEX;

        switch (m_impl->config.textureDesc.format) {
            case SharedTextureFormat::BGRA8:
                desc.Format = DXGI_FORMAT_B8G8R8A8_UNORM; break;
            case SharedTextureFormat::NV12:
                desc.Format = DXGI_FORMAT_NV12; break;
            case SharedTextureFormat::RGBA8:
                desc.Format = DXGI_FORMAT_R8G8B8A8_UNORM; break;
            case SharedTextureFormat::RGBA16F:
                desc.Format = DXGI_FORMAT_R16G16B16A16_FLOAT; break;
            case SharedTextureFormat::R10G10B10A2:
                desc.Format = DXGI_FORMAT_R10G10B10A2_UNORM; break;
        }

        HRESULT hr = m_impl->device->CreateTexture2D(
            &desc, nullptr, slot.texture.GetAddressOf());
        if (FAILED(hr)) {
            return std::unexpected(core::Error{
                core::ErrorCode::GPUContextFailed,
                "Failed to create shared texture (slot " + std::to_string(i) + ")",
                "D3D11SharedTexturePool"});
        }

        hr = slot.texture.As(&slot.keyedMutex);
        if (FAILED(hr)) {
            return std::unexpected(core::Error{
                core::ErrorCode::GPUContextFailed,
                "Failed to get keyed mutex (slot " + std::to_string(i) + ")",
                "D3D11SharedTexturePool"});
        }

        ComPtr<IDXGIResource> dxgiResource;
        hr = slot.texture.As(&dxgiResource);
        if (FAILED(hr)) {
            return std::unexpected(core::Error{
                core::ErrorCode::GPUContextFailed,
                "Failed to get DXGI resource (slot " + std::to_string(i) + ")",
                "D3D11SharedTexturePool"});
        }

        HANDLE sharedHandle = nullptr;
        hr = dxgiResource->GetSharedHandle(&sharedHandle);
        if (FAILED(hr)) {
            return std::unexpected(core::Error{
                core::ErrorCode::GPUContextFailed,
                "Failed to get shared handle (slot " + std::to_string(i) + ")",
                "D3D11SharedTexturePool"});
        }

        slot.metadata.sharedHandle = reinterpret_cast<uint64_t>(sharedHandle);
        slot.metadata.desc = m_impl->config.textureDesc;
        slot.metadata.isValid = false;
    }

    m_impl->initialized = true;
    Log().Info("D3D11SharedTexturePool initialized: {} slots, {}x{}",
              m_impl->config.poolSize,
              m_impl->config.textureDesc.width,
              m_impl->config.textureDesc.height);

    return {};
#else
    return std::unexpected(core::Error{
        core::ErrorCode::NotSupported,
        "D3D11 shared textures not supported on this platform",
        "D3D11SharedTexturePool"});
#endif
}

core::VoidResult D3D11SharedTexturePool::OpenShared(
    const std::vector<SharedTextureSlot>& slots) {
#ifdef _WIN32
    std::lock_guard lock(m_impl->mutex);

    if (!m_impl->device) {
        return std::unexpected(core::Error{
            core::ErrorCode::InvalidArgument,
            "D3D11 device is null",
            "D3D11SharedTexturePool"});
    }

    m_impl->slots.resize(slots.size());

    for (size_t i = 0; i < slots.size(); ++i) {
        auto& slot = m_impl->slots[i];
        slot.metadata = slots[i];

        auto sharedHandle = reinterpret_cast<HANDLE>(slots[i].sharedHandle);
        HRESULT hr = m_impl->device->OpenSharedResource(
            sharedHandle,
            __uuidof(ID3D11Texture2D),
            reinterpret_cast<void**>(slot.texture.GetAddressOf()));
        if (FAILED(hr)) {
            return std::unexpected(core::Error{
                core::ErrorCode::GPUContextFailed,
                "Failed to open shared texture (slot " + std::to_string(i) + ")",
                "D3D11SharedTexturePool"});
        }

        hr = slot.texture.As(&slot.keyedMutex);
        if (FAILED(hr)) {
            return std::unexpected(core::Error{
                core::ErrorCode::GPUContextFailed,
                "Failed to get keyed mutex from shared texture (slot " + std::to_string(i) + ")",
                "D3D11SharedTexturePool"});
        }
    }

    m_impl->initialized = true;
    Log().Info("D3D11SharedTexturePool opened {} shared textures", slots.size());

    return {};
#else
    return std::unexpected(core::Error{
        core::ErrorCode::NotSupported,
        "D3D11 shared textures not supported on this platform",
        "D3D11SharedTexturePool"});
#endif
}

void D3D11SharedTexturePool::Destroy() {
    std::lock_guard lock(m_impl->mutex);
    m_impl->slots.clear();
    m_impl->initialized = false;
}

bool D3D11SharedTexturePool::IsInitialized() const {
    return m_impl->initialized;
}

ID3D11Texture2D* D3D11SharedTexturePool::AcquireForWrite(uint32_t slotIndex) {
#ifdef _WIN32
    std::lock_guard lock(m_impl->mutex);
    if (slotIndex >= m_impl->slots.size()) return nullptr;

    auto& slot = m_impl->slots[slotIndex];
    if (!slot.keyedMutex) return nullptr;

    HRESULT hr = slot.keyedMutex->AcquireSync(
        0, static_cast<DWORD>(m_impl->config.acquireTimeoutMs));
    if (FAILED(hr)) {
        Log().Warn("Failed to acquire keyed mutex for write (slot {})", slotIndex);
        return nullptr;
    }

    return slot.texture.Get();
#else
    return nullptr;
#endif
}

void D3D11SharedTexturePool::ReleaseAfterWrite(
    uint32_t slotIndex, uint64_t pts, uint64_t frameNumber, bool isKeyFrame) {
#ifdef _WIN32
    std::lock_guard lock(m_impl->mutex);
    if (slotIndex >= m_impl->slots.size()) return;

    auto& slot = m_impl->slots[slotIndex];
    slot.metadata.pts = pts;
    slot.metadata.frameNumber = frameNumber;
    slot.metadata.isKeyFrame = isKeyFrame;
    slot.metadata.isValid = true;

    if (slot.keyedMutex) {
        slot.keyedMutex->ReleaseSync(1);
    }
#endif
}

ID3D11Texture2D* D3D11SharedTexturePool::AcquireForRead(uint32_t slotIndex) {
#ifdef _WIN32
    std::lock_guard lock(m_impl->mutex);
    if (slotIndex >= m_impl->slots.size()) return nullptr;

    auto& slot = m_impl->slots[slotIndex];
    if (!slot.keyedMutex) return nullptr;

    HRESULT hr = slot.keyedMutex->AcquireSync(
        1, static_cast<DWORD>(m_impl->config.acquireTimeoutMs));
    if (FAILED(hr)) {
        return nullptr;
    }

    return slot.texture.Get();
#else
    return nullptr;
#endif
}

void D3D11SharedTexturePool::ReleaseAfterRead(uint32_t slotIndex) {
#ifdef _WIN32
    std::lock_guard lock(m_impl->mutex);
    if (slotIndex >= m_impl->slots.size()) return;

    auto& slot = m_impl->slots[slotIndex];
    if (slot.keyedMutex) {
        slot.keyedMutex->ReleaseSync(0);
    }
#endif
}

SharedTextureSlot D3D11SharedTexturePool::GetSlotInfo(uint32_t slotIndex) const {
    std::lock_guard lock(m_impl->mutex);
    if (slotIndex >= m_impl->slots.size()) return {};
    return m_impl->slots[slotIndex].metadata;
}

std::vector<SharedTextureSlot> D3D11SharedTexturePool::GetAllSlots() const {
    std::lock_guard lock(m_impl->mutex);
    std::vector<SharedTextureSlot> result;
    result.reserve(m_impl->slots.size());
    for (const auto& slot : m_impl->slots) {
        result.push_back(slot.metadata);
    }
    return result;
}

uint32_t D3D11SharedTexturePool::GetPoolSize() const {
    return m_impl->config.poolSize;
}

SharedTextureDesc D3D11SharedTexturePool::GetTextureDesc() const {
    return m_impl->config.textureDesc;
}

} // namespace openmedia::ipc
