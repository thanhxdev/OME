#include "openmedia/webrtc/WebRTCSource.h"

namespace openmedia::webrtc {

WebRTCSource::WebRTCSource() {
}

WebRTCSource::~WebRTCSource() {
    Disconnect();
}

bool WebRTCSource::Connect(const std::string& signalingUri) {
    // Stub: connect to WebRTC stream
    return true;
}

void WebRTCSource::Disconnect() {
    // Stub: disconnect from WebRTC stream
}

} // namespace openmedia::webrtc
