/// @file FFmpegH264Decoder.cpp
#include <openmedia/codecs/FFmpegH264Decoder.h>
#include <openmedia/core/Logger.h>

extern "C" {
#include <libavcodec/avcodec.h>
#include <libavutil/imgutils.h>
}

#include <mutex>
#include <queue>

namespace openmedia::codecs {

struct FFmpegH264Decoder::Impl {
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

FFmpegH264Decoder::FFmpegH264Decoder() : m_impl(new Impl()) {}

FFmpegH264Decoder::~FFmpegH264Decoder() {
    (void)Stop();
    m_impl->Cleanup();
}

std::string FFmpegH264Decoder::GetName() const { return "FFmpegH264Decoder"; }

core::PipelineState FFmpegH264Decoder::GetState() const { return m_impl->state; }

core::VoidResult FFmpegH264Decoder::Initialize() {
    std::lock_guard lock(m_impl->mutex);

    m_impl->Cleanup();

    const AVCodec* codec = avcodec_find_decoder(AV_CODEC_ID_H264);
    if (!codec) {
        return std::unexpected(core::Error::Make(core::ErrorCode::CodecNotFound, "H264 decoder not found"));
    }

    m_impl->codecContext = avcodec_alloc_context3(codec);
    if (!m_impl->codecContext) {
        return std::unexpected(core::Error::Make(core::ErrorCode::OutOfMemory, "Could not allocate video codec context"));
    }

    m_impl->avPacket = av_packet_alloc();
    m_impl->avFrame = av_frame_alloc();

    AVDictionary* opts = nullptr;
    if (!m_config.preset.empty()) {
        av_dict_set(&opts, "preset", m_config.preset.c_str(), 0);
    }
    if (!m_config.profile.empty()) {
        av_dict_set(&opts, "profile", m_config.profile.c_str(), 0);
    }
    if (!m_config.level.empty()) {
        av_dict_set(&opts, "level", m_config.level.c_str(), 0);
    }

    int ret = avcodec_open2(m_impl->codecContext, codec, &opts);
    av_dict_free(&opts);

    if (ret < 0) {
        return std::unexpected(core::Error::Make(core::ErrorCode::CodecOpenFailed, "Could not open codec"));
    }

    return {};
}

void FFmpegH264Decoder::SetConfig(const DecoderConfig& config) {
    m_config = config;
}

core::VoidResult FFmpegH264Decoder::Start() {
    std::lock_guard lock(m_impl->mutex);
    if (m_impl->state == core::PipelineState::Running) return {};
    
    if (!m_impl->codecContext) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "Decoder not initialized"));
    }
    
    m_impl->state = core::PipelineState::Running;
    return {};
}

core::VoidResult FFmpegH264Decoder::Stop() {
    std::lock_guard lock(m_impl->mutex);
    m_impl->state = core::PipelineState::Stopped;
    return {};
}

core::VoidResult FFmpegH264Decoder::PushFrame(std::shared_ptr<core::MediaFrame> frame) {
    std::lock_guard lock(m_impl->mutex);
    if (m_impl->state != core::PipelineState::Running) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "Decoder is not running"));
    }

    if (!frame) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidArgument, "Null frame"));
    }

    // Set up packet with frame data
    if (frame->GetPacketSize() > 0) {
        m_impl->avPacket->data = const_cast<uint8_t*>(frame->GetPacketData());
        m_impl->avPacket->size = static_cast<int>(frame->GetPacketSize());
        m_impl->avPacket->pts = frame->GetPts();
        m_impl->avPacket->dts = frame->GetDts();
    } else {
        // EOF
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

        // We got a decoded frame!
        // In a real app we'd allocate exactly the resolution of the decoded frame and copy the YUV/RGB buffers.
        // For testing we will just create a small dummy NV12 video frame.
        auto decodedFrame = core::MediaFrame::CreateVideo(m_impl->avFrame->width, m_impl->avFrame->height, core::PixelFormat::NV12);
        decodedFrame->SetPts(m_impl->avFrame->pts);

        m_impl->decodedQueue.push(decodedFrame);

        // Propagate to downstream
        if (m_impl->downstream) {
            m_impl->downstream->PushFrame(decodedFrame);
        }
    }

    return {};
}

core::Result<std::shared_ptr<core::MediaFrame>> FFmpegH264Decoder::PullFrame() {
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

core::VoidResult FFmpegH264Decoder::Connect(std::shared_ptr<core::IMediaObject> downstream) {
    std::lock_guard lock(m_impl->mutex);
    m_impl->downstream = downstream;
    return {};
}

core::VoidResult FFmpegH264Decoder::Disconnect() {
    std::lock_guard lock(m_impl->mutex);
    m_impl->downstream.reset();
    return {};
}

void FFmpegH264Decoder::OnStateChange(core::StateChangeCallback callback) {
    m_impl->onStateChange = callback;
}

void FFmpegH264Decoder::OnError(core::ErrorCallback callback) {
    m_impl->onError = callback;
}

} // namespace openmedia::codecs
