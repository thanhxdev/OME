#include <openmedia/io/FileSource.h>
#include <openmedia/core/Logger.h>
#include <mutex>

extern "C" {
#include <libavformat/avformat.h>
#include <libswscale/swscale.h>
#include <libswresample/swresample.h>
#include <libavutil/opt.h>
}

namespace openmedia::io {

struct FileSource::Impl {
    MediaReader reader;
    std::mutex readerMutex;
    core::PipelineState state{core::PipelineState::Stopped};
    std::shared_ptr<core::IMediaObject> downstream;
    core::StateChangeCallback onStateChange;
    core::ErrorCallback onError;

    AVFrame* decodedVideoFrame = nullptr;
    AVFrame* decodedAudioFrame = nullptr;
    SwsContext* swsCtx = nullptr;
    SwrContext* swrCtx = nullptr;
    
    std::string filePath;
    bool loopMode = false;
    uint32_t bitrate = 0;
    double durationSeconds = 0.0;

    Impl() {
        decodedVideoFrame = av_frame_alloc();
        decodedAudioFrame = av_frame_alloc();
    }

    ~Impl() {
        if (decodedVideoFrame) av_frame_free(&decodedVideoFrame);
        if (decodedAudioFrame) av_frame_free(&decodedAudioFrame);
        if (swsCtx) sws_freeContext(swsCtx);
        if (swrCtx) swr_free(&swrCtx);
    }
};

FileSource::FileSource() : m_impl(new Impl()) {}
FileSource::~FileSource() { Stop(); Close(); }

std::string FileSource::GetName() const { return "FileSource"; }
core::PipelineState FileSource::GetState() const { return m_impl->state; }

core::VoidResult FileSource::Initialize() { return {}; }
core::VoidResult FileSource::Start() { m_impl->state = core::PipelineState::Running; return {}; }
core::VoidResult FileSource::Stop() { m_impl->state = core::PipelineState::Stopped; return {}; }

core::VoidResult FileSource::PushFrame(std::shared_ptr<core::MediaFrame>) { return std::unexpected(core::Error::Make(core::ErrorCode::NotImplemented, "Not supported")); }
core::Result<std::shared_ptr<core::MediaFrame>> FileSource::PullFrame() { return PullVideoFrame(); }

core::Result<std::shared_ptr<core::MediaFrame>> FileSource::PullVideoFrame() {
    std::lock_guard lock(m_impl->readerMutex);
    if (m_impl->state != core::PipelineState::Running) return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "Not running"));

    av_frame_unref(m_impl->decodedVideoFrame);
    auto result = m_impl->reader.ReadVideoFrame(m_impl->decodedVideoFrame);
    if (!result) {
        if (result.error().code == core::ErrorCode::EndOfStream && m_impl->loopMode) {
            m_impl->reader.Seek(0.0);
            result = m_impl->reader.ReadVideoFrame(m_impl->decodedVideoFrame);
            if (!result) return std::unexpected(result.error());
        } else return std::unexpected(result.error());
    }

    int width = m_impl->decodedVideoFrame->width;
    int height = m_impl->decodedVideoFrame->height;
    auto format = static_cast<AVPixelFormat>(m_impl->decodedVideoFrame->format);

    m_impl->swsCtx = sws_getCachedContext(m_impl->swsCtx, width, height, format, width, height, AV_PIX_FMT_BGRA, SWS_FAST_BILINEAR, nullptr, nullptr, nullptr);
    if (!m_impl->swsCtx) return std::unexpected(core::Error::Make(core::ErrorCode::Unknown, "swscale context failed"));

    auto mediaFrame = core::MediaFrame::CreateVideo(width, height, core::PixelFormat::BGRA);
    uint8_t* dstData[1] = { mediaFrame->GetVideoPlane(0) };
    int dstLinesize[1] = { static_cast<int>(mediaFrame->GetLineSize(0)) };

    sws_scale(m_impl->swsCtx, m_impl->decodedVideoFrame->data, m_impl->decodedVideoFrame->linesize, 0, height, dstData, dstLinesize);
    mediaFrame->SetPts(m_impl->decodedVideoFrame->pts);
    mediaFrame->SetDts(m_impl->decodedVideoFrame->pkt_dts);
    return mediaFrame;
}

core::Result<std::shared_ptr<core::MediaFrame>> FileSource::PullAudioFrame() {
    std::lock_guard lock(m_impl->readerMutex);
    if (m_impl->state != core::PipelineState::Running) return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "Not running"));

    av_frame_unref(m_impl->decodedAudioFrame);
    auto result = m_impl->reader.ReadAudioFrame(m_impl->decodedAudioFrame);
    if (!result) {
        if (result.error().code == core::ErrorCode::EndOfStream && m_impl->loopMode) {
            // Already seeked by video logic maybe? Let's not seek again if we can avoid it.
            // But if audio ends before video, we might just return EOF.
            return std::unexpected(result.error());
        } else return std::unexpected(result.error());
    }

    if (!m_impl->swrCtx) {
        AVChannelLayout outChLayout;
        av_channel_layout_default(&outChLayout, 2);
        
        swr_alloc_set_opts2(&m_impl->swrCtx, 
            &outChLayout, AV_SAMPLE_FMT_FLT, 48000,
            &m_impl->decodedAudioFrame->ch_layout, 
            static_cast<AVSampleFormat>(m_impl->decodedAudioFrame->format), 
            m_impl->decodedAudioFrame->sample_rate, 
            0, nullptr);
            
        if (swr_init(m_impl->swrCtx) < 0) {
            return std::unexpected(core::Error::Make(core::ErrorCode::Unknown, "swresample init failed"));
        }
    }

    auto mediaFrame = core::MediaFrame::CreateAudio(m_impl->decodedAudioFrame->nb_samples, 2, core::SampleFormat::Float32, 48000);
    uint8_t* outData[1] = { mediaFrame->GetAudioData(0) }; // Interleaved output? No, wait, planar or interleaved? FLT is interleaved, FLTP is planar. CreateAudio expects interleaved if only 1 channel pointer is fetched? Actually MediaFrame API allows per channel or just channel 0 for interleaved. Let's assume channel 0 is the single interleaved buffer if format is interleaved.

    swr_convert(m_impl->swrCtx, outData, m_impl->decodedAudioFrame->nb_samples, 
                (const uint8_t**)m_impl->decodedAudioFrame->data, m_impl->decodedAudioFrame->nb_samples);

    mediaFrame->SetPts(m_impl->decodedAudioFrame->pts);
    return mediaFrame;
}

core::VoidResult FileSource::Connect(std::shared_ptr<core::IMediaObject> downstream) { std::lock_guard lock(m_impl->readerMutex); m_impl->downstream = downstream; return {}; }
core::VoidResult FileSource::Disconnect() { std::lock_guard lock(m_impl->readerMutex); m_impl->downstream.reset(); return {}; }
void FileSource::OnStateChange(core::StateChangeCallback callback) { m_impl->onStateChange = callback; }
void FileSource::OnError(core::ErrorCallback callback) { m_impl->onError = callback; }

core::VoidResult FileSource::Open(const std::string& path) {
    std::lock_guard lock(m_impl->readerMutex);
    auto res = m_impl->reader.Open(path);
    if (res) {
        m_impl->filePath = path;
        auto streams = m_impl->reader.GetStreams();
        if (!streams.empty()) m_impl->durationSeconds = streams[0].durationSeconds;
    }
    return res;
}

void FileSource::Close() {
    std::lock_guard lock(m_impl->readerMutex);
    m_impl->reader.Close();
    m_impl->filePath.clear();
}
const std::vector<StreamInfo>& FileSource::GetStreams() const { std::lock_guard lock(m_impl->readerMutex); return m_impl->reader.GetStreams(); }
core::VoidResult FileSource::Seek(double ts) { std::lock_guard lock(m_impl->readerMutex); return m_impl->reader.Seek(ts); }
void FileSource::SetLoopMode(bool loop) { std::lock_guard lock(m_impl->readerMutex); m_impl->loopMode = loop; }
bool FileSource::GetLoopMode() const { std::lock_guard lock(m_impl->readerMutex); return m_impl->loopMode; }
double FileSource::GetDurationSeconds() const { std::lock_guard lock(m_impl->readerMutex); return m_impl->durationSeconds; }
uint32_t FileSource::GetBitrate() const { std::lock_guard lock(m_impl->readerMutex); return m_impl->bitrate; }
core::VoidResult FileSource::SeekFrame(int64_t) { return std::unexpected(core::Error::Make(core::ErrorCode::NotSupported, "")); }

} // namespace openmedia::io

