/// @file MemoryPool.cpp
/// @brief Pre-allocated memory pool implementation

#include <openmedia/core/MemoryPool.h>

#include <algorithm>
#include <cstring>
#include <queue>

namespace openmedia::core {

struct MemoryPool::Impl {
    size_t blockSize = 0;
    uint32_t blockCount = 0;
    std::vector<uint8_t> memory;          // Contiguous memory block
    std::queue<uint32_t> freeSlots;       // Available slot indices
    std::mutex mutex;
    uint32_t usedCount = 0;
};

MemoryPool::MemoryPool(size_t blockSize, uint32_t blockCount)
    : m_impl(std::make_unique<Impl>()) {
    m_impl->blockSize = blockSize;
    m_impl->blockCount = blockCount;

    // Pre-allocate contiguous memory
    m_impl->memory.resize(blockSize * blockCount, 0);

    // Initialize free list
    for (uint32_t i = 0; i < blockCount; ++i) {
        m_impl->freeSlots.push(i);
    }
}

MemoryPool::~MemoryPool() = default;

MemoryPool::MemoryPool(MemoryPool&&) noexcept = default;
MemoryPool& MemoryPool::operator=(MemoryPool&&) noexcept = default;

MemoryPool::Block MemoryPool::Allocate() {
    std::lock_guard lock(m_impl->mutex);

    if (m_impl->freeSlots.empty()) {
        return Block{nullptr, 0, 0};
    }

    uint32_t index = m_impl->freeSlots.front();
    m_impl->freeSlots.pop();
    m_impl->usedCount++;

    return Block{
        m_impl->memory.data() + (index * m_impl->blockSize),
        m_impl->blockSize,
        index
    };
}

void MemoryPool::Release(Block block) {
    if (!block.IsValid()) return;

    std::lock_guard lock(m_impl->mutex);
    m_impl->freeSlots.push(block.index);
    m_impl->usedCount--;
}

uint32_t MemoryPool::GetTotalBlocks() const { return m_impl->blockCount; }
uint32_t MemoryPool::GetAvailableBlocks() const {
    std::lock_guard lock(m_impl->mutex);
    return static_cast<uint32_t>(m_impl->freeSlots.size());
}
uint32_t MemoryPool::GetUsedBlocks() const {
    std::lock_guard lock(m_impl->mutex);
    return m_impl->usedCount;
}
size_t MemoryPool::GetBlockSize() const { return m_impl->blockSize; }
size_t MemoryPool::GetTotalMemory() const { return m_impl->blockSize * m_impl->blockCount; }

} // namespace openmedia::core
