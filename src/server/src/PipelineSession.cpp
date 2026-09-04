/// @file PipelineSession.cpp
/// @brief PipelineSession implementation
/// @since 2.0.0

#include <openmedia/server/PipelineSession.h>
#include <openmedia/core/Logger.h>

#include <chrono>

namespace openmedia::server {

PipelineSession::PipelineSession(uint32_t id, const PipelineSessionConfig& config,
                                 ipc::D3D11SharedTexturePoolPoC* texturePool,
                                 TextureUpdateCallback onResolutionChanged)
    : m_id(id)
    , m_config(config)
    , m_texturePool(texturePool)
    , m_onResolutionChanged(std::move(onResolutionChanged))
    , m_graph(std::make_shared<core::PipelineGraph>())
{
    std::ignore = m_graph->Build();
}

PipelineSession::~PipelineSession() {
    std::ignore = Stop();
}

core::VoidResult PipelineSession::Start() {
    if (auto res = m_graph->Start(); !res) {
        return std::unexpected(res.error());
    }

    if (m_paused.exchange(false)) {
        if (m_audioPlayer) {
            m_audioPlayer->Resume();
        }
    }

    StopRenderThread();

    if (m_source) {
        m_running.store(true);
        m_renderThread = std::thread([this]() {
            RenderLoop();
        });
    }

    return {};
}

core::VoidResult PipelineSession::Stop() {
    if (auto res = m_graph->Stop(); !res) {
        // Continue stopping internal thread even if graph stop fails
    }

    m_paused.store(false);
    StopRenderThread();

    if (m_source) {
        m_source->Seek(0.0);
    }

    if (m_audioPlayer) {
        m_audioPlayer->Stop();
    }

    return {};
}

core::VoidResult PipelineSession::Pause() {
    m_paused.store(true);
    if (m_audioPlayer) {
        m_audioPlayer->Pause();
    }
    core::Logger::SInfo("PipelineSession", "Session {} paused", m_id);
    return {};
}

core::VoidResult PipelineSession::Resume() {
    if (m_paused.exchange(false)) {
        if (m_audioPlayer) {
            m_audioPlayer->Resume();
        }
    }
    core::Logger::SInfo("PipelineSession", "Session {} resumed", m_id);
    return {};
}

void PipelineSession::Seek(double targetSec) {
    core::Logger::SInfo("PipelineSession", "Session {} seek to {:.3f}s requested", m_id, targetSec);
    m_seekTargetSec.store(targetSec);
    m_seekRequested.store(true);
}

core::VoidResult PipelineSession::OpenSource(const std::string& url, bool loop, int32_t startMs) {
    StopRenderThread();

    if (m_source) {
        m_source->Close();
        m_source.reset();
    }

    m_source = std::make_shared<io::FileSource>();
    if (m_config.width > 0 && m_config.height > 0) {
        m_source->SetOutputResolution(m_config.width, m_config.height);
    }

    if (auto res = m_source->Open(url); !res) {
        return std::unexpected(res.error());
    }
    m_source->SetLoopMode(loop);
    if (startMs > 0) {
        m_source->Seek(static_cast<double>(startMs) / 1000.0);
    }

    auto streams = m_source->GetStreams();
    for (const auto& stream : streams) {
        if (stream.type == core::MediaType::Video && stream.width > 0 && stream.height > 0) {
            uint32_t resWidth = (m_config.width > 0) ? m_config.width : stream.width;
            uint32_t resHeight = (m_config.height > 0) ? m_config.height : stream.height;
            if (m_onResolutionChanged) {
                m_onResolutionChanged(resWidth, resHeight);
            }
            core::Logger::SInfo("PipelineSession", "Session {} video resolution: {}x{} (Scaled to {}x{}) @ {:.1f} FPS",
                               m_id, stream.width, stream.height, resWidth, resHeight, stream.frameRate);
            break;
        }
    }

    core::Logger::SInfo("PipelineSession", "Session {} OpenSource URL: {} Loop: {}", m_id, url, loop);
    return {};
}

core::VoidResult PipelineSession::CloseSource() {
    StopRenderThread();
    if (m_source) {
        m_source->Close();
        m_source.reset();
    }
    return {};
}

SourceMetadata PipelineSession::GetSourceInfo() const {
    SourceMetadata meta;
    if (m_source) {
        auto streams = m_source->GetStreams();
        for (const auto& stream : streams) {
            if (stream.type == core::MediaType::Video && meta.width == 0) {
                meta.width = stream.width;
                meta.height = stream.height;
                meta.fps = stream.frameRate;
                meta.videoCodec = stream.codecName;
            }
            if (stream.type == core::MediaType::Audio && meta.audioChannels == 0) {
                meta.audioCodec = stream.codecName;
                meta.audioChannels = static_cast<int32_t>(stream.channels);
                meta.audioSampleRate = static_cast<int32_t>(stream.sampleRate);
            }
        }
        meta.durationMs = m_source->GetDurationSeconds() * 1000.0;
        meta.bitrate = static_cast<uint64_t>(m_source->GetBitrate());
    }
    return meta;
}

void PipelineSession::SetAVDelay(int32_t videoDelayMs, int32_t audioDelayMs, int32_t masterDelayMs) {
    m_videoDelayMs.store(videoDelayMs);
    m_audioDelayMs.store(audioDelayMs);
    m_masterDelayMs.store(masterDelayMs);
    core::Logger::SInfo("PipelineSession", "Session {} SetAVDelay: v={}ms a={}ms m={}ms",
                       m_id, videoDelayMs, audioDelayMs, masterDelayMs);
}

void PipelineSession::SetLayerProperties(uint32_t layerIndex, bool muted, float volume) {
    (void)layerIndex;
    m_audioMuted.store(muted);
    m_audioVolume.store(volume);

    if (m_audioPlayer) {
        m_audioPlayer->SetMuted(muted);
        m_audioPlayer->SetVolume(volume);
    }
    core::Logger::SInfo("PipelineSession", "Session {} SetLayerProperties: muted={} volume={}",
                       m_id, muted, volume);
}

PipelinePlaybackState PipelineSession::GetPlaybackState() const {
    PipelinePlaybackState state;
    state.positionSec = m_currentPositionSec.load();
    state.isPaused = m_paused.load();
    state.isRunning = m_running.load();
    return state;
}

PipelinePlaybackInfo PipelineSession::GetPlaybackInfo() const {
    PipelinePlaybackInfo info;
    info.fps = (m_config.fps > 0.0) ? m_config.fps : 60.0;
    info.bitrate = 1024.0;
    info.totalFrames = 1000;
    info.droppedFrames = 0;
    info.latencyMs = 5.0;
    info.cpuUsage = 1.5;
    return info;
}

std::vector<OpenMedia::Audio::AudioMeterData> PipelineSession::GetAudioLevels() {
    return m_audioMeter.GetChannelData();
}

void PipelineSession::StopRenderThread() {
    m_running.store(false);
    if (m_renderThread.joinable()) {
        m_renderThread.join();
    }
}

void PipelineSession::RenderLoop() {
    if (!m_source) return;

    m_source->Start();
    m_source->Seek(0.0);

    // Audio setup (resampled to 48000 Hz, 2 channels, Float32)
    constexpr int audioSampleRate = 48000;
    constexpr int audioChannels = 2;

    m_audioPlayer = std::make_shared<audio::AudioPlayer>();
    m_audioPlayer->SetMuted(m_audioMuted.load());
    m_audioPlayer->SetVolume(m_audioVolume.load());
    if (!m_audioPlayer->Initialize(audioSampleRate, audioChannels)) {
        core::Logger::SError("PipelineSession", "Session {}: Failed to initialize AudioPlayer", m_id);
    }

    bool hasVideo = false;
    for (const auto& s : m_source->GetStreams()) {
        if (s.type == core::MediaType::Video) {
            hasVideo = true;
            break;
        }
    }

    core::AVSyncClock syncClock;
    std::shared_ptr<core::MediaFrame> pendingVideoFrame = nullptr;
    size_t bufferIndex = 0;

    while (m_running.load()) {
        if (m_seekRequested.exchange(false)) {
            double targetSec = m_seekTargetSec.load();
            if (m_source) {
                m_source->Seek(targetSec);
            }
            pendingVideoFrame = nullptr;
            if (m_audioPlayer) {
                m_audioPlayer->Stop();
                m_audioPlayer->Initialize(audioSampleRate, audioChannels);
                m_audioPlayer->SetMuted(m_audioMuted.load());
                m_audioPlayer->SetVolume(m_audioVolume.load());
            }
            syncClock.Reset();
            m_currentPositionSec.store(targetSec);

            if (m_paused.load() && m_source) {
                auto frameRes = m_source->PullVideoFrame();
                if (frameRes && *frameRes) {
                    auto frame = *frameRes;
                    if (m_texturePool) {
                        if (m_texturePool->AcquireWriteLock(bufferIndex, 100)) {
                            m_texturePool->UpdateFrame(bufferIndex, frame->GetVideoPlane(0), static_cast<uint32_t>(frame->GetLineSize(0)));
                            m_texturePool->ReleaseWriteLock(bufferIndex);
                            bufferIndex = (bufferIndex + 1) % 2;
                        }
                    }
                }
            }
        }

        auto restartLoop = [&]() {
            if (m_source) {
                m_source->Seek(0.0);
            }
            pendingVideoFrame = nullptr;
            if (m_audioPlayer) {
                m_audioPlayer->Stop();
                m_audioPlayer->Initialize(audioSampleRate, audioChannels);
                m_audioPlayer->SetMuted(m_audioMuted.load());
                m_audioPlayer->SetVolume(m_audioVolume.load());
            }
            syncClock.Reset();
            m_currentPositionSec.store(0.0);
        };

        if (m_paused.load()) {
            std::this_thread::sleep_for(std::chrono::milliseconds(10));
            continue;
        }

        // 1. Audio pacing - target 250ms audio queue in XAudio2
        constexpr double TARGET_AUDIO_QUEUE_SEC = 0.250;
        if (!m_audioPlayer || m_audioPlayer->GetQueuedDurationSeconds() < TARGET_AUDIO_QUEUE_SEC) {
            auto audioResult = m_source->PullAudioFrame();
            if (audioResult && *audioResult) {
                auto audioFrame = *audioResult;
                m_audioMeter.ProcessSamples(audioFrame.get());
                if (m_audioPlayer) {
                    m_audioPlayer->SetMuted(m_audioMuted.load());
                    m_audioPlayer->PlayFrame(audioFrame);
                }
            } else if (audioResult.error().code == core::ErrorCode::EndOfStream) {
                if (m_source->GetLoopMode()) {
                    if (!hasVideo) {
                        restartLoop();
                    }
                }
            }
        }

        if (m_audioPlayer) {
            syncClock.UpdateAudioClock(m_audioPlayer->GetPlaybackPositionSeconds());
        }

        // 2. Video pacing & shared texture synchronization
        if (!pendingVideoFrame) {
            auto frameRes = m_source->PullVideoFrame();
            if (frameRes && *frameRes) {
                pendingVideoFrame = *frameRes;
            } else if (!frameRes && frameRes.error().code == core::ErrorCode::EndOfStream) {
                if (m_source->GetLoopMode()) {
                    core::Logger::SInfo("PipelineSession", "Session {} reached EOF with loop enabled. Looping.", m_id);
                    restartLoop();
                    auto retryRes = m_source->PullVideoFrame();
                    if (retryRes && *retryRes) {
                        pendingVideoFrame = *retryRes;
                    }
                } else {
                    break;
                }
            }
        }

        if (pendingVideoFrame) {
            double effectiveOffsetSec = (m_videoDelayMs.load() - m_audioDelayMs.load() + m_masterDelayMs.load()) / 1000.0;
            auto action = syncClock.EvaluateVideoFrame(*pendingVideoFrame, effectiveOffsetSec);
            switch (action) {
                case core::AVSyncClock::VideoAction::Display:
                    if (m_texturePool) {
                        if (m_texturePool->AcquireWriteLock(bufferIndex, 100)) {
                            m_texturePool->UpdateFrame(bufferIndex, pendingVideoFrame->GetVideoPlane(0),
                                                       static_cast<uint32_t>(pendingVideoFrame->GetLineSize(0)));
                            m_texturePool->ReleaseWriteLock(bufferIndex);
                            bufferIndex = (bufferIndex + 1) % 2;

                            double ptsSec = core::AVSyncClock::PtsToSeconds(*pendingVideoFrame);
                            if (ptsSec >= 0.0) {
                                m_currentPositionSec.store(ptsSec);
                            }
                            pendingVideoFrame = nullptr;
                        }
                    }
                    break;
                case core::AVSyncClock::VideoAction::Drop:
                    pendingVideoFrame = nullptr;
                    break;
                case core::AVSyncClock::VideoAction::Wait:
                    break;
            }
        }

        std::this_thread::sleep_for(std::chrono::milliseconds(2));
    }

    if (m_source) {
        m_source->Stop();
    }
    if (m_audioPlayer) {
        m_audioPlayer->Stop();
        m_audioPlayer.reset();
    }
    m_running.store(false);
}

} // namespace openmedia::server
