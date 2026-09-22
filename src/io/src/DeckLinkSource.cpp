/// @file DeckLinkSource.cpp
#include <openmedia/io/DeckLinkSource.h>
#include <openmedia/core/Logger.h>
#include <openmedia/core/ErrorCodes.h>
#include <DeckLinkAPI.h>

#include <mutex>
#include <queue>
#include <atomic>
#include <condition_variable>
#include <algorithm>
#include <cstring>

namespace openmedia::io {

namespace {

// Fast, branchless integer conversion from UYVY 4:2:2 to BGRA 8-bit
inline void ConvertUYVYToBGRA(
    const uint8_t* src,
    uint8_t* dst,
    uint32_t width,
    uint32_t height,
    int32_t srcStride,
    int32_t dstStride)
{
    for (uint32_t y = 0; y < height; ++y) {
        const uint8_t* s = src + static_cast<size_t>(y) * srcStride;
        uint8_t* d = dst + static_cast<size_t>(y) * dstStride;

        for (uint32_t x = 0; x < width; x += 2) {
            int u  = s[0] - 128;
            int y0 = s[1];
            int v  = s[2] - 128;
            int y1 = s[3];
            s += 4;

            int c0 = y0 - 16;
            int c1 = y1 - 16;
            if (c0 < 0) c0 = 0;
            if (c1 < 0) c1 = 0;

            int r0 = (298 * c0 + 409 * v + 128) >> 8;
            int g0 = (298 * c0 - 100 * u - 208 * v + 128) >> 8;
            int b0 = (298 * c0 + 516 * u + 128) >> 8;

            int r1 = (298 * c1 + 409 * v + 128) >> 8;
            int g1 = (298 * c1 - 100 * u - 208 * v + 128) >> 8;
            int b1 = (298 * c1 + 516 * u + 128) >> 8;

            d[0] = static_cast<uint8_t>(std::clamp(b0, 0, 255));
            d[1] = static_cast<uint8_t>(std::clamp(g0, 0, 255));
            d[2] = static_cast<uint8_t>(std::clamp(r0, 0, 255));
            d[3] = 255;

            d[4] = static_cast<uint8_t>(std::clamp(b1, 0, 255));
            d[5] = static_cast<uint8_t>(std::clamp(g1, 0, 255));
            d[6] = static_cast<uint8_t>(std::clamp(r1, 0, 255));
            d[7] = 255;

            d += 8;
        }
    }
}

} // anonymous namespace

// Callback implementation to receive frames from DeckLink hardware
class DeckLinkInputCallback : public IDeckLinkInputCallback {
public:
    DeckLinkInputCallback(
        std::function<void(IDeckLinkVideoInputFrame*)> onFrame,
        std::function<void(IDeckLinkDisplayMode*)> onFormatChange = nullptr)
        : m_onFrame(std::move(onFrame)), m_onFormatChange(std::move(onFormatChange)), m_refCount(1) {}

    int VideoInputFormatChanged(int /*notificationEvents*/, IDeckLinkDisplayMode* newDisplayMode, int /*detectedSignalFlags*/) override {
        if (newDisplayMode && m_onFormatChange) {
            m_onFormatChange(newDisplayMode);
        }
        return 0; // S_OK
    }

    int VideoInputFrameArrived(IDeckLinkVideoInputFrame* videoFrame, IDeckLinkAudioInputPacket* /*audioPacket*/) override {
        if (videoFrame && m_onFrame) {
            m_onFrame(videoFrame);
        }
        return 0;
    }

    // IUnknown implementation
#ifdef _WIN32
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID /*iid*/, void** /*ppv*/) override { return E_NOINTERFACE; }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++m_refCount; }
    ULONG STDMETHODCALLTYPE Release() override {
        ULONG ref = --m_refCount;
        if (ref == 0) delete this;
        return ref;
    }
#else
    int QueryInterface(void* /*iid*/, void** /*ppv*/) override { return -1; }
    unsigned long AddRef() override { return ++m_refCount; }
    unsigned long Release() override {
        unsigned long ref = --m_refCount;
        if (ref == 0) delete this;
        return ref;
    }
#endif

private:
    std::function<void(IDeckLinkVideoInputFrame*)> m_onFrame;
    std::function<void(IDeckLinkDisplayMode*)> m_onFormatChange;
    std::atomic<unsigned long> m_refCount;
};

struct DeckLinkSource::Impl {
    std::mutex mutex;
    core::PipelineState state{core::PipelineState::Stopped};
    std::shared_ptr<core::IMediaObject> downstream;
    core::StateChangeCallback onStateChange;
    core::ErrorCallback onError;

    DeviceInfo info{"Blackmagic DeckLink Device", "DeckLink0", DeviceType::VideoInput};
    DeviceFormat currentFormat{1920, 1080, 59.94f, core::PixelFormat::BGRA, core::SampleFormat::Unknown, 0, 0};
    bool autoFormatDetection = true;

    // DeckLink hardware pointers
    IDeckLink* deckLink = nullptr;
    IDeckLinkInput* deckLinkInput = nullptr;
    DeckLinkInputCallback* callback = nullptr;

    // Frame queue
    std::queue<std::shared_ptr<core::MediaFrame>> frameQueue;
    int64_t frameCount = 0;

    void ClearQueue() {
        std::queue<std::shared_ptr<core::MediaFrame>> empty;
        std::swap(frameQueue, empty);
    }
};

DeckLinkSource::DeckLinkSource() : m_impl(new Impl()) {}

DeckLinkSource::~DeckLinkSource() {
    (void)Stop();
    if (m_impl->deckLinkInput) {
        m_impl->deckLinkInput->Release();
        m_impl->deckLinkInput = nullptr;
    }
    if (m_impl->deckLink) {
        m_impl->deckLink->Release();
        m_impl->deckLink = nullptr;
    }
}

std::string DeckLinkSource::GetName() const { return "DeckLinkSource"; }

core::PipelineState DeckLinkSource::GetState() const { return m_impl->state; }

void DeckLinkSource::EnableAutoFormatDetection(bool enable) {
    std::lock_guard lock(m_impl->mutex);
    m_impl->autoFormatDetection = enable;
}

core::VoidResult DeckLinkSource::Open(int deviceIndex) {
    std::lock_guard lock(m_impl->mutex);
    if (m_impl->state == core::PipelineState::Running) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "Cannot open device while running"));
    }

    if (m_impl->deckLinkInput) {
        m_impl->deckLinkInput->Release();
        m_impl->deckLinkInput = nullptr;
    }
    if (m_impl->deckLink) {
        m_impl->deckLink->Release();
        m_impl->deckLink = nullptr;
    }

    IDeckLinkIterator* iterator = CreateDeckLinkIteratorInstance();
    if (!iterator) {
        return std::unexpected(core::Error::Make(core::ErrorCode::NotSupported, "DeckLink drivers not installed or COM initialization failed"));
    }

    int curIdx = 0;
    IDeckLink* dl = nullptr;
    bool found = false;

    while (iterator->Next(&dl) == 0 && dl != nullptr) {
        if (curIdx == deviceIndex) {
            m_impl->deckLink = dl;
            found = true;
            break;
        }
        dl->Release();
        curIdx++;
    }
    iterator->Release();

    if (!found || !m_impl->deckLink) {
        return std::unexpected(core::Error::Make(core::ErrorCode::FileNotFound, "No DeckLink device found at specified index"));
    }

    const char* modelName = nullptr;
    if (m_impl->deckLink->GetModelName(&modelName) == 0 && modelName) {
        m_impl->info.name = modelName;
        m_impl->info.id = modelName;
    } else {
        m_impl->info.name = "DeckLink Device (" + std::to_string(deviceIndex) + ")";
        m_impl->info.id = "DeckLink" + std::to_string(deviceIndex);
    }

#ifdef _WIN32
    if (m_impl->deckLink->QueryInterface(IID_IDeckLinkInput, (void**)&m_impl->deckLinkInput) != 0 || !m_impl->deckLinkInput) {
#else
    if (m_impl->deckLink->QueryInterface(nullptr, (void**)&m_impl->deckLinkInput) != 0 || !m_impl->deckLinkInput) {
#endif
        return std::unexpected(core::Error::Make(core::ErrorCode::NotSupported, "DeckLink device does not support video input"));
    }

    return {};
}

core::VoidResult DeckLinkSource::Open(const std::string& deviceNameOrId) {
    std::lock_guard lock(m_impl->mutex);
    if (m_impl->state == core::PipelineState::Running) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "Cannot open device while running"));
    }

    // Check if input is a pure integer index
    try {
        size_t idx = 0;
        int parsed = std::stoi(deviceNameOrId, &idx);
        if (idx == deviceNameOrId.length()) {
            return Open(parsed);
        }
    } catch (...) {}

    if (m_impl->deckLinkInput) {
        m_impl->deckLinkInput->Release();
        m_impl->deckLinkInput = nullptr;
    }
    if (m_impl->deckLink) {
        m_impl->deckLink->Release();
        m_impl->deckLink = nullptr;
    }

    IDeckLinkIterator* iterator = CreateDeckLinkIteratorInstance();
    if (!iterator) {
        return std::unexpected(core::Error::Make(core::ErrorCode::NotSupported, "DeckLink drivers not installed"));
    }

    IDeckLink* dl = nullptr;
    bool found = false;

    while (iterator->Next(&dl) == 0 && dl != nullptr) {
        const char* modelName = nullptr;
        if (dl->GetModelName(&modelName) == 0 && modelName) {
            std::string nameStr(modelName);
            if (nameStr.find(deviceNameOrId) != std::string::npos || deviceNameOrId.find(nameStr) != std::string::npos) {
                m_impl->deckLink = dl;
                m_impl->info.name = nameStr;
                m_impl->info.id = nameStr;
                found = true;
                break;
            }
        }
        dl->Release();
    }
    iterator->Release();

    if (!found || !m_impl->deckLink) {
        return std::unexpected(core::Error::Make(core::ErrorCode::FileNotFound, "DeckLink device not found: " + deviceNameOrId));
    }

#ifdef _WIN32
    if (m_impl->deckLink->QueryInterface(IID_IDeckLinkInput, (void**)&m_impl->deckLinkInput) != 0 || !m_impl->deckLinkInput) {
#else
    if (m_impl->deckLink->QueryInterface(nullptr, (void**)&m_impl->deckLinkInput) != 0 || !m_impl->deckLinkInput) {
#endif
        return std::unexpected(core::Error::Make(core::ErrorCode::NotSupported, "DeckLink device does not support video input"));
    }

    return {};
}

core::VoidResult DeckLinkSource::Initialize() {
    std::lock_guard lock(m_impl->mutex);

    // If device not yet opened, try opening device 0 by default
    if (!m_impl->deckLinkInput) {
        IDeckLinkIterator* iterator = CreateDeckLinkIteratorInstance();
        if (!iterator) {
            return std::unexpected(core::Error::Make(core::ErrorCode::NotSupported, "DeckLink drivers not installed"));
        }

        if (iterator->Next(&m_impl->deckLink) != 0 || !m_impl->deckLink) {
            iterator->Release();
            return std::unexpected(core::Error::Make(core::ErrorCode::FileNotFound, "No DeckLink device found"));
        }
        iterator->Release();

        const char* modelName = nullptr;
        if (m_impl->deckLink->GetModelName(&modelName) == 0 && modelName) {
            m_impl->info.name = modelName;
            m_impl->info.id = modelName;
        }

#ifdef _WIN32
        if (m_impl->deckLink->QueryInterface(IID_IDeckLinkInput, (void**)&m_impl->deckLinkInput) != 0 || !m_impl->deckLinkInput) {
#else
        if (m_impl->deckLink->QueryInterface(nullptr, (void**)&m_impl->deckLinkInput) != 0 || !m_impl->deckLinkInput) {
#endif
            return std::unexpected(core::Error::Make(core::ErrorCode::NotSupported, "Device does not support input"));
        }
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
    m_impl->ClearQueue();

    // Setup input callback for Zero-Latency DMA frame arrival and dynamic format changes
    m_impl->callback = new DeckLinkInputCallback(
        [this](IDeckLinkVideoInputFrame* bmdFrame) {
            if (!bmdFrame) return;

            long width = bmdFrame->GetWidth();
            long height = bmdFrame->GetHeight();
            long rowBytes = bmdFrame->GetRowBytes();
            BMDPixelFormat bmdPixFmt = bmdFrame->GetPixelFormat();

            void* rawBytes = nullptr;
            if (bmdFrame->GetBytes(&rawBytes) != 0 || !rawBytes) {
                return;
            }

            // Create target MediaFrame in BGRA format
            auto frame = core::MediaFrame::CreateVideo(
                static_cast<uint32_t>(width),
                static_cast<uint32_t>(height),
                core::PixelFormat::BGRA
            );
            if (!frame) return;

            uint8_t* dstPlane = frame->GetVideoPlane(0);
            int32_t dstStride = frame->GetLineSize(0);

            if (bmdPixFmt == bmdFormat8BitBGRA) {
                // Zero-Latency Direct Memory Copy from DMA slab
                if (rowBytes == dstStride) {
                    std::memcpy(dstPlane, rawBytes, static_cast<size_t>(rowBytes) * height);
                } else {
                    for (long y = 0; y < height; ++y) {
                        std::memcpy(
                            dstPlane + static_cast<size_t>(y) * dstStride,
                            static_cast<const uint8_t*>(rawBytes) + static_cast<size_t>(y) * rowBytes,
                            std::min<size_t>(rowBytes, dstStride));
                    }
                }
            } else if (bmdPixFmt == bmdFormat8BitYUV) {
                // Fast conversion from UYVY 4:2:2 to BGRA
                ConvertUYVYToBGRA(
                    static_cast<const uint8_t*>(rawBytes),
                    dstPlane,
                    static_cast<uint32_t>(width),
                    static_cast<uint32_t>(height),
                    rowBytes,
                    dstStride
                );
            } else {
                // Fallback direct copy
                std::memcpy(dstPlane, rawBytes, std::min<size_t>(static_cast<size_t>(rowBytes) * height, static_cast<size_t>(dstStride) * height));
            }

            frame->SetPts(m_impl->frameCount * 3000);

            std::shared_ptr<core::IMediaObject> down;
            {
                std::lock_guard lockQ(m_impl->mutex);
                m_impl->frameQueue.push(frame);
                m_impl->frameCount++;
                down = m_impl->downstream;
            }

            if (down) {
                (void)down->PushFrame(frame);
            }
        },
        [this](IDeckLinkDisplayMode* newDisplayMode) {
            // Auto Format Detection: dynamically adapts width, height, framerate
            std::lock_guard lockFormat(m_impl->mutex);
            if (!m_impl->deckLinkInput || !newDisplayMode) return;

            long newWidth = newDisplayMode->GetWidth();
            long newHeight = newDisplayMode->GetHeight();
            long long duration = 0, timeScale = 0;
            float fps = 59.94f;
            if (newDisplayMode->GetFrameRate(&duration, &timeScale) == 0 && duration > 0) {
                fps = static_cast<float>(timeScale) / static_cast<float>(duration);
            }

            m_impl->currentFormat.width = static_cast<uint32_t>(newWidth);
            m_impl->currentFormat.height = static_cast<uint32_t>(newHeight);
            m_impl->currentFormat.fps = fps;

            BMDDisplayMode mode = newDisplayMode->GetDisplayMode();

            // Safe stream flush & re-arm to prevent buffer overflow and screen tearing
            m_impl->deckLinkInput->PauseStreams();
            BMDVideoInputFlags flags = m_impl->autoFormatDetection ? bmdVideoInputEnableFormatDetection : bmdVideoInputFlagDefault;
            m_impl->deckLinkInput->EnableVideoInput(mode, bmdFormat8BitBGRA, flags);
            m_impl->deckLinkInput->FlushStreams();
            m_impl->ClearQueue();
            m_impl->deckLinkInput->StartStreams();
        }
    );

    m_impl->deckLinkInput->SetCallback(m_impl->callback);

    BMDVideoInputFlags flags = m_impl->autoFormatDetection ? bmdVideoInputEnableFormatDetection : bmdVideoInputFlagDefault;
    m_impl->deckLinkInput->EnableVideoInput(bmdModeHD1080p5994, bmdFormat8BitBGRA, flags);
    m_impl->deckLinkInput->StartStreams();

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

    m_impl->ClearQueue();

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
    formats.push_back({3840, 2160, 60.0f, core::PixelFormat::BGRA, core::SampleFormat::Unknown, 0, 0});
    formats.push_back({3840, 2160, 59.94f, core::PixelFormat::BGRA, core::SampleFormat::Unknown, 0, 0});
    formats.push_back({3840, 2160, 50.0f, core::PixelFormat::BGRA, core::SampleFormat::Unknown, 0, 0});
    formats.push_back({3840, 2160, 30.0f, core::PixelFormat::BGRA, core::SampleFormat::Unknown, 0, 0});
    formats.push_back({3840, 2160, 25.0f, core::PixelFormat::BGRA, core::SampleFormat::Unknown, 0, 0});
    formats.push_back({1920, 1080, 60.0f, core::PixelFormat::BGRA, core::SampleFormat::Unknown, 0, 0});
    formats.push_back({1920, 1080, 59.94f, core::PixelFormat::BGRA, core::SampleFormat::Unknown, 0, 0});
    formats.push_back({1920, 1080, 50.0f, core::PixelFormat::BGRA, core::SampleFormat::Unknown, 0, 0});
    formats.push_back({1920, 1080, 30.0f, core::PixelFormat::BGRA, core::SampleFormat::Unknown, 0, 0});
    formats.push_back({1920, 1080, 25.0f, core::PixelFormat::BGRA, core::SampleFormat::Unknown, 0, 0});
    formats.push_back({1280, 720, 60.0f, core::PixelFormat::BGRA, core::SampleFormat::Unknown, 0, 0});
    formats.push_back({1280, 720, 59.94f, core::PixelFormat::BGRA, core::SampleFormat::Unknown, 0, 0});
    formats.push_back({1280, 720, 50.0f, core::PixelFormat::BGRA, core::SampleFormat::Unknown, 0, 0});
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
