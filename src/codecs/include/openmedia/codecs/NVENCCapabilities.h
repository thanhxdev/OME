/// @file NVENCCapabilities.h
/// @brief Query NVIDIA GPU hardware encoding capabilities
#pragma once

#include <openmedia/core/ErrorCodes.h>
#include <string>
#include <cstdint>

#ifdef HAS_NVCODEC
#include <nvEncodeAPI.h>
#include <cuda.h>
#endif

namespace openmedia::codecs {

/// @brief NVENC GPU capabilities snapshot
struct NVENCCaps {
    bool supportsH264 = false;
    bool supportsHEVC = false;
    bool supportsAV1  = false;

    int maxWidth  = 0;
    int maxHeight = 0;
    int maxMBPerSec = 0;       ///< Max macroblocks/sec (throughput indicator)
    int maxConcurrentSessions = 0;

    bool supportsBFrames     = false;
    bool supportsLookahead   = false;
    bool supportsTemporalAQ  = false;
    bool supports10Bit       = false;
    bool supportsLossless    = false;
    bool supportsWeightedPred = false;

    std::string deviceName;
    int driverVersion = 0;

    bool IsValid() const { return supportsH264 || supportsHEVC; }
};

#ifdef HAS_NVCODEC

/// @brief Query NVENC capabilities for a given CUDA context
/// @param cuCtx  A valid CUcontext
/// @return NVENCCaps struct on success, Error otherwise
core::Result<NVENCCaps> QueryNVENCCapabilities(CUcontext cuCtx);

#endif

} // namespace openmedia::codecs
