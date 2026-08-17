/// @file SDKSource.cpp
/// @brief Client-side source proxy implementation

#include <openmedia/sdk/SDKSource.h>
#include <openmedia/ipc/IPCClient.h>
#include <openmedia/ipc/CommandMessage.h>
#include <openmedia/core/Logger.h>

namespace openmedia::sdk {

namespace {
auto& Log() { return core::Logger::Get("sdk.source"); }
}

struct SDKSource::Impl {
    ipc::IPCClient& client;
    uint32_t pipelineId;
    uint32_t sourceId;
    bool open = false;

    Impl(ipc::IPCClient& c, uint32_t pipId, uint32_t srcId)
        : client(c), pipelineId(pipId), sourceId(srcId) {}
};

SDKSource::SDKSource(ipc::IPCClient& client, uint32_t pipelineId, uint32_t sourceId)
    : m_impl(std::make_unique<Impl>(client, pipelineId, sourceId)) {}

SDKSource::~SDKSource() {
    if (m_impl->open) {
        Close();
    }
}

core::VoidResult SDKSource::Open(const SourceConfig& config) {
    ipc::MessageBuilder builder;
    builder.WriteU32(m_impl->pipelineId);
    builder.WriteU32(m_impl->sourceId);
    builder.WriteString(config.url);
    builder.WriteBool(config.enableLoop);
    builder.WriteI32(static_cast<int32_t>(config.startTimeMs));

    auto result = m_impl->client.SendCommand(
        ipc::CommandType::OpenSource, builder.Finish());
    if (!result) {
        return std::unexpected(result.error());
    }

    m_impl->open = true;
    Log().Info("Source {} opened: {}", m_impl->sourceId, config.url);

    return {};
}

void SDKSource::Close() {
    if (!m_impl->open) return;

    ipc::MessageBuilder builder;
    builder.WriteU32(m_impl->pipelineId);
    builder.WriteU32(m_impl->sourceId);

    auto result = m_impl->client.SendCommand(
        ipc::CommandType::CloseSource, builder.Finish());
    if (!result) {
        Log().Warn("Failed to close source {}: {}", m_impl->sourceId, result.error().message);
    }

    m_impl->open = false;
}

core::VoidResult SDKSource::Seek(int64_t positionMs) {
    ipc::MessageBuilder builder;
    builder.WriteU32(m_impl->pipelineId);
    builder.WriteU32(m_impl->sourceId);
    builder.WriteU64(static_cast<uint64_t>(positionMs));

    auto result = m_impl->client.SendCommand(
        ipc::CommandType::SeekSource, builder.Finish());
    if (!result) {
        return std::unexpected(result.error());
    }

    return {};
}

core::Result<SourceInfo> SDKSource::GetInfo() const {
    ipc::MessageBuilder builder;
    builder.WriteU32(m_impl->pipelineId);
    builder.WriteU32(m_impl->sourceId);

    auto result = m_impl->client.SendCommand(
        ipc::CommandType::GetSourceInfo, builder.Finish());
    if (!result) {
        return std::unexpected(result.error());
    }

    ipc::MessageReader reader(result.value());
    SourceInfo info;
    info.url = reader.ReadString();
    info.durationMs = reader.ReadF64();
    info.width = reader.ReadU32();
    info.height = reader.ReadU32();
    info.frameRate = reader.ReadF64();
    info.videoCodec = reader.ReadString();
    info.audioCodec = reader.ReadString();
    info.audioChannels = reader.ReadI32();
    info.audioSampleRate = reader.ReadI32();
    info.bitrateKbps = static_cast<int64_t>(reader.ReadU64());

    if (reader.HasError()) {
        return std::unexpected(core::Error{
            core::ErrorCode::IPCProtocolError,
            "Failed to parse source info response",
            "SDKSource"});
    }

    return info;
}

uint32_t SDKSource::GetSourceId() const { return m_impl->sourceId; }
uint32_t SDKSource::GetPipelineId() const { return m_impl->pipelineId; }
bool SDKSource::IsOpen() const { return m_impl->open; }

} // namespace openmedia::sdk
