#pragma once

#include <string>
#include <vector>
#include <openmedia/codecs/CodecFactory.h>

namespace openmedia::webrtc {

enum class WebRTCMode {
    Single,             // Single stream encoding
    Simulcast,          // Multi-layer WebRTC simulcast
    MultiDestination    // Multi-destination (fan-out via TeeNode)
};

enum class WebRTCDegradationStrategy {
    Balanced,           // Balance between framerate and resolution degradation
    MaintainFramerate,  // Degrade resolution first to maintain framerate
    MaintainResolution  // Degrade framerate first to maintain resolution
};

struct SimulcastLayer {
    int width = 1280;
    int height = 720;
    int fps = 30;
    int bitrate = 2000000;
    std::string rid = "m"; // "h" for high, "m" for medium, "l" for low
};

struct WebRTCConfig {
    std::string signalingUri;
    std::vector<std::string> iceServers = {"stun:stun.l.google.com:19302"};
    
    WebRTCMode mode = WebRTCMode::Single;
    
    // Encoder settings
    codecs::VideoCodec videoCodec = codecs::VideoCodec::H264;
    bool enableGpuFallback = true;
    
    // Adaptive Bitrate (ABR) settings
    bool enableABR = true;
    WebRTCDegradationStrategy degradation = WebRTCDegradationStrategy::Balanced;
    
    // Settings specific to Simulcast mode
    std::vector<SimulcastLayer> simulcastLayers;
};

} // namespace openmedia::webrtc
