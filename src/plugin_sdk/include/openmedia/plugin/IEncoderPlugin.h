#pragma once

#include "IPlugin.h"
#include <openmedia/core/MediaFrame.h>
#include <vector>
#include <cstdint>

namespace openmedia::plugin {

struct EncoderConfig {
    int width, height;
    double frameRate;
    int bitrate;        // kbps
    int gopSize;
    int bFrames;
    const char* preset; // "ultrafast", "medium", "slow"
    const char* profile;
    const char* pixelFormat;
};

class IEncoderPlugin : public IPlugin {
public:
    virtual bool Open(const EncoderConfig& config) = 0;
    virtual bool EncodeFrame(
        const core::MediaFrame& frame,
        std::vector<uint8_t>& encodedData
    ) = 0;
    virtual bool Flush(std::vector<std::vector<uint8_t>>& remaining) = 0;
    virtual void Close() = 0;

    virtual const char* GetCodecName() const = 0;
    virtual const char* GetCodecFourCC() const = 0;
};

class IDecoderPlugin : public IPlugin {
public:
    virtual bool Open(const char* codecName) = 0;
    virtual bool DecodePacket(
        const uint8_t* data,
        size_t size,
        core::MediaFrame& outputFrame
    ) = 0;
    virtual void Close() = 0;
};

} // namespace openmedia::plugin
