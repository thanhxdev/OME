/// @file DeckLinkSource.cpp
#include <openmedia/io/DeckLinkSource.h>
#include <openmedia/core/Logger.h>
#include <openmedia/core/ErrorCodes.h>
#include <DeckLinkAPI.h>

#include <mutex>
#include <queue>
#include <atomic>
#include <condition_variable>

namespace openmedia::io {

// Callback implementation to receive frames from DeckLink hardware
class DeckLinkInputCallback : public IDeckLinkInputCallback {
public:
    DeckLinkInputCallback(
        std::function<void(IDeckLinkVideoInputFrame*)> onFrame,
        std::function<void(BMDDisplayMode)> onFormatChange = nullptr) 
        : m_onFrame(std::move(onFrame)), m_onFormatChange(std::move(onFormatChange)), m_refCount(1) {}

    int VideoInputFormatChanged(int notificationEvents, IDeckLinkDisplayMode* newDisplayMode, int /*detectedSignalFlags*/) override {
        // if (notificationEvents & bmdVideoInputDisplayModeChanged) {
            BMDDisplayMode mode = newDisplayMode->GetDisplayMode();
            if (m_onFormatChange) {
                m_onFormatChange(mode);
            }
        // }
        return 0; // Return S_OK equivalent
    }

    int VideoInputFrameArrived(IDeckLinkVideoInputFrame* videoFrame, IDeckLinkAudioInputPacket* /*audioPacket*/) override {
        if (videoFrame) {
            m_onFrame(videoFrame);
        }
        return 0;
    }

    // IUnknown
#ifdef _WIN32
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID /*iid*/, void** /*ppv*/) override { return E_NOINTERFACE; }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++m_refCount; }
    ULONG STDMETHODCALLTYPE Release() override { 
        ULONG ref = --m_refCount;
        if (ref == 0) delete this;
        return ref;
    }
#else
    int QueryInterface(void* /*iid*/, void** /*ppv*/) override { return -1; } // E_NOINTERFACE
    unsigned long AddRef() override { return ++m_refCount; }
    unsigned long Release() override { 
        unsigned long ref = --m_refCount;
        if (ref == 0) delete this;
        return ref;
    }
#endif

private:
    std::function<void(IDeckLinkVideoInputFrame*)> m_onFrame;
    std::function<void(BMDDisplayMode)> m_onFormatChange;
    std::atomic<unsigned long> m_refCount;
};

struct DeckLinkSource::Impl {
    std::mutex mutex;
    core::PipelineState state{core::PipelineState::Stopped};
    std::shared_ptr<core::IMediaObject> downstream;
    core::StateChangeCallback onStateChange;
    core::ErrorCallback onError;

    DeviceInfo info{"DeckLink Video Capture", "DeckLink0", DeviceType::VideoInput};
    DeviceFormat currentFormat{1920, 1080, 30.0f, core::PixelFormat::BGRA, core::SampleFormat::Unknown, 0, 0};

    // DeckLink hardware pointers
    IDeckLink* deckLink = nullptr;
    IDeckLinkInput* deckLinkInput = nullptr;
    DeckLinkInputCallback* callback = nullptr;

    // Frame queue
    std::queue<std::shared_ptr<core::MediaFrame>> frameQueue;
    int64_t frameCount = 0;
};

DeckLinkSource::DeckLinkSource() : m_impl(new Impl()) {}

DeckLinkSource::~DeckLinkSource() {
    (void)Stop();
    if (m_impl->deckLinkInput) {
        m_impl->deckLinkInput->Release();
    }
    if (m_impl->deckLink) {
        m_impl->deckLink->Release();
    }
}

std::string DeckLinkSource::GetName() const { return "DeckLinkSource"; }

core::PipelineState DeckLinkSource::GetState() const { return m_impl->state; }

core::VoidResult DeckLinkSource::Initialize() {
    std::lock_guard lock(m_impl->mutex);
    
    IDeckLinkIterator* iterator = CreateDeckLinkIteratorInstance();
    if (!iterator) {
        return std::unexpected(core::Error::Make(core::ErrorCode::NotSupported, "DeckLink drivers not installed"));
    }

    // Just grab the first device for now
    if (iterator->Next(&m_impl->deckLink) != 0) { // Assume 0 is S_OK
        return std::unexpected(core::Error::Make(core::ErrorCode::FileNotFound, "No DeckLink device found"));
    }
    
#ifdef _WIN32
    IID dummyIID = {};
    if (m_impl->deckLink->QueryInterface(dummyIID, (void**)&m_impl->deckLinkInput) != 0) {
#else
    if (m_impl->deckLink->QueryInterface(nullptr, (void**)&m_impl->deckLinkInput) != 0) {
#endif
        return std::unexpected(core::Error::Make(core::ErrorCode::NotSupported, "Device does not support input"));
    }

    return {};
}

core::VoidResult DeckLinkSource::Start() {
    std::lock_guard lock(m_impl->mutex);
    if (m_impl->state == core::PipelineState::Running) return {};

    if (!m_impl->deckLinkInput) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "DeckLinkSource not initialized"));
    }

    m_impl->frameCount = 0;

    m_impl->callback = new DeckLinkInputCallback([this](IDeckLinkVideoInputFrame* bmdFrame) {
        // This is called from a DeckLink SDK thread
        auto frame = core::MediaFrame::CreateVideo(
            bmdFrame->GetWidth(),
            bmdFrame->GetHeight(),
            core::PixelFormat::BGRA // Simplified mapping
        );
        frame->SetPts(m_impl->frameCount * 3000);

        std::lock_guard lockQ(m_impl->mutex);
        m_impl->frameQueue.push(frame);
        m_impl->frameCount++;
    }, [this](BMDDisplayMode newMode) {
        // Handle format change from DeckLink hardware
        std::lock_guard lock(m_impl->mutex);
        if (m_impl->deckLinkInput) {
            // m_impl->deckLinkInput->PauseStreams();
            // m_impl->deckLinkInput->EnableVideoInput(newMode, bmdFormat8BitBGRA, bmdVideoInputEnableFormatDetection);
            // m_impl->deckLinkInput->FlushStreams();
            // Clear old frames to prevent glitch
            std::queue<std::shared_ptr<core::MediaFrame>> empty;
            std::swap(m_impl->frameQueue, empty);
        }
    });

    m_impl->deckLinkInput->SetCallback(m_impl->callback);
    // m_impl->deckLinkInput->EnableVideoInput(bmdModeHD1080p30, bmdFormat8BitBGRA, bmdVideoInputEnableFormatDetection);
    // m_impl->deckLinkInput->StartStreams();

    auto oldState = m_impl->state;
    m_impl->state = core::PipelineState::Running;
    if (m_impl->onStateChange) {
        m_impl->onStateChange(oldState, core::PipelineState::Running);
    }
    return {};
}

core::VoidResult DeckLinkSource::Stop() {
    std::lock_guard lock(m_impl->mutex);
    if (m_impl->state == core::PipelineState::Stopped) return {};

    if (m_impl->deckLinkInput) {
        m_impl->deckLinkInput->StopStreams();
        m_impl->deckLinkInput->DisableVideoInput();
        m_impl->deckLinkInput->SetCallback(nullptr);
    }

    if (m_impl->callback) {
        m_impl->callback->Release();
        m_impl->callback = nullptr;
    }

    // Clear queue
    std::queue<std::shared_ptr<core::MediaFrame>> empty;
    std::swap(m_impl->frameQueue, empty);

    auto oldState = m_impl->state;
    m_impl->state = core::PipelineState::Stopped;
    if (m_impl->onStateChange) {
        m_impl->onStateChange(oldState, core::PipelineState::Stopped);
    }
    return {};
}

core::VoidResult DeckLinkSource::PushFrame(std::shared_ptr<core::MediaFrame> /*frame*/) {
    return std::unexpected(core::Error::Make(core::ErrorCode::NotImplemented, "DeckLinkSource is an input node"));
}

core::Result<std::shared_ptr<core::MediaFrame>> DeckLinkSource::PullFrame() {
    std::lock_guard lock(m_impl->mutex);
    if (m_impl->state != core::PipelineState::Running) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "DeckLinkSource is not running"));
    }

    if (m_impl->frameQueue.empty()) {
        return std::unexpected(core::Error::Make(core::ErrorCode::WouldBlock, "No frame available from DeckLink"));
    }

    auto frame = m_impl->frameQueue.front();
    m_impl->frameQueue.pop();
    return frame;
}

core::VoidResult DeckLinkSource::Connect(std::shared_ptr<core::IMediaObject> downstream) {
    std::lock_guard lock(m_impl->mutex);
    m_impl->downstream = downstream;
    return {};
}

core::VoidResult DeckLinkSource::Disconnect() {
    std::lock_guard lock(m_impl->mutex);
    m_impl->downstream.reset();
    return {};
}

void DeckLinkSource::OnStateChange(core::StateChangeCallback callback) {
    m_impl->onStateChange = callback;
}

void DeckLinkSource::OnError(core::ErrorCallback callback) {
    m_impl->onError = callback;
}

const DeviceInfo& DeckLinkSource::GetDeviceInfo() const {
    return m_impl->info;
}

std::vector<DeviceFormat> DeckLinkSource::GetSupportedFormats() const {
    std::vector<DeviceFormat> formats;
    formats.push_back({1920, 1080, 60.0f, core::PixelFormat::BGRA, core::SampleFormat::Unknown, 0, 0});
    formats.push_back({1920, 1080, 30.0f, core::PixelFormat::BGRA, core::SampleFormat::Unknown, 0, 0});
    formats.push_back({1280, 720, 60.0f, core::PixelFormat::BGRA, core::SampleFormat::Unknown, 0, 0});
    return formats;
}

core::VoidResult DeckLinkSource::SetFormat(const DeviceFormat& format) {
    std::lock_guard lock(m_impl->mutex);
    if (m_impl->state == core::PipelineState::Running) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "Cannot change format while running"));
    }
    m_impl->currentFormat = format;
    return {};
}

const DeviceFormat& DeckLinkSource::GetCurrentFormat() const {
    return m_impl->currentFormat;
}

} // namespace openmedia::io
