#pragma once

/// @file MediaFrame.h
/// @brief Unified video+audio+metadata frame container with zero-allocation contiguous slab
/// @since 1.0.0

#include <openmedia/core/Types.h>
#include <openmedia/core/MediaMetadata.h>

#include <array>
#include <cstdint>
#include <memory>
#include <vector>

namespace openmedia::core {

/// @brief Unified media frame container
///
/// Holds video (CPU or GPU), audio, and metadata in a contiguous aligned buffer.
/// Completely eliminates dynamic vector reallocations on the hot processing path.
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

    /// @brief Create an encoded media packet
    static std::shared_ptr<MediaFrame> CreatePacket(size_t size);

    /// @brief Clone this frame (fast single memcpy of contiguous memory)
    [[nodiscard]] std::shared_ptr<MediaFrame> Clone() const;

    // --- Video Properties ---
    [[nodiscard]] uint32_t GetWidth() const { return m_width; }
    [[nodiscard]] uint32_t GetHeight() const { return m_height; }
    [[nodiscard]] PixelFormat GetPixelFormat() const { return m_pixelFormat; }
    [[nodiscard]] uint32_t GetVideoPlaneCount() const;

    void SetColorSpace(ColorSpace cs) { m_colorSpace = cs; }
    [[nodiscard]] ColorSpace GetColorSpace() const { return m_colorSpace; }

    void SetTransferFunction(TransferFunction tf) { m_transferFunction = tf; }
    [[nodiscard]] TransferFunction GetTransferFunction() const { return m_transferFunction; }

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

    // --- Encoded Packet Properties ---
    [[nodiscard]] uint8_t* GetPacketData();
    [[nodiscard]] const uint8_t* GetPacketData() const;
    [[nodiscard]] size_t GetPacketSize() const;

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

protected:
    MediaFrame();

    // Aligned contiguous memory buffer
    uint8_t* m_buffer = nullptr;
    size_t m_bufferCapacity = 0;
    size_t m_totalSize = 0;

    // Plane metadata
    struct PlaneLayout {
        uint8_t* data = nullptr;
        size_t size = 0;
        int32_t lineSize = 0;
    };
    static constexpr size_t MAX_PLANES = 4;
    std::array<PlaneLayout, MAX_PLANES> m_planes{};
    uint32_t m_planeCount = 0;

    uint32_t m_width = 0;
    uint32_t m_height = 0;
    PixelFormat m_pixelFormat = PixelFormat::Unknown;
    ColorSpace m_colorSpace = ColorSpace::Unknown;
    TransferFunction m_transferFunction = TransferFunction::Unknown;

    // Audio metadata
    static constexpr size_t MAX_AUDIO_CHANNELS = 16;
    std::array<uint8_t*, MAX_AUDIO_CHANNELS> m_audioPointers{};
    size_t m_audioBytesPerChannel = 0;
    uint32_t m_sampleCount = 0;
    uint32_t m_channelCount = 0;
    uint32_t m_sampleRate = 0;
    SampleFormat m_sampleFormat = SampleFormat::Unknown;

    // Packet metadata
    size_t m_packetSize = 0;

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

    void AllocateContiguousBuffer(size_t size);
    void FreeContiguousBuffer();
};

} // namespace openmedia::core
