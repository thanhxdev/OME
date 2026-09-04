/// @file PipelineEngineManager.cpp
/// @brief PipelineEngineManager implementation
/// @since 2.0.0

#include <openmedia/server/PipelineEngineManager.h>
#include <openmedia/core/Logger.h>

#include <mutex>

namespace openmedia::server {

PipelineEngineManager::PipelineEngineManager() = default;

PipelineEngineManager::~PipelineEngineManager() {
    StopAll();
}

core::Result<uint32_t> PipelineEngineManager::CreatePipeline(
    const PipelineSessionConfig& config,
    ipc::D3D11SharedTexturePoolPoC* texturePool,
    PipelineSession::TextureUpdateCallback onResolutionChanged)
{
    uint32_t id = m_nextPipelineId.fetch_add(1);
    auto session = std::make_shared<PipelineSession>(id, config, texturePool, std::move(onResolutionChanged));

    {
        std::unique_lock lock(m_mutex);
        m_sessions[id] = session;
    }

    core::Logger::SInfo("PipelineEngineManager", "Created pipeline session {} ('{}', {}x{} @ {:.1f} fps). Total active: {}",
                       id, config.name, config.width, config.height, config.fps, GetActivePipelineCount());

    return id;
}

core::VoidResult PipelineEngineManager::DestroyPipeline(uint32_t id) {
    std::shared_ptr<PipelineSession> session;
    {
        std::unique_lock lock(m_mutex);
        auto it = m_sessions.find(id);
        if (it == m_sessions.end()) {
            return std::unexpected(core::Error{core::ErrorCode::NotFound, "Pipeline not found"});
        }
        session = std::move(it->second);
        m_sessions.erase(it);
    }

    if (session) {
        std::ignore = session->Stop();
    }

    core::Logger::SInfo("PipelineEngineManager", "Destroyed pipeline session {}. Remaining active: {}",
                       id, GetActivePipelineCount());
    return {};
}

std::shared_ptr<PipelineSession> PipelineEngineManager::GetSession(uint32_t id) const {
    std::shared_lock lock(m_mutex);
    auto it = m_sessions.find(id);
    if (it != m_sessions.end()) {
        return it->second;
    }
    return nullptr;
}

size_t PipelineEngineManager::GetActivePipelineCount() const {
    std::shared_lock lock(m_mutex);
    return m_sessions.size();
}

void PipelineEngineManager::StopAll() {
    std::unordered_map<uint32_t, std::shared_ptr<PipelineSession>> sessionsToStop;
    {
        std::unique_lock lock(m_mutex);
        sessionsToStop = std::move(m_sessions);
        m_sessions.clear();
    }

    for (auto& [id, session] : sessionsToStop) {
        if (session) {
            std::ignore = session->Stop();
        }
    }
}

} // namespace openmedia::server
