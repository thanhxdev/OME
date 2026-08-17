/// @file FFmpegQSVDecoder.cpp
#include <openmedia/codecs/FFmpegQSVDecoder.h>
#include <openmedia/core/Logger.h>

extern "C" {
#include <libavcodec/avcodec.h>
#include <libavutil/imgutils.h>
}

#include <mutex>
#include <queue>

namespace openmedia::codecs {

struct FFmpegQSVDecoder::Impl {
    bool isHevc;
    std::mutex mutex;
    core::PipelineState state{core::PipelineState::Stopped};
    std::shared_ptr<core::IMediaObject> downstream;
    core::StateChangeCallback onStateChange;
    core::ErrorCallback onError;

    AVCodecContext* codecContext = nullptr;
    AVFrame* avFrame = nullptr;
    AVPacket* avPacket = nullptr;

    std::queue<std::shared_ptr<core::MediaFrame>> decodedQueue;

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

FFmpegQSVDecoder::FFmpegQSVDecoder(bool isHevc) : m_impl(new Impl()) {
    m_impl->isHevc = isHevc;
}

FFmpegQSVDecoder::~FFmpegQSVDecoder() {
    (void)Stop();
    m_impl->Cleanup();
}

std::string FFmpegQSVDecoder::GetName() const { 
    return m_impl->isHevc ? "FFmpegQSVDecoder(HEVC)" : "FFmpegQSVDecoder(H264)"; 
}

core::PipelineState FFmpegQSVDecoder::GetState() const { return m_impl->state; }

core::VoidResult FFmpegQSVDecoder::Initialize() {
    std::lock_guard lock(m_impl->mutex);

    m_impl->Cleanup();

    const char* codecName = m_impl->isHevc ? "hevc_qsv" : "h264_qsv";
    const AVCodec* codec = avcodec_find_decoder_by_name(codecName);
    if (!codec) {
        return std::unexpected(core::Error::Make(core::ErrorCode::CodecNotFound, std::string(codecName) + " decoder not found"));
    }

    m_impl->codecContext = avcodec_alloc_context3(codec);
    if (!m_impl->codecContext) {
        return std::unexpected(core::Error::Make(core::ErrorCode::OutOfMemory, "Could not allocate video codec context"));
    }

    m_impl->avPacket = av_packet_alloc();
    m_impl->avFrame = av_frame_alloc();

    int ret = avcodec_open2(m_impl->codecContext, codec, nullptr);
    if (ret < 0) {
        return std::unexpected(core::Error::Make(core::ErrorCode::CodecOpenFailed, "Could not open QSV codec"));
    }

    return {};
}

core::VoidResult FFmpegQSVDecoder::Start() {
    std::lock_guard lock(m_impl->mutex);
    if (m_impl->state == core::PipelineState::Running) return {};
    
    if (!m_impl->codecContext) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "Decoder not initialized"));
    }
    
    m_impl->state = core::PipelineState::Running;
    return {};
}

core::VoidResult FFmpegQSVDecoder::Stop() {
    std::lock_guard lock(m_impl->mutex);
    m_impl->state = core::PipelineState::Stopped;
    return {};
}

core::VoidResult FFmpegQSVDecoder::PushFrame(std::shared_ptr<core::MediaFrame> frame) {
    std::lock_guard lock(m_impl->mutex);
    if (m_impl->state != core::PipelineState::Running) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "Decoder is not running"));
    }

    if (!frame) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidArgument, "Null frame"));
    }

    if (frame->GetPacketSize() > 0) {
        m_impl->avPacket->data = const_cast<uint8_t*>(frame->GetPacketData());
        m_impl->avPacket->size = static_cast<int>(frame->GetPacketSize());
        m_impl->avPacket->pts = frame->GetPts();
        m_impl->avPacket->dts = frame->GetDts();
    } else {
        m_impl->avPacket->data = nullptr;
        m_impl->avPacket->size = 0;
    }

    int ret = avcodec_send_packet(m_impl->codecContext, m_impl->avPacket);
    if (ret < 0) {
        return std::unexpected(core::Error::Make(core::ErrorCode::DecodeFailed, "Error sending packet to codec"));
    }

    while (ret >= 0) {
        ret = avcodec_receive_frame(m_impl->codecContext, m_impl->avFrame);
        if (ret == AVERROR(EAGAIN) || ret == AVERROR_EOF) {
            break;
        } else if (ret < 0) {
            return std::unexpected(core::Error::Make(core::ErrorCode::DecodeFailed, "Error during decoding"));
        }

        auto decodedFrame = core::MediaFrame::CreateVideo(m_impl->avFrame->width, m_impl->avFrame->height, core::PixelFormat::NV12);
        decodedFrame->SetPts(m_impl->avFrame->pts);
        
        // In a real application with a hardware HW_DEVICE_CTX set on codecContext,
        // we might get an AV_PIX_FMT_QSV frame back, which needs to be downloaded to NV12
        // or mapped to D3D11 for zero-copy. For now, simulate copying it.

        m_impl->decodedQueue.push(decodedFrame);

        if (m_impl->downstream) {
            (void)m_impl->downstream->PushFrame(decodedFrame);
        }
    }

    return {};
}

core::Result<std::shared_ptr<core::MediaFrame>> FFmpegQSVDecoder::PullFrame() {
    std::lock_guard lock(m_impl->mutex);
    if (m_impl->state != core::PipelineState::Running) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "Decoder is not running"));
    }

    if (m_impl->decodedQueue.empty()) {
        return std::unexpected(core::Error::Make(core::ErrorCode::WouldBlock, "No decoded frames available"));
    }

    auto frame = m_impl->decodedQueue.front();
    m_impl->decodedQueue.pop();
    return frame;
}

core::VoidResult FFmpegQSVDecoder::Connect(std::shared_ptr<core::IMediaObject> downstream) {
    std::lock_guard lock(m_impl->mutex);
    m_impl->downstream = downstream;
    return {};
}

core::VoidResult FFmpegQSVDecoder::Disconnect() {
    std::lock_guard lock(m_impl->mutex);
    m_impl->downstream.reset();
    return {};
}

void FFmpegQSVDecoder::OnStateChange(core::StateChangeCallback callback) {
    m_impl->onStateChange = callback;
}

void FFmpegQSVDecoder::OnError(core::ErrorCallback callback) {
    m_impl->onError = callback;
}

} // namespace openmedia::codecs
