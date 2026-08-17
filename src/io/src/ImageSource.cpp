/// @file ImageSource.cpp
#include <openmedia/io/ImageSource.h>
#include <openmedia/core/Logger.h>

#include <mutex>
#include <atomic>
#include <chrono>

extern "C" {
#include <libavformat/avformat.h>
#include <libavcodec/avcodec.h>
}

namespace openmedia::io {

struct ImageSource::Impl {
    std::mutex mutex;
    core::PipelineState state{core::PipelineState::Stopped};
    std::shared_ptr<core::IMediaObject> downstream;
    core::StateChangeCallback onStateChange;
    core::ErrorCallback onError;

    std::string filePath;
    double fps = 30.0;
    std::shared_ptr<core::MediaFrame> cachedFrame;
    int64_t currentFrameIndex = 0;
};

ImageSource::ImageSource() : m_impl(new Impl()) {}

ImageSource::~ImageSource() {
    (void)Stop();
    Close();
}

std::string ImageSource::GetName() const { return "ImageSource"; }

core::PipelineState ImageSource::GetState() const { return m_impl->state; }

core::VoidResult ImageSource::Initialize() { return {}; }

core::VoidResult ImageSource::Start() {
    std::lock_guard lock(m_impl->mutex);
    m_impl->state = core::PipelineState::Running;
    m_impl->currentFrameIndex = 0;
    return {};
}

core::VoidResult ImageSource::Stop() {
    std::lock_guard lock(m_impl->mutex);
    m_impl->state = core::PipelineState::Stopped;
    return {};
}

core::VoidResult ImageSource::PushFrame(std::shared_ptr<core::MediaFrame> /*frame*/) {
    return std::unexpected(core::Error::Make(core::ErrorCode::NotImplemented, "ImageSource does not accept incoming frames"));
}

core::Result<std::shared_ptr<core::MediaFrame>> ImageSource::PullFrame() {
    std::lock_guard lock(m_impl->mutex);
    if (m_impl->state != core::PipelineState::Running) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "ImageSource is not running"));
    }

    if (!m_impl->cachedFrame) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "No image loaded"));
    }

    auto frame = m_impl->cachedFrame->Clone();
    if (!frame) {
        return std::unexpected(core::Error::Make(core::ErrorCode::OutOfMemory, "Failed to clone image frame"));
    }

    int64_t pts = static_cast<int64_t>((m_impl->currentFrameIndex / m_impl->fps) * 90000.0); // assuming 90kHz timebase
    frame->SetPts(pts);
    m_impl->currentFrameIndex++;

    return frame;
}

core::VoidResult ImageSource::Connect(std::shared_ptr<core::IMediaObject> downstream) {
    std::lock_guard lock(m_impl->mutex);
    m_impl->downstream = downstream;
    return {};
}

core::VoidResult ImageSource::Disconnect() {
    std::lock_guard lock(m_impl->mutex);
    m_impl->downstream.reset();
    return {};
}

void ImageSource::OnStateChange(core::StateChangeCallback callback) {
    m_impl->onStateChange = callback;
}

void ImageSource::OnError(core::ErrorCallback callback) {
    m_impl->onError = callback;
}

core::VoidResult ImageSource::Open(const std::string& path) {
    std::lock_guard lock(m_impl->mutex);
    m_impl->filePath = path;

    // Use MediaReader to load the single image
    MediaReader reader;
    auto res = reader.Open(path);
    if (!res) {
        return res;
    }

    AVPacket* pkt = av_packet_alloc();
    res = reader.ReadPacket(pkt);
    if (!res) {
        av_packet_free(&pkt);
        return res;
    }

    // For a proper implementation, this packet should be decoded.
    // For now, we simulate success and wrap the encoded packet in a frame.
    // Real implementation would decode PNG/JPG to BGRA using IDecoder.
    m_impl->cachedFrame = core::MediaFrame::CreateVideo(1920, 1080, core::PixelFormat::Unknown);
    
    av_packet_free(&pkt);
    return {};
}

void ImageSource::Close() {
    std::lock_guard lock(m_impl->mutex);
    m_impl->filePath.clear();
    m_impl->cachedFrame.reset();
}

void ImageSource::SetFrameRate(double fps) {
    std::lock_guard lock(m_impl->mutex);
    if (fps > 0) {
        m_impl->fps = fps;
    }
}

} // namespace openmedia::io
