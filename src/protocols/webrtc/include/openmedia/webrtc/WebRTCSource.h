#pragma once

#include <string>

namespace openmedia::webrtc {

class WebRTCSource {
public:
    WebRTCSource();
    ~WebRTCSource();

    bool Connect(const std::string& signalingUri);
    void Disconnect();

private:
    void* m_peerConnection = nullptr;
};

} // namespace openmedia::webrtc
