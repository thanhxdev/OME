/// @file FFmpegAACEncoder.cpp
#include <openmedia/codecs/FFmpegAACEncoder.h>
#include <openmedia/core/Logger.h>

extern "C" {
#include <libavcodec/avcodec.h>
#include <libavutil/opt.h>
#include <libavutil/channel_layout.h>
}

#include <mutex>
#include <queue>
#include <cstring>

namespace openmedia::codecs {

struct FFmpegAACEncoder::Impl {
    std::mutex mutex;
    core::PipelineState state{core::PipelineState::Stopped};
    std::shared_ptr<core::IMediaObject> downstream;
    core::StateChangeCallback onStateChange;
    core::ErrorCallback onError;

    EncoderConfig config;

    AVCodecContext* codecContext = nullptr;
    AVFrame* avFrame = nullptr;
    AVPacket* avPacket = nullptr;

    std::queue<std::shared_ptr<core::MediaFrame>> encodedQueue;

    void Cleanup() {
        if (codecContext) {
            avcodec_free_context(&codecContext);
            codecContext = nullptr;
        }
        if (avFrame) {
            av_frame_free(&avFrame);
            avFrame = nullptr;
        }
        if (avPacket) {
            av_packet_free(&avPacket);
            avPacket = nullptr;
        }
    }
};

FFmpegAACEncoder::FFmpegAACEncoder() : m_impl(new Impl()) {}

FFmpegAACEncoder::~FFmpegAACEncoder() {
    (void)Stop();
    m_impl->Cleanup();
}

std::string FFmpegAACEncoder::GetName() const { return "FFmpegAACEncoder"; }

core::PipelineState FFmpegAACEncoder::GetState() const { return m_impl->state; }

core::VoidResult FFmpegAACEncoder::Initialize() {
    std::lock_guard lock(m_impl->mutex);

    m_impl->Cleanup();

    const AVCodec* codec = avcodec_find_encoder(AV_CODEC_ID_AAC);
    if (!codec) {
        return std::unexpected(core::Error::Make(core::ErrorCode::CodecNotFound, "AAC encoder not found"));
    }

    m_impl->codecContext = avcodec_alloc_context3(codec);
    if (!m_impl->codecContext) {
        return std::unexpected(core::Error::Make(core::ErrorCode::OutOfMemory, "Could not allocate audio codec context"));
    }

    m_impl->avPacket = av_packet_alloc();
    m_impl->avFrame = av_frame_alloc();

    m_impl->codecContext->bit_rate = m_impl->config.bitrate;
    m_impl->codecContext->sample_rate = m_impl->config.sampleRate;
    m_impl->codecContext->ch_layout.nb_channels = m_impl->config.channels;
    av_channel_layout_default(&m_impl->codecContext->ch_layout, m_impl->config.channels);
    m_impl->codecContext->sample_fmt = AV_SAMPLE_FMT_FLTP;
    m_impl->codecContext->time_base = {1, m_impl->config.sampleRate};

    int ret = avcodec_open2(m_impl->codecContext, codec, nullptr);
    if (ret < 0) {
        return std::unexpected(core::Error::Make(core::ErrorCode::CodecOpenFailed, "Could not open AAC codec"));
    }

    m_impl->avFrame->format = m_impl->codecContext->sample_fmt;
    m_impl->avFrame->ch_layout = m_impl->codecContext->ch_layout;
    m_impl->avFrame->sample_rate = m_impl->codecContext->sample_rate;
    m_impl->avFrame->nb_samples = m_impl->codecContext->frame_size;

    ret = av_frame_get_buffer(m_impl->avFrame, 0);
    if (ret < 0) {
        return std::unexpected(core::Error::Make(core::ErrorCode::OutOfMemory, "Could not allocate audio frame data"));
    }

    return {};
}

core::VoidResult FFmpegAACEncoder::Start() {
    std::lock_guard lock(m_impl->mutex);
    if (m_impl->state == core::PipelineState::Running) return {};
    
    if (!m_impl->codecContext) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "Encoder not initialized"));
    }
    
    m_impl->state = core::PipelineState::Running;
    return {};
}

core::VoidResult FFmpegAACEncoder::Stop() {
    std::lock_guard lock(m_impl->mutex);
    m_impl->state = core::PipelineState::Stopped;
    return {};
}

core::VoidResult FFmpegAACEncoder::PushFrame(std::shared_ptr<core::MediaFrame> frame) {
    std::lock_guard lock(m_impl->mutex);
    if (m_impl->state != core::PipelineState::Running) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "Encoder is not running"));
    }

    if (!frame) {
        // flush
        avcodec_send_frame(m_impl->codecContext, nullptr);
    } else {
        int ret = av_frame_make_writable(m_impl->avFrame);
        if (ret < 0) {
            return std::unexpected(core::Error::Make(core::ErrorCode::EncodeFailed, "Frame not writable"));
        }
        
        m_impl->avFrame->pts = frame->GetPts();
        // Here we would normally copy PCM data from MediaFrame to AVFrame.
        
        ret = avcodec_send_frame(m_impl->codecContext, m_impl->avFrame);
        if (ret < 0) {
            return std::unexpected(core::Error::Make(core::ErrorCode::EncodeFailed, "Error sending frame to codec"));
        }
    }

    int ret = 0;
    while (ret >= 0) {
        ret = avcodec_receive_packet(m_impl->codecContext, m_impl->avPacket);
        if (ret == AVERROR(EAGAIN) || ret == AVERROR_EOF) {
            break;
        } else if (ret < 0) {
            return std::unexpected(core::Error::Make(core::ErrorCode::EncodeFailed, "Error during encoding"));
        }

        auto encodedFrame = core::MediaFrame::CreatePacket(m_impl->avPacket->size);
        encodedFrame->SetPts(m_impl->avPacket->pts);
        encodedFrame->SetDts(m_impl->avPacket->dts);
        encodedFrame->SetTimeBase({m_impl->codecContext->time_base.num, m_impl->codecContext->time_base.den});
        std::memcpy(encodedFrame->GetPacketData(), m_impl->avPacket->data, m_impl->avPacket->size);

        m_impl->encodedQueue.push(encodedFrame);
        av_packet_unref(m_impl->avPacket);

        if (m_impl->downstream) {
            m_impl->downstream->PushFrame(encodedFrame);
        }
    }

    return {};
}

core::Result<std::shared_ptr<core::MediaFrame>> FFmpegAACEncoder::PullFrame() {
    std::lock_guard lock(m_impl->mutex);
    if (m_impl->state != core::PipelineState::Running) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "Encoder is not running"));
    }

    if (m_impl->encodedQueue.empty()) {
        return std::unexpected(core::Error::Make(core::ErrorCode::WouldBlock, "No encoded frames available"));
    }

    auto frame = m_impl->encodedQueue.front();
    m_impl->encodedQueue.pop();
    return frame;
}

core::VoidResult FFmpegAACEncoder::Connect(std::shared_ptr<core::IMediaObject> downstream) {
    std::lock_guard lock(m_impl->mutex);
    m_impl->downstream = downstream;
    return {};
}

core::VoidResult FFmpegAACEncoder::Disconnect() {
    std::lock_guard lock(m_impl->mutex);
    m_impl->downstream.reset();
    return {};
}

void FFmpegAACEncoder::OnStateChange(core::StateChangeCallback callback) {
    m_impl->onStateChange = callback;
}

void FFmpegAACEncoder::OnError(core::ErrorCallback callback) {
    m_impl->onError = callback;
}

core::VoidResult FFmpegAACEncoder::Configure(const EncoderConfig& config) {
    std::lock_guard lock(m_impl->mutex);
    m_impl->config = config;
    return {};
}

} // namespace openmedia::codecs
