#pragma once

/// @file SharedMemoryBuffer.h
/// @brief Shared memory buffer for zero-copy frame transfer between processes
/// @since 1.0.0

#include <openmedia/core/ErrorCodes.h>
#include <openmedia/core/Types.h>

#include <cstdint>
#include <memory>
#include <string>

namespace openmedia::ipc {

/// @brief Configuration for shared memory buffer
struct SharedMemoryConfig {
    std::string name = "OpenMedia_SharedMem";  ///< Named shared memory identifier
    uint32_t frameSlotCount = 8;               ///< Number of frame slots in ring buffer
    uint32_t maxFrameSize = 1920 * 1080 * 4;   ///< Max bytes per frame (BGRA 1080p default)
    bool enableGPUSharing = true;               ///< Enable D3D11 shared textures
};

/// @brief Frame slot metadata in shared memory header
struct FrameSlotHeader {
    uint64_t pts = 0;                   ///< Presentation timestamp
    uint64_t dts = 0;                   ///< Decode timestamp
    uint32_t width = 0;                 ///< Frame width
    uint32_t height = 0;                ///< Frame height
    uint32_t pixelFormat = 0;           ///< PixelFormat enum value
    uint32_t lineSize = 0;              ///< Bytes per scanline
    uint32_t dataSize = 0;              ///< Actual data size
    uint32_t sequenceNumber = 0;        ///< Frame sequence
    uint32_t flags = 0;                 ///< Frame flags (keyframe, etc.)
    std::atomic<uint32_t> state{0};     ///< 0=empty, 1=writing, 2=ready, 3=reading
};

/// @brief Shared memory ring buffer for frame data
///
/// Layout:
/// ```
/// [RingBufferHeader][FrameSlot0][FrameSlot1]...[FrameSlotN-1]
///  Each slot: [FrameSlotHeader][frame data bytes...]
/// ```
///
/// Producer (server) writes frames, consumer (client) reads frames.
/// Zero-copy: client maps the same memory, reads directly.
class SharedMemoryBuffer {
public:
    /// @brief Create/open shared memory (server creates, client opens)
    /// @param config Buffer configuration
    /// @param isCreator true for server (creates), false for client (opens)
    SharedMemoryBuffer(const SharedMemoryConfig& config, bool isCreator);
    ~SharedMemoryBuffer();

    SharedMemoryBuffer(const SharedMemoryBuffer&) = delete;
    SharedMemoryBuffer& operator=(const SharedMemoryBuffer&) = delete;

    /// @brief Initialize the shared memory region
    [[nodiscard]] core::VoidResult Initialize();

    /// @brief Close and release the shared memory
    void Close();

    /// @brief Check if initialized
    [[nodiscard]] bool IsInitialized() const;

    // --- Producer (Server) API ---

    /// @brief Acquire a writable frame slot
    /// @return Pointer to frame data buffer, or nullptr if no slot available
    [[nodiscard]] uint8_t* AcquireWriteSlot(uint32_t& slotIndex);

    /// @brief Commit a written frame slot (makes it available for reading)
    void CommitWriteSlot(uint32_t slotIndex, const FrameSlotHeader& metadata);

    // --- Consumer (Client) API ---

    /// @brief Acquire a readable frame slot
    /// @return Pointer to frame data buffer, or nullptr if no frame ready
    [[nodiscard]] const uint8_t* AcquireReadSlot(uint32_t& slotIndex,
                                                  FrameSlotHeader& metadata);

    /// @brief Release a read slot (makes it available for writing again)
    void ReleaseReadSlot(uint32_t slotIndex);

    // --- Info ---

    /// @brief Get total buffer size
    [[nodiscard]] size_t GetTotalSize() const;

    /// @brief Get number of frame slots
    [[nodiscard]] uint32_t GetSlotCount() const;

    /// @brief Get maximum frame size per slot
    [[nodiscard]] uint32_t GetMaxFrameSize() const;

    /// @brief Get current write position
    [[nodiscard]] uint32_t GetWritePosition() const;

    /// @brief Get current read position
    [[nodiscard]] uint32_t GetReadPosition() const;

    /// @brief Get the shared memory name
    [[nodiscard]] std::string GetName() const;

private:
    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

} // namespace openmedia::ipc
