#include "openmedia/ndi/NDISource.h"
#include <Processing.NDI.Lib.h>
#include <spdlog/spdlog.h>

namespace openmedia::ndi {

NDISource::NDISource() = default;

NDISource::~NDISource() {
    Disconnect();
}

bool NDISource::Connect(const std::string& sourceName) {
    if (m_ndiReceiver) return true;

    NDIlib_recv_create_v3_t recv_create_desc;
    recv_create_desc.source_to_connect_to.p_ndi_name = sourceName.c_str();
    recv_create_desc.source_to_connect_to.p_url_address = nullptr;
    recv_create_desc.color_format = 1; // NDIlib_recv_color_format_e_UYVY_BGRA
    recv_create_desc.bandwidth = 100;  // NDIlib_recv_bandwidth_highest
    recv_create_desc.allow_video_fields = false;
    recv_create_desc.p_ndi_recv_name = "OpenMedia_NDI_Receiver";

    m_ndiReceiver = NDIlib_recv_create_v3(&recv_create_desc);
    if (!m_ndiReceiver) {
        spdlog::error("Failed to create NDI receiver for source: {}", sourceName);
        return false;
    }

    if (m_hxEnabled) {
        // In real NDI SDK, HX doesn't require a special flag on receive, 
        // but we can set a bandwidth hint or use NDIlib_recv_bandwidth_highest vs lowest
        // This is a placeholder for HX configuration if needed.
        spdlog::info("NDI|HX explicitly requested for source: {}", sourceName);
    }

    spdlog::info("NDI receiver connected to source: {}", sourceName);
    return true;
}

void NDISource::Disconnect() {
    if (!m_ndiReceiver) return;

    NDIlib_recv_destroy(static_cast<NDIlib_recv_instance_t>(m_ndiReceiver));
    m_ndiReceiver = nullptr;
    spdlog::info("NDI receiver disconnected");
}

void NDISource::EnableHX(bool enable) {
    m_hxEnabled = enable;
}

std::string NDISource::FetchMetadata() const {
    if (!m_ndiReceiver) return "";
    
    // In real NDI SDK, we would call NDIlib_recv_capture_v2 and check for NDIlib_metadata_frame_t
    // Mocking metadata retrieval
    return "<ndi_metadata><tally on_program=\"true\" on_preview=\"false\"/></ndi_metadata>";
}

} // namespace openmedia::ndi
