#include <openmedia/io/WASAPISource.h>
#include <openmedia/io/MediaReader.h>
#include <openmedia/core/Logger.h>
#include <openmedia/core/ErrorCodes.h>

#include <mutex>
#include <thread>
#include <atomic>

extern "C" {
#include <libswresample/swresample.h>
}

namespace openmedia::io {

struct WASAPISource::Impl {
    std::mutex mutex;
    core::PipelineState state{core::PipelineState::Stopped};
    std::shared_ptr<core::IMediaObject> downstream;
    core::StateChangeCallback onStateChange;
    core::ErrorCallback onError;

    std::string deviceName;
    std::atomic<bool> isRunning{false};
    std::thread captureThread;
    
    MediaReader reader;
    AVFrame* decodedFrame = nullptr;
    SwrContext* swrCtx = nullptr;

    Impl() {
        decodedFrame = av_frame_alloc();
    }

    ~Impl() {
        if (decodedFrame) av_frame_free(&decodedFrame);
        if (swrCtx) swr_free(&swrCtx);
    }

    void CaptureLoop() {
        while (isRunning) {
            if (state != core::PipelineState::Running) {
                std::this_thread::sleep_for(std::chrono::milliseconds(10));
                continue;
            }

            av_frame_unref(decodedFrame);
            auto result = reader.ReadAudioFrame(decodedFrame);
            if (!result) {
                if (result.error().code == core::ErrorCode::EndOfStream) {
                    break;
                }
                std::this_thread::sleep_for(std::chrono::milliseconds(10));
                continue;
            }

            if (downstream) {
                if (!swrCtx) {
                    AVChannelLayout outChLayout;
                    av_channel_layout_default(&outChLayout, 2);
                    
                    swr_alloc_set_opts2(&swrCtx, 
                        &outChLayout, AV_SAMPLE_FMT_FLT, 48000,
                        &decodedFrame->ch_layout, 
                        static_cast<AVSampleFormat>(decodedFrame->format), 
                        decodedFrame->sample_rate, 
                        0, nullptr);
                        
                    if (swrCtx) swr_init(swrCtx);
                }

                if (swrCtx) {
                    auto mediaFrame = core::MediaFrame::CreateAudio(decodedFrame->nb_samples, 2, core::SampleFormat::Float32, 48000);
                    uint8_t* outData[1] = { mediaFrame->GetAudioData(0) };

                    swr_convert(swrCtx, outData, decodedFrame->nb_samples, 
                                (const uint8_t**)decodedFrame->data, decodedFrame->nb_samples);

                    mediaFrame->SetPts(decodedFrame->pts);
                    downstream->PushFrame(mediaFrame);
                }
            }
        }
    }
};

WASAPISource::WASAPISource() : m_impl(new Impl()) {}

WASAPISource::~WASAPISource() {
    (void)Stop();
}

core::VoidResult WASAPISource::Open(const std::string& deviceName) {
    std::lock_guard lock(m_impl->mutex);
    m_impl->deviceName = deviceName;
    std::string url = "audio=" + deviceName;
    return m_impl->reader.Open(url, "dshow");
}

std::string WASAPISource::GetName() const { return "WASAPISource"; }

core::PipelineState WASAPISource::GetState() const { return m_impl->state; }

core::VoidResult WASAPISource::Initialize() {
    return {};
}

core::VoidResult WASAPISource::Start() {
    std::lock_guard lock(m_impl->mutex);
    if (m_impl->state == core::PipelineState::Running) return {};
    
    core::PipelineState oldState = m_impl->state;
    m_impl->state = core::PipelineState::Running;
    m_impl->isRunning = true;
    m_impl->captureThread = std::thread(&Impl::CaptureLoop, m_impl.get());
    
    if (m_impl->onStateChange) m_impl->onStateChange(oldState, m_impl->state);
    return {};
}

core::VoidResult WASAPISource::Stop() {
    core::PipelineState oldState;
    {
        std::lock_guard lock(m_impl->mutex);
        if (m_impl->state == core::PipelineState::Stopped) return {};
        
        oldState = m_impl->state;
        m_impl->state = core::PipelineState::Stopped;
        m_impl->isRunning = false;
    }
    
    if (m_impl->captureThread.joinable()) {
        m_impl->captureThread.join();
    }
    
    if (m_impl->onStateChange) m_impl->onStateChange(oldState, m_impl->state);
    return {};
}

core::VoidResult WASAPISource::PushFrame(std::shared_ptr<core::MediaFrame> frame) {
    return std::unexpected(core::Error::Make(core::ErrorCode::NotSupported, "Source cannot accept frames"));
}

core::Result<std::shared_ptr<core::MediaFrame>> WASAPISource::PullFrame() {
    return std::unexpected(core::Error::Make(core::ErrorCode::NotSupported, "Pull mode not supported. Use push model."));
}

core::VoidResult WASAPISource::Connect(std::shared_ptr<core::IMediaObject> downstream) {
    std::lock_guard lock(m_impl->mutex);
    m_impl->downstream = downstream;
    return {};
}

core::VoidResult WASAPISource::Disconnect() {
    std::lock_guard lock(m_impl->mutex);
    m_impl->downstream.reset();
    return {};
}

void WASAPISource::OnStateChange(core::StateChangeCallback callback) {
    m_impl->onStateChange = callback;
}

void WASAPISource::OnError(core::ErrorCallback callback) {
    m_impl->onError = callback;
}

const DeviceInfo& WASAPISource::GetDeviceInfo() const {
    static DeviceInfo info{"Microphone Array", "Microphone Array", DeviceType::AudioInput};
    return info;
}

std::vector<DeviceFormat> WASAPISource::GetSupportedFormats() const {
    std::vector<DeviceFormat> formats;
    formats.push_back({0, 0, 0.0f, core::PixelFormat::Unknown, core::SampleFormat::Float32P, 48000, 2});
    return formats;
}

core::VoidResult WASAPISource::SetFormat(const DeviceFormat& format) {
    return {};
}

const DeviceFormat& WASAPISource::GetCurrentFormat() const {
    static DeviceFormat fmt{0, 0, 0.0f, core::PixelFormat::Unknown, core::SampleFormat::Float32P, 48000, 2};
    return fmt;
}

} // namespace openmedia::io
