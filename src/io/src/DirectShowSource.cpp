#include <openmedia/io/DirectShowSource.h>
#include <openmedia/io/MediaReader.h>
#include <openmedia/core/Logger.h>
#include <openmedia/core/ErrorCodes.h>

#include <mutex>
#include <thread>
#include <atomic>

extern "C" {
#include <libswscale/swscale.h>
}

namespace openmedia::io {

struct DirectShowSource::Impl {
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
    SwsContext* swsCtx = nullptr;

    Impl() {
        decodedFrame = av_frame_alloc();
    }

    ~Impl() {
        if (decodedFrame) av_frame_free(&decodedFrame);
        if (swsCtx) sws_freeContext(swsCtx);
    }

    void CaptureLoop() {
        while (isRunning) {
            if (state != core::PipelineState::Running) {
                std::this_thread::sleep_for(std::chrono::milliseconds(10));
                continue;
            }

            av_frame_unref(decodedFrame);
            auto result = reader.ReadVideoFrame(decodedFrame);
            if (!result) {
                if (result.error().code == core::ErrorCode::EndOfStream) {
                    break;
                }
                std::this_thread::sleep_for(std::chrono::milliseconds(10));
                continue;
            }

            if (downstream) {
                int width = decodedFrame->width;
                int height = decodedFrame->height;
                auto format = static_cast<AVPixelFormat>(decodedFrame->format);

                swsCtx = sws_getCachedContext(swsCtx, width, height, format, width, height, AV_PIX_FMT_NV12, SWS_FAST_BILINEAR, nullptr, nullptr, nullptr);
                
                if (swsCtx) {
                    auto mediaFrame = core::MediaFrame::CreateVideo(width, height, core::PixelFormat::NV12);
                    uint8_t* dstData[2] = { mediaFrame->GetVideoPlane(0), mediaFrame->GetVideoPlane(1) };
                    int dstLinesize[2] = { static_cast<int>(mediaFrame->GetLineSize(0)), static_cast<int>(mediaFrame->GetLineSize(1)) };

                    sws_scale(swsCtx, decodedFrame->data, decodedFrame->linesize, 0, height, dstData, dstLinesize);
                    mediaFrame->SetPts(decodedFrame->pts);
                    mediaFrame->SetDts(decodedFrame->pkt_dts);
                    downstream->PushFrame(mediaFrame);
                }
            }
        }
    }
};

DirectShowSource::DirectShowSource() : m_impl(new Impl()) {}

DirectShowSource::~DirectShowSource() {
    (void)Stop();
}

core::VoidResult DirectShowSource::Open(const std::string& deviceName) {
    std::lock_guard lock(m_impl->mutex);
    m_impl->deviceName = deviceName;
    std::string url = "video=" + deviceName;
    return m_impl->reader.Open(url, "dshow");
}

std::string DirectShowSource::GetName() const { return "DirectShowSource"; }

core::PipelineState DirectShowSource::GetState() const { return m_impl->state; }

core::VoidResult DirectShowSource::Initialize() {
    return {};
}

core::VoidResult DirectShowSource::Start() {
    std::lock_guard lock(m_impl->mutex);
    if (m_impl->state == core::PipelineState::Running) return {};
    
    core::PipelineState oldState = m_impl->state;
    m_impl->state = core::PipelineState::Running;
    m_impl->isRunning = true;
    m_impl->captureThread = std::thread(&Impl::CaptureLoop, m_impl.get());
    
    if (m_impl->onStateChange) m_impl->onStateChange(oldState, m_impl->state);
    return {};
}

core::VoidResult DirectShowSource::Stop() {
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

core::VoidResult DirectShowSource::PushFrame(std::shared_ptr<core::MediaFrame> frame) {
    return std::unexpected(core::Error::Make(core::ErrorCode::NotSupported, "Source cannot accept frames"));
}

core::Result<std::shared_ptr<core::MediaFrame>> DirectShowSource::PullFrame() {
    return std::unexpected(core::Error::Make(core::ErrorCode::NotSupported, "Pull mode not supported. Use push model."));
}

core::VoidResult DirectShowSource::Connect(std::shared_ptr<core::IMediaObject> downstream) {
    std::lock_guard lock(m_impl->mutex);
    m_impl->downstream = downstream;
    return {};
}

core::VoidResult DirectShowSource::Disconnect() {
    std::lock_guard lock(m_impl->mutex);
    m_impl->downstream.reset();
    return {};
}

void DirectShowSource::OnStateChange(core::StateChangeCallback callback) {
    m_impl->onStateChange = callback;
}

void DirectShowSource::OnError(core::ErrorCallback callback) {
    m_impl->onError = callback;
}

const DeviceInfo& DirectShowSource::GetDeviceInfo() const {
    static DeviceInfo info{"Integrated Camera", "Integrated Camera", DeviceType::VideoInput};
    return info;
}

std::vector<DeviceFormat> DirectShowSource::GetSupportedFormats() const {
    std::vector<DeviceFormat> formats;
    formats.push_back({1920, 1080, 30.0f, core::PixelFormat::YUV420P, core::SampleFormat::Unknown, 0, 0});
    return formats;
}

core::VoidResult DirectShowSource::SetFormat(const DeviceFormat& format) {
    return {};
}

const DeviceFormat& DirectShowSource::GetCurrentFormat() const {
    static DeviceFormat fmt{1920, 1080, 30.0f, core::PixelFormat::YUV420P, core::SampleFormat::Unknown, 0, 0};
    return fmt;
}

} // namespace openmedia::io
