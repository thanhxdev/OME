/// @file ServerApp.cpp
/// @brief OpenMediaServer.exe application implementation (V2 Refactored)
/// @since 2.0.0

#include <openmedia/server/ServerApp.h>
#include <openmedia/server/PipelineEngineManager.h>
#include <openmedia/core/Logger.h>
#include <openmedia/core/Config.h>
#include <openmedia/ipc/CommandMessage.h>
#include <openmedia/ipc/D3D11SharedTexturePoolPoC.h>
#include <openmedia/audio/AudioMeter.h>

#include <vector>

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
    std::unique_ptr<PipelineEngineManager> pipelineManager;

    // Lifecycle
    std::atomic<bool> shutdownRequested{false};
    std::condition_variable shutdownCV;
    std::mutex shutdownMutex;
    std::chrono::steady_clock::time_point startTime;

    // Watchdog
    std::thread watchdogThread;
    std::atomic<bool> watchdogRunning{false};

    ~Impl() {
        if (pipelineManager) {
            pipelineManager->StopAll();
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
    m_impl->pipelineManager = std::make_unique<PipelineEngineManager>();

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
                        "Watchdog: dispatched={}, success={}, errors={}, clients={}, activePipelines={}",
                        stats.totalDispatched, stats.totalSuccess,
                        stats.totalErrors, m_impl->ipcServer->GetClientCount(),
                        m_impl->pipelineManager->GetActivePipelineCount());
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

    // 2. Stop all pipeline sessions
    m_impl->pipelineManager->StopAll();

    // 3. Stop IPC Server (no new connections)
    m_impl->ipcServer->Stop();

    // 4. Stop Worker Pool (finish running tasks)
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

PipelineEngineManager& ServerApp::GetPipelineManager() {
    return *m_impl->pipelineManager;
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
    return "2.0.0-alpha";
}

void ServerApp::RegisterBuiltinHandlers() {
    auto& dispatcher = *m_impl->dispatcher;

    // System: GetStatus
    dispatcher.Register(ipc::CommandType::GetStatus,
        [](uint32_t, const std::vector<uint8_t>&)
            -> core::Result<std::vector<uint8_t>> {
            ipc::MessageBuilder builder;
            builder.WriteString(GetVersion());
            return builder.Finish();
        });

    // System: Handshake
    dispatcher.Register(ipc::CommandType::Handshake,
        [](uint32_t clientId, const std::vector<uint8_t>& payload)
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
            core::Logger::SInfo("ServerApp", "Shutdown requested by client {}", clientId);
            RequestShutdown();
            return std::vector<uint8_t>{};
        });

    // System: GetMetrics
    dispatcher.Register(ipc::CommandType::GetMetrics,
        [this](uint32_t, const std::vector<uint8_t>&)
            -> core::Result<std::vector<uint8_t>> {
            auto wpStats = m_impl->workerPool->GetStats();
            auto dpStats = m_impl->dispatcher->GetStats();

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

            PipelineSessionConfig cfg{name, width, height, fps};
            auto idRes = m_impl->pipelineManager->CreatePipeline(
                cfg,
                m_impl->ipcServer->GetSharedTexturePool(),
                [this](uint32_t w, uint32_t h) {
                    m_impl->ipcServer->SetVideoResolution(w, h);
                    m_impl->pipelineManager->UpdateTexturePool(m_impl->ipcServer->GetSharedTexturePool());
                });

            if (!idRes) {
                return std::unexpected(idRes.error());
            }

            uint32_t id = *idRes;
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

            auto session = m_impl->pipelineManager->GetSession(id);
            if (!session) {
                return std::unexpected(core::Error{core::ErrorCode::NotFound, "Pipeline not found"});
            }

            if (auto res = session->Start(); !res) {
                return std::unexpected(res.error());
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

            float volume = 1.0f;
            if (reader.HasMore() && reader.Remaining() >= sizeof(double)) {
                volume = static_cast<float>(reader.ReadF64());
            }

            auto session = m_impl->pipelineManager->GetSession(pipelineId);
            if (session) {
                session->SetLayerProperties(layerIndex, muted, volume);
            }

            return std::vector<uint8_t>{};
        });

    // Pipeline: SetAVDelay
    dispatcher.Register(ipc::CommandType::SetAVDelay,
        [this](uint32_t, const std::vector<uint8_t>& payload)
            -> core::Result<std::vector<uint8_t>> {
            ipc::MessageReader reader(payload);
            uint32_t pipelineId = reader.ReadU32();
            int32_t videoDelay = reader.ReadI32();
            int32_t audioDelay = reader.ReadI32();
            int32_t masterDelay = reader.ReadI32();
            
            if (reader.HasError()) {
                return std::unexpected(core::Error{core::ErrorCode::InvalidArgument, "Invalid payload for SetAVDelay"});
            }

            auto session = m_impl->pipelineManager->GetSession(pipelineId);
            if (session) {
                session->SetAVDelay(videoDelay, audioDelay, masterDelay);
            }

            return std::vector<uint8_t>{};
        });

    // Pipeline: SetVideoPTSOffset
    dispatcher.Register(ipc::CommandType::SetVideoPTSOffset,
        [this](uint32_t, const std::vector<uint8_t>& payload)
            -> core::Result<std::vector<uint8_t>> {
            ipc::MessageReader reader(payload);
            uint32_t pipelineId = reader.ReadU32();
            int32_t offsetMs = reader.ReadI32();
            
            if (reader.HasError()) {
                return std::unexpected(core::Error{core::ErrorCode::InvalidArgument, "Invalid payload for SetVideoPTSOffset"});
            }

            auto session = m_impl->pipelineManager->GetSession(pipelineId);
            if (session) {
                session->SetAVDelay(offsetMs, 0, 0);
            }

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

            auto session = m_impl->pipelineManager->GetSession(id);
            if (!session) {
                return std::unexpected(core::Error{core::ErrorCode::NotFound, "Pipeline not found"});
            }

            return session->Stop().transform([]{ return std::vector<uint8_t>{}; });
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

            return m_impl->pipelineManager->DestroyPipeline(id).transform([]{ return std::vector<uint8_t>{}; });
        });

    // Pipeline: GetPipelineState
    dispatcher.Register(ipc::CommandType::GetPipelineState,
        [this](uint32_t, const std::vector<uint8_t>& payload)
            -> core::Result<std::vector<uint8_t>> {
            uint32_t id = 1;
            if (!payload.empty()) {
                ipc::MessageReader reader(payload);
                id = reader.ReadU32();
                if (reader.HasError()) return std::unexpected(core::Error{core::ErrorCode::InvalidArgument, "Invalid payload"});
            }
            
            auto session = m_impl->pipelineManager->GetSession(id);
            PipelinePlaybackState state{};
            if (session) {
                state = session->GetPlaybackState();
            }

            ipc::MessageBuilder builder;
            builder.WriteF64(state.positionSec * 1000.0);
            builder.WriteBool(state.isPaused);
            return builder.Finish();
        });

    // Pipeline: PausePipeline
    dispatcher.Register(ipc::CommandType::PausePipeline,
        [this](uint32_t, const std::vector<uint8_t>& payload)
            -> core::Result<std::vector<uint8_t>> {
            uint32_t id = 1;
            if (!payload.empty()) {
                ipc::MessageReader reader(payload);
                id = reader.ReadU32();
                if (reader.HasError()) return std::unexpected(core::Error{core::ErrorCode::InvalidArgument, "Invalid payload"});
            }
            
            auto session = m_impl->pipelineManager->GetSession(id);
            if (!session) return std::unexpected(core::Error{core::ErrorCode::NotFound, "Pipeline not found"});

            return session->Pause().transform([]{ return std::vector<uint8_t>{}; });
        });

    // Pipeline: ResumePipeline
    dispatcher.Register(ipc::CommandType::ResumePipeline,
        [this](uint32_t, const std::vector<uint8_t>& payload)
            -> core::Result<std::vector<uint8_t>> {
            uint32_t id = 1;
            if (!payload.empty()) {
                ipc::MessageReader reader(payload);
                id = reader.ReadU32();
                if (reader.HasError()) return std::unexpected(core::Error{core::ErrorCode::InvalidArgument, "Invalid payload"});
            }
            
            auto session = m_impl->pipelineManager->GetSession(id);
            if (!session) return std::unexpected(core::Error{core::ErrorCode::NotFound, "Pipeline not found"});

            return session->Resume().transform([]{ return std::vector<uint8_t>{}; });
        });

    // Pipeline: GetPipelineInfo
    dispatcher.Register(ipc::CommandType::GetPipelineInfo,
        [this](uint32_t, const std::vector<uint8_t>& payload)
            -> core::Result<std::vector<uint8_t>> {
            uint32_t id = 1;
            if (!payload.empty()) {
                ipc::MessageReader reader(payload);
                id = reader.ReadU32();
                if (reader.HasError()) return std::unexpected(core::Error{core::ErrorCode::InvalidArgument, "Invalid payload"});
            }
            
            auto session = m_impl->pipelineManager->GetSession(id);
            PipelinePlaybackInfo info{};
            if (session) {
                info = session->GetPlaybackInfo();
            }

            ipc::MessageBuilder builder;
            builder.WriteF64(info.fps);
            builder.WriteF64(info.bitrate);
            builder.WriteU64(info.totalFrames);
            builder.WriteU64(info.droppedFrames);
            builder.WriteF64(info.latencyMs);
            builder.WriteF64(info.cpuUsage);
            return builder.Finish();
        });

    // Pipeline: GetAudioLevels
    dispatcher.Register(ipc::CommandType::GetAudioLevels,
        [this](uint32_t, const std::vector<uint8_t>& payload)
            -> core::Result<std::vector<uint8_t>> {
            uint32_t id = 1;
            if (!payload.empty()) {
                ipc::MessageReader reader(payload);
                id = reader.ReadU32();
                if (reader.HasError()) return std::unexpected(core::Error{core::ErrorCode::InvalidArgument, "Invalid payload"});
            }
            
            auto session = m_impl->pipelineManager->GetSession(id);
            std::vector<OpenMedia::Audio::AudioMeterData> channelData;
            if (session) {
                channelData = session->GetAudioLevels();
            }

            ipc::MessageBuilder builder;
            uint32_t channelCount = static_cast<uint32_t>(channelData.size());
            builder.WriteU32(channelCount);
            for (uint32_t i = 0; i < channelCount; ++i) {
                builder.WriteF32(channelData[i].peak_db);
                builder.WriteF32(channelData[i].rms_db);
                builder.WriteF32(channelData[i].lufs);
                builder.WriteBool(channelData[i].clipping);
                builder.WriteF32(channelData[i].peak_db);
            }
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
            (void)sourceId;
            if (reader.HasError()) return std::unexpected(core::Error{core::ErrorCode::InvalidArgument, "Invalid payload"});

            auto session = m_impl->pipelineManager->GetSession(pipelineId);
            if (!session) {
                return std::unexpected(core::Error{core::ErrorCode::NotFound, "Pipeline not found"});
            }

            return session->OpenSource(url, loop, startMs).transform([]{ return std::vector<uint8_t>{}; });
        });

    // Source: CloseSource
    dispatcher.Register(ipc::CommandType::CloseSource,
        [this](uint32_t, const std::vector<uint8_t>& payload)
            -> core::Result<std::vector<uint8_t>> {
            ipc::MessageReader reader(payload);
            uint32_t pipelineId = reader.ReadU32();
            uint32_t sourceId = reader.ReadU32();
            (void)sourceId;
            if (reader.HasError()) return std::unexpected(core::Error{core::ErrorCode::InvalidArgument, "Invalid payload"});
            
            auto session = m_impl->pipelineManager->GetSession(pipelineId);
            if (session) {
                return session->CloseSource().transform([]{ return std::vector<uint8_t>{}; });
            }
            return std::vector<uint8_t>{};
        });

    // Source: SeekSource
    dispatcher.Register(ipc::CommandType::SeekSource,
        [this](uint32_t, const std::vector<uint8_t>& payload)
            -> core::Result<std::vector<uint8_t>> {
            ipc::MessageReader reader(payload);
            uint32_t pipelineId = reader.ReadU32();
            uint32_t sourceId = reader.ReadU32();
            uint64_t posMs = reader.ReadU64();
            (void)sourceId;
            if (reader.HasError()) return std::unexpected(core::Error{core::ErrorCode::InvalidArgument, "Invalid payload"});
            
            auto session = m_impl->pipelineManager->GetSession(pipelineId);
            if (session) {
                session->Seek(static_cast<double>(posMs) / 1000.0);
            }
            return std::vector<uint8_t>{};
        });

    // Source: GetSourceInfo
    dispatcher.Register(ipc::CommandType::GetSourceInfo,
        [this](uint32_t, const std::vector<uint8_t>& payload)
            -> core::Result<std::vector<uint8_t>> {
            ipc::MessageReader reader(payload);
            uint32_t pipelineId = reader.ReadU32();
            uint32_t sourceId = reader.ReadU32();
            (void)sourceId;
            if (reader.HasError()) return std::unexpected(core::Error{core::ErrorCode::InvalidArgument, "Invalid payload"});
            
            auto session = m_impl->pipelineManager->GetSession(pipelineId);
            SourceMetadata meta{};
            if (session) {
                meta = session->GetSourceInfo();
            }

            ipc::MessageBuilder builder;
            builder.WriteString(meta.url);
            builder.WriteF64(meta.durationMs);
            builder.WriteU32(meta.width);
            builder.WriteU32(meta.height);
            builder.WriteF64(meta.fps);
            builder.WriteString(meta.videoCodec);
            builder.WriteString(meta.audioCodec);
            builder.WriteI32(meta.audioChannels);
            builder.WriteI32(meta.audioSampleRate);
            builder.WriteU64(meta.bitrate);
            return builder.Finish();
        });

    // Output: AddOutput
    dispatcher.Register(ipc::CommandType::AddOutput,
        [](uint32_t, const std::vector<uint8_t>& payload)
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
        [](uint32_t, const std::vector<uint8_t>& payload)
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
