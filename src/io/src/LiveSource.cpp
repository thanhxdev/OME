/// @file LiveSource.cpp
#include <openmedia/io/LiveSource.h>
#include <openmedia/io/JitterBuffer.h>
#include <openmedia/core/Logger.h>

extern "C" {
#include <libavformat/avformat.h>
#include <libavcodec/avcodec.h>
}

#include <mutex>
#include <atomic>
#include <thread>
#include <chrono>

namespace openmedia::io {

struct LiveSource::Impl {
    MediaReader reader;
    std::mutex readerMutex;
    core::PipelineState state{core::PipelineState::Stopped};
    std::shared_ptr<core::IMediaObject> downstream;
    core::StateChangeCallback onStateChange;
    core::ErrorCallback onError;

    std::string url;
    int reconnectAttempts = 5;
    int reconnectDelayMs = 2000;

    JitterBuffer jitterBuffer;
    std::thread readerThread;
    std::atomic<bool> stopThread{false};

    Impl() {}

    ~Impl() {}

    core::VoidResult Reconnect() {
        int attempt = 0;
        while (attempt < reconnectAttempts && !stopThread) {
            attempt++;
            reader.Close();
            std::this_thread::sleep_for(std::chrono::milliseconds(reconnectDelayMs));
            
            auto res = reader.Open(url);
            if (res) {
                return {};
            }
        }
        return std::unexpected(core::Error::Make(core::ErrorCode::StreamConnectionLost, "Max reconnect attempts reached"));
    }

    void ReaderLoop() {
        AVPacket* packet = av_packet_alloc();
        while (!stopThread) {
            av_packet_unref(packet);
            
            core::Result<core::VoidResult> result;
            {
                std::lock_guard lock(readerMutex);
                result = reader.ReadPacket(packet);
            }

            if (!result) {
                if (result.error().code == core::ErrorCode::EndOfStream || result.error().code == core::ErrorCode::StreamConnectionLost) {
                    auto reconnectRes = Reconnect();
                    if (!reconnectRes) {
                        std::this_thread::sleep_for(std::chrono::milliseconds(100));
                    }
                    continue;
                } else {
                    std::this_thread::sleep_for(std::chrono::milliseconds(10));
                    continue;
                }
            }

            bool isVideo = false;
            bool isAudio = false;
            {
                std::lock_guard lock(readerMutex);
                isVideo = (packet->stream_index == reader.GetBestVideoStreamIndex());
                isAudio = (packet->stream_index == reader.GetBestAudioStreamIndex());
            }

            core::MediaType type = core::MediaType::Unknown;
            if (isVideo) type = core::MediaType::Video;
            else if (isAudio) type = core::MediaType::Audio;

            if (type != core::MediaType::Unknown) {
                auto frame = type == core::MediaType::Video ? 
                    core::MediaFrame::CreateVideo(0, 0, core::PixelFormat::Unknown) : 
                    core::MediaFrame::CreateAudio(0, 0, core::SampleFormat::Unknown, 0);
                
                frame->SetPts(packet->pts);
                jitterBuffer.Push(frame);
            }
        }
        av_packet_free(&packet);
    }
};

LiveSource::LiveSource() : m_impl(new Impl()) {}

LiveSource::~LiveSource() {
    (void)Stop();
    Close();
}

std::string LiveSource::GetName() const { return "LiveSource"; }

core::PipelineState LiveSource::GetState() const { return m_impl->state; }

core::VoidResult LiveSource::Initialize() { return {}; }

core::VoidResult LiveSource::Start() {
    std::lock_guard lock(m_impl->readerMutex);
    if (m_impl->state == core::PipelineState::Running) {
        return {};
    }
    m_impl->state = core::PipelineState::Running;
    m_impl->stopThread = false;
    m_impl->readerThread = std::thread([this]() { this->m_impl->ReaderLoop(); });
    return {};
}

core::VoidResult LiveSource::Stop() {
    {
        std::lock_guard lock(m_impl->readerMutex);
        if (m_impl->state == core::PipelineState::Stopped) {
            return {};
        }
        m_impl->state = core::PipelineState::Stopped;
        m_impl->stopThread = true;
    }
    if (m_impl->readerThread.joinable()) {
        m_impl->readerThread.join();
    }
    m_impl->jitterBuffer.Clear();
    return {};
}

core::VoidResult LiveSource::PushFrame(std::shared_ptr<core::MediaFrame> /*frame*/) {
    return std::unexpected(core::Error::Make(core::ErrorCode::NotImplemented, "LiveSource does not accept incoming frames"));
}

core::Result<std::shared_ptr<core::MediaFrame>> LiveSource::PullFrame() {
    if (m_impl->state != core::PipelineState::Running) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "LiveSource is not running"));
    }

    auto frameOpt = m_impl->jitterBuffer.Pop();
    if (frameOpt) {
        return *frameOpt;
    }
    
    // Return ErrorCode::WouldBlock to indicate no frame ready yet
    return std::unexpected(core::Error::Make(core::ErrorCode::WouldBlock, "Jitter buffer is buffering or empty"));
}

core::VoidResult LiveSource::Connect(std::shared_ptr<core::IMediaObject> downstream) {
    std::lock_guard lock(m_impl->readerMutex);
    m_impl->downstream = downstream;
    return {};
}

core::VoidResult LiveSource::Disconnect() {
    std::lock_guard lock(m_impl->readerMutex);
    m_impl->downstream.reset();
    return {};
}

void LiveSource::OnStateChange(core::StateChangeCallback callback) {
    m_impl->onStateChange = callback;
}

void LiveSource::OnError(core::ErrorCallback callback) {
    m_impl->onError = callback;
}

core::VoidResult LiveSource::Open(const std::string& url) {
    std::lock_guard lock(m_impl->readerMutex);
    auto res = m_impl->reader.Open(url);
    if (res) {
        m_impl->url = url;
    }
    return res;
}

void LiveSource::Close() {
    std::lock_guard lock(m_impl->readerMutex);
    m_impl->reader.Close();
    m_impl->url.clear();
}

const std::vector<StreamInfo>& LiveSource::GetStreams() const {
    std::lock_guard lock(m_impl->readerMutex);
    return m_impl->reader.GetStreams();
}

void LiveSource::SetReconnectAttempts(int attempts) {
    std::lock_guard lock(m_impl->readerMutex);
    m_impl->reconnectAttempts = attempts;
}

void LiveSource::SetReconnectDelayMs(int delayMs) {
    std::lock_guard lock(m_impl->readerMutex);
    m_impl->reconnectDelayMs = delayMs;
}

void LiveSource::SetJitterBufferLatency(uint32_t latencyMs) {
    m_impl->jitterBuffer.SetTargetLatency(latencyMs);
}

} // namespace openmedia::io
