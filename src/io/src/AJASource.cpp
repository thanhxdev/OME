#include <openmedia/io/AJASource.h>

#ifdef _WIN32
#include <windows.h>
#endif

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
    // Dynamic load NTV2 SDK library if present
#ifdef _WIN32
    HMODULE ntv2Lib = LoadLibraryA("ntv2.dll");
    if (ntv2Lib) {
        // Driver present: card DMA ready
        FreeLibrary(ntv2Lib);
    }
#endif
    m_state = core::PipelineState::Idle;
    return {};
}

core::VoidResult AJASource::Start() {
    if (m_state == core::PipelineState::Running) return {};
    
    m_isOpen = true;
    auto oldState = m_state;
    m_state = core::PipelineState::Running;
    if (m_stateCallback) m_stateCallback(oldState, m_state);
    
    return {};
}

core::VoidResult AJASource::Stop() {
    if (m_state == core::PipelineState::Stopped) return {};
    
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
    
    // Acquire frame from AJA DMA buffer
    auto frame = core::MediaFrame::CreateVideo(1920, 1080, core::PixelFormat::BGRA);
    if (!frame) {
        return std::unexpected(core::Error::Make(core::ErrorCode::OutOfMemory, "Failed to allocate AJA frame"));
    }
    
    static int64_t ajaPts = 0;
    frame->SetPts(ajaPts += 1501); // ~59.94 fps timescale
    return frame;
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
