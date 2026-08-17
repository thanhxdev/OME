#include "openmedia/webrtc/WebRTCOutput.h"

#include <openmedia/core/Logger.h>
#include <mutex>

namespace openmedia::webrtc {

struct WebRTCOutput::Impl {
    std::mutex mutex;
    core::PipelineState state{core::PipelineState::Stopped};
    std::shared_ptr<core::IMediaObject> downstream;
    core::StateChangeCallback onStateChange;
    core::ErrorCallback onError;
    
    core::Logger* logger = nullptr;
    bool isOpened = false;
    std::string signalingUri;
    void* peerConnection = nullptr; // TODO: libwebrtc PeerConnection
};

WebRTCOutput::WebRTCOutput() : m_impl(std::make_unique<Impl>()) {
    m_impl->logger = &core::Logger::Get("WebRTCOutput");
}

WebRTCOutput::~WebRTCOutput() { Close(); }

core::PipelineState WebRTCOutput::GetState() const { return m_impl->state; }

core::VoidResult WebRTCOutput::Initialize() {
    auto old = m_impl->state;
    m_impl->state = core::PipelineState::Ready;
    if (m_impl->onStateChange) m_impl->onStateChange(old, m_impl->state);
    return {};
}

core::VoidResult WebRTCOutput::Start() {
    auto old = m_impl->state;
    m_impl->state = core::PipelineState::Running;
    if (m_impl->onStateChange) m_impl->onStateChange(old, m_impl->state);
    return {};
}

core::VoidResult WebRTCOutput::Stop() {
    auto old = m_impl->state;
    m_impl->state = core::PipelineState::Stopped;
    if (m_impl->onStateChange) m_impl->onStateChange(old, m_impl->state);
    return {};
}

core::VoidResult WebRTCOutput::PushFrame(std::shared_ptr<core::MediaFrame> frame) {
    std::lock_guard lock(m_impl->mutex);
    if (m_impl->state != core::PipelineState::Running) return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "WebRTCOutput is not running"));
    if (!m_impl->isOpened) return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "WebRTC connection is not opened"));

    OME_LOG_INFO(*m_impl->logger, "Pushing frame to WebRTC peer connection");
    // TODO: Pass frame to WebRTC video track
    
    if (m_impl->downstream) {
        return m_impl->downstream->PushFrame(frame);
    }
    return {};
}

core::Result<std::shared_ptr<core::MediaFrame>> WebRTCOutput::PullFrame() {
    return std::unexpected(core::Error::Make(core::ErrorCode::NotImplemented, "WebRTCOutput is a push sink"));
}

core::VoidResult WebRTCOutput::Connect(std::shared_ptr<core::IMediaObject> downstream) { std::lock_guard lock(m_impl->mutex); m_impl->downstream = downstream; return {}; }
core::VoidResult WebRTCOutput::Disconnect() { std::lock_guard lock(m_impl->mutex); m_impl->downstream.reset(); return {}; }
void WebRTCOutput::OnStateChange(core::StateChangeCallback callback) { m_impl->onStateChange = callback; }
void WebRTCOutput::OnError(core::ErrorCallback callback) { m_impl->onError = callback; }

core::VoidResult WebRTCOutput::Open(const std::string& signalingUri) {
    std::lock_guard lock(m_impl->mutex);
    if (m_impl->isOpened) return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "Already opened"));
    
    OME_LOG_INFO(*m_impl->logger, "Opening WebRTC connection to {}", signalingUri);
    m_impl->signalingUri = signalingUri;
    
    // TODO: Setup libwebrtc connection here
    
    m_impl->isOpened = true;
    return {};
}

void WebRTCOutput::Close() {
    std::lock_guard lock(m_impl->mutex);
    if (m_impl->isOpened) {
        OME_LOG_INFO(*m_impl->logger, "Closing WebRTC connection");
        // TODO: Teardown libwebrtc connection
        m_impl->isOpened = false;
        m_impl->peerConnection = nullptr;
    }
}

} // namespace openmedia::webrtc
