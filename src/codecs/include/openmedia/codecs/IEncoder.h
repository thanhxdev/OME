/// @file IEncoder.h
#pragma once

#include <openmedia/core/IMediaObject.h>

namespace openmedia::codecs {

enum class CodecType {
    Video,
    Audio
};

enum class RateControlMode {
    CBR,
    VBR,
    CQ
};

struct EncoderConfig {
    CodecType type = CodecType::Video;

    // Video settings
    int width = 1920;
    int height = 1080;
    int fps = 30;
    std::string preset = "medium";
    std::string profile = "";
    core::PixelFormat pixelFormat = core::PixelFormat::NV12;

    // Audio settings
    int sampleRate = 48000;
    int channels = 2;

    // Common settings
    int bitrate = 5000000;
    RateControlMode rcMode = RateControlMode::CBR;
    int quality = 23; // CRF or CQ value if rcMode == CQ
};

class IEncoder : public core::IMediaObject {
public:
    virtual ~IEncoder() = default;

    // Additional encoder specific APIs could go here
    virtual core::VoidResult Configure(const EncoderConfig& config) = 0;
};

} // namespace openmedia::codecs
