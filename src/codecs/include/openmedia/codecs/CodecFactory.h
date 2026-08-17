/// @file CodecFactory.h
#pragma once

#include <openmedia/codecs/IEncoder.h>
#include <openmedia/codecs/IDecoder.h>

#include <memory>
#include <string_view>

namespace openmedia::codecs {

enum class VideoCodec {
    Unknown,
    H264,
    H265,
    H264_NVENC,          ///< NVENC via FFmpeg wrapper
    H265_NVENC,          ///< NVENC via FFmpeg wrapper
    H264_NVENC_NATIVE,   ///< Native NVENC SDK (direct API, zero-copy)
    H265_NVENC_NATIVE,   ///< Native NVENC SDK (direct API, zero-copy)
    H264_QSV,
    H265_QSV,
    VP8,
    VP9,
    AV1,
    AV1_NVENC            ///< NVENC AV1 (Ada Lovelace+)
};

enum class AudioCodec {
    Unknown,
    AAC,
    Opus,
    MP3
};

class CodecFactory {
public:
    /// @brief Create an encoder for the specified codec
    static std::shared_ptr<IEncoder> CreateEncoder(VideoCodec type);

    /// @brief Create a decoder for the specified codec
    static std::shared_ptr<IDecoder> CreateDecoder(VideoCodec type);

    /// @brief Create an audio encoder for the specified codec
    static std::shared_ptr<IEncoder> CreateAudioEncoder(AudioCodec type);

    /// @brief Create an audio decoder for the specified codec
    static std::shared_ptr<IDecoder> CreateAudioDecoder(AudioCodec type);

    /// @brief Auto-select the best available encoder (NVENC native > NVENC FFmpeg > QSV > software)
    static std::shared_ptr<IEncoder> AutoSelectBestEncoder(VideoCodec preferred = VideoCodec::H264);
    
    /// @brief Callback for when an encoder fails during runtime
    using EncoderFailureCallback = std::function<void(std::shared_ptr<IEncoder> failedEncoder)>;

    /// @brief Create an encoder with automatic fallback chain (e.g. GPU -> CPU)
    static std::shared_ptr<IEncoder> CreateEncoderWithFallback(
        VideoCodec preferred,
        bool enableGpuFallback = true,
        EncoderFailureCallback onFail = nullptr);
};

} // namespace openmedia::codecs
