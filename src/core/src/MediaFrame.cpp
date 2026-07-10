/// @file MediaFrame.cpp
/// @brief Unified media frame implementation

#include <openmedia/core/MediaFrame.h>

#include <algorithm>
#include <cstring>

namespace openmedia::core {

MediaFrame::~MediaFrame() = default;

std::shared_ptr<MediaFrame> MediaFrame::CreateVideo(
    uint32_t width, uint32_t height, PixelFormat format) {

    auto frame = std::shared_ptr<MediaFrame>(new MediaFrame());
    frame->m_mediaType = MediaType::Video;
    frame->m_width = width;
    frame->m_height = height;
    frame->m_pixelFormat = format;

    // Allocate planes based on format
    switch (format) {
        case PixelFormat::NV12: {
            // Y plane + UV interleaved plane
            size_t ySize = static_cast<size_t>(width) * height;
            size_t uvSize = static_cast<size_t>(width) * (height / 2);
            frame->m_videoPlanes.resize(2);
            frame->m_videoPlanes[0].resize(ySize, 0);
            frame->m_videoPlanes[1].resize(uvSize, 0);
            frame->m_lineSizes = {static_cast<int32_t>(width), static_cast<int32_t>(width)};
            break;
        }
        case PixelFormat::YUV420P: {
            size_t ySize = static_cast<size_t>(width) * height;
            size_t uSize = static_cast<size_t>(width / 2) * (height / 2);
            frame->m_videoPlanes.resize(3);
            frame->m_videoPlanes[0].resize(ySize, 0);
            frame->m_videoPlanes[1].resize(uSize, 0);
            frame->m_videoPlanes[2].resize(uSize, 0);
            frame->m_lineSizes = {
                static_cast<int32_t>(width),
                static_cast<int32_t>(width / 2),
                static_cast<int32_t>(width / 2)
            };
            break;
        }
        case PixelFormat::BGRA:
        case PixelFormat::RGBA: {
            size_t size = static_cast<size_t>(width) * height * 4;
            frame->m_videoPlanes.resize(1);
            frame->m_videoPlanes[0].resize(size, 0);
            frame->m_lineSizes = {static_cast<int32_t>(width * 4)};
            break;
        }
        case PixelFormat::RGB24:
        case PixelFormat::BGR24: {
            size_t size = static_cast<size_t>(width) * height * 3;
            frame->m_videoPlanes.resize(1);
            frame->m_videoPlanes[0].resize(size, 0);
            frame->m_lineSizes = {static_cast<int32_t>(width * 3)};
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

    // Calculate bytes per sample
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

    // Allocate audio data
    bool isPlanar = (format == SampleFormat::S16P || format == SampleFormat::Float32P);
    if (isPlanar) {
        frame->m_audioChannels.resize(channelCount);
        for (auto& ch : frame->m_audioChannels) {
            ch.resize(sampleCount * bytesPerSample, 0);
        }
    } else {
        // Interleaved: single buffer
        frame->m_audioChannels.resize(1);
        frame->m_audioChannels[0].resize(sampleCount * channelCount * bytesPerSample, 0);
    }

    return frame;
}

std::shared_ptr<MediaFrame> MediaFrame::Clone() const {
    auto clone = std::shared_ptr<MediaFrame>(new MediaFrame());
    clone->m_videoPlanes = m_videoPlanes;
    clone->m_lineSizes = m_lineSizes;
    clone->m_width = m_width;
    clone->m_height = m_height;
    clone->m_pixelFormat = m_pixelFormat;
    clone->m_audioChannels = m_audioChannels;
    clone->m_sampleCount = m_sampleCount;
    clone->m_channelCount = m_channelCount;
    clone->m_sampleRate = m_sampleRate;
    clone->m_sampleFormat = m_sampleFormat;
    clone->m_pts = m_pts;
    clone->m_dts = m_dts;
    clone->m_duration = m_duration;
    clone->m_timeBase = m_timeBase;
    clone->m_mediaType = m_mediaType;
    clone->m_metadata = m_metadata;
    // GPU handle is NOT cloned — GPU frames need explicit copy
    return clone;
}

uint32_t MediaFrame::GetVideoPlaneCount() const {
    return static_cast<uint32_t>(m_videoPlanes.size());
}

uint8_t* MediaFrame::GetVideoPlane(uint32_t planeIndex) {
    if (planeIndex >= m_videoPlanes.size()) return nullptr;
    return m_videoPlanes[planeIndex].data();
}

const uint8_t* MediaFrame::GetVideoPlane(uint32_t planeIndex) const {
    if (planeIndex >= m_videoPlanes.size()) return nullptr;
    return m_videoPlanes[planeIndex].data();
}

int32_t MediaFrame::GetLineSize(uint32_t planeIndex) const {
    if (planeIndex >= m_lineSizes.size()) return 0;
    return m_lineSizes[planeIndex];
}

uint8_t* MediaFrame::GetAudioData(uint32_t channel) {
    if (channel >= m_audioChannels.size()) return nullptr;
    return m_audioChannels[channel].data();
}

const uint8_t* MediaFrame::GetAudioData(uint32_t channel) const {
    if (channel >= m_audioChannels.size()) return nullptr;
    return m_audioChannels[channel].data();
}

size_t MediaFrame::GetAudioBufferSize() const {
    size_t total = 0;
    for (const auto& ch : m_audioChannels) {
        total += ch.size();
    }
    return total;
}

size_t MediaFrame::GetTotalSize() const {
    size_t total = 0;
    for (const auto& plane : m_videoPlanes) total += plane.size();
    for (const auto& ch : m_audioChannels) total += ch.size();
    return total;
}

bool MediaFrame::IsValid() const {
    if (m_mediaType == MediaType::Video) {
        return m_width > 0 && m_height > 0 && !m_videoPlanes.empty();
    }
    if (m_mediaType == MediaType::Audio) {
        return m_sampleCount > 0 && m_channelCount > 0 && !m_audioChannels.empty();
    }
    return false;
}

} // namespace openmedia::core
