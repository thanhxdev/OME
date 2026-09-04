#pragma once

/// @file PipelineEngineManager.h
/// @brief Multi-tenant orchestrator managing all active pipeline sessions
/// @since 2.0.0

#include <openmedia/core/ErrorCodes.h>
#include <openmedia/server/PipelineSession.h>

#include <memory>
#include <unordered_map>
#include <shared_mutex>
#include <atomic>

namespace openmedia::server {

/// @brief Orchestrator managing dynamic, isolated pipeline sessions (supports up to 32+ pipelines)
class PipelineEngineManager {
public:
    PipelineEngineManager();
    ~PipelineEngineManager();

    PipelineEngineManager(const PipelineEngineManager&) = delete;
    PipelineEngineManager& operator=(const PipelineEngineManager&) = delete;

    /// @brief Create a new pipeline session
    [[nodiscard]] core::Result<uint32_t> CreatePipeline(
        const PipelineSessionConfig& config,
        ipc::D3D11SharedTexturePoolPoC* texturePool = nullptr,
        PipelineSession::TextureUpdateCallback onResolutionChanged = nullptr);

    /// @brief Destroy an existing pipeline session
    [[nodiscard]] core::VoidResult DestroyPipeline(uint32_t id);

    /// @brief Retrieve an existing session by ID
    [[nodiscard]] std::shared_ptr<PipelineSession> GetSession(uint32_t id) const;

    /// @brief Get the count of active sessions
    [[nodiscard]] size_t GetActivePipelineCount() const;

    /// @brief Stop and tear down all sessions (during shutdown)
    void StopAll();

private:
    std::atomic<uint32_t> m_nextPipelineId{1};
    mutable std::shared_mutex m_mutex;
    std::unordered_map<uint32_t, std::shared_ptr<PipelineSession>> m_sessions;
};

} // namespace openmedia::server
