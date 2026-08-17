#pragma once

#include <memory>
#include <string>

namespace openmedia::webrtc {

class WebRTCEngine {
public:
    static WebRTCEngine& Get(); // Singleton for global WebRTC state

    WebRTCEngine();
    ~WebRTCEngine();

    bool Initialize();
    void Shutdown();

    // Get the global PeerConnectionFactory (opaque pointer for now)
    void* GetPeerConnectionFactory() const;

private:
    struct Impl;
    std::unique_ptr<Impl> m_impl;
    bool m_initialized = false;
};

} // namespace openmedia::webrtc
