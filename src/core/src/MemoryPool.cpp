/// @file MemoryPool.cpp
/// @brief High-performance lock-free aligned slab memory pool implementation

#include <openmedia/core/MemoryPool.h>

#if __has_include(<moodycamel/concurrentqueue.h>)
#include <moodycamel/concurrentqueue.h>
#elif __has_include(<concurrentqueue/moodycamel/concurrentqueue.h>)
#include <concurrentqueue/moodycamel/concurrentqueue.h>
#elif __has_include(<concurrentqueue.h>)
#include <concurrentqueue.h>
#endif

#include <atomic>
#include <cstdlib>
#include <cstring>

namespace openmedia::core {

struct MemoryPool::Impl {
    size_t rawBlockSize = 0;
    size_t alignedBlockSize = 0;
    uint32_t blockCount = 0;
    uint8_t* memory = nullptr;
    moodycamel::ConcurrentQueue<uint32_t> freeSlots;
    std::atomic<uint32_t> usedCount{0};

    Impl(size_t blockSize, uint32_t count)
        : rawBlockSize(blockSize), blockCount(count) {
        // 64-byte cacheline / AVX-512 alignment
        alignedBlockSize = (blockSize + 63) & ~size_t(63);
        size_t totalBytes = alignedBlockSize * count;

#if defined(_MSC_VER) || defined(__MINGW32__)
        memory = static_cast<uint8_t*>(_aligned_malloc(totalBytes, 64));
#else
        memory = static_cast<uint8_t*>(std::aligned_alloc(64, totalBytes));
#endif
        if (memory) {
            std::memset(memory, 0, totalBytes);
        }

        for (uint32_t i = 0; i < count; ++i) {
            freeSlots.enqueue(i);
        }
    }

    ~Impl() {
        if (memory) {
#if defined(_MSC_VER) || defined(__MINGW32__)
            _aligned_free(memory);
#else
            std::free(memory);
#endif
            memory = nullptr;
        }
    }
};

MemoryPool::MemoryPool(size_t blockSize, uint32_t blockCount)
    : m_impl(std::make_unique<Impl>(blockSize, blockCount)) {}

MemoryPool::~MemoryPool() = default;
MemoryPool::MemoryPool(MemoryPool&&) noexcept = default;
MemoryPool& MemoryPool::operator=(MemoryPool&&) noexcept = default;

MemoryPool::Block MemoryPool::Allocate() {
    uint32_t index = 0;
    if (m_impl->freeSlots.try_dequeue(index)) {
        m_impl->usedCount.fetch_add(1, std::memory_order_relaxed);
        return Block{
            m_impl->memory + (index * m_impl->alignedBlockSize),
            m_impl->rawBlockSize,
            index
        };
    }
    return Block{nullptr, 0, 0};
}

void MemoryPool::Release(Block block) {
    if (!block.IsValid() || !m_impl->memory) return;
    m_impl->freeSlots.enqueue(block.index);
    m_impl->usedCount.fetch_sub(1, std::memory_order_relaxed);
}

uint32_t MemoryPool::GetTotalBlocks() const { return m_impl->blockCount; }

uint32_t MemoryPool::GetAvailableBlocks() const {
    uint32_t used = m_impl->usedCount.load(std::memory_order_relaxed);
    return (m_impl->blockCount > used) ? (m_impl->blockCount - used) : 0;
}

uint32_t MemoryPool::GetUsedBlocks() const {
    return m_impl->usedCount.load(std::memory_order_relaxed);
}

size_t MemoryPool::GetBlockSize() const { return m_impl->rawBlockSize; }

size_t MemoryPool::GetTotalMemory() const {
    return m_impl->alignedBlockSize * m_impl->blockCount;
}

} // namespace openmedia::core
