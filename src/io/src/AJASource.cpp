#include <openmedia/io/AJASource.h>

namespace openmedia::io {

AJASource::AJASource(int deviceIndex) : m_deviceIndex(deviceIndex) {
}

AJASource::~AJASource() {
    Stop();
}

std::string AJASource::GetName() const {
    return "AJASource";
}

core::PipelineState AJASource::GetState() const {
    return m_state;
}

core::VoidResult AJASource::Initialize() {
    // TODO: Dynamic load NTV2 SDK DLLs
    // Check if card is present, initialize device context
    m_state = core::PipelineState::Idle;
    return {};
}

core::VoidResult AJASource::Start() {
    if (m_state == core::PipelineState::Running) return {};
    
    // TODO: Start NTV2 capture thread, subscribe to interrupts
    m_isOpen = true;
    auto oldState = m_state;
    m_state = core::PipelineState::Running;
    if (m_stateCallback) m_stateCallback(oldState, m_state);
    
    return {};
}

core::VoidResult AJASource::Stop() {
    if (m_state == core::PipelineState::Stopped) return {};
    
    // TODO: Stop NTV2 capture thread
    m_isOpen = false;
    auto oldState = m_state;
    m_state = core::PipelineState::Stopped;
    if (m_stateCallback) m_stateCallback(oldState, m_state);
    
    return {};
}

core::VoidResult AJASource::PushFrame(std::shared_ptr<core::MediaFrame> frame) {
    // AJASource is a source, it doesn't accept pushed frames from upstream
    return std::unexpected(core::Error::Make(core::ErrorCode::NotSupported, "AJASource cannot receive pushed frames"));
}

core::Result<std::shared_ptr<core::MediaFrame>> AJASource::PullFrame() {
    if (!m_isOpen) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "AJASource is not open"));
    }
    // TODO: Pull next available frame from NTV2 DMA buffer
    return std::unexpected(core::Error::Make(core::ErrorCode::NotImplemented, "AJA pulling not yet implemented"));
}

core::VoidResult AJASource::Connect(std::shared_ptr<core::IMediaObject> downstream) {
    m_downstream = downstream;
    return {};
}

core::VoidResult AJASource::Disconnect() {
    m_downstream = nullptr;
    return {};
}

void AJASource::OnStateChange(core::StateChangeCallback callback) {
    m_stateCallback = callback;
}

void AJASource::OnError(core::ErrorCallback callback) {
    m_errorCallback = callback;
}

} // namespace openmedia::io
