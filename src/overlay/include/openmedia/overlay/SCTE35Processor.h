/// @file SCTE35Processor.h
#pragma once

#include <string>
#include <vector>
#include <memory>
#include <openmedia/core/IMediaObject.h>

namespace openmedia::overlay {

struct SCTE35Marker {
    double pts;          // Presentation Timestamp
    uint32_t eventId;    // Splice Event ID
    uint32_t duration;   // Duration in ticks (usually 90kHz)
    bool isOutNetwork;   // true if splice out, false if splice in
};

class SCTE35Processor {
public:
    SCTE35Processor();
    ~SCTE35Processor();

    /// @brief Insert a SCTE-35 marker to be embedded in the output stream
    void InsertMarker(const SCTE35Marker& marker);

    /// @brief Retrieve and clear any pending markers to be embedded
    std::vector<SCTE35Marker> GetPendingMarkers();

    /// @brief Parse raw SCTE-35 payload (for detection)
    static std::vector<SCTE35Marker> DetectMarkers(const uint8_t* data, size_t size);

private:
    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

} // namespace openmedia::overlay
