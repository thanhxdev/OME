#include <openmedia/io/NetworkSource.h>
#include <openmedia/core/ErrorCodes.h>
#include <openmedia/core/Logger.h>
#include <regex>

namespace openmedia::io {

struct NetworkSource::Impl {
    std::string m_name = "NetworkSource";
    core::PipelineState m_state = core::PipelineState::Idle;
    NetworkProtocol m_protocol = NetworkProtocol::Unknown;
    std::string m_url;

    // TODO: In a real implementation, this would instantiate a LiveSource
    // or specific protocol engine (SRTEngine, NDI, etc.) based on m_protocol.
};

NetworkSource::NetworkSource() : m_impl(std::make_unique<Impl>()) {
}

NetworkSource::~NetworkSource() {
    Close();
}

std::string NetworkSource::GetName() const {
    return m_impl->m_name;
}

core::PipelineState NetworkSource::GetState() const {
    return m_impl->m_state;
}

core::VoidResult NetworkSource::Initialize() {
    m_impl->m_state = core::PipelineState::Ready;
    return {};
}

core::VoidResult NetworkSource::Start() {
    if (m_impl->m_state != core::PipelineState::Ready) {
        return std::unexpected(core::Error{core::ErrorCode::InvalidState, "Cannot start NetworkSource from current state"});
    }
    m_impl->m_state = core::PipelineState::Running;
    auto& logger = core::Logger::Get("IO");
    OME_LOG_INFO(logger, "NetworkSource started.");
    return {};
}

core::VoidResult NetworkSource::Stop() {
    m_impl->m_state = core::PipelineState::Stopped;
    auto& logger = core::Logger::Get("IO");
    OME_LOG_INFO(logger, "NetworkSource stopped.");
    return {};
}

core::VoidResult NetworkSource::PushFrame(std::shared_ptr<core::MediaFrame> frame) {
    return std::unexpected(core::Error{core::ErrorCode::NotSupported, "NetworkSource does not support PushFrame"});
}

core::Result<std::shared_ptr<core::MediaFrame>> NetworkSource::PullFrame() {
    if (m_impl->m_state != core::PipelineState::Running) {
        return std::unexpected(core::Error{core::ErrorCode::InvalidState, "NetworkSource is not running"});
    }
    // TODO: Pull frame from underlying source
    return std::unexpected(core::Error(core::ErrorCode::NotImplemented, "PullFrame not implemented yet"));
}

core::VoidResult NetworkSource::Connect(std::shared_ptr<core::IMediaObject> downstream) {
    return std::unexpected(core::Error(core::ErrorCode::NotImplemented, "Connect not implemented yet"));
}

core::VoidResult NetworkSource::Disconnect() {
    return std::unexpected(core::Error(core::ErrorCode::NotImplemented, "Disconnect not implemented yet"));
}

void NetworkSource::OnStateChange(core::StateChangeCallback callback) {
    // TODO: implement
}

void NetworkSource::OnError(core::ErrorCallback callback) {
    // TODO: implement
}

core::VoidResult NetworkSource::OpenURL(const std::string& url) {
    m_impl->m_url = url;
    
    // Basic protocol detection
    if (url.find("rtmp://") == 0) m_impl->m_protocol = NetworkProtocol::RTMP;
    else if (url.find("rtsp://") == 0) m_impl->m_protocol = NetworkProtocol::RTSP;
    else if (url.find("srt://") == 0) m_impl->m_protocol = NetworkProtocol::SRT;
    else if (url.find("http://") == 0 || url.find("https://") == 0) {
        if (url.find(".m3u8") != std::string::npos) {
            m_impl->m_protocol = NetworkProtocol::HLS;
        } else {
            m_impl->m_protocol = NetworkProtocol::HTTP;
        }
    } else if (url.find("udp://") == 0 || url.find("tcp://") == 0) {
        m_impl->m_protocol = NetworkProtocol::MPEG_TS;
    } else {
        m_impl->m_protocol = NetworkProtocol::Unknown;
        auto& logger = core::Logger::Get("IO");
        OME_LOG_WARN(logger, "NetworkSource: Unknown protocol for URL {}", url);
    }

    auto& logger = core::Logger::Get("IO");
    OME_LOG_INFO(logger, "NetworkSource opened URL: {} (Protocol: {})", url, static_cast<int>(m_impl->m_protocol));
    return {};
}

void NetworkSource::Close() {
    Stop();
    m_impl->m_url.clear();
    m_impl->m_protocol = NetworkProtocol::Unknown;
}

NetworkProtocol NetworkSource::GetDetectedProtocol() const {
    return m_impl->m_protocol;
}

} // namespace openmedia::io
