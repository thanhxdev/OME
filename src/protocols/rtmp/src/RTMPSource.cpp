#include "openmedia/rtmp/RTMPSource.h"
#include <openmedia/core/Logger.h>
#include <mutex>
#include <thread>
#include <atomic>

extern "C" {
#include <libavformat/avformat.h>
#include <libswscale/swscale.h>
#include <libswresample/swresample.h>
#include <libavutil/opt.h>
}

namespace openmedia::rtmp {

struct RTMPSource::Impl {
    io::MediaReader reader;
    std::string url;
    core::PipelineState state = core::PipelineState::Stopped;
    std::shared_ptr<core::IMediaObject> downstream;
    core::StateChangeCallback onStateChange;
    core::ErrorCallback onError;
    std::mutex readerMutex;

    AVFrame* decodedVideoFrame = nullptr;
    AVFrame* decodedAudioFrame = nullptr;
    SwsContext* swsCtx = nullptr;
    SwrContext* swrCtx = nullptr;
};

RTMPSource::RTMPSource() : m_impl(std::make_unique<Impl>()) {
    m_impl->decodedVideoFrame = av_frame_alloc();
    m_impl->decodedAudioFrame = av_frame_alloc();
}

RTMPSource::~RTMPSource() {
    Close();
    if (m_impl->decodedVideoFrame) av_frame_free(&m_impl->decodedVideoFrame);
    if (m_impl->decodedAudioFrame) av_frame_free(&m_impl->decodedAudioFrame);
    if (m_impl->swsCtx) sws_freeContext(m_impl->swsCtx);
    if (m_impl->swrCtx) swr_free(&m_impl->swrCtx);
}

core::PipelineState RTMPSource::GetState() const { return m_impl->state; }

core::VoidResult RTMPSource::Initialize() {
    auto old = m_impl->state;
    m_impl->state = core::PipelineState::Ready;
    if (m_impl->onStateChange) m_impl->onStateChange(old, m_impl->state);
    return {};
}

core::VoidResult RTMPSource::Start() {
    auto old = m_impl->state;
    m_impl->state = core::PipelineState::Running;
    if (m_impl->onStateChange) m_impl->onStateChange(old, m_impl->state);
    return {};
}

core::VoidResult RTMPSource::Stop() {
    auto old = m_impl->state;
    m_impl->state = core::PipelineState::Stopped;
    if (m_impl->onStateChange) m_impl->onStateChange(old, m_impl->state);
    return {};
}

core::VoidResult RTMPSource::PushFrame(std::shared_ptr<core::MediaFrame>) {
    return std::unexpected(core::Error::Make(core::ErrorCode::NotSupported, "RTMPSource is a pull source"));
}

core::Result<std::shared_ptr<core::MediaFrame>> RTMPSource::PullFrame() {
    return PullVideoFrame();
}

core::Result<std::shared_ptr<core::MediaFrame>> RTMPSource::PullVideoFrame() {
    std::lock_guard lock(m_impl->readerMutex);
    
    if (m_impl->state != core::PipelineState::Running) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "Not running"));
    }

    av_frame_unref(m_impl->decodedVideoFrame);
    auto result = m_impl->reader.ReadVideoFrame(m_impl->decodedVideoFrame);
    if (!result) return std::unexpected(result.error());

    int width = m_impl->decodedVideoFrame->width;
    int height = m_impl->decodedVideoFrame->height;
    auto format = static_cast<AVPixelFormat>(m_impl->decodedVideoFrame->format);

    m_impl->swsCtx = sws_getCachedContext(m_impl->swsCtx, width, height, format, width, height, AV_PIX_FMT_NV12, SWS_FAST_BILINEAR, nullptr, nullptr, nullptr);
    if (!m_impl->swsCtx) return std::unexpected(core::Error::Make(core::ErrorCode::Unknown, "swscale context failed"));

    auto mediaFrame = core::MediaFrame::CreateVideo(width, height, core::PixelFormat::NV12);
    uint8_t* dstData[2] = { mediaFrame->GetVideoPlane(0), mediaFrame->GetVideoPlane(1) };
    int dstLinesize[2] = { static_cast<int>(mediaFrame->GetLineSize(0)), static_cast<int>(mediaFrame->GetLineSize(1)) };

    sws_scale(m_impl->swsCtx, m_impl->decodedVideoFrame->data, m_impl->decodedVideoFrame->linesize, 0, height, dstData, dstLinesize);
    mediaFrame->SetPts(m_impl->decodedVideoFrame->pts);
    mediaFrame->SetDts(m_impl->decodedVideoFrame->pkt_dts);
    return mediaFrame;
}

core::Result<std::shared_ptr<core::MediaFrame>> RTMPSource::PullAudioFrame() {
    std::lock_guard lock(m_impl->readerMutex);
    if (m_impl->state != core::PipelineState::Running) return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "Not running"));

    av_frame_unref(m_impl->decodedAudioFrame);
    auto result = m_impl->reader.ReadAudioFrame(m_impl->decodedAudioFrame);
    if (!result) return std::unexpected(result.error());

    if (!m_impl->swrCtx) {
        AVChannelLayout out_ch_layout;
        av_channel_layout_default(&out_ch_layout, 2);
        swr_alloc_set_opts2(&m_impl->swrCtx, &out_ch_layout, AV_SAMPLE_FMT_FLT, 48000,
                            &m_impl->decodedAudioFrame->ch_layout,
                            static_cast<AVSampleFormat>(m_impl->decodedAudioFrame->format),
                            m_impl->decodedAudioFrame->sample_rate, 0, nullptr);
        if (!m_impl->swrCtx || swr_init(m_impl->swrCtx) < 0) {
            return std::unexpected(core::Error::Make(core::ErrorCode::Unknown, "swr init failed"));
        }
    }

    auto mediaFrame = core::MediaFrame::CreateAudio(m_impl->decodedAudioFrame->nb_samples, 2, core::SampleFormat::Float32, 48000);
    uint8_t* outData[] = { mediaFrame->GetAudioData() };
    
    swr_convert(m_impl->swrCtx, outData, m_impl->decodedAudioFrame->nb_samples, 
                (const uint8_t**)m_impl->decodedAudioFrame->data, m_impl->decodedAudioFrame->nb_samples);

    mediaFrame->SetPts(m_impl->decodedAudioFrame->pts);
    return mediaFrame;
}

core::VoidResult RTMPSource::Connect(std::shared_ptr<core::IMediaObject> downstream) { std::lock_guard lock(m_impl->readerMutex); m_impl->downstream = downstream; return {}; }
core::VoidResult RTMPSource::Disconnect() { std::lock_guard lock(m_impl->readerMutex); m_impl->downstream.reset(); return {}; }
void RTMPSource::OnStateChange(core::StateChangeCallback callback) { m_impl->onStateChange = callback; }
void RTMPSource::OnError(core::ErrorCallback callback) { m_impl->onError = callback; }

core::VoidResult RTMPSource::Open(const std::string& rtmpUrl) {
    std::lock_guard lock(m_impl->readerMutex);
    auto res = m_impl->reader.Open(rtmpUrl);
    if (res) {
        m_impl->url = rtmpUrl;
    }
    return res;
}

void RTMPSource::Close() {
    std::lock_guard lock(m_impl->readerMutex);
    m_impl->reader.Close();
    m_impl->url.clear();
}
const std::vector<io::StreamInfo>& RTMPSource::GetStreams() const { std::lock_guard lock(m_impl->readerMutex); return m_impl->reader.GetStreams(); }

} // namespace openmedia::rtmp

