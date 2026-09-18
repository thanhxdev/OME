#include "openmedia/outputs/decklink/DeckLinkOutput.h"
#include <DeckLinkAPI.h>
#include <mutex>
#include <atomic>

namespace openmedia {
namespace outputs {
namespace decklink {

struct DeckLinkOutput::Impl {
    bool started = false;
    int deviceIndex = 0;
    int videoMode = 0;
    mutable std::mutex mutex;

    IDeckLink* deckLink = nullptr;
    IDeckLinkOutput* deckLinkOutput = nullptr;

    bool Init(int devIdx, int mode) {
        std::lock_guard<std::mutex> lock(mutex);
        deviceIndex = devIdx;
        videoMode = mode;

#ifdef _WIN32
        IDeckLinkIterator* iterator = nullptr;
        HRESULT hr = CoCreateInstance(CLSID_CDeckLinkIterator, nullptr, CLSCTX_ALL, IID_IDeckLinkIterator, (void**)&iterator);
        if (SUCCEEDED(hr) && iterator) {
            int curIdx = 0;
            IDeckLink* dl = nullptr;
            while (iterator->Next(&dl) == S_OK) {
                if (curIdx == deviceIndex) {
                    deckLink = dl;
                    break;
                }
                dl->Release();
                curIdx++;
            }
            iterator->Release();

            if (deckLink) {
                if (deckLink->QueryInterface(IID_IDeckLinkOutput, (void**)&deckLinkOutput) == S_OK) {
                    BMDDisplayMode bmdMode = bmdModeHD1080p5994;
                    deckLinkOutput->EnableVideoOutput(bmdMode, bmdVideoOutputFlagDefault);
                    deckLinkOutput->StartScheduledPlayback(0, 1000, 1.0);
                }
            }
        }
#endif
        started = true;
        return true;
    }

    void Release() {
        std::lock_guard<std::mutex> lock(mutex);
        if (!started) return;

#ifdef _WIN32
        if (deckLinkOutput) {
            deckLinkOutput->StopScheduledPlayback(0, nullptr, 0);
            deckLinkOutput->DisableVideoOutput();
            deckLinkOutput->Release();
            deckLinkOutput = nullptr;
        }
        if (deckLink) {
            deckLink->Release();
            deckLink = nullptr;
        }
#endif
        started = false;
    }

    bool Display(std::shared_ptr<core::MediaFrame> frame) {
        std::lock_guard<std::mutex> lock(mutex);
        if (!started || !frame) return false;

#ifdef _WIN32
        if (deckLinkOutput) {
            // Display frame directly to DeckLink hardware output
            IDeckLinkMutableVideoFrame* dlFrame = nullptr;
            if (deckLinkOutput->CreateVideoFrame(
                    static_cast<int32_t>(frame->GetWidth()),
                    static_cast<int32_t>(frame->GetHeight()),
                    frame->GetLineSize(0),
                    bmdFormat8BitBGRA,
                    bmdFrameFlagDefault,
                    &dlFrame) == S_OK && dlFrame) {
                void* bytes = nullptr;
                if (dlFrame->GetBytes(&bytes) == S_OK && bytes) {
                    std::memcpy(bytes, frame->GetVideoPlane(0), static_cast<size_t>(frame->GetLineSize(0)) * frame->GetHeight());
                }
                deckLinkOutput->DisplayVideoFrameSync(dlFrame);
                dlFrame->Release();
                return true;
            }
        }
#endif
        return true;
    }
};

DeckLinkOutput::DeckLinkOutput() : m_impl(std::make_unique<Impl>()) {
}

DeckLinkOutput::~DeckLinkOutput() {
    Stop();
}

bool DeckLinkOutput::Start(int device_index, int video_mode) {
    return m_impl->Init(device_index, video_mode);
}

void DeckLinkOutput::Stop() {
    m_impl->Release();
}

bool DeckLinkOutput::IsStarted() const {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    return m_impl->started;
}

bool DeckLinkOutput::DisplayVideoFrame(std::shared_ptr<core::MediaFrame> frame) {
    return m_impl->Display(frame);
}

} // namespace decklink
} // namespace outputs
} // namespace openmedia
