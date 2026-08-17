/// @file FileOutput.cpp
#include <openmedia/io/FileOutput.h>
#include <openmedia/core/Logger.h>

extern "C" {
#include <libavformat/avformat.h>
#include <libavutil/opt.h>
#include <libavutil/timestamp.h>
}

#include <mutex>
#include <atomic>
#include <vector>

namespace openmedia::io {

struct FileOutput::Impl {
    std::mutex mutex;
    core::PipelineState state{core::PipelineState::Stopped};
    std::shared_ptr<core::IMediaObject> downstream;
    core::StateChangeCallback onStateChange;
    core::ErrorCallback onError;

    std::string filePath;
    AVFormatContext* formatContext = nullptr;
    bool isOpened = false;

    // Track stream mapping
    int videoStreamIndex = -1;
    int audioStreamIndex = -1;

    ~Impl() {
        if (formatContext) {
            avformat_free_context(formatContext);
        }
    }
};

FileOutput::FileOutput() : m_impl(new Impl()) {}

FileOutput::~FileOutput() {
    (void)Stop();
    Close();
}

std::string FileOutput::GetName() const { return "FileOutput"; }

core::PipelineState FileOutput::GetState() const { return m_impl->state; }

core::VoidResult FileOutput::Initialize() { return {}; }

core::VoidResult FileOutput::Start() {
    std::lock_guard lock(m_impl->mutex);
    m_impl->state = core::PipelineState::Running;
    return {};
}

core::VoidResult FileOutput::Stop() {
    std::lock_guard lock(m_impl->mutex);
    m_impl->state = core::PipelineState::Stopped;
    return {};
}

core::VoidResult FileOutput::PushFrame(std::shared_ptr<core::MediaFrame> frame) {
    std::lock_guard lock(m_impl->mutex);
    if (m_impl->state != core::PipelineState::Running) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "FileOutput is not running"));
    }

    if (!m_impl->isOpened) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "File is not opened"));
    }

    const uint8_t* packetData = frame->GetPacketData();
    size_t packetSize = frame->GetPacketSize();

    if (packetData != nullptr && packetSize > 0) {
        AVPacket* pkt = av_packet_alloc();
        if (pkt) {
            if (av_new_packet(pkt, static_cast<int>(packetSize)) == 0) {
                std::memcpy(pkt->data, packetData, packetSize);

                // FIXME: Determine if video or audio frame properly. For now assume Video if videoStreamIndex >= 0.
                int streamIndex = m_impl->videoStreamIndex >= 0 ? m_impl->videoStreamIndex : 0;
                AVStream* st = m_impl->formatContext->streams[streamIndex];

                AVRational frameTimeBase = {frame->GetTimeBase().num, frame->GetTimeBase().den};
                pkt->pts = av_rescale_q(frame->GetPts(), frameTimeBase, st->time_base);
                pkt->dts = av_rescale_q(frame->GetDts(), frameTimeBase, st->time_base);
                pkt->stream_index = streamIndex;

                // Set keyframe flag if needed, usually encoder outputs it in packetData or we can add IsKeyFrame() to MediaFrame

                int ret = av_interleaved_write_frame(m_impl->formatContext, pkt);
                if (ret < 0) {
                    core::Logger::SError("io", "Error while writing frame: {}", ret);
                }
            }
            av_packet_free(&pkt);
        }
    }

    // Propagate to downstream if connected
    if (m_impl->downstream) {
        return m_impl->downstream->PushFrame(frame);
    }
    return {};
}

core::Result<std::shared_ptr<core::MediaFrame>> FileOutput::PullFrame() {
    return std::unexpected(core::Error::Make(core::ErrorCode::NotImplemented, "FileOutput does not produce frames"));
}

core::VoidResult FileOutput::Connect(std::shared_ptr<core::IMediaObject> downstream) {
    std::lock_guard lock(m_impl->mutex);
    m_impl->downstream = downstream;
    return {};
}

core::VoidResult FileOutput::Disconnect() {
    std::lock_guard lock(m_impl->mutex);
    m_impl->downstream.reset();
    return {};
}

void FileOutput::OnStateChange(core::StateChangeCallback callback) {
    m_impl->onStateChange = callback;
}

void FileOutput::OnError(core::ErrorCallback callback) {
    m_impl->onError = callback;
}

core::VoidResult FileOutput::AddVideoStream(const std::string& codecName, int width, int height, int fps_num, int fps_den, int bitrate) {
    std::lock_guard lock(m_impl->mutex);

    if (m_impl->isOpened) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "Cannot add stream after opening file"));
    }

    if (!m_impl->formatContext) {
        // Allocate just to hold streams temporarily. Path is unknown until Open()
        avformat_alloc_output_context2(&m_impl->formatContext, nullptr, nullptr, "dummy.mp4");
        if (!m_impl->formatContext) {
            return std::unexpected(core::Error::Make(core::ErrorCode::FileOpenFailed, "Failed to allocate format context"));
        }
    }

    const AVCodec* codec = avcodec_find_encoder_by_name(codecName.c_str());
    if (!codec) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidArgument, "Unsupported codec name"));
    }

    AVStream* stream = avformat_new_stream(m_impl->formatContext, nullptr);
    if (!stream) {
        return std::unexpected(core::Error::Make(core::ErrorCode::FileOpenFailed, "Could not create stream"));
    }

    stream->id = m_impl->formatContext->nb_streams - 1;
    stream->time_base = {fps_den, fps_num};

    AVCodecParameters* par = stream->codecpar;
    par->codec_id = codec->id;
    par->codec_type = AVMEDIA_TYPE_VIDEO;
    par->width = width;
    par->height = height;
    if (bitrate > 0) {
        par->bit_rate = bitrate;
    }

    m_impl->videoStreamIndex = stream->index;
    return {};
}

core::VoidResult FileOutput::AddAudioStream(const std::string& codecName, int sampleRate, int channels, int bitrate) {
    std::lock_guard lock(m_impl->mutex);

    if (m_impl->isOpened) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "Cannot add stream after opening file"));
    }

    if (!m_impl->formatContext) {
        avformat_alloc_output_context2(&m_impl->formatContext, nullptr, nullptr, "dummy.mp4");
        if (!m_impl->formatContext) {
            return std::unexpected(core::Error::Make(core::ErrorCode::FileOpenFailed, "Failed to allocate format context"));
        }
    }

    const AVCodec* codec = avcodec_find_encoder_by_name(codecName.c_str());
    if (!codec) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidArgument, "Unsupported codec name"));
    }

    AVStream* stream = avformat_new_stream(m_impl->formatContext, nullptr);
    if (!stream) {
        return std::unexpected(core::Error::Make(core::ErrorCode::FileOpenFailed, "Could not create stream"));
    }

    stream->id = m_impl->formatContext->nb_streams - 1;
    stream->time_base = {1, sampleRate};

    AVCodecParameters* par = stream->codecpar;
    par->codec_id = codec->id;
    par->codec_type = AVMEDIA_TYPE_AUDIO;
    par->sample_rate = sampleRate;
    par->ch_layout.nb_channels = channels;
    if (bitrate > 0) {
        par->bit_rate = bitrate;
    }

    m_impl->audioStreamIndex = stream->index;
    return {};
}

core::VoidResult FileOutput::Open(const std::string& path) {
    std::lock_guard lock(m_impl->mutex);
    
    if (m_impl->isOpened) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "File already opened"));
    }

    // Re-allocate format context to guess format by the actual path
    if (m_impl->formatContext) {
        const AVOutputFormat* fmt = av_guess_format(nullptr, path.c_str(), nullptr);
        if (fmt) {
            m_impl->formatContext->oformat = fmt;
        }
        av_freep(&m_impl->formatContext->url);
        m_impl->formatContext->url = av_strdup(path.c_str());
    } else {
        int ret = avformat_alloc_output_context2(&m_impl->formatContext, nullptr, nullptr, path.c_str());
        if (ret < 0 || !m_impl->formatContext) {
            return std::unexpected(core::Error::Make(core::ErrorCode::FileOpenFailed, "Could not allocate output context"));
        }
    }

    if (!(m_impl->formatContext->oformat->flags & AVFMT_NOFILE)) {
        int ret = avio_open(&m_impl->formatContext->pb, path.c_str(), AVIO_FLAG_WRITE);
        if (ret < 0) {
            return std::unexpected(core::Error::Make(core::ErrorCode::FileOpenFailed, "Could not open file IO"));
        }
    }

    AVDictionary* opts = nullptr;
    // For MP4, place moov atom at the front
    if (path.find(".mp4") != std::string::npos || path.find(".mov") != std::string::npos) {
        av_dict_set(&opts, "movflags", "faststart", 0);
    }

    int ret = avformat_write_header(m_impl->formatContext, &opts);
    av_dict_free(&opts);

    if (ret < 0) {
        return std::unexpected(core::Error::Make(core::ErrorCode::FileOpenFailed, "Could not write file header"));
    }

    m_impl->filePath = path;
    m_impl->isOpened = true;
    return {};
}

void FileOutput::Close() {
    std::lock_guard lock(m_impl->mutex);
    if (m_impl->isOpened && m_impl->formatContext) {
        av_write_trailer(m_impl->formatContext);
        if (!(m_impl->formatContext->oformat->flags & AVFMT_NOFILE)) {
            avio_closep(&m_impl->formatContext->pb);
        }
        avformat_free_context(m_impl->formatContext);
        m_impl->formatContext = nullptr;
    }
    m_impl->isOpened = false;
    m_impl->filePath.clear();
}

} // namespace openmedia::io
