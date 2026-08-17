#pragma once

/// @file D3D11SharedTexture.h
/// @brief D3D11 shared texture pool for cross-process GPU frame sharing
/// @since 1.0.0

#include <openmedia/core/ErrorCodes.h>
#include <openmedia/core/Types.h>

#include <cstdint>
#include <memory>
#include <string>
#include <vector>

// Forward declarations — avoid pulling in D3D11 headers in public header
struct ID3D11Device;
struct ID3D11DeviceContext;
struct ID3D11Texture2D;
struct IDXGIKeyedMutex;

namespace openmedia::ipc {

/// @brief Texture format for shared textures
enum class SharedTextureFormat : uint32_t {
    BGRA8 = 0,     ///< 8-bit BGRA (DXGI_FORMAT_B8G8R8A8_UNORM)
    NV12,           ///< NV12 planar (DXGI_FORMAT_NV12)
    RGBA8,          ///< 8-bit RGBA (DXGI_FORMAT_R8G8B8A8_UNORM)
    RGBA16F,        ///< 16-bit float RGBA (DXGI_FORMAT_R16G16B16A16_FLOAT)
    R10G10B10A2,    ///< 10-bit RGB + 2-bit alpha
};

/// @brief Descriptor for a shared texture
struct SharedTextureDesc {
    uint32_t width = 1920;
    uint32_t height = 1080;
    SharedTextureFormat format = SharedTextureFormat::BGRA8;
};

/// @brief Metadata associated with a shared texture slot
struct SharedTextureSlot {
    uint64_t sharedHandle = 0;      ///< DXGI shared handle (HANDLE cast to uint64_t)
    SharedTextureDesc desc;         ///< Texture descriptor
    uint64_t pts = 0;               ///< Presentation timestamp (microseconds)
    uint64_t frameNumber = 0;       ///< Frame counter
    bool isKeyFrame = false;        ///< Key frame flag
    bool isValid = false;           ///< Slot has valid data
};

/// @brief Configuration for the shared texture pool
struct SharedTexturePoolConfig {
    SharedTextureDesc textureDesc;
    uint32_t poolSize = 4;          ///< Number of textures in the pool
    uint64_t acquireTimeoutMs = 100; ///< Timeout for acquiring keyed mutex
};

/// @brief Cross-process D3D11 shared texture pool
///
/// Manages a pool of D3D11 textures shared between server and client
/// processes via DXGI shared handles and keyed mutex synchronization.
///
/// Server creates textures with `D3D11_RESOURCE_MISC_SHARED_KEYEDMUTEX`,
/// client opens them via `ID3D11Device::OpenSharedResource()`.
///
/// @code
/// // Server side — create pool and write frames
/// D3D11SharedTexturePool pool(device, {.poolSize = 4});
/// pool.Initialize();
///
/// auto slot = pool.AcquireForWrite(0);
/// // copy frame into slot.texture
/// pool.ReleaseAfterWrite(0);
///
/// // Client side — open and read frames
/// D3D11SharedTexturePool clientPool(clientDevice, config);
/// clientPool.OpenShared(serverSlots);
///
/// auto slot = clientPool.AcquireForRead(0);
/// // use slot.texture
/// clientPool.ReleaseAfterRead(0);
/// @endcode
class D3D11SharedTexturePool {
public:
    explicit D3D11SharedTexturePool(ID3D11Device* device,
                                     const SharedTexturePoolConfig& config = {});
    ~D3D11SharedTexturePool();

    D3D11SharedTexturePool(const D3D11SharedTexturePool&) = delete;
    D3D11SharedTexturePool& operator=(const D3D11SharedTexturePool&) = delete;

    // --- Lifecycle ---

    /// @brief Initialize the pool (create textures — server side)
    [[nodiscard]] core::VoidResult Initialize();

    /// @brief Open shared textures from handles (client side)
    [[nodiscard]] core::VoidResult OpenShared(
        const std::vector<SharedTextureSlot>& slots);

    /// @brief Destroy all textures and release resources
    void Destroy();

    /// @brief Check if pool is initialized
    [[nodiscard]] bool IsInitialized() const;

    // --- Producer (Server) ---

    /// @brief Acquire a texture slot for writing (locks keyed mutex)
    /// @param slotIndex Index into the pool [0, poolSize)
    /// @return Pointer to the D3D11 texture, or nullptr on failure
    [[nodiscard]] ID3D11Texture2D* AcquireForWrite(uint32_t slotIndex);

    /// @brief Release a texture slot after writing (unlocks keyed mutex)
    void ReleaseAfterWrite(uint32_t slotIndex, uint64_t pts, uint64_t frameNumber, bool isKeyFrame);

    // --- Consumer (Client) ---

    /// @brief Acquire a texture slot for reading (locks keyed mutex)
    [[nodiscard]] ID3D11Texture2D* AcquireForRead(uint32_t slotIndex);

    /// @brief Release a texture slot after reading (unlocks keyed mutex)
    void ReleaseAfterRead(uint32_t slotIndex);

    // --- Info ---

    /// @brief Get slot metadata
    [[nodiscard]] SharedTextureSlot GetSlotInfo(uint32_t slotIndex) const;

    /// @brief Get all slot info (for sharing with client)
    [[nodiscard]] std::vector<SharedTextureSlot> GetAllSlots() const;

    /// @brief Get pool size
    [[nodiscard]] uint32_t GetPoolSize() const;

    /// @brief Get texture descriptor
    [[nodiscard]] SharedTextureDesc GetTextureDesc() const;

private:
    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

} // namespace openmedia::ipc
