#include <openmedia/overlay/SCTE35Processor.h>
#include <mutex>
#include <vector>

namespace openmedia::overlay {

struct SCTE35Processor::Impl {
    std::vector<SCTE35Marker> pendingMarkers;
    std::mutex mutex;
};

SCTE35Processor::SCTE35Processor() : m_impl(std::make_unique<Impl>()) {
}

SCTE35Processor::~SCTE35Processor() = default;

void SCTE35Processor::InsertMarker(const SCTE35Marker& marker) {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    m_impl->pendingMarkers.push_back(marker);
}

std::vector<SCTE35Marker> SCTE35Processor::GetPendingMarkers() {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    std::vector<SCTE35Marker> markers = std::move(m_impl->pendingMarkers);
    m_impl->pendingMarkers.clear();
    return markers;
}

std::vector<SCTE35Marker> SCTE35Processor::DetectMarkers(const uint8_t* data, size_t size) {
    std::vector<SCTE35Marker> detected;
    // Mock parsing
    if (size > 0 && data[0] == 0xFC) { // SCTE-35 Table ID
        SCTE35Marker marker;
        marker.eventId = 1;
        marker.isOutNetwork = true;
        detected.push_back(marker);
    }
    return detected;
}

} // namespace openmedia::overlay
