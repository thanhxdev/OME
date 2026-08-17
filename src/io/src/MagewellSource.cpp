#include <openmedia/io/MagewellSource.h>

namespace openmedia::io {

MagewellSource::MagewellSource(int deviceIndex) : m_deviceIndex(deviceIndex) {
}

MagewellSource::~MagewellSource() {
    Stop();
}

std::string MagewellSource::GetName() const {
    return "MagewellSource";
}

core::PipelineState MagewellSource::GetState() const {
    return m_state;
}

core::VoidResult MagewellSource::Initialize() {
    // TODO: Dynamic load MWCapture SDK DLLs
    // Check if card is present, initialize device context
    m_state = core::PipelineState::Idle;
    return {};
}

core::VoidResult MagewellSource::Start() {
    if (m_state == core::PipelineState::Running) return {};
    
    // TODO: Start MWCapture stream
    m_isOpen = true;
    auto oldState = m_state;
    m_state = core::PipelineState::Running;
    if (m_stateCallback) m_stateCallback(oldState, m_state);
    
    return {};
}

core::VoidResult MagewellSource::Stop() {
    if (m_state == core::PipelineState::Stopped) return {};
    
    // TODO: Stop MWCapture stream
    m_isOpen = false;
    auto oldState = m_state;
    m_state = core::PipelineState::Stopped;
    if (m_stateCallback) m_stateCallback(oldState, m_state);
    
    return {};
}

core::VoidResult MagewellSource::PushFrame(std::shared_ptr<core::MediaFrame> frame) {
    // MagewellSource is a source, it doesn't accept pushed frames from upstream
    return std::unexpected(core::Error::Make(core::ErrorCode::NotSupported, "MagewellSource cannot receive pushed frames"));
}

core::Result<std::shared_ptr<core::MediaFrame>> MagewellSource::PullFrame() {
    if (!m_isOpen) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "MagewellSource is not open"));
    }
    // TODO: Pull next available frame from MWCapture buffer
    return std::unexpected(core::Error::Make(core::ErrorCode::NotImplemented, "Magewell pulling not yet implemented"));
}

core::VoidResult MagewellSource::Connect(std::shared_ptr<core::IMediaObject> downstream) {
    m_downstream = downstream;
    return {};
}

core::VoidResult MagewellSource::Disconnect() {
    m_downstream = nullptr;
    return {};
}

void MagewellSource::OnStateChange(core::StateChangeCallback callback) {
    m_stateCallback = callback;
}

void MagewellSource::OnError(core::ErrorCallback callback) {
    m_errorCallback = callback;
}

} // namespace openmedia::io
