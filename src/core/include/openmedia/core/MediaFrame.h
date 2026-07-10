#pragma once

/// @file MediaFrame.h
/// @brief Unified video+audio+metadata frame container
/// @since 1.0.0

#include <openmedia/core/Types.h>
#include <openmedia/core/MediaMetadata.h>

#include <cstdint>
#include <memory>
#include <vector>

namespace openmedia::core {

/// @brief Unified media frame container
///
/// Holds video (CPU or GPU), audio, and metadata in a single structure.
/// Supports zero-copy operations through shared memory when used with IPC.
///
/// @code
/// auto frame = MediaFrame::CreateVideo(1920, 1080, PixelFormat::NV12);
/// frame->SetPts(90000);
/// auto data = frame->GetVideoPlane(0);
/// @endcode
class MediaFrame {
public:
    /// @brief Create a video frame
    static std::shared_ptr<MediaFrame> CreateVideo(
        uint32_t width, uint32_t height, PixelFormat format);

    /// @brief Create an audio frame
    static std::shared_ptr<MediaFrame> CreateAudio(
        uint32_t sampleCount, uint32_t channelCount, SampleFormat format, uint32_t sampleRate);

    /// @brief Clone this frame (deep copy of data)
    [[nodiscard]] std::shared_ptr<MediaFrame> Clone() const;

    // --- Video Properties ---
    [[nodiscard]] uint32_t GetWidth() const { return m_width; }
    [[nodiscard]] uint32_t GetHeight() const { return m_height; }
    [[nodiscard]] PixelFormat GetPixelFormat() const { return m_pixelFormat; }
    [[nodiscard]] uint32_t GetVideoPlaneCount() const;

    /// @brief Get video data plane
    /// @param planeIndex 0=Y/BGRA, 1=U/UV, 2=V
    [[nodiscard]] uint8_t* GetVideoPlane(uint32_t planeIndex);
    [[nodiscard]] const uint8_t* GetVideoPlane(uint32_t planeIndex) const;

    /// @brief Get line size (stride) for a video plane
    [[nodiscard]] int32_t GetLineSize(uint32_t planeIndex) const;

    // --- Audio Properties ---
    [[nodiscard]] uint32_t GetSampleCount() const { return m_sampleCount; }
    [[nodiscard]] uint32_t GetChannelCount() const { return m_channelCount; }
    [[nodiscard]] uint32_t GetSampleRate() const { return m_sampleRate; }
    [[nodiscard]] SampleFormat GetSampleFormat() const { return m_sampleFormat; }

    /// @brief Get audio data for a channel
    [[nodiscard]] uint8_t* GetAudioData(uint32_t channel = 0);
    [[nodiscard]] const uint8_t* GetAudioData(uint32_t channel = 0) const;

    /// @brief Get audio buffer size in bytes
    [[nodiscard]] size_t GetAudioBufferSize() const;

    // --- GPU ---
    /// @brief Set GPU texture handle (platform-specific)
    void SetGPUTextureHandle(void* handle) { m_gpuTextureHandle = handle; }
    [[nodiscard]] void* GetGPUTextureHandle() const { return m_gpuTextureHandle; }
    [[nodiscard]] bool IsGPUFrame() const { return m_gpuTextureHandle != nullptr; }

    // --- Timing ---
    void SetPts(int64_t pts) { m_pts = pts; }
    [[nodiscard]] int64_t GetPts() const { return m_pts; }

    void SetDts(int64_t dts) { m_dts = dts; }
    [[nodiscard]] int64_t GetDts() const { return m_dts; }

    void SetDuration(int64_t duration) { m_duration = duration; }
    [[nodiscard]] int64_t GetDuration() const { return m_duration; }

    void SetTimeBase(Rational timeBase) { m_timeBase = timeBase; }
    [[nodiscard]] Rational GetTimeBase() const { return m_timeBase; }

    // --- Media Type ---
    [[nodiscard]] MediaType GetMediaType() const { return m_mediaType; }

    // --- Metadata ---
    [[nodiscard]] MediaMetadata& GetMetadata() { return m_metadata; }
    [[nodiscard]] const MediaMetadata& GetMetadata() const { return m_metadata; }

    // --- Memory Info ---
    [[nodiscard]] size_t GetTotalSize() const;
    [[nodiscard]] bool IsValid() const;

    ~MediaFrame();

private:
    MediaFrame() = default;

    // Video data
    std::vector<std::vector<uint8_t>> m_videoPlanes;
    std::vector<int32_t> m_lineSizes;
    uint32_t m_width = 0;
    uint32_t m_height = 0;
    PixelFormat m_pixelFormat = PixelFormat::Unknown;

    // Audio data
    std::vector<std::vector<uint8_t>> m_audioChannels;
    uint32_t m_sampleCount = 0;
    uint32_t m_channelCount = 0;
    uint32_t m_sampleRate = 0;
    SampleFormat m_sampleFormat = SampleFormat::Unknown;

    // GPU
    void* m_gpuTextureHandle = nullptr;

    // Timing
    int64_t m_pts = 0;
    int64_t m_dts = 0;
    int64_t m_duration = 0;
    Rational m_timeBase = {1, 90000};

    // Type & metadata
    MediaType m_mediaType = MediaType::Unknown;
    MediaMetadata m_metadata;
};

} // namespace openmedia::core
