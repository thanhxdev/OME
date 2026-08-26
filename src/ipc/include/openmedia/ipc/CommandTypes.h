#pragma once

/// @file CommandTypes.h
/// @brief Command type definitions for IPC communication
/// @since 1.0.0

#include <cstdint>
#include <string>
#include <string_view>

namespace openmedia::ipc {

/// @brief All command types for client→server IPC communication
enum class CommandType : uint32_t {
    // System commands (0x0000-0x00FF)
    Noop = 0x0000,
    Handshake = 0x0001,
    Heartbeat = 0x0002,
    GetStatus = 0x0003,
    GetMetrics = 0x0004,
    SetConfig = 0x0005,
    Shutdown = 0x0006,
    ListDevices = 0x0007,

    // Pipeline commands (0x0100-0x01FF)
    CreatePipeline = 0x0100,
    DestroyPipeline = 0x0101,
    StartPipeline = 0x0102,
    StopPipeline = 0x0103,
    PausePipeline = 0x0104,
    ResumePipeline = 0x0105,
    GetPipelineState = 0x0106,
    GetPipelineInfo = 0x0107,

    // Source commands (0x0200-0x02FF)
    OpenSource = 0x0200,
    CloseSource = 0x0201,
    SeekSource = 0x0202,
    GetSourceInfo = 0x0203,
    SetSourceProperty = 0x0204,

    // Mixer commands (0x0300-0x03FF)
    AddMixerInput = 0x0300,
    RemoveMixerInput = 0x0301,
    SetTransition = 0x0302,
    SetLayerProperties = 0x0303,
    SetMixerOutput = 0x0304,

    // Encoder commands (0x0400-0x04FF)
    ConfigureEncoder = 0x0400,
    StartEncoder = 0x0401,
    StopEncoder = 0x0402,
    GetEncoderStats = 0x0403,

    // Output commands (0x0500-0x05FF)
    AddOutput = 0x0500,
    RemoveOutput = 0x0501,
    ConfigureOutput = 0x0502,
    GetOutputStats = 0x0503,

    // Plugin commands (0x0600-0x06FF)
    LoadPlugin = 0x0600,
    UnloadPlugin = 0x0601,
    ListPlugins = 0x0602,
    ConfigurePlugin = 0x0603,
    PluginProcessFrame = 0x0604,

    // Frame commands (0x0700-0x07FF)
    RequestFrame = 0x0700,
    FrameReady = 0x0701,
    MapSharedMemory = 0x0702,
    UnmapSharedMemory = 0x0703,
    ShareD3D11Texture = 0x0704,
};

/// @brief Response status codes
enum class ResponseStatus : uint32_t {
    Success = 0,
    Error = 1,
    Timeout = 2,
    NotFound = 3,
    InvalidCommand = 4,
    InvalidArgument = 5,
    ServerBusy = 6,
    NotImplemented = 7,
};

/// @brief IPC message header — binary protocol
/// All messages start with this header
struct MessageHeader {
    static constexpr uint32_t MAGIC = 0x4F4D4549;  // "OMEI" (OpenMedia IPC)
    static constexpr uint16_t VERSION = 1;

    uint32_t magic = MAGIC;             ///< Magic number for validation
    uint16_t version = VERSION;         ///< Protocol version
    CommandType commandType;            ///< Command type
    uint32_t sequenceNumber = 0;        ///< Sequence number for request/response matching
    uint32_t payloadSize = 0;           ///< Size of payload following header
    uint64_t timestamp = 0;             ///< Timestamp in microseconds
    uint32_t clientId = 0;              ///< Client identifier

    [[nodiscard]] bool IsValid() const {
        return magic == MAGIC && version == VERSION;
    }
};

/// @brief Response header
struct ResponseHeader {
    static constexpr uint32_t MAGIC = 0x4F4D4552;  // "OMER" (OpenMedia IPC Response)
    static constexpr uint16_t VERSION = 1;

    uint32_t magic = MAGIC;
    uint16_t version = VERSION;
    ResponseStatus status = ResponseStatus::Success;
    uint32_t sequenceNumber = 0;        ///< Matching request sequence
    uint32_t payloadSize = 0;           ///< Size of response payload
    uint32_t errorCode = 0;             ///< Error code if status != Success
    uint64_t timestamp = 0;

    [[nodiscard]] bool IsValid() const {
        return magic == MAGIC && version == VERSION;
    }
};

/// @brief Convert CommandType to string for logging
[[nodiscard]] constexpr std::string_view CommandTypeToString(CommandType cmd) {
    switch (cmd) {
        // System commands
        case CommandType::Noop:             return "Noop";
        case CommandType::Handshake:        return "Handshake";
        case CommandType::Heartbeat:        return "Heartbeat";
        case CommandType::GetStatus:        return "GetStatus";
        case CommandType::GetMetrics:       return "GetMetrics";
        case CommandType::SetConfig:        return "SetConfig";
        case CommandType::Shutdown:         return "Shutdown";
        case CommandType::ListDevices:      return "ListDevices";
        
        // Pipeline
        case CommandType::CreatePipeline:   return "CreatePipeline";
        case CommandType::DestroyPipeline:  return "DestroyPipeline";
        case CommandType::StartPipeline:    return "StartPipeline";
        case CommandType::StopPipeline:     return "StopPipeline";
        case CommandType::PausePipeline:    return "PausePipeline";
        case CommandType::ResumePipeline:   return "ResumePipeline";
        case CommandType::GetPipelineState: return "GetPipelineState";
        case CommandType::GetPipelineInfo:  return "GetPipelineInfo";
        
        // Source
        case CommandType::OpenSource:       return "OpenSource";
        case CommandType::CloseSource:      return "CloseSource";
        case CommandType::SeekSource:       return "SeekSource";
        case CommandType::GetSourceInfo:    return "GetSourceInfo";
        case CommandType::SetSourceProperty:return "SetSourceProperty";
        
        // Mixer
        case CommandType::AddMixerInput:    return "AddMixerInput";
        case CommandType::RemoveMixerInput: return "RemoveMixerInput";
        case CommandType::SetTransition:    return "SetTransition";
        case CommandType::SetLayerProperties:return "SetLayerProperties";
        case CommandType::SetMixerOutput:   return "SetMixerOutput";
        
        // Encoder
        case CommandType::ConfigureEncoder: return "ConfigureEncoder";
        case CommandType::StartEncoder:     return "StartEncoder";
        case CommandType::StopEncoder:      return "StopEncoder";
        case CommandType::GetEncoderStats:  return "GetEncoderStats";
        
        // Output
        case CommandType::AddOutput:        return "AddOutput";
        case CommandType::RemoveOutput:     return "RemoveOutput";
        case CommandType::ConfigureOutput:  return "ConfigureOutput";
        case CommandType::GetOutputStats:   return "GetOutputStats";
        
        // Plugins
        case CommandType::LoadPlugin:       return "LoadPlugin";
        case CommandType::UnloadPlugin:     return "UnloadPlugin";
        case CommandType::ListPlugins:      return "ListPlugins";
        case CommandType::ConfigurePlugin:  return "ConfigurePlugin";
        case CommandType::PluginProcessFrame: return "PluginProcessFrame";
        
        // Frames
        case CommandType::RequestFrame:     return "RequestFrame";
        case CommandType::FrameReady:       return "FrameReady";
        case CommandType::MapSharedMemory:  return "MapSharedMemory";
        case CommandType::UnmapSharedMemory:return "UnmapSharedMemory";
        case CommandType::ShareD3D11Texture:return "ShareD3D11Texture";
        
        default:                            return "Unknown";
    }
}

} // namespace openmedia::ipc
