#pragma once

/// @file Types.h
/// @brief Core type definitions for OpenMedia SDK
/// @since 1.0.0

#include <cstdint>
#include <string>
#include <string_view>

namespace openmedia::core {

/// @brief Pixel formats supported by the engine
enum class PixelFormat : uint32_t {
    Unknown = 0,
    NV12,       ///< Semi-planar YUV 4:2:0 (GPU-friendly)
    YUV420P,    ///< Planar YUV 4:2:0
    YUV422P,    ///< Planar YUV 4:2:2
    YUV444P,    ///< Planar YUV 4:4:4
    BGRA,       ///< Packed BGRA 8-bit (D3D11 default)
    RGBA,       ///< Packed RGBA 8-bit
    RGB24,      ///< Packed RGB 8-bit
    BGR24,      ///< Packed BGR 8-bit
    P010LE,     ///< 10-bit YUV 4:2:0 (HDR)
    YUV420P10LE,///< 10-bit planar YUV 4:2:0
    GRAY8,      ///< 8-bit grayscale
};

/// @brief Audio sample formats
enum class SampleFormat : uint32_t {
    Unknown = 0,
    S16,        ///< Signed 16-bit integer
    S32,        ///< Signed 32-bit integer
    Float32,    ///< 32-bit floating point (default)
    Float64,    ///< 64-bit floating point
    S16P,       ///< Planar signed 16-bit
    Float32P,   ///< Planar 32-bit float
};

/// @brief Color space identifiers
enum class ColorSpace : uint32_t {
    Unknown = 0,
    BT601,      ///< ITU-R BT.601 (SD)
    BT709,      ///< ITU-R BT.709 (HD)
    BT2020,     ///< ITU-R BT.2020 (UHD/HDR)
    SRGB,       ///< sRGB
};

/// @brief Transfer function / EOTF
enum class TransferFunction : uint32_t {
    Unknown = 0,
    SDR,        ///< Standard Dynamic Range (BT.709 gamma)
    PQ,         ///< Perceptual Quantizer (HDR10, SMPTE ST 2084)
    HLG,        ///< Hybrid Log-Gamma (BBC/NHK)
    Linear,     ///< Linear light
};

/// @brief Media type identifiers
enum class MediaType : uint32_t {
    Unknown = 0,
    Video,
    Audio,
    Subtitle,
    Data,
};

/// @brief Pipeline state machine states
enum class PipelineState : uint32_t {
    Idle = 0,       ///< Initial state
    Building,       ///< Pipeline being constructed
    Ready,          ///< Built and validated, ready to start
    Running,        ///< Actively processing
    Paused,         ///< Paused, can resume
    Stopping,       ///< In process of stopping
    Stopped,        ///< Stopped cleanly
    Error,          ///< Error state
};

/// @brief Channel layout for audio
enum class ChannelLayout : uint32_t {
    Unknown = 0,
    Mono = 1,
    Stereo = 2,
    Surround51 = 6,
    Surround71 = 8,
};

/// @brief Rational number for timestamps and framerates
struct Rational {
    int32_t num = 0;    ///< Numerator
    int32_t den = 1;    ///< Denominator

    [[nodiscard]] double ToDouble() const {
        return den != 0 ? static_cast<double>(num) / den : 0.0;
    }
};

/// @brief Video resolution
struct Resolution {
    uint32_t width = 0;
    uint32_t height = 0;
};

/// @brief Convert PixelFormat to string
[[nodiscard]] constexpr std::string_view PixelFormatToString(PixelFormat fmt) {
    switch (fmt) {
        case PixelFormat::NV12:         return "NV12";
        case PixelFormat::YUV420P:      return "YUV420P";
        case PixelFormat::YUV422P:      return "YUV422P";
        case PixelFormat::YUV444P:      return "YUV444P";
        case PixelFormat::BGRA:         return "BGRA";
        case PixelFormat::RGBA:         return "RGBA";
        case PixelFormat::RGB24:        return "RGB24";
        case PixelFormat::BGR24:        return "BGR24";
        case PixelFormat::P010LE:       return "P010LE";
        case PixelFormat::YUV420P10LE:  return "YUV420P10LE";
        case PixelFormat::GRAY8:        return "GRAY8";
        default:                        return "Unknown";
    }
}

/// @brief Convert SampleFormat to string
[[nodiscard]] constexpr std::string_view SampleFormatToString(SampleFormat fmt) {
    switch (fmt) {
        case SampleFormat::S16:       return "S16";
        case SampleFormat::S32:       return "S32";
        case SampleFormat::Float32:   return "Float32";
        case SampleFormat::Float64:   return "Float64";
        case SampleFormat::S16P:      return "S16P";
        case SampleFormat::Float32P:  return "Float32P";
        default:                      return "Unknown";
    }
}

/// @brief Get bytes per pixel for a given format
[[nodiscard]] constexpr uint32_t BytesPerPixel(PixelFormat fmt) {
    switch (fmt) {
        case PixelFormat::BGRA:
        case PixelFormat::RGBA:       return 4;
        case PixelFormat::RGB24:
        case PixelFormat::BGR24:      return 3;
        case PixelFormat::GRAY8:      return 1;
        case PixelFormat::NV12:       return 1;  // Y plane; UV interleaved separately
        case PixelFormat::YUV420P:    return 1;  // Y plane
        default:                      return 0;
    }
}

} // namespace openmedia::core
