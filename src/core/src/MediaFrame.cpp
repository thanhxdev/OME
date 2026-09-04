/// @file MediaFrame.cpp
/// @brief Unified media frame implementation with zero-allocation contiguous slab

#include <openmedia/core/MediaFrame.h>

#include <algorithm>
#include <cstdlib>
#include <cstring>

namespace openmedia::core {

MediaFrame::MediaFrame() = default;

MediaFrame::~MediaFrame() {
    FreeContiguousBuffer();
}

void MediaFrame::AllocateContiguousBuffer(size_t size) {
    FreeContiguousBuffer();
    if (size == 0) return;

    // Align buffer size to 64 bytes (AVX2/AVX-512)
    m_bufferCapacity = (size + 63) & ~size_t(63);
#if defined(_MSC_VER) || defined(__MINGW32__)
    m_buffer = static_cast<uint8_t*>(_aligned_malloc(m_bufferCapacity, 64));
#else
    m_buffer = static_cast<uint8_t*>(std::aligned_alloc(64, m_bufferCapacity));
#endif
    if (m_buffer) {
        std::memset(m_buffer, 0, m_bufferCapacity);
    }
    m_totalSize = size;
}

void MediaFrame::FreeContiguousBuffer() {
    if (m_buffer) {
#if defined(_MSC_VER) || defined(__MINGW32__)
        _aligned_free(m_buffer);
#else
        std::free(m_buffer);
#endif
        m_buffer = nullptr;
        m_bufferCapacity = 0;
        m_totalSize = 0;
    }
}

std::shared_ptr<MediaFrame> MediaFrame::CreateVideo(
    uint32_t width, uint32_t height, PixelFormat format) {

    auto frame = std::shared_ptr<MediaFrame>(new MediaFrame());
    frame->m_mediaType = MediaType::Video;
    frame->m_width = width;
    frame->m_height = height;
    frame->m_pixelFormat = format;

    switch (format) {
        case PixelFormat::NV12: {
            size_t ySize = static_cast<size_t>(width) * height;
            size_t uvSize = static_cast<size_t>(width) * (height / 2);
            frame->AllocateContiguousBuffer(ySize + uvSize);
            frame->m_planeCount = 2;
            frame->m_planes[0] = {frame->m_buffer, ySize, static_cast<int32_t>(width)};
            frame->m_planes[1] = {frame->m_buffer + ySize, uvSize, static_cast<int32_t>(width)};
            break;
        }
        case PixelFormat::YUV420P: {
            size_t ySize = static_cast<size_t>(width) * height;
            size_t uSize = static_cast<size_t>(width / 2) * (height / 2);
            frame->AllocateContiguousBuffer(ySize + 2 * uSize);
            frame->m_planeCount = 3;
            frame->m_planes[0] = {frame->m_buffer, ySize, static_cast<int32_t>(width)};
            frame->m_planes[1] = {frame->m_buffer + ySize, uSize, static_cast<int32_t>(width / 2)};
            frame->m_planes[2] = {frame->m_buffer + ySize + uSize, uSize, static_cast<int32_t>(width / 2)};
            break;
        }
        case PixelFormat::BGRA:
        case PixelFormat::RGBA: {
            size_t size = static_cast<size_t>(width) * height * 4;
            frame->AllocateContiguousBuffer(size);
            frame->m_planeCount = 1;
            frame->m_planes[0] = {frame->m_buffer, size, static_cast<int32_t>(width * 4)};
            break;
        }
        case PixelFormat::RGB24:
        case PixelFormat::BGR24: {
            size_t size = static_cast<size_t>(width) * height * 3;
            frame->AllocateContiguousBuffer(size);
            frame->m_planeCount = 1;
            frame->m_planes[0] = {frame->m_buffer, size, static_cast<int32_t>(width * 3)};
            break;
        }
        default:
            break;
    }

    return frame;
}

std::shared_ptr<MediaFrame> MediaFrame::CreateAudio(
    uint32_t sampleCount, uint32_t channelCount,
    SampleFormat format, uint32_t sampleRate) {

    auto frame = std::shared_ptr<MediaFrame>(new MediaFrame());
    frame->m_mediaType = MediaType::Audio;
    frame->m_sampleCount = sampleCount;
    frame->m_channelCount = channelCount;
    frame->m_sampleFormat = format;
    frame->m_sampleRate = sampleRate;

    size_t bytesPerSample = 0;
    switch (format) {
        case SampleFormat::S16:
        case SampleFormat::S16P:     bytesPerSample = 2; break;
        case SampleFormat::S32:      bytesPerSample = 4; break;
        case SampleFormat::Float32:
        case SampleFormat::Float32P: bytesPerSample = 4; break;
        case SampleFormat::Float64:  bytesPerSample = 8; break;
        default: break;
    }

    size_t totalBytes = sampleCount * channelCount * bytesPerSample;
    frame->AllocateContiguousBuffer(totalBytes);

    bool isPlanar = (format == SampleFormat::S16P || format == SampleFormat::Float32P);
    if (isPlanar) {
        size_t chSize = sampleCount * bytesPerSample;
        frame->m_audioBytesPerChannel = chSize;
        uint32_t effectiveChannels = std::min<uint32_t>(channelCount, static_cast<uint32_t>(MAX_AUDIO_CHANNELS));
        for (uint32_t i = 0; i < effectiveChannels; ++i) {
            frame->m_audioPointers[i] = frame->m_buffer + (i * chSize);
        }
    } else {
        frame->m_audioBytesPerChannel = totalBytes;
        frame->m_audioPointers[0] = frame->m_buffer;
    }

    return frame;
}

std::shared_ptr<MediaFrame> MediaFrame::CreatePacket(size_t size) {
    auto frame = std::shared_ptr<MediaFrame>(new MediaFrame());
    frame->m_mediaType = MediaType::Unknown;
    frame->AllocateContiguousBuffer(size);
    frame->m_packetSize = size;
    return frame;
}

std::shared_ptr<MediaFrame> MediaFrame::Clone() const {
    auto clone = std::shared_ptr<MediaFrame>(new MediaFrame());
    clone->m_width = m_width;
    clone->m_height = m_height;
    clone->m_pixelFormat = m_pixelFormat;
    clone->m_colorSpace = m_colorSpace;
    clone->m_transferFunction = m_transferFunction;

    clone->m_sampleCount = m_sampleCount;
    clone->m_channelCount = m_channelCount;
    clone->m_sampleRate = m_sampleRate;
    clone->m_sampleFormat = m_sampleFormat;
    clone->m_audioBytesPerChannel = m_audioBytesPerChannel;

    clone->m_packetSize = m_packetSize;
    clone->m_pts = m_pts;
    clone->m_dts = m_dts;
    clone->m_duration = m_duration;
    clone->m_timeBase = m_timeBase;
    clone->m_mediaType = m_mediaType;
    clone->m_metadata = m_metadata;

    if (m_buffer && m_totalSize > 0) {
        clone->AllocateContiguousBuffer(m_totalSize);
        std::memcpy(clone->m_buffer, m_buffer, m_totalSize);

        // Re-establish plane pointers relative to new buffer
        clone->m_planeCount = m_planeCount;
        for (uint32_t i = 0; i < m_planeCount; ++i) {
            size_t offset = m_planes[i].data - m_buffer;
            clone->m_planes[i] = {
                clone->m_buffer + offset,
                m_planes[i].size,
                m_planes[i].lineSize
            };
        }

        // Re-establish audio pointers
        if (m_mediaType == MediaType::Audio) {
            for (uint32_t i = 0; i < MAX_AUDIO_CHANNELS; ++i) {
                if (m_audioPointers[i]) {
                    size_t offset = m_audioPointers[i] - m_buffer;
                    clone->m_audioPointers[i] = clone->m_buffer + offset;
                }
            }
        }
    }

    return clone;
}

uint32_t MediaFrame::GetVideoPlaneCount() const {
    return m_planeCount;
}

uint8_t* MediaFrame::GetVideoPlane(uint32_t planeIndex) {
    if (planeIndex >= m_planeCount) return nullptr;
    return m_planes[planeIndex].data;
}

const uint8_t* MediaFrame::GetVideoPlane(uint32_t planeIndex) const {
    if (planeIndex >= m_planeCount) return nullptr;
    return m_planes[planeIndex].data;
}

int32_t MediaFrame::GetLineSize(uint32_t planeIndex) const {
    if (planeIndex >= m_planeCount) return 0;
    return m_planes[planeIndex].lineSize;
}

uint8_t* MediaFrame::GetAudioData(uint32_t channel) {
    if (channel >= MAX_AUDIO_CHANNELS) return nullptr;
    return m_audioPointers[channel];
}

const uint8_t* MediaFrame::GetAudioData(uint32_t channel) const {
    if (channel >= MAX_AUDIO_CHANNELS) return nullptr;
    return m_audioPointers[channel];
}

size_t MediaFrame::GetAudioBufferSize() const {
    return m_totalSize;
}

uint8_t* MediaFrame::GetPacketData() {
    return m_buffer;
}

const uint8_t* MediaFrame::GetPacketData() const {
    return m_buffer;
}

size_t MediaFrame::GetPacketSize() const {
    return m_packetSize;
}

size_t MediaFrame::GetTotalSize() const {
    return m_totalSize;
}

bool MediaFrame::IsValid() const {
    if (m_mediaType == MediaType::Video) {
        return m_width > 0 && m_height > 0 && m_buffer != nullptr && m_planeCount > 0;
    }
    if (m_mediaType == MediaType::Audio) {
        return m_sampleCount > 0 && m_channelCount > 0 && m_buffer != nullptr;
    }
    if (m_mediaType == MediaType::Unknown) {
        return m_buffer != nullptr && m_packetSize > 0;
    }
    return false;
}

} // namespace openmedia::core
