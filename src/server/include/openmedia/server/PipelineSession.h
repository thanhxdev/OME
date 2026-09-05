#pragma once

/// @file PipelineSession.h
/// @brief Independent session representing a single media pipeline instance
/// @since 2.0.0

#include <openmedia/core/ErrorCodes.h>
#include <openmedia/core/PipelineGraph.h>
#include <openmedia/core/MediaFrame.h>
#include <openmedia/core/AVSyncClock.h>
#include <openmedia/io/FileSource.h>
#include <openmedia/audio/AudioPlayer.h>
#include <openmedia/audio/AudioMeter.h>
#include <openmedia/ipc/D3D11SharedTexturePoolPoC.h>

#include <memory>
#include <string>
#include <thread>
#include <atomic>
#include <functional>
#include <vector>

namespace openmedia::server {

/// @brief Configuration for creating a pipeline session
struct PipelineSessionConfig {
    std::string name;
    uint32_t width = 0;
    uint32_t height = 0;
    double fps = 0.0;
};

/// @brief Playback status
struct PipelinePlaybackState {
    double positionSec = 0.0;
    bool isPaused = false;
    bool isRunning = false;
};

/// @brief Pipeline performance metrics
struct PipelinePlaybackInfo {
    double fps = 60.0;
    double bitrate = 1024.0;
    uint64_t totalFrames = 1000;
    uint64_t droppedFrames = 0;
    double latencyMs = 5.0;
    double cpuUsage = 1.5;
};

/// @brief Media source metadata description
struct SourceMetadata {
    std::string url;
    double durationMs = 0.0;
    uint32_t width = 0;
    uint32_t height = 0;
    double fps = 0.0;
    std::string videoCodec;
    std::string audioCodec;
    int32_t audioChannels = 0;
    int32_t audioSampleRate = 0;
    uint64_t bitrate = 0;
};

/// @brief Represents an isolated, active media pipeline instance
class PipelineSession {
public:
    using TextureUpdateCallback = std::function<void(uint32_t width, uint32_t height)>;

    PipelineSession(uint32_t id, const PipelineSessionConfig& config,
                    ipc::D3D11SharedTexturePoolPoC* texturePool = nullptr,
                    TextureUpdateCallback onResolutionChanged = nullptr);
    ~PipelineSession();

    PipelineSession(const PipelineSession&) = delete;
    PipelineSession& operator=(const PipelineSession&) = delete;

    [[nodiscard]] uint32_t GetId() const { return m_id; }
    [[nodiscard]] const PipelineSessionConfig& GetConfig() const { return m_config; }

    // --- Lifecycle & Playback ---
    [[nodiscard]] core::VoidResult Start();
    [[nodiscard]] core::VoidResult Stop();
    [[nodiscard]] core::VoidResult Pause();
    [[nodiscard]] core::VoidResult Resume();
    void Seek(double targetSec);

    // --- Source Control ---
    [[nodiscard]] core::VoidResult OpenSource(const std::string& url, bool loop = false, int32_t startMs = 0);
    [[nodiscard]] core::VoidResult CloseSource();
    [[nodiscard]] SourceMetadata GetSourceInfo() const;

    // --- Audio / Video Properties & Delays ---
    void SetAVDelay(int32_t videoDelayMs, int32_t audioDelayMs, int32_t masterDelayMs);
    void SetLayerProperties(uint32_t layerIndex, bool muted, float volume);

    // --- Status & Monitoring ---
    [[nodiscard]] PipelinePlaybackState GetPlaybackState() const;
    [[nodiscard]] PipelinePlaybackInfo GetPlaybackInfo() const;
    [[nodiscard]] std::vector<OpenMedia::Audio::AudioMeterData> GetAudioLevels();

    [[nodiscard]] std::shared_ptr<core::PipelineGraph> GetGraph() const { return m_graph; }

    void SetTexturePool(ipc::D3D11SharedTexturePoolPoC* texturePool) { m_texturePool = texturePool; }

private:
    void RenderLoop();
    void StopRenderThread();

    uint32_t m_id;
    PipelineSessionConfig m_config;
    ipc::D3D11SharedTexturePoolPoC* m_texturePool;
    TextureUpdateCallback m_onResolutionChanged;

    std::shared_ptr<core::PipelineGraph> m_graph;
    std::shared_ptr<io::FileSource> m_source;

    std::thread m_renderThread;
    std::atomic<bool> m_running{false};
    std::atomic<bool> m_paused{false};
    std::atomic<bool> m_seekRequested{false};
    std::atomic<double> m_seekTargetSec{0.0};
    std::atomic<double> m_currentPositionSec{0.0};

    std::shared_ptr<audio::AudioPlayer> m_audioPlayer;
    OpenMedia::Audio::AudioMeter m_audioMeter;
    std::atomic<bool> m_audioMuted{false};
    std::atomic<float> m_audioVolume{1.0f};

    std::atomic<int32_t> m_videoDelayMs{0};
    std::atomic<int32_t> m_audioDelayMs{0};
    std::atomic<int32_t> m_masterDelayMs{0};
};

} // namespace openmedia::server
