#include "openmedia/webrtc/WebRTCEngine.h"
#include <spdlog/spdlog.h>

namespace openmedia::webrtc {

struct WebRTCEngine::Impl {
    // libwebrtc dependencies (opaque/stubbed for now)
    void* networkThread = nullptr;
    void* workerThread = nullptr;
    void* signalingThread = nullptr;
    void* peerConnectionFactory = nullptr;
};

WebRTCEngine& WebRTCEngine::Get() {
    static WebRTCEngine instance;
    return instance;
}

WebRTCEngine::WebRTCEngine() : m_impl(std::make_unique<Impl>()) {
}

WebRTCEngine::~WebRTCEngine() {
    Shutdown();
}

bool WebRTCEngine::Initialize() {
    if (m_initialized) return true;
    
    spdlog::info("Initializing WebRTC Engine");

    // TODO: Initialize rtc::Thread for network, worker, signaling
    // m_impl->networkThread = rtc::Thread::CreateWithSocketServer();
    // m_impl->networkThread->Start();
    // ...
    
    // TODO: Create webrtc::PeerConnectionFactoryInterface
    // m_impl->peerConnectionFactory = webrtc::CreatePeerConnectionFactory(...)

    m_initialized = true;
    return true;
}

void WebRTCEngine::Shutdown() {
    if (m_initialized) {
        spdlog::info("Shutting down WebRTC Engine");
        
        // TODO: Release PeerConnectionFactory
        m_impl->peerConnectionFactory = nullptr;
        
        // TODO: Stop and release threads
        m_impl->networkThread = nullptr;
        m_impl->workerThread = nullptr;
        m_impl->signalingThread = nullptr;

        m_initialized = false;
    }
}

void* WebRTCEngine::GetPeerConnectionFactory() const {
    return m_impl->peerConnectionFactory;
}

} // namespace openmedia::webrtc
