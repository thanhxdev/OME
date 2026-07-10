#pragma once

/// @file MemoryPool.h
/// @brief Pre-allocated memory pool with slab allocator
/// @since 1.0.0

#include <cstddef>
#include <cstdint>
#include <memory>
#include <mutex>
#include <vector>

namespace openmedia::core {

/// @brief Pre-allocated memory pool for high-performance frame allocation
///
/// Avoids dynamic allocation in hot paths by pre-allocating memory slabs.
/// Thread-safe with lock-free fast path when possible.
///
/// @code
/// MemoryPool pool(1920 * 1080 * 4, 8);  // 8 BGRA frames
/// auto block = pool.Allocate();
/// // ... use block.data ...
/// pool.Release(std::move(block));
/// @endcode
class MemoryPool {
public:
    /// @brief A block of memory from the pool
    struct Block {
        uint8_t* data = nullptr;    ///< Pointer to allocated memory
        size_t size = 0;            ///< Size of the block
        uint32_t index = 0;         ///< Pool slot index

        [[nodiscard]] bool IsValid() const { return data != nullptr; }
    };

    /// @brief Create a memory pool
    /// @param blockSize Size of each block in bytes
    /// @param blockCount Number of pre-allocated blocks
    MemoryPool(size_t blockSize, uint32_t blockCount);
    ~MemoryPool();

    MemoryPool(const MemoryPool&) = delete;
    MemoryPool& operator=(const MemoryPool&) = delete;
    MemoryPool(MemoryPool&&) noexcept;
    MemoryPool& operator=(MemoryPool&&) noexcept;

    /// @brief Allocate a block from the pool
    /// @return A memory block, or invalid block if pool is exhausted
    [[nodiscard]] Block Allocate();

    /// @brief Release a block back to the pool
    void Release(Block block);

    /// @brief Get pool statistics
    [[nodiscard]] uint32_t GetTotalBlocks() const;
    [[nodiscard]] uint32_t GetAvailableBlocks() const;
    [[nodiscard]] uint32_t GetUsedBlocks() const;
    [[nodiscard]] size_t GetBlockSize() const;
    [[nodiscard]] size_t GetTotalMemory() const;

private:
    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

} // namespace openmedia::core
