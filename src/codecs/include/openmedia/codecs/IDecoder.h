/// @file IDecoder.h
#pragma once

#include <openmedia/core/IMediaObject.h>

namespace openmedia::codecs {

class IDecoder : public core::IMediaObject {
public:
    virtual ~IDecoder() = default;

    // Additional decoder specific APIs could go here
};

} // namespace openmedia::codecs
