/// @file FFmpegNVDECDecoder.cpp
#include <openmedia/codecs/FFmpegNVDECDecoder.h>
#include <openmedia/core/Logger.h>

extern "C" {
#include <libavcodec/avcodec.h>
#include <libavutil/imgutils.h>
}

#include <mutex>
#include <queue>

namespace openmedia::codecs {

struct FFmpegNVDECDecoder::Impl {
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

FFmpegNVDECDecoder::FFmpegNVDECDecoder(bool isHevc) : m_impl(new Impl()) {
    m_impl->isHevc = isHevc;
}

FFmpegNVDECDecoder::~FFmpegNVDECDecoder() {
    (void)Stop();
    m_impl->Cleanup();
}

std::string FFmpegNVDECDecoder::GetName() const { 
    return m_impl->isHevc ? "FFmpegNVDECDecoder(HEVC)" : "FFmpegNVDECDecoder(H264)"; 
}

core::PipelineState FFmpegNVDECDecoder::GetState() const { return m_impl->state; }

core::VoidResult FFmpegNVDECDecoder::Initialize() {
    std::lock_guard lock(m_impl->mutex);

    m_impl->Cleanup();

    const char* codecName = m_impl->isHevc ? "hevc_cuvid" : "h264_cuvid";
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
        return std::unexpected(core::Error::Make(core::ErrorCode::CodecOpenFailed, "Could not open NVDEC codec"));
    }

    return {};
}

core::VoidResult FFmpegNVDECDecoder::Start() {
    std::lock_guard lock(m_impl->mutex);
    if (m_impl->state == core::PipelineState::Running) return {};
    
    if (!m_impl->codecContext) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "Decoder not initialized"));
    }
    
    m_impl->state = core::PipelineState::Running;
    return {};
}

core::VoidResult FFmpegNVDECDecoder::Stop() {
    std::lock_guard lock(m_impl->mutex);
    m_impl->state = core::PipelineState::Stopped;
    return {};
}

core::VoidResult FFmpegNVDECDecoder::PushFrame(std::shared_ptr<core::MediaFrame> frame) {
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
        
        // In a real application, we would check if the frame is hardware (AV_PIX_FMT_CUDA)
        // and either keep it on GPU or av_hwframe_transfer_data to CPU.
        // For cuvid, if not explicitly requested HW frame output, it usually downloads it to NV12 on CPU.
        // We simulate the copy for now.

        m_impl->decodedQueue.push(decodedFrame);

        if (m_impl->downstream) {
            m_impl->downstream->PushFrame(decodedFrame);
        }
    }

    return {};
}

core::Result<std::shared_ptr<core::MediaFrame>> FFmpegNVDECDecoder::PullFrame() {
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

core::VoidResult FFmpegNVDECDecoder::Connect(std::shared_ptr<core::IMediaObject> downstream) {
    std::lock_guard lock(m_impl->mutex);
    m_impl->downstream = downstream;
    return {};
}

core::VoidResult FFmpegNVDECDecoder::Disconnect() {
    std::lock_guard lock(m_impl->mutex);
    m_impl->downstream.reset();
    return {};
}

void FFmpegNVDECDecoder::OnStateChange(core::StateChangeCallback callback) {
    m_impl->onStateChange = callback;
}

void FFmpegNVDECDecoder::OnError(core::ErrorCallback callback) {
    m_impl->onError = callback;
}

} // namespace openmedia::codecs
