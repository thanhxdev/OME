#include "openmedia/rtmp/RTMPOutput.h"
#include <openmedia/core/Logger.h>

extern "C" {
#include <libavformat/avformat.h>
#include <libavutil/opt.h>
#include <libavutil/timestamp.h>
}

#include <mutex>
#include <atomic>

namespace openmedia::rtmp {

struct RTMPOutput::Impl {
    std::mutex mutex;
    core::PipelineState state{core::PipelineState::Stopped};
    std::shared_ptr<core::IMediaObject> downstream;
    core::StateChangeCallback onStateChange;
    core::ErrorCallback onError;

    std::string url;
    AVFormatContext* formatContext = nullptr;
    bool isOpened = false;

    int videoStreamIndex = -1;
    int audioStreamIndex = -1;
};

RTMPOutput::RTMPOutput() : m_impl(std::make_unique<Impl>()) {}
RTMPOutput::~RTMPOutput() { Close(); }

core::PipelineState RTMPOutput::GetState() const { return m_impl->state; }

core::VoidResult RTMPOutput::Initialize() {
    auto old = m_impl->state;
    m_impl->state = core::PipelineState::Ready;
    if (m_impl->onStateChange) m_impl->onStateChange(old, m_impl->state);
    return {};
}

core::VoidResult RTMPOutput::Start() {
    auto old = m_impl->state;
    m_impl->state = core::PipelineState::Running;
    if (m_impl->onStateChange) m_impl->onStateChange(old, m_impl->state);
    return {};
}

core::VoidResult RTMPOutput::Stop() {
    auto old = m_impl->state;
    m_impl->state = core::PipelineState::Stopped;
    if (m_impl->onStateChange) m_impl->onStateChange(old, m_impl->state);
    return {};
}

core::VoidResult RTMPOutput::PushFrame(std::shared_ptr<core::MediaFrame> frame) {
    std::lock_guard lock(m_impl->mutex);
    if (m_impl->state != core::PipelineState::Running) return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "RTMPOutput is not running"));
    if (!m_impl->isOpened) return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "RTMP URL is not opened"));

    const uint8_t* packetData = frame->GetPacketData();
    size_t packetSize = frame->GetPacketSize();

    if (packetData != nullptr && packetSize > 0) {
        AVPacket* pkt = av_packet_alloc();
        if (pkt) {
            if (av_new_packet(pkt, static_cast<int>(packetSize)) == 0) {
                std::memcpy(pkt->data, packetData, packetSize);

                int streamIndex = (frame->GetMediaType() == core::MediaType::Audio) ? m_impl->audioStreamIndex : m_impl->videoStreamIndex;
                if (streamIndex < 0) streamIndex = 0; // Fallback

                AVStream* st = m_impl->formatContext->streams[streamIndex];
                AVRational frameTimeBase = {frame->GetTimeBase().num, frame->GetTimeBase().den};
                pkt->pts = av_rescale_q(frame->GetPts(), frameTimeBase, st->time_base);
                pkt->dts = av_rescale_q(frame->GetDts(), frameTimeBase, st->time_base);
                pkt->stream_index = streamIndex;

                // Note: IsKeyFrame should be handled properly later if needed.
                pkt->flags |= AV_PKT_FLAG_KEY;

                int ret = av_interleaved_write_frame(m_impl->formatContext, pkt);
                if (ret < 0) {
                    openmedia::core::Logger::SError("OME", "Error writing RTMP frame: {}", ret);
                }
            }
            av_packet_free(&pkt);
        }
    }

    if (m_impl->downstream) {
        return m_impl->downstream->PushFrame(frame);
    }
    return {};
}

core::Result<std::shared_ptr<core::MediaFrame>> RTMPOutput::PullFrame() {
    return std::unexpected(core::Error::Make(core::ErrorCode::NotImplemented, "RTMPOutput is a push sink"));
}

core::VoidResult RTMPOutput::Connect(std::shared_ptr<core::IMediaObject> downstream) { std::lock_guard lock(m_impl->mutex); m_impl->downstream = downstream; return {}; }
core::VoidResult RTMPOutput::Disconnect() { std::lock_guard lock(m_impl->mutex); m_impl->downstream.reset(); return {}; }
void RTMPOutput::OnStateChange(core::StateChangeCallback callback) { m_impl->onStateChange = callback; }
void RTMPOutput::OnError(core::ErrorCallback callback) { m_impl->onError = callback; }

core::VoidResult RTMPOutput::AddVideoStream(int width, int height, int fps, const std::string& codecName) {
    std::lock_guard lock(m_impl->mutex);
    if (m_impl->isOpened) return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "Cannot add stream after opening"));

    if (!m_impl->formatContext) {
        avformat_alloc_output_context2(&m_impl->formatContext, nullptr, "flv", nullptr);
        if (!m_impl->formatContext) return std::unexpected(core::Error::Make(core::ErrorCode::FileOpenFailed, "Failed to allocate format context"));
    }

    const AVCodec* codec = avcodec_find_encoder_by_name(codecName.c_str());
    if (!codec) return std::unexpected(core::Error::Make(core::ErrorCode::InvalidArgument, "Unsupported codec name"));

    AVStream* stream = avformat_new_stream(m_impl->formatContext, nullptr);
    if (!stream) return std::unexpected(core::Error::Make(core::ErrorCode::FileOpenFailed, "Could not create stream"));

    stream->codecpar->codec_id = codec->id;
    stream->codecpar->codec_type = AVMEDIA_TYPE_VIDEO;
    stream->codecpar->width = width;
    stream->codecpar->height = height;
    stream->time_base = {1, fps};
    m_impl->videoStreamIndex = stream->index;
    return {};
}

core::VoidResult RTMPOutput::AddAudioStream(int sampleRate, int channels, const std::string& codecName) {
    std::lock_guard lock(m_impl->mutex);
    if (m_impl->isOpened) return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "Cannot add stream after opening"));

    if (!m_impl->formatContext) {
        avformat_alloc_output_context2(&m_impl->formatContext, nullptr, "flv", nullptr);
    }

    const AVCodec* codec = avcodec_find_encoder_by_name(codecName.c_str());
    if (!codec) return std::unexpected(core::Error::Make(core::ErrorCode::InvalidArgument, "Unsupported codec name"));

    AVStream* stream = avformat_new_stream(m_impl->formatContext, nullptr);
    if (!stream) return std::unexpected(core::Error::Make(core::ErrorCode::FileOpenFailed, "Could not create stream"));

    stream->codecpar->codec_id = codec->id;
    stream->codecpar->codec_type = AVMEDIA_TYPE_AUDIO;
    stream->codecpar->sample_rate = sampleRate;
    stream->codecpar->ch_layout.nb_channels = channels;
    stream->time_base = {1, sampleRate};
    m_impl->audioStreamIndex = stream->index;
    return {};
}

core::VoidResult RTMPOutput::Open(const std::string& rtmpUrl) {
    std::lock_guard lock(m_impl->mutex);
    if (m_impl->isOpened) return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "Already opened"));

    if (!m_impl->formatContext) {
        int ret = avformat_alloc_output_context2(&m_impl->formatContext, nullptr, "flv", rtmpUrl.c_str());
        if (ret < 0 || !m_impl->formatContext) return std::unexpected(core::Error::Make(core::ErrorCode::FileOpenFailed, "Could not allocate output context"));
    } else {
        m_impl->formatContext->url = av_strdup(rtmpUrl.c_str());
    }

    if (!(m_impl->formatContext->oformat->flags & AVFMT_NOFILE)) {
        int ret = avio_open(&m_impl->formatContext->pb, rtmpUrl.c_str(), AVIO_FLAG_WRITE);
        if (ret < 0) return std::unexpected(core::Error::Make(core::ErrorCode::FileOpenFailed, "Could not open URL"));
    }

    int ret = avformat_write_header(m_impl->formatContext, nullptr);
    if (ret < 0) return std::unexpected(core::Error::Make(core::ErrorCode::FileOpenFailed, "Could not write header"));

    m_impl->url = rtmpUrl;
    m_impl->isOpened = true;
    return {};
}

void RTMPOutput::Close() {
    std::lock_guard lock(m_impl->mutex);
    if (m_impl->isOpened && m_impl->formatContext) {
        av_write_trailer(m_impl->formatContext);
        if (!(m_impl->formatContext->oformat->flags & AVFMT_NOFILE) && m_impl->formatContext->pb) {
            avio_closep(&m_impl->formatContext->pb);
        }
        avformat_free_context(m_impl->formatContext);
        m_impl->formatContext = nullptr;
        m_impl->isOpened = false;
        m_impl->url.clear();
    }
}

} // namespace openmedia::rtmp

