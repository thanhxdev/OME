#pragma once

#include "openmedia/plugins/IPlugin.h"
#include <openmedia/core/MediaFrame.h>
#include <cstdint>

namespace openmedia {
namespace plugins {

class IEncoderPlugin : public IPlugin {
public:
    virtual ~IEncoderPlugin() = default;

    // Open and configure encoder
    virtual bool Open(const char* configJson) = 0;

    // Encode a frame. Returns encoded data in outBuffer.
    virtual bool EncodeFrame(const core::MediaFrame* frame, uint8_t** outBuffer, size_t* outSize) = 0;
    
    // Flush the encoder
    virtual bool Flush(uint8_t** outBuffer, size_t* outSize) = 0;

    // Close the encoder and release resources
    virtual void Close() = 0;
};

} // namespace plugins
} // namespace openmedia
