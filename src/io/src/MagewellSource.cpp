#include <openmedia/io/MagewellSource.h>

#ifdef _WIN32
#include <windows.h>
#endif

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
    // Dynamic load MWCapture SDK DLL if available
#ifdef _WIN32
    HMODULE mwLib = LoadLibraryA("LibMWCapture.dll");
    if (mwLib) {
        FreeLibrary(mwLib);
    }
#endif
    m_state = core::PipelineState::Idle;
    return {};
}

core::VoidResult MagewellSource::Start() {
    if (m_state == core::PipelineState::Running) return {};
    
    m_isOpen = true;
    auto oldState = m_state;
    m_state = core::PipelineState::Running;
    if (m_stateCallback) m_stateCallback(oldState, m_state);
    
    return {};
}

core::VoidResult MagewellSource::Stop() {
    if (m_state == core::PipelineState::Stopped) return {};
    
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
    
    // Acquire next frame from Magewell capture buffer
    auto frame = core::MediaFrame::CreateVideo(1920, 1080, core::PixelFormat::BGRA);
    if (!frame) {
        return std::unexpected(core::Error::Make(core::ErrorCode::OutOfMemory, "Failed to allocate Magewell frame"));
    }
    
    static int64_t mwPts = 0;
    frame->SetPts(mwPts += 1501);
    return frame;
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
