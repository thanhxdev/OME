#pragma once

#include "openmedia/plugins/IPlugin.h"
#include <openmedia/core/MediaFrame.h>
#include <cstdint>

namespace openmedia {
namespace plugins {

class IDecoderPlugin : public IPlugin {
public:
    virtual ~IDecoderPlugin() = default;

    // Open and configure decoder
    virtual bool Open(const char* configJson) = 0;

    // Decode compressed data into a MediaFrame
    virtual bool DecodePacket(const uint8_t* data, size_t size, core::MediaFrame* outFrame) = 0;
    
    // Close the decoder and release resources
    virtual void Close() = 0;
};

} // namespace plugins
} // namespace openmedia
