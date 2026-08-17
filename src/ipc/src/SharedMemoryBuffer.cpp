/// @file SharedMemoryBuffer.cpp
/// @brief Shared memory ring buffer implementation for zero-copy frame transfer

#include <openmedia/ipc/SharedMemoryBuffer.h>
#include <openmedia/core/Logger.h>

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>

#include <atomic>
#include <cstring>

namespace openmedia::ipc {

/// @brief Ring buffer header in shared memory
struct RingBufferHeader {
    uint32_t magic = 0x4F4D5348;  // "OMSH" (OpenMedia SHared memory)
    uint32_t version = 1;
    uint32_t slotCount = 0;
    uint32_t maxFrameSize = 0;
    uint32_t slotTotalSize = 0;       ///< sizeof(FrameSlotHeader) + maxFrameSize
    std::atomic<uint32_t> writePos{0};
    std::atomic<uint32_t> readPos{0};
    std::atomic<uint32_t> frameCount{0};
};

struct SharedMemoryBuffer::Impl {
    SharedMemoryConfig config;
    bool isCreator;
    HANDLE fileMapping = nullptr;
    uint8_t* baseAddress = nullptr;
    size_t totalSize = 0;

    RingBufferHeader* header = nullptr;
    bool initialized = false;

    Impl(const SharedMemoryConfig& cfg, bool creator)
        : config(cfg), isCreator(creator) {
        uint32_t slotTotalSize = static_cast<uint32_t>(
            sizeof(FrameSlotHeader) + config.maxFrameSize);
        totalSize = sizeof(RingBufferHeader) + slotTotalSize * config.frameSlotCount;
    }

    uint8_t* GetSlotBase(uint32_t index) {
        uint32_t slotTotalSize = static_cast<uint32_t>(
            sizeof(FrameSlotHeader) + config.maxFrameSize);
        return baseAddress + sizeof(RingBufferHeader) + slotTotalSize * index;
    }

    FrameSlotHeader* GetSlotHeader(uint32_t index) {
        return reinterpret_cast<FrameSlotHeader*>(GetSlotBase(index));
    }

    uint8_t* GetSlotData(uint32_t index) {
        return GetSlotBase(index) + sizeof(FrameSlotHeader);
    }
};

SharedMemoryBuffer::SharedMemoryBuffer(const SharedMemoryConfig& config, bool isCreator)
    : m_impl(std::make_unique<Impl>(config, isCreator)) {}

SharedMemoryBuffer::~SharedMemoryBuffer() {
    Close();
}

core::VoidResult SharedMemoryBuffer::Initialize() {
    std::wstring wideName(m_impl->config.name.begin(), m_impl->config.name.end());

    if (m_impl->isCreator) {
        m_impl->fileMapping = CreateFileMappingW(
            INVALID_HANDLE_VALUE, nullptr,
            PAGE_READWRITE,
            0, static_cast<DWORD>(m_impl->totalSize),
            wideName.c_str());
    } else {
        m_impl->fileMapping = OpenFileMappingW(
            FILE_MAP_ALL_ACCESS, FALSE,
            wideName.c_str());
    }

    if (!m_impl->fileMapping) {
        return std::unexpected(core::Error{
            core::ErrorCode::IOError,
            "Failed to " + std::string(m_impl->isCreator ? "create" : "open") +
            " shared memory: " + std::to_string(GetLastError())});
    }

    m_impl->baseAddress = static_cast<uint8_t*>(
        MapViewOfFile(m_impl->fileMapping, FILE_MAP_ALL_ACCESS,
                      0, 0, m_impl->totalSize));

    if (!m_impl->baseAddress) {
        CloseHandle(m_impl->fileMapping);
        m_impl->fileMapping = nullptr;
        return std::unexpected(core::Error{
            core::ErrorCode::IOError,
            "Failed to map view: " + std::to_string(GetLastError())});
    }

    m_impl->header = reinterpret_cast<RingBufferHeader*>(m_impl->baseAddress);

    if (m_impl->isCreator) {
        // Initialize header
        std::memset(m_impl->baseAddress, 0, m_impl->totalSize);
        m_impl->header->magic = 0x4F4D5348;
        m_impl->header->version = 1;
        m_impl->header->slotCount = m_impl->config.frameSlotCount;
        m_impl->header->maxFrameSize = m_impl->config.maxFrameSize;
        m_impl->header->slotTotalSize = static_cast<uint32_t>(
            sizeof(FrameSlotHeader) + m_impl->config.maxFrameSize);
        m_impl->header->writePos.store(0);
        m_impl->header->readPos.store(0);
        m_impl->header->frameCount.store(0);

        core::Logger::SInfo("SharedMemoryBuffer",
            "Created shared memory '{}': {} slots, {} bytes/frame, {} total",
            m_impl->config.name, m_impl->config.frameSlotCount,
            m_impl->config.maxFrameSize, m_impl->totalSize);
    } else {
        // Validate header
        if (m_impl->header->magic != 0x4F4D5348) {
            UnmapViewOfFile(m_impl->baseAddress);
            CloseHandle(m_impl->fileMapping);
            return std::unexpected(core::Error{
                core::ErrorCode::InvalidData,
                "Invalid shared memory header"});
        }

        core::Logger::SInfo("SharedMemoryBuffer",
            "Opened shared memory '{}': {} slots",
            m_impl->config.name, m_impl->header->slotCount);
    }

    m_impl->initialized = true;
    return {};
}

void SharedMemoryBuffer::Close() {
    if (m_impl->baseAddress) {
        UnmapViewOfFile(m_impl->baseAddress);
        m_impl->baseAddress = nullptr;
        m_impl->header = nullptr;
    }

    if (m_impl->fileMapping) {
        CloseHandle(m_impl->fileMapping);
        m_impl->fileMapping = nullptr;
    }

    m_impl->initialized = false;
}

bool SharedMemoryBuffer::IsInitialized() const {
    return m_impl->initialized;
}

uint8_t* SharedMemoryBuffer::AcquireWriteSlot(uint32_t& slotIndex) {
    if (!m_impl->initialized) return nullptr;

    uint32_t pos = m_impl->header->writePos.load();
    slotIndex = pos % m_impl->header->slotCount;

    auto* slotHeader = m_impl->GetSlotHeader(slotIndex);
    uint32_t expected = 0;  // empty
    if (!slotHeader->state.compare_exchange_strong(expected, 1)) {
        // Slot not available (still being read or not released)
        return nullptr;
    }

    return m_impl->GetSlotData(slotIndex);
}

void SharedMemoryBuffer::CommitWriteSlot(uint32_t slotIndex,
                                          const FrameSlotHeader& metadata) {
    if (!m_impl->initialized) return;

    auto* slotHeader = m_impl->GetSlotHeader(slotIndex);
    slotHeader->pts = metadata.pts;
    slotHeader->dts = metadata.dts;
    slotHeader->width = metadata.width;
    slotHeader->height = metadata.height;
    slotHeader->pixelFormat = metadata.pixelFormat;
    slotHeader->lineSize = metadata.lineSize;
    slotHeader->dataSize = metadata.dataSize;
    slotHeader->sequenceNumber = metadata.sequenceNumber;
    slotHeader->flags = metadata.flags;
    slotHeader->state.store(2);  // ready

    m_impl->header->writePos.fetch_add(1);
    m_impl->header->frameCount.fetch_add(1);
}

const uint8_t* SharedMemoryBuffer::AcquireReadSlot(uint32_t& slotIndex,
                                                     FrameSlotHeader& metadata) {
    if (!m_impl->initialized) return nullptr;

    uint32_t pos = m_impl->header->readPos.load();
    slotIndex = pos % m_impl->header->slotCount;

    auto* slotHeader = m_impl->GetSlotHeader(slotIndex);
    uint32_t expected = 2;  // ready
    if (!slotHeader->state.compare_exchange_strong(expected, 3)) {
        return nullptr;  // No frame ready
    }

    metadata.pts = slotHeader->pts;
    metadata.dts = slotHeader->dts;
    metadata.width = slotHeader->width;
    metadata.height = slotHeader->height;
    metadata.pixelFormat = slotHeader->pixelFormat;
    metadata.lineSize = slotHeader->lineSize;
    metadata.dataSize = slotHeader->dataSize;
    metadata.sequenceNumber = slotHeader->sequenceNumber;
    metadata.flags = slotHeader->flags;

    return m_impl->GetSlotData(slotIndex);
}

void SharedMemoryBuffer::ReleaseReadSlot(uint32_t slotIndex) {
    if (!m_impl->initialized) return;

    auto* slotHeader = m_impl->GetSlotHeader(slotIndex);
    slotHeader->state.store(0);  // empty

    m_impl->header->readPos.fetch_add(1);
}

size_t SharedMemoryBuffer::GetTotalSize() const {
    return m_impl->totalSize;
}

uint32_t SharedMemoryBuffer::GetSlotCount() const {
    return m_impl->config.frameSlotCount;
}

uint32_t SharedMemoryBuffer::GetMaxFrameSize() const {
    return m_impl->config.maxFrameSize;
}

uint32_t SharedMemoryBuffer::GetWritePosition() const {
    if (!m_impl->header) return 0;
    return m_impl->header->writePos.load();
}

uint32_t SharedMemoryBuffer::GetReadPosition() const {
    if (!m_impl->header) return 0;
    return m_impl->header->readPos.load();
}

std::string SharedMemoryBuffer::GetName() const {
    return m_impl->config.name;
}

} // namespace openmedia::ipc
