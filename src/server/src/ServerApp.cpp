/// @file ServerApp.cpp
/// @brief OpenMediaServer.exe application implementation

#include <openmedia/server/ServerApp.h>
#include <openmedia/core/Logger.h>
#include <openmedia/core/Config.h>
#include <openmedia/core/PipelineGraph.h>
#include <openmedia/ipc/CommandMessage.h>
#include <openmedia/io/FileSource.h>
#include <openmedia/ipc/D3D11SharedTexturePoolPoC.h>
#include <openmedia/core/MediaFrame.h>
#include <openmedia/audio/AudioPlayer.h>
#include <openmedia/core/AVSyncClock.h>

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>

#include <nlohmann/json.hpp>
#include <fstream>

#include <atomic>
#include <chrono>
#include <condition_variable>
#include <mutex>
#include <thread>

namespace openmedia::server {

// Global signal for graceful shutdown
static std::atomic<ServerApp*> g_serverInstance{nullptr};

struct ServerApp::Impl {
    ServerConfig config;
    ServerState state = ServerState::Stopped;
    std::mutex stateMutex;

    // Core components
    std::unique_ptr<ipc::IPCServer> ipcServer;
    std::unique_ptr<command_dispatcher::CommandDispatcher> dispatcher;
    std::unique_ptr<worker_pool::WorkerPool> workerPool;

    // Lifecycle
    std::atomic<bool> shutdownRequested{false};
    std::condition_variable shutdownCV;
    std::mutex shutdownMutex;
    std::chrono::steady_clock::time_point startTime;

    // Watchdog
    std::thread watchdogThread;
    std::atomic<bool> watchdogRunning{false};

    // Pipelines
    uint32_t nextPipelineId = 1;
    std::unordered_map<uint32_t, std::shared_ptr<core::PipelineGraph>> pipelines;
    struct PipelineConfig {
        uint32_t width = 0;
        uint32_t height = 0;
        double fps = 0.0;
    };
    std::unordered_map<uint32_t, PipelineConfig> pipelineConfigs;

    // PoC: FileSource and render thread
    std::shared_ptr<io::FileSource> source;
    std::thread renderThread;
    std::atomic<bool> pipelineRunning{false};
    std::atomic<bool> pipelinePaused{false};
    std::shared_ptr<audio::AudioPlayer> audioPlayer;
    std::atomic<bool> audioMuted{false};
    std::atomic<float> audioVolume{1.0f};

    ~Impl() {
        if (pipelineRunning.exchange(false)) {
            if (renderThread.joinable()) {
                renderThread.join();
            }
        }
    }
};

ServerApp::ServerApp()
    : m_impl(std::make_unique<Impl>()) {}

ServerApp::~ServerApp() {
    Stop();
    g_serverInstance.store(nullptr);
}

core::VoidResult ServerApp::Initialize(const ServerConfig& config) {
    std::lock_guard lock(m_impl->stateMutex);
    if (m_impl->state != ServerState::Stopped) {
        return std::unexpected(core::Error{
            core::ErrorCode::InvalidState,
            "Server not in stopped state"});
    }

    m_impl->config = config;
    m_impl->state = ServerState::Starting;

    core::Logger::SInfo("ServerApp", "Initializing OpenMediaServer v{}...",
                       GetVersion());

    // Initialize Logger
    core::Logger::Initialize("OpenMediaServer");

    // Parse JSON config if provided
    if (!m_impl->config.configFile.empty()) {
        try {
            std::ifstream file(m_impl->config.configFile);
            if (file.is_open()) {
                nlohmann::json j;
                file >> j;
                
                if (j.contains("ipc") && j["ipc"].is_object()) {
                    if (j["ipc"].contains("pipeName")) {
                        m_impl->config.ipcConfig.pipeConfig.pipeName = j["ipc"]["pipeName"];
                    }
                }
                if (j.contains("workers") && j["workers"].is_object()) {
                    if (j["workers"].contains("threadCount")) {
                        m_impl->config.workerConfig.threadCount = j["workers"]["threadCount"];
                    }
                }
                if (j.contains("watchdogTimeoutMs")) {
                    m_impl->config.watchdogTimeoutMs = j["watchdogTimeoutMs"];
                }
                core::Logger::SInfo("ServerApp", "Loaded config from {}", m_impl->config.configFile);
            } else {
                core::Logger::SWarn("ServerApp", "Could not open config file: {}", m_impl->config.configFile);
            }
        } catch (const std::exception& e) {
            core::Logger::SError("ServerApp", "Failed to parse config file: {}", e.what());
        }
    }

    // Create components
    m_impl->ipcServer = std::make_unique<ipc::IPCServer>(config.ipcConfig);
    m_impl->dispatcher = std::make_unique<command_dispatcher::CommandDispatcher>();
    m_impl->workerPool = std::make_unique<worker_pool::WorkerPool>(config.workerConfig);

    // Register built-in handlers
    RegisterBuiltinHandlers();

    // Wire IPC Server to Command Dispatcher
    m_impl->ipcServer->SetDefaultHandler(
        [this](uint32_t clientId, ipc::CommandType type, const std::vector<uint8_t>& payload)
            -> core::Result<std::vector<uint8_t>> {
            return m_impl->dispatcher->Dispatch(clientId, type, payload);
        });

    // Setup signal handlers
    SetupSignalHandlers();

    core::Logger::SInfo("ServerApp", "Initialization complete");
    return {};
}

core::VoidResult ServerApp::Run() {
    // Start Worker Pool
    auto wpResult = m_impl->workerPool->Start();
    if (!wpResult) {
        m_impl->state = ServerState::Error;
        return std::unexpected(wpResult.error());
    }

    // Start IPC Server
    auto ipcResult = m_impl->ipcServer->Start();
    if (!ipcResult) {
        m_impl->workerPool->Stop();
        m_impl->state = ServerState::Error;
        return std::unexpected(ipcResult.error());
    }

    m_impl->state = ServerState::Running;
    m_impl->startTime = std::chrono::steady_clock::now();

    core::Logger::SInfo("ServerApp",
        "Server running. Workers={}, IPC={}",
        m_impl->workerPool->GetThreadCount(),
        m_impl->ipcServer->GetClientCount());

    // Start watchdog if enabled
    if (m_impl->config.enableWatchdog) {
        m_impl->watchdogRunning.store(true);
        m_impl->watchdogThread = std::thread([this] {
            while (m_impl->watchdogRunning.load()) {
                std::this_thread::sleep_for(
                    std::chrono::milliseconds(m_impl->config.watchdogTimeoutMs / 3));
                // Health check
                if (m_impl->state == ServerState::Running) {
                    auto stats = m_impl->dispatcher->GetStats();
                    core::Logger::SDebug("ServerApp",
                        "Watchdog: dispatched={}, success={}, errors={}, clients={}",
                        stats.totalDispatched, stats.totalSuccess,
                        stats.totalErrors, m_impl->ipcServer->GetClientCount());
                }
            }
        });
    }

    // Block until shutdown requested
    {
        std::unique_lock lock(m_impl->shutdownMutex);
        m_impl->shutdownCV.wait(lock, [this] {
            return m_impl->shutdownRequested.load();
        });
    }

    core::Logger::SInfo("ServerApp", "Shutdown requested, stopping...");

    // Graceful shutdown sequence
    m_impl->state = ServerState::Stopping;

    // 1. Stop watchdog
    m_impl->watchdogRunning.store(false);
    if (m_impl->watchdogThread.joinable()) {
        m_impl->watchdogThread.join();
    }

    // 2. Stop IPC Server (no new connections)
    m_impl->ipcServer->Stop();

    // 3. Stop Worker Pool (finish running tasks)
    m_impl->workerPool->Stop();

    m_impl->state = ServerState::Stopped;

    auto uptime = GetUptime();
    core::Logger::SInfo("ServerApp",
        "Server stopped. Uptime: {}s", uptime.count());

    return {};
}

void ServerApp::Stop() {
    RequestShutdown();
}

void ServerApp::RequestShutdown() {
    m_impl->shutdownRequested.store(true);
    m_impl->shutdownCV.notify_all();
}

ServerState ServerApp::GetState() const {
    return m_impl->state;
}

bool ServerApp::IsRunning() const {
    return m_impl->state == ServerState::Running;
}

ipc::IPCServer& ServerApp::GetIPCServer() {
    return *m_impl->ipcServer;
}

command_dispatcher::CommandDispatcher& ServerApp::GetDispatcher() {
    return *m_impl->dispatcher;
}

worker_pool::WorkerPool& ServerApp::GetWorkerPool() {
    return *m_impl->workerPool;
}

std::chrono::seconds ServerApp::GetUptime() const {
    if (m_impl->state != ServerState::Running &&
        m_impl->state != ServerState::Stopping) {
        return std::chrono::seconds(0);
    }
    return std::chrono::duration_cast<std::chrono::seconds>(
        std::chrono::steady_clock::now() - m_impl->startTime);
}

std::string ServerApp::GetVersion() {
    return "1.0.0-alpha";
}

void ServerApp::RegisterBuiltinHandlers() {
    auto& dispatcher = *m_impl->dispatcher;

    // System: GetStatus
    dispatcher.Register(ipc::CommandType::GetStatus,
        [this](uint32_t, const std::vector<uint8_t>&)
            -> core::Result<std::vector<uint8_t>> {
            // Return server state as JSON (simplified binary for now)
            uint32_t state = static_cast<uint32_t>(m_impl->state);
            std::vector<uint8_t> response(sizeof(uint32_t));
            std::memcpy(response.data(), &state, sizeof(uint32_t));
            return response;
        });

    // System: Handshake
    dispatcher.Register(ipc::CommandType::Handshake,
        [this](uint32_t clientId, const std::vector<uint8_t>& payload)
            -> core::Result<std::vector<uint8_t>> {
            ipc::MessageReader reader(payload);
            std::string sdkVersion = reader.ReadString();
            core::Logger::SInfo("ServerApp", "Handshake from client {} (SDK {})", clientId, sdkVersion);
            
            ipc::MessageBuilder builder;
            builder.WriteString("OK");
            return builder.Finish();
        });

    // System: Heartbeat
    dispatcher.Register(ipc::CommandType::Heartbeat,
        [](uint32_t, const std::vector<uint8_t>&)
            -> core::Result<std::vector<uint8_t>> {
            return std::vector<uint8_t>{};
        });

    // System: Shutdown
    dispatcher.Register(ipc::CommandType::Shutdown,
        [this](uint32_t clientId, const std::vector<uint8_t>&)
            -> core::Result<std::vector<uint8_t>> {
            core::Logger::SInfo("ServerApp",
                "Shutdown requested by client {}", clientId);
            RequestShutdown();
            return std::vector<uint8_t>{};
        });

    // System: GetMetrics
    dispatcher.Register(ipc::CommandType::GetMetrics,
        [this](uint32_t, const std::vector<uint8_t>&)
            -> core::Result<std::vector<uint8_t>> {
            auto wpStats = m_impl->workerPool->GetStats();
            auto dpStats = m_impl->dispatcher->GetStats();
            // Pack stats into response
            struct MetricsResponse {
                uint64_t uptime;
                uint64_t tasksSubmitted;
                uint64_t tasksCompleted;
                uint64_t commandsDispatched;
                uint64_t commandsSuccess;
                uint32_t clientCount;
                uint32_t workerThreads;
            };
            MetricsResponse metrics{};
            metrics.uptime = static_cast<uint64_t>(GetUptime().count());
            metrics.tasksSubmitted = wpStats.totalSubmitted;
            metrics.tasksCompleted = wpStats.totalCompleted;
            metrics.commandsDispatched = dpStats.totalDispatched;
            metrics.commandsSuccess = dpStats.totalSuccess;
            metrics.clientCount = m_impl->ipcServer->GetClientCount();
            metrics.workerThreads = m_impl->workerPool->GetThreadCount();

            std::vector<uint8_t> response(sizeof(MetricsResponse));
            std::memcpy(response.data(), &metrics, sizeof(MetricsResponse));
            return response;
        });

    // Pipeline: CreatePipeline
    dispatcher.Register(ipc::CommandType::CreatePipeline,
        [this](uint32_t, const std::vector<uint8_t>& payload)
            -> core::Result<std::vector<uint8_t>> {
            ipc::MessageReader reader(payload);
            std::string name = reader.ReadString();
            uint32_t width = reader.ReadU32();
            uint32_t height = reader.ReadU32();
            double fps = reader.ReadF64();

            if (reader.HasError()) {
                return std::unexpected(core::Error{core::ErrorCode::InvalidArgument, "Invalid payload"});
            }

            auto graph = std::make_shared<core::PipelineGraph>();
            if (auto buildRes = graph->Build(); !buildRes) {
                return std::unexpected(buildRes.error());
            }

            std::lock_guard lock(m_impl->stateMutex);
            uint32_t id = m_impl->nextPipelineId++;
            m_impl->pipelines[id] = graph;
            m_impl->pipelineConfigs[id] = {width, height, fps};

            std::vector<uint8_t> response(sizeof(uint32_t));
            std::memcpy(response.data(), &id, sizeof(uint32_t));
            return response;
        });

    // Pipeline: StartPipeline
    dispatcher.Register(ipc::CommandType::StartPipeline,
        [this](uint32_t, const std::vector<uint8_t>& payload)
            -> core::Result<std::vector<uint8_t>> {
            if (payload.size() < sizeof(uint32_t)) {
                return std::unexpected(core::Error{core::ErrorCode::InvalidArgument, "Missing pipeline ID"});
            }
            uint32_t id = 0;
            std::memcpy(&id, payload.data(), sizeof(uint32_t));

            std::shared_ptr<core::PipelineGraph> graph;
            {
                std::lock_guard lock(m_impl->stateMutex);
                auto it = m_impl->pipelines.find(id);
                if (it == m_impl->pipelines.end()) {
                    return std::unexpected(core::Error{core::ErrorCode::NotFound, "Pipeline not found"});
                }
                graph = it->second;
            }

            if (auto res = graph->Start(); !res) {
                return std::unexpected(res.error());
            }

            if (m_impl->pipelinePaused.exchange(false)) {
                if (m_impl->audioPlayer) {
                    m_impl->audioPlayer->Resume();
                }
            }

            // PoC: start render thread
            if (!m_impl->pipelineRunning.exchange(true) && m_impl->source) {
                m_impl->renderThread = std::thread([this, graph]() {
                    m_impl->source->Start();

                    // FileSource unconditionally resamples to 48000 Hz, 2 channels, Float32
                    int audioSampleRate = 48000;
                    int audioChannels = 2;

                    m_impl->audioPlayer = std::make_shared<audio::AudioPlayer>();
                    m_impl->audioPlayer->SetMuted(m_impl->audioMuted);
                    m_impl->audioPlayer->SetVolume(m_impl->audioVolume);
                    if (!m_impl->audioPlayer->Initialize(audioSampleRate, audioChannels)) {
                        core::Logger::SError("ServerApp", "Failed to initialize AudioPlayer on the server");
                    }

                    core::AVSyncClock syncClock;
                    std::shared_ptr<core::MediaFrame> pendingVideoFrame = nullptr;
                    size_t bufferIndex = 0;

                    while (m_impl->pipelineRunning) {
                        if (m_impl->pipelinePaused.load()) {
                            std::this_thread::sleep_for(std::chrono::milliseconds(10));
                            continue;
                        }

                        // 1. Audio pacing - target 250ms audio queue in XAudio2 to prevent buffer overflow/underflow
                        constexpr double TARGET_AUDIO_QUEUE_SEC = 0.250;
                        if (!m_impl->audioPlayer || m_impl->audioPlayer->GetQueuedDurationSeconds() < TARGET_AUDIO_QUEUE_SEC) {
                            auto audioResult = m_impl->source->PullAudioFrame();
                            if (audioResult && *audioResult) {
                                auto audioFrame = *audioResult;
                                if (m_impl->audioPlayer) {
                                    m_impl->audioPlayer->SetMuted(m_impl->audioMuted);
                                    m_impl->audioPlayer->PlayFrame(audioFrame);
                                }
                            } else if (audioResult.error().code == core::ErrorCode::EndOfStream) {
                                if (m_impl->source->GetLoopMode()) {
                                    m_impl->source->Seek(0.0);
                                    if (m_impl->audioPlayer) {
                                        m_impl->audioPlayer->Stop();
                                        m_impl->audioPlayer->Initialize(audioSampleRate, audioChannels);
                                    }
                                    syncClock.Reset();
                                } else {
                                    // Do not break immediately, video might still have frames
                                }
                            }
                        }

                        // Update audio clock from hardware
                        if (m_impl->audioPlayer) {
                            syncClock.UpdateAudioClock(m_impl->audioPlayer->GetPlaybackPositionSeconds());
                        }

                        // 2. Video pacing & shared texture synchronization
                        if (!pendingVideoFrame) {
                            auto frameRes = m_impl->source->PullVideoFrame();
                            if (frameRes && *frameRes) {
                                pendingVideoFrame = *frameRes;
                            } else if (m_impl->source->GetLoopMode()) {
                                // Loop mode handled primarily in audio branch, but just in case
                                m_impl->source->Seek(0.0);
                                auto retryRes = m_impl->source->PullVideoFrame();
                                if (retryRes && *retryRes) {
                                    pendingVideoFrame = *retryRes;
                                }
                            } else if (!frameRes && frameRes.error().code == core::ErrorCode::EndOfStream) {
                                break;
                            }
                        }

                        if (pendingVideoFrame) {
                            auto action = syncClock.EvaluateVideoFrame(*pendingVideoFrame);
                            switch (action) {
                                case core::AVSyncClock::VideoAction::Display:
                                    if (auto* pool = m_impl->ipcServer->GetSharedTexturePool()) {
                                        // Acquire Key 0 lock from client/initial state
                                        if (pool->AcquireWriteLock(bufferIndex, 100)) {
                                            pool->UpdateFrame(bufferIndex, pendingVideoFrame->GetVideoPlane(0), static_cast<uint32_t>(pendingVideoFrame->GetLineSize(0)));
                                            pool->ReleaseWriteLock(bufferIndex);
                                            bufferIndex = (bufferIndex + 1) % 2;
                                            pendingVideoFrame = nullptr;
                                        }
                                        else {
                                            // Client has not released buffer yet; hold frame
                                        }
                                    }
                                    break;
                                case core::AVSyncClock::VideoAction::Drop:
                                    pendingVideoFrame = nullptr;  // skip, pull next
                                    break;
                                case core::AVSyncClock::VideoAction::Wait:
                                    break;  // hold frame, try again next iteration
                            }
                        }

                        std::this_thread::sleep_for(std::chrono::milliseconds(2));
                    }

                    if (m_impl->source) {
                        m_impl->source->Stop();
                    }
                    if (m_impl->audioPlayer) {
                        m_impl->audioPlayer->Stop();
                        m_impl->audioPlayer.reset();
                    }
                });
            }

            return std::vector<uint8_t>{};
        });

    // Mixer: SetLayerProperties
    dispatcher.Register(ipc::CommandType::SetLayerProperties,
        [this](uint32_t, const std::vector<uint8_t>& payload)
            -> core::Result<std::vector<uint8_t>> {
            ipc::MessageReader reader(payload);
            uint32_t pipelineId = reader.ReadU32();
            uint32_t layerIndex = reader.ReadU32();
            bool muted = reader.ReadBool();
            if (reader.HasError()) {
                return std::unexpected(core::Error{core::ErrorCode::InvalidArgument, "Invalid payload"});
            }

            m_impl->audioMuted = muted;
            if (reader.HasMore() && reader.Remaining() >= sizeof(double)) {
                double vol = reader.ReadF64();
                m_impl->audioVolume = static_cast<float>(vol);
            }

            if (m_impl->audioPlayer) {
                m_impl->audioPlayer->SetMuted(m_impl->audioMuted);
                m_impl->audioPlayer->SetVolume(m_impl->audioVolume);
            }

            core::Logger::SInfo("ServerApp", "SetLayerProperties: pipeline {} layer {} muted={} volume={}",
                pipelineId, layerIndex, muted, m_impl->audioVolume.load());
            return std::vector<uint8_t>{};
        });

    // Pipeline: StopPipeline
    dispatcher.Register(ipc::CommandType::StopPipeline,
        [this](uint32_t, const std::vector<uint8_t>& payload)
            -> core::Result<std::vector<uint8_t>> {
            if (payload.size() < sizeof(uint32_t)) {
                return std::unexpected(core::Error{core::ErrorCode::InvalidArgument, "Missing pipeline ID"});
            }
            uint32_t id = 0;
            std::memcpy(&id, payload.data(), sizeof(uint32_t));

            std::shared_ptr<core::PipelineGraph> graph;
            {
                std::lock_guard lock(m_impl->stateMutex);
                auto it = m_impl->pipelines.find(id);
                if (it == m_impl->pipelines.end()) {
                    return std::unexpected(core::Error{core::ErrorCode::NotFound, "Pipeline not found"});
                }
                graph = it->second;
            }

            if (auto res = graph->Stop(); !res) {
                return std::unexpected(res.error());
            }

            m_impl->pipelinePaused.store(false);

            // PoC: stop render thread cleanly
            if (m_impl->pipelineRunning.exchange(false)) {
                if (m_impl->renderThread.joinable()) {
                    m_impl->renderThread.join();
                }
            }

            if (m_impl->source) {
                m_impl->source->Seek(0.0);
            }

            if (m_impl->audioPlayer) {
                m_impl->audioPlayer->Stop();
            }

            return std::vector<uint8_t>{};
        });

    // Pipeline: DestroyPipeline
    dispatcher.Register(ipc::CommandType::DestroyPipeline,
        [this](uint32_t, const std::vector<uint8_t>& payload)
            -> core::Result<std::vector<uint8_t>> {
            if (payload.size() < sizeof(uint32_t)) {
                return std::unexpected(core::Error{core::ErrorCode::InvalidArgument, "Missing pipeline ID"});
            }
            uint32_t id = 0;
            std::memcpy(&id, payload.data(), sizeof(uint32_t));

            std::shared_ptr<core::PipelineGraph> graph;
            {
                std::lock_guard lock(m_impl->stateMutex);
                auto it = m_impl->pipelines.find(id);
                if (it == m_impl->pipelines.end()) {
                    return std::unexpected(core::Error{core::ErrorCode::NotFound, "Pipeline not found"});
                }
                graph = it->second;
                m_impl->pipelines.erase(it);
            }

            std::ignore = graph->Stop();

            if (m_impl->pipelineRunning.exchange(false)) {
                if (m_impl->renderThread.joinable()) {
                    m_impl->renderThread.join();
                }
            }

            if (m_impl->source) {
                m_impl->source->Stop();
            }

            if (m_impl->audioPlayer) {
                m_impl->audioPlayer->Stop();
            }

            return std::vector<uint8_t>{};
        });

    // Pipeline: GetPipelineState
    dispatcher.Register(ipc::CommandType::GetPipelineState,
        [this](uint32_t, const std::vector<uint8_t>& payload)
            -> core::Result<std::vector<uint8_t>> {
            ipc::MessageReader reader(payload);
            uint32_t id = reader.ReadU32();
            (void)id;
            if (reader.HasError()) return std::unexpected(core::Error{core::ErrorCode::InvalidArgument, "Invalid payload"});
            
            // Just return success for now
            return std::vector<uint8_t>{};
        });

    // Pipeline: PausePipeline
    dispatcher.Register(ipc::CommandType::PausePipeline,
        [this](uint32_t, const std::vector<uint8_t>& payload)
            -> core::Result<std::vector<uint8_t>> {
            ipc::MessageReader reader(payload);
            uint32_t id = reader.ReadU32();
            (void)id;
            if (reader.HasError()) return std::unexpected(core::Error{core::ErrorCode::InvalidArgument, "Invalid payload"});
            
            m_impl->pipelinePaused.store(true);
            if (m_impl->audioPlayer) {
                m_impl->audioPlayer->Pause();
            }
            core::Logger::SInfo("ServerApp", "Pipeline paused");
            return std::vector<uint8_t>{};
        });

    // Pipeline: ResumePipeline
    dispatcher.Register(ipc::CommandType::ResumePipeline,
        [this](uint32_t, const std::vector<uint8_t>& payload)
            -> core::Result<std::vector<uint8_t>> {
            ipc::MessageReader reader(payload);
            uint32_t id = reader.ReadU32();
            (void)id;
            if (reader.HasError()) return std::unexpected(core::Error{core::ErrorCode::InvalidArgument, "Invalid payload"});
            
            if (m_impl->pipelinePaused.exchange(false)) {
                if (m_impl->audioPlayer) {
                    m_impl->audioPlayer->Resume();
                }
            }
            core::Logger::SInfo("ServerApp", "Pipeline resumed");
            return std::vector<uint8_t>{};
        });

    // Pipeline: GetPipelineInfo
    dispatcher.Register(ipc::CommandType::GetPipelineInfo,
        [this](uint32_t, const std::vector<uint8_t>& payload)
            -> core::Result<std::vector<uint8_t>> {
            ipc::MessageReader reader(payload);
            uint32_t id = reader.ReadU32();
            (void)id;
            if (reader.HasError()) return std::unexpected(core::Error{core::ErrorCode::InvalidArgument, "Invalid payload"});
            
            ipc::MessageBuilder builder;
            builder.WriteF64(60.0); // fps
            builder.WriteF64(1024.0); // bitrate
            builder.WriteU64(1000); // total frames
            builder.WriteU64(0); // dropped frames
            builder.WriteF64(5.0); // latency
            builder.WriteF64(1.5); // cpu usage
            
            return builder.Finish();
        });

    // Source: OpenSource
    dispatcher.Register(ipc::CommandType::OpenSource,
        [this](uint32_t, const std::vector<uint8_t>& payload)
            -> core::Result<std::vector<uint8_t>> {
            ipc::MessageReader reader(payload);
            uint32_t pipelineId = reader.ReadU32();
            uint32_t sourceId = reader.ReadU32();
            std::string url = reader.ReadString();
            bool loop = reader.ReadBool();
            int32_t startMs = reader.ReadI32();
            (void)loop; (void)startMs;
            if (reader.HasError()) return std::unexpected(core::Error{core::ErrorCode::InvalidArgument, "Invalid payload"});

            auto configIt = m_impl->pipelineConfigs.find(pipelineId);
            uint32_t targetWidth = 0;
            uint32_t targetHeight = 0;
            if (configIt != m_impl->pipelineConfigs.end()) {
                targetWidth = configIt->second.width;
                targetHeight = configIt->second.height;
            }

            m_impl->source = std::make_shared<io::FileSource>();
            if (targetWidth > 0 && targetHeight > 0) {
                m_impl->source->SetOutputResolution(targetWidth, targetHeight);
            }
            
            if (auto res = m_impl->source->Open(url); !res) {
                return std::unexpected(res.error());
            }
            m_impl->source->SetLoopMode(loop);
            if (startMs > 0) {
                m_impl->source->Seek(static_cast<double>(startMs) / 1000.0);
            }

            // Read actual video dimensions and set for shared texture allocation
            auto streams = m_impl->source->GetStreams();
            for (const auto& stream : streams) {
                if (stream.type == core::MediaType::Video && stream.width > 0 && stream.height > 0) {
                    uint32_t resWidth = (targetWidth > 0) ? targetWidth : stream.width;
                    uint32_t resHeight = (targetHeight > 0) ? targetHeight : stream.height;
                    m_impl->ipcServer->SetVideoResolution(resWidth, resHeight);
                    core::Logger::SInfo("ServerApp", "Video resolution: {}x{} (Scaled to {}x{}) @ {:.1f} FPS",
                                       stream.width, stream.height, resWidth, resHeight, stream.frameRate);
                    break;
                }
            }

            core::Logger::SInfo("ServerApp", "OpenSource P:{} S:{} URL:{} Loop:{}", pipelineId, sourceId, url, loop);
            return std::vector<uint8_t>{};
        });

    // Source: CloseSource
    dispatcher.Register(ipc::CommandType::CloseSource,
        [this](uint32_t, const std::vector<uint8_t>& payload)
            -> core::Result<std::vector<uint8_t>> {
            ipc::MessageReader reader(payload);
            uint32_t pipelineId = reader.ReadU32();
            uint32_t sourceId = reader.ReadU32();
            (void)pipelineId; (void)sourceId;
            if (reader.HasError()) return std::unexpected(core::Error{core::ErrorCode::InvalidArgument, "Invalid payload"});
            
            return std::vector<uint8_t>{};
        });

    // Source: SeekSource
    dispatcher.Register(ipc::CommandType::SeekSource,
        [this](uint32_t, const std::vector<uint8_t>& payload)
            -> core::Result<std::vector<uint8_t>> {
            ipc::MessageReader reader(payload);
            uint32_t pipelineId = reader.ReadU32();
            uint32_t sourceId = reader.ReadU32();
            uint64_t pos = reader.ReadU64();
            (void)pipelineId; (void)sourceId; (void)pos;
            if (reader.HasError()) return std::unexpected(core::Error{core::ErrorCode::InvalidArgument, "Invalid payload"});
            
            return std::vector<uint8_t>{};
        });

    // Source: GetSourceInfo
    dispatcher.Register(ipc::CommandType::GetSourceInfo,
        [this](uint32_t, const std::vector<uint8_t>& payload)
            -> core::Result<std::vector<uint8_t>> {
            ipc::MessageReader reader(payload);
            uint32_t pipelineId = reader.ReadU32();
            uint32_t sourceId = reader.ReadU32();
            (void)pipelineId; (void)sourceId;
            if (reader.HasError()) return std::unexpected(core::Error{core::ErrorCode::InvalidArgument, "Invalid payload"});
            
            ipc::MessageBuilder builder;

            // Return real metadata from the active FileSource
            if (m_impl->source) {
                auto streams = m_impl->source->GetStreams();
                uint32_t vWidth = 0, vHeight = 0;
                double vFps = 0.0;
                std::string vCodec, aCodec;
                int32_t aChannels = 0, aSampleRate = 0;
                for (const auto& stream : streams) {
                    if (stream.type == core::MediaType::Video && vWidth == 0) {
                        vWidth = stream.width;
                        vHeight = stream.height;
                        vFps = stream.frameRate;
                        vCodec = stream.codecName;
                    }
                    if (stream.type == core::MediaType::Audio && aChannels == 0) {
                        aCodec = stream.codecName;
                        aChannels = static_cast<int32_t>(stream.channels);
                        aSampleRate = static_cast<int32_t>(stream.sampleRate);
                    }
                }
                builder.WriteString(""); // url (not stored separately)
                builder.WriteF64(m_impl->source->GetDurationSeconds() * 1000.0);
                builder.WriteU32(vWidth);
                builder.WriteU32(vHeight);
                builder.WriteF64(vFps);
                builder.WriteString(vCodec);
                builder.WriteString(aCodec);
                builder.WriteI32(aChannels);
                builder.WriteI32(aSampleRate);
                builder.WriteU64(static_cast<uint64_t>(m_impl->source->GetBitrate()));
            } else {
                builder.WriteString("");
                builder.WriteF64(0.0);
                builder.WriteU32(0);
                builder.WriteU32(0);
                builder.WriteF64(0.0);
                builder.WriteString("");
                builder.WriteString("");
                builder.WriteI32(0);
                builder.WriteI32(0);
                builder.WriteU64(0);
            }

            return builder.Finish();
        });

    // Output: AddOutput
    dispatcher.Register(ipc::CommandType::AddOutput,
        [this](uint32_t, const std::vector<uint8_t>& payload)
            -> core::Result<std::vector<uint8_t>> {
            ipc::MessageReader reader(payload);
            uint32_t pipelineId = reader.ReadU32();
            uint32_t outputId = reader.ReadU32();
            std::string url = reader.ReadString();
            std::string format = reader.ReadString();
            std::string codec = reader.ReadString();
            uint32_t bitrate = reader.ReadU32();
            std::string preset = reader.ReadString();
            bool hwAccel = reader.ReadBool();
            (void)format; (void)codec; (void)bitrate; (void)preset; (void)hwAccel;
            if (reader.HasError()) return std::unexpected(core::Error{core::ErrorCode::InvalidArgument, "Invalid payload"});
            
            core::Logger::SInfo("ServerApp", "AddOutput P:{} O:{} URL:{}", pipelineId, outputId, url);
            return std::vector<uint8_t>{};
        });

    // Output: RemoveOutput
    dispatcher.Register(ipc::CommandType::RemoveOutput,
        [this](uint32_t, const std::vector<uint8_t>& payload)
            -> core::Result<std::vector<uint8_t>> {
            ipc::MessageReader reader(payload);
            uint32_t pipelineId = reader.ReadU32();
            uint32_t outputId = reader.ReadU32();
            (void)pipelineId; (void)outputId;
            if (reader.HasError()) return std::unexpected(core::Error{core::ErrorCode::InvalidArgument, "Invalid payload"});
            
            return std::vector<uint8_t>{};
        });

    core::Logger::SInfo("ServerApp", "Registered {} built-in handlers",
                       dispatcher.GetHandlerCount());
}

void ServerApp::SetupSignalHandlers() {
    g_serverInstance.store(this);

    SetConsoleCtrlHandler([](DWORD ctrlType) -> BOOL {
        if (ctrlType == CTRL_C_EVENT || ctrlType == CTRL_BREAK_EVENT ||
            ctrlType == CTRL_CLOSE_EVENT) {
            auto* server = g_serverInstance.load();
            if (server) {
                server->RequestShutdown();
            }
            return TRUE;
        }
        return FALSE;
    }, TRUE);
}

} // namespace openmedia::server
