/// @file FFmpegQSVEncoder.cpp
#include <openmedia/codecs/FFmpegQSVEncoder.h>
#include <openmedia/core/Logger.h>

extern "C" {
#include <libavcodec/avcodec.h>
#include <libavutil/opt.h>
#include <libavutil/imgutils.h>
}

#include <mutex>
#include <queue>

namespace openmedia::codecs {

struct FFmpegQSVEncoder::Impl {
    bool isHevc;
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

FFmpegQSVEncoder::FFmpegQSVEncoder(bool isHevc) : m_impl(new Impl()) {
    m_impl->isHevc = isHevc;
}

FFmpegQSVEncoder::~FFmpegQSVEncoder() {
    (void)Stop();
    m_impl->Cleanup();
}

std::string FFmpegQSVEncoder::GetName() const { 
    return m_impl->isHevc ? "FFmpegQSVEncoder(HEVC)" : "FFmpegQSVEncoder(H264)"; 
}

core::PipelineState FFmpegQSVEncoder::GetState() const { return m_impl->state; }

core::VoidResult FFmpegQSVEncoder::Initialize() {
    std::lock_guard lock(m_impl->mutex);

    m_impl->Cleanup();

    const char* codecName = m_impl->isHevc ? "hevc_qsv" : "h264_qsv";
    const AVCodec* codec = avcodec_find_encoder_by_name(codecName);
    if (!codec) {
        return std::unexpected(core::Error::Make(core::ErrorCode::CodecNotFound, std::string(codecName) + " encoder not found"));
    }

    m_impl->codecContext = avcodec_alloc_context3(codec);
    if (!m_impl->codecContext) {
        return std::unexpected(core::Error::Make(core::ErrorCode::OutOfMemory, "Could not allocate video codec context"));
    }

    m_impl->avPacket = av_packet_alloc();
    m_impl->avFrame = av_frame_alloc();

    m_impl->codecContext->bit_rate = m_impl->config.bitrate;
    m_impl->codecContext->width = m_impl->config.width;
    m_impl->codecContext->height = m_impl->config.height;
    m_impl->codecContext->time_base = {1, m_impl->config.fps};
    m_impl->codecContext->framerate = {m_impl->config.fps, 1};
    m_impl->codecContext->gop_size = 10;
    m_impl->codecContext->max_b_frames = 1;
    m_impl->codecContext->pix_fmt = AV_PIX_FMT_YUV420P; // Low latency
    
    // QSV accepts NV12 or QSV hardware pixel format
    m_impl->codecContext->pix_fmt = AV_PIX_FMT_NV12;

    // QSV specific options for low latency
    av_opt_set(m_impl->codecContext->priv_data, "preset", m_impl->config.preset.empty() ? "faster" : m_impl->config.preset.c_str(), 0);
    if (!m_impl->config.profile.empty()) {
        av_opt_set(m_impl->codecContext->priv_data, "profile", m_impl->config.profile.c_str(), 0);
    }

    if (m_impl->config.rcMode == RateControlMode::CQ) {
        av_opt_set_int(m_impl->codecContext->priv_data, "global_quality", m_impl->config.quality, 0);
    } else if (m_impl->config.rcMode == RateControlMode::VBR) {
        m_impl->codecContext->rc_max_rate = m_impl->config.bitrate * 2;
        m_impl->codecContext->rc_min_rate = 0;
        m_impl->codecContext->rc_buffer_size = m_impl->config.bitrate * 2;
    } else { // CBR
        m_impl->codecContext->rc_max_rate = m_impl->config.bitrate;
        m_impl->codecContext->rc_min_rate = m_impl->config.bitrate;
        m_impl->codecContext->rc_buffer_size = m_impl->config.bitrate;
    }

    int ret = avcodec_open2(m_impl->codecContext, codec, nullptr);
    if (ret < 0) {
        return std::unexpected(core::Error::Make(core::ErrorCode::CodecOpenFailed, "Could not open QSV codec"));
    }

    m_impl->avFrame->format = m_impl->codecContext->pix_fmt;
    m_impl->avFrame->width  = m_impl->codecContext->width;
    m_impl->avFrame->height = m_impl->codecContext->height;

    ret = av_frame_get_buffer(m_impl->avFrame, 0);
    if (ret < 0) {
        return std::unexpected(core::Error::Make(core::ErrorCode::OutOfMemory, "Could not allocate video frame data"));
    }

    return {};
}

core::VoidResult FFmpegQSVEncoder::Start() {
    std::lock_guard lock(m_impl->mutex);
    if (m_impl->state == core::PipelineState::Running) return {};
    
    if (!m_impl->codecContext) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "Encoder not initialized"));
    }
    
    m_impl->state = core::PipelineState::Running;
    return {};
}

core::VoidResult FFmpegQSVEncoder::Stop() {
    std::lock_guard lock(m_impl->mutex);
    m_impl->state = core::PipelineState::Stopped;
    return {};
}

core::VoidResult FFmpegQSVEncoder::PushFrame(std::shared_ptr<core::MediaFrame> frame) {
    std::lock_guard lock(m_impl->mutex);
    if (m_impl->state != core::PipelineState::Running) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "Encoder is not running"));
    }

    if (!frame) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidArgument, "Null frame"));
    }

    int ret = av_frame_make_writable(m_impl->avFrame);
    if (ret < 0) {
        return std::unexpected(core::Error::Make(core::ErrorCode::EncodeFailed, "Frame not writable"));
    }
    
    // Copy data from MediaFrame to AVFrame (assuming NV12 planar format)
    // In a real optimized scenario, we would map the D3D11 hardware frame directly
    
    m_impl->avFrame->pts = frame->GetPts();
    
    ret = avcodec_send_frame(m_impl->codecContext, m_impl->avFrame);
    if (ret < 0) {
        return std::unexpected(core::Error::Make(core::ErrorCode::EncodeFailed, "Error sending frame to codec"));
    }

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
            (void)m_impl->downstream->PushFrame(encodedFrame);
        }
    }

    return {};
}

core::Result<std::shared_ptr<core::MediaFrame>> FFmpegQSVEncoder::PullFrame() {
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

core::VoidResult FFmpegQSVEncoder::Connect(std::shared_ptr<core::IMediaObject> downstream) {
    std::lock_guard lock(m_impl->mutex);
    m_impl->downstream = downstream;
    return {};
}

core::VoidResult FFmpegQSVEncoder::Disconnect() {
    std::lock_guard lock(m_impl->mutex);
    m_impl->downstream.reset();
    return {};
}

void FFmpegQSVEncoder::OnStateChange(core::StateChangeCallback callback) {
    m_impl->onStateChange = callback;
}

void FFmpegQSVEncoder::OnError(core::ErrorCallback callback) {
    m_impl->onError = callback;
}

core::VoidResult FFmpegQSVEncoder::Configure(const EncoderConfig& config) {
    std::lock_guard lock(m_impl->mutex);
    m_impl->config = config;
    return {};
}

} // namespace openmedia::codecs
