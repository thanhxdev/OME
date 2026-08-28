#include <openmedia/io/MediaReader.h>
#include <openmedia/core/Logger.h>
#include <queue>

extern "C" {
#include <libavformat/avformat.h>
#include <libavcodec/avcodec.h>
#include <libavutil/error.h>
#include <libavdevice/avdevice.h>
}

namespace openmedia::io {

struct MediaReader::Impl {
    AVFormatContext* formatContext = nullptr;
    AVCodecContext* videoCodecCtx = nullptr;
    AVCodecContext* audioCodecCtx = nullptr;
    std::vector<StreamInfo> streams;
    int bestVideoIndex = -1;
    int bestAudioIndex = -1;
    
    std::queue<AVPacket*> videoQueue;
    std::queue<AVPacket*> audioQueue;

    std::string FFmpegErrorToString(int errnum) {
        char errbuf[AV_ERROR_MAX_STRING_SIZE] = {0};
        av_strerror(errnum, errbuf, AV_ERROR_MAX_STRING_SIZE);
        return std::string(errbuf);
    }
    
    void ClearQueues() {
        while (!videoQueue.empty()) {
            AVPacket* pkt = videoQueue.front();
            av_packet_free(&pkt);
            videoQueue.pop();
        }
        while (!audioQueue.empty()) {
            AVPacket* pkt = audioQueue.front();
            av_packet_free(&pkt);
            audioQueue.pop();
        }
    }
};

MediaReader::MediaReader() : m_impl(new Impl()) {}

MediaReader::~MediaReader() { Close(); }

core::VoidResult MediaReader::Open(const std::string& path, const std::string& format) {
    avdevice_register_all();
    if (m_impl->formatContext) Close();

    AVFormatContext* fmtCtx = avformat_alloc_context();
    if (!fmtCtx) return std::unexpected(core::Error{core::ErrorCode::OutOfMemory, "Failed to allocate AVFormatContext"});

    const AVInputFormat* inFmt = nullptr;
    if (!format.empty()) {
        inFmt = av_find_input_format(format.c_str());
    }

    int ret = avformat_open_input(&fmtCtx, path.c_str(), inFmt, nullptr);
    if (ret < 0) {
        avformat_free_context(fmtCtx);
        return std::unexpected(core::Error{core::ErrorCode::FileNotFound, "Failed to open input: " + m_impl->FFmpegErrorToString(ret)});
    }
    m_impl->formatContext = fmtCtx;

    ret = avformat_find_stream_info(fmtCtx, nullptr);
    if (ret < 0) {
        Close();
        return std::unexpected(core::Error{core::ErrorCode::InvalidFormat, "Failed to find stream info: " + m_impl->FFmpegErrorToString(ret)});
    }

    for (unsigned int i = 0; i < fmtCtx->nb_streams; ++i) {
        AVStream* stream = fmtCtx->streams[i];
        const AVCodecParameters* codecpar = stream->codecpar;
        StreamInfo info;
        info.index = i;
        info.timeBase = {stream->time_base.num, stream->time_base.den};
        info.durationSeconds = stream->duration * av_q2d(stream->time_base);
        if (info.durationSeconds < 0 || stream->duration == AV_NOPTS_VALUE) {
            info.durationSeconds = (double)fmtCtx->duration / AV_TIME_BASE;
        }

        if (codecpar->codec_type == AVMEDIA_TYPE_VIDEO) {
            info.type = core::MediaType::Video;
            info.width = codecpar->width;
            info.height = codecpar->height;
            if (stream->avg_frame_rate.den > 0) info.frameRate = av_q2d(stream->avg_frame_rate);
        } else if (codecpar->codec_type == AVMEDIA_TYPE_AUDIO) {
            info.type = core::MediaType::Audio;
            info.sampleRate = codecpar->sample_rate;
            info.channels = codecpar->ch_layout.nb_channels;
        }
        const AVCodecDescriptor* desc = avcodec_descriptor_get(codecpar->codec_id);
        if (desc) info.codecName = desc->name;
        m_impl->streams.push_back(info);
    }

    m_impl->bestVideoIndex = av_find_best_stream(fmtCtx, AVMEDIA_TYPE_VIDEO, -1, -1, nullptr, 0);
    m_impl->bestAudioIndex = av_find_best_stream(fmtCtx, AVMEDIA_TYPE_AUDIO, -1, -1, nullptr, 0);

    if (m_impl->bestVideoIndex >= 0) {
        AVStream* stream = fmtCtx->streams[m_impl->bestVideoIndex];
        const AVCodec* codec = avcodec_find_decoder(stream->codecpar->codec_id);
        if (codec) {
            m_impl->videoCodecCtx = avcodec_alloc_context3(codec);
            if (m_impl->videoCodecCtx) {
                avcodec_parameters_to_context(m_impl->videoCodecCtx, stream->codecpar);
                avcodec_open2(m_impl->videoCodecCtx, codec, nullptr);
            }
        }
    }
    
    if (m_impl->bestAudioIndex >= 0) {
        AVStream* stream = fmtCtx->streams[m_impl->bestAudioIndex];
        const AVCodec* codec = avcodec_find_decoder(stream->codecpar->codec_id);
        if (codec) {
            m_impl->audioCodecCtx = avcodec_alloc_context3(codec);
            if (m_impl->audioCodecCtx) {
                avcodec_parameters_to_context(m_impl->audioCodecCtx, stream->codecpar);
                avcodec_open2(m_impl->audioCodecCtx, codec, nullptr);
            }
        }
    }

    return {};
}

void MediaReader::Close() {
    m_impl->ClearQueues();
    if (m_impl->videoCodecCtx) avcodec_free_context(&m_impl->videoCodecCtx);
    if (m_impl->audioCodecCtx) avcodec_free_context(&m_impl->audioCodecCtx);
    if (m_impl->formatContext) {
        avformat_close_input(&m_impl->formatContext);
        m_impl->formatContext = nullptr;
    }
    m_impl->streams.clear();
    m_impl->bestVideoIndex = -1;
    m_impl->bestAudioIndex = -1;
}

const std::vector<StreamInfo>& MediaReader::GetStreams() const { return m_impl->streams; }
int MediaReader::GetBestVideoStreamIndex() const { return m_impl->bestVideoIndex; }
int MediaReader::GetBestAudioStreamIndex() const { return m_impl->bestAudioIndex; }

core::VoidResult MediaReader::ReadVideoFrame(AVFrame* frame) {
    auto codecCtx = m_impl->videoCodecCtx;
    auto& queue = m_impl->videoQueue;
    if (!codecCtx) return std::unexpected(core::Error{core::ErrorCode::InvalidState, "Decoder not initialized"});

    while (true) {
        int ret = avcodec_receive_frame(codecCtx, frame);
        if (ret == 0) return {};
        if (ret == AVERROR_EOF) return std::unexpected(core::Error{core::ErrorCode::EndOfStream, "End of stream"});
        if (ret != AVERROR(EAGAIN)) return std::unexpected(core::Error{core::ErrorCode::IOError, "Error decoding frame"});

        if (queue.empty()) {
            AVPacket* pkt = av_packet_alloc();
            ret = av_read_frame(m_impl->formatContext, pkt);
            if (ret == AVERROR_EOF) {
                av_packet_free(&pkt);
                avcodec_send_packet(codecCtx, nullptr);
                continue;
            } else if (ret < 0) {
                av_packet_free(&pkt);
                return std::unexpected(core::Error{core::ErrorCode::IOError, "Error reading packet"});
            }
            if (pkt->stream_index == m_impl->bestVideoIndex) m_impl->videoQueue.push(pkt);
            else if (pkt->stream_index == m_impl->bestAudioIndex) m_impl->audioQueue.push(pkt);
            else av_packet_free(&pkt);
        }

        if (!queue.empty()) {
            AVPacket* pkt = queue.front();
            queue.pop();
            avcodec_send_packet(codecCtx, pkt);
            av_packet_free(&pkt);
        }
    }
}

core::VoidResult MediaReader::ReadAudioFrame(AVFrame* frame) {
    auto codecCtx = m_impl->audioCodecCtx;
    auto& queue = m_impl->audioQueue;
    if (!codecCtx) return std::unexpected(core::Error{core::ErrorCode::InvalidState, "Decoder not initialized"});

    while (true) {
        int ret = avcodec_receive_frame(codecCtx, frame);
        if (ret == 0) return {};
        if (ret == AVERROR_EOF) return std::unexpected(core::Error{core::ErrorCode::EndOfStream, "End of stream"});
        if (ret != AVERROR(EAGAIN)) return std::unexpected(core::Error{core::ErrorCode::IOError, "Error decoding frame"});

        if (queue.empty()) {
            AVPacket* pkt = av_packet_alloc();
            ret = av_read_frame(m_impl->formatContext, pkt);
            if (ret == AVERROR_EOF) {
                av_packet_free(&pkt);
                avcodec_send_packet(codecCtx, nullptr);
                continue;
            } else if (ret < 0) {
                av_packet_free(&pkt);
                return std::unexpected(core::Error{core::ErrorCode::IOError, "Error reading packet"});
            }
            if (pkt->stream_index == m_impl->bestVideoIndex) m_impl->videoQueue.push(pkt);
            else if (pkt->stream_index == m_impl->bestAudioIndex) m_impl->audioQueue.push(pkt);
            else av_packet_free(&pkt);
        }

        if (!queue.empty()) {
            AVPacket* pkt = queue.front();
            queue.pop();
            avcodec_send_packet(codecCtx, pkt);
            av_packet_free(&pkt);
        }
    }
}

core::VoidResult MediaReader::ReadPacket(AVPacket* packet) {
    if (!m_impl->formatContext) return std::unexpected(core::Error{core::ErrorCode::InvalidState, "Reader is not opened"});
    int ret = av_read_frame(m_impl->formatContext, packet);
    if (ret == AVERROR_EOF) return std::unexpected(core::Error{core::ErrorCode::EndOfStream, "End of file reached"});
    if (ret < 0) return std::unexpected(core::Error{core::ErrorCode::IOError, "Error reading packet"});
    return {};
}

core::VoidResult MediaReader::Seek(double timestampSeconds) {
    if (!m_impl->formatContext) return std::unexpected(core::Error{core::ErrorCode::InvalidState, "Reader is not opened"});
    m_impl->ClearQueues();
    if (m_impl->videoCodecCtx) avcodec_flush_buffers(m_impl->videoCodecCtx);
    if (m_impl->audioCodecCtx) avcodec_flush_buffers(m_impl->audioCodecCtx);

    int64_t timestamp = static_cast<int64_t>(timestampSeconds * AV_TIME_BASE);
    int ret = avformat_seek_file(m_impl->formatContext, -1, INT64_MIN, timestamp, INT64_MAX, AVSEEK_FLAG_BACKWARD);
    if (ret < 0) {
        ret = av_seek_frame(m_impl->formatContext, -1, timestamp, AVSEEK_FLAG_BACKWARD);
    }
    if (ret < 0 && timestampSeconds == 0.0) {
        ret = av_seek_frame(m_impl->formatContext, -1, 0, AVSEEK_FLAG_BYTE);
    }
    if (ret < 0) return std::unexpected(core::Error{core::ErrorCode::IOError, "Error seeking"});
    return {};
}

} // namespace openmedia::io

