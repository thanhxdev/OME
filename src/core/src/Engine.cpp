/// @file Engine.cpp
/// @brief Main engine factory and lifecycle

#include <openmedia/core/Engine.h>
#include <openmedia/core/Config.h>
#include <openmedia/core/Logger.h>

#include <atomic>
#include <mutex>
#include <vector>

#ifdef OME_DEMO
    #define OME_BUILD_TYPE_STR "Demo"
#elif defined(OME_PRODUCTION)
    #define OME_BUILD_TYPE_STR "Production"
#else
    #define OME_BUILD_TYPE_STR "Unknown"
#endif

namespace openmedia::core {

struct Engine::Impl {
    std::atomic<bool> running{false};
    std::mutex mutex;
    std::vector<std::unique_ptr<MediaPipeline>> pipelines;
};

Engine::Engine() : m_impl(std::make_unique<Impl>()) {}
Engine::~Engine() {
    if (m_impl->running.load()) {
        Stop();
    }
}

std::unique_ptr<Engine> Engine::Create() {
    return std::unique_ptr<Engine>(new Engine());
}

VoidResult Engine::Initialize() {
    auto& log = Logger::Get("engine");

    // Initialize logging from config
    auto& config = Config::Instance();
    auto logLevel = config.Get("OME_LOG_LEVEL", "debug");
    bool logToConsole = config.GetBool("OME_LOG_TO_CONSOLE", true);
    bool logToFile = config.GetBool("OME_LOG_TO_FILE", true);
    auto logDir = config.Get("OME_LOG_DIR", "./logs");

    LogLevel level = LogLevel::Debug;
    if (logLevel == "trace") level = LogLevel::Trace;
    else if (logLevel == "debug") level = LogLevel::Debug;
    else if (logLevel == "info") level = LogLevel::Info;
    else if (logLevel == "warn") level = LogLevel::Warn;
    else if (logLevel == "error") level = LogLevel::Error;
    else if (logLevel == "critical") level = LogLevel::Critical;

    Logger::Initialize(level, logToConsole, logToFile, logDir);

    log.Info("OpenMedia SDK v{} initializing ({})",
        GetVersion().major, config.GetTagString());
    log.Info("Build: {}", GetBuildInfo());

    return {};
}

VoidResult Engine::Run() {
    auto& log = Logger::Get("engine");
    m_impl->running.store(true, std::memory_order_release);
    log.Info("Engine running");

    // Engine event loop — in real implementation this would be a
    // message pump / event loop for the server process
    while (m_impl->running.load(std::memory_order_acquire)) {
        // Process events, check pipeline health, etc.
        std::this_thread::sleep_for(std::chrono::milliseconds(10));
    }

    log.Info("Engine stopped");
    return {};
}

VoidResult Engine::Stop() {
    auto& log = Logger::Get("engine");
    log.Info("Engine stopping...");

    m_impl->running.store(false, std::memory_order_release);

    // Stop all pipelines
    std::lock_guard lock(m_impl->mutex);
    for (auto& pipeline : m_impl->pipelines) {
        if (pipeline->GetState() == PipelineState::Running ||
            pipeline->GetState() == PipelineState::Paused) {
            pipeline->Stop();
        }
    }

    Logger::Shutdown();
    return {};
}

bool Engine::IsRunning() const {
    return m_impl->running.load(std::memory_order_acquire);
}

std::unique_ptr<MediaPipeline> Engine::CreatePipeline(std::string_view name) {
    return MediaPipeline::Create(name);
}

EngineVersion Engine::GetVersion() {
    return {
        1, 0, 0,
        "v1.0.0",
        OME_BUILD_TYPE_STR
    };
}

std::string Engine::GetBuildInfo() {
    auto ver = GetVersion();
    return "OpenMedia SDK v" + std::to_string(ver.major) + "." +
           std::to_string(ver.minor) + "." + std::to_string(ver.patch) +
           " (" + ver.buildType + ")";
}

std::vector<MediaPipeline*> Engine::GetActivePipelines() const {
    std::vector<MediaPipeline*> result;
    for (const auto& p : m_impl->pipelines) {
        if (p->GetState() == PipelineState::Running ||
            p->GetState() == PipelineState::Paused) {
            result.push_back(p.get());
        }
    }
    return result;
}

size_t Engine::GetActivePipelineCount() const {
    return GetActivePipelines().size();
}

} // namespace openmedia::core
