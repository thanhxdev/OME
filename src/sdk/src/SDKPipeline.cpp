/// @file SDKPipeline.cpp
/// @brief Client-side pipeline proxy implementation

#include <openmedia/sdk/SDKPipeline.h>
#include <openmedia/ipc/IPCClient.h>
#include <openmedia/ipc/CommandMessage.h>
#include <openmedia/core/Logger.h>

#include <atomic>
#include <mutex>

namespace openmedia::sdk {

namespace {
auto& Log() { return core::Logger::Get("sdk.pipeline"); }
}

struct SDKPipeline::Impl {
    ipc::IPCClient& client;
    uint32_t pipelineId;
    std::atomic<PipelineState> state{PipelineState::Idle};
    uint32_t nextSourceId = 1;
    uint32_t nextOutputId = 1;

    std::function<void(PipelineState)> onStateChanged;
    std::function<void(const core::Error&)> onError;
    std::function<void(uint64_t)> onFrameProcessed;

    mutable std::mutex callbackMutex;

    Impl(ipc::IPCClient& c, uint32_t id)
        : client(c), pipelineId(id) {}
};

SDKPipeline::SDKPipeline(ipc::IPCClient& client, uint32_t pipelineId)
    : m_impl(std::make_unique<Impl>(client, pipelineId)) {}

SDKPipeline::~SDKPipeline() {
    if (m_impl->state != PipelineState::Idle) {
        Destroy();
    }
}

core::VoidResult SDKPipeline::Build() {
    ipc::MessageBuilder builder;
    builder.WriteU32(m_impl->pipelineId);

    auto result = m_impl->client.SendCommand(
        ipc::CommandType::GetPipelineState, builder.Finish());
    if (!result) {
        return std::unexpected(result.error());
    }

    m_impl->state = PipelineState::Ready;
    Log().Info("Pipeline {} built", m_impl->pipelineId);
    return {};
}

core::VoidResult SDKPipeline::Start() {
    ipc::MessageBuilder builder;
    builder.WriteU32(m_impl->pipelineId);

    auto result = m_impl->client.SendCommand(
        ipc::CommandType::StartPipeline, builder.Finish());
    if (!result) {
        return std::unexpected(result.error());
    }

    m_impl->state = PipelineState::Running;
    Log().Info("Pipeline {} started", m_impl->pipelineId);

    {
        std::lock_guard lock(m_impl->callbackMutex);
        if (m_impl->onStateChanged) {
            m_impl->onStateChanged(PipelineState::Running);
        }
    }

    return {};
}

core::VoidResult SDKPipeline::Stop() {
    ipc::MessageBuilder builder;
    builder.WriteU32(m_impl->pipelineId);

    auto result = m_impl->client.SendCommand(
        ipc::CommandType::StopPipeline, builder.Finish());
    if (!result) {
        return std::unexpected(result.error());
    }

    m_impl->state = PipelineState::Stopped;
    Log().Info("Pipeline {} stopped", m_impl->pipelineId);

    {
        std::lock_guard lock(m_impl->callbackMutex);
        if (m_impl->onStateChanged) {
            m_impl->onStateChanged(PipelineState::Stopped);
        }
    }

    return {};
}

core::VoidResult SDKPipeline::Pause() {
    ipc::MessageBuilder builder;
    builder.WriteU32(m_impl->pipelineId);

    auto result = m_impl->client.SendCommand(
        ipc::CommandType::PausePipeline, builder.Finish());
    if (!result) {
        return std::unexpected(result.error());
    }

    m_impl->state = PipelineState::Paused;
    return {};
}

core::VoidResult SDKPipeline::Resume() {
    ipc::MessageBuilder builder;
    builder.WriteU32(m_impl->pipelineId);

    auto result = m_impl->client.SendCommand(
        ipc::CommandType::ResumePipeline, builder.Finish());
    if (!result) {
        return std::unexpected(result.error());
    }

    m_impl->state = PipelineState::Running;
    return {};
}

void SDKPipeline::Destroy() {
    ipc::MessageBuilder builder;
    builder.WriteU32(m_impl->pipelineId);

    auto result = m_impl->client.SendCommand(
        ipc::CommandType::DestroyPipeline, builder.Finish());
    if (!result) {
        Log().Warn("Failed to destroy pipeline {}: {}",
                   m_impl->pipelineId, result.error().message);
    }

    m_impl->state = PipelineState::Idle;
}

PipelineState SDKPipeline::GetState() const {
    return m_impl->state.load();
}

core::Result<PipelineStats> SDKPipeline::GetStats() const {
    ipc::MessageBuilder builder;
    builder.WriteU32(m_impl->pipelineId);

    auto result = m_impl->client.SendCommand(
        ipc::CommandType::GetPipelineInfo, builder.Finish());
    if (!result) {
        return std::unexpected(result.error());
    }

    ipc::MessageReader reader(result.value());
    PipelineStats stats;
    stats.fps = reader.ReadF64();
    stats.bitrateKbps = reader.ReadF64();
    stats.totalFrames = reader.ReadU64();
    stats.droppedFrames = reader.ReadU64();
    stats.latencyMs = reader.ReadF64();
    stats.cpuUsagePercent = reader.ReadF64();

    if (reader.HasError()) {
        return std::unexpected(core::Error{
            core::ErrorCode::IPCProtocolError,
            "Failed to parse pipeline stats response",
            "SDKPipeline"});
    }

    return stats;
}

uint32_t SDKPipeline::GetPipelineId() const {
    return m_impl->pipelineId;
}

core::Result<std::unique_ptr<SDKSource>> SDKPipeline::OpenSource(const SourceConfig& config) {
    uint32_t sourceId = m_impl->nextSourceId++;
    auto source = std::make_unique<SDKSource>(m_impl->client, m_impl->pipelineId, sourceId);

    auto result = source->Open(config);
    if (!result) {
        return std::unexpected(result.error());
    }

    return source;
}

core::Result<uint32_t> SDKPipeline::AddOutput(const OutputConfig& config) {
    uint32_t outputId = m_impl->nextOutputId++;

    ipc::MessageBuilder builder;
    builder.WriteU32(m_impl->pipelineId);
    builder.WriteU32(outputId);
    builder.WriteString(config.url);
    builder.WriteString(config.format);
    builder.WriteString(config.videoEncoder.codec);
    builder.WriteU32(config.videoEncoder.bitrate);
    builder.WriteString(config.videoEncoder.preset);
    builder.WriteBool(config.videoEncoder.hardwareAccel);

    auto result = m_impl->client.SendCommand(
        ipc::CommandType::AddOutput, builder.Finish());
    if (!result) {
        return std::unexpected(result.error());
    }

    Log().Info("Pipeline {}: added output {} → {}", m_impl->pipelineId, outputId, config.url);
    return outputId;
}

core::VoidResult SDKPipeline::RemoveOutput(uint32_t outputId) {
    ipc::MessageBuilder builder;
    builder.WriteU32(m_impl->pipelineId);
    builder.WriteU32(outputId);

    auto result = m_impl->client.SendCommand(
        ipc::CommandType::RemoveOutput, builder.Finish());
    if (!result) {
        return std::unexpected(result.error());
    }

    return {};
}

void SDKPipeline::OnStateChanged(std::function<void(PipelineState)> callback) {
    std::lock_guard lock(m_impl->callbackMutex);
    m_impl->onStateChanged = std::move(callback);
}

void SDKPipeline::OnError(std::function<void(const core::Error&)> callback) {
    std::lock_guard lock(m_impl->callbackMutex);
    m_impl->onError = std::move(callback);
}

void SDKPipeline::OnFrameProcessed(std::function<void(uint64_t)> callback) {
    std::lock_guard lock(m_impl->callbackMutex);
    m_impl->onFrameProcessed = std::move(callback);
}

} // namespace openmedia::sdk
