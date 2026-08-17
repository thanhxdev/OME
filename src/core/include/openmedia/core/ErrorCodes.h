#pragma once

/// @file ErrorCodes.h
/// @brief Error handling types for OpenMedia SDK
/// @since 1.0.0

#include <cstdint>
#include <expected>
#include <string>
#include <string_view>

namespace openmedia::core {

/// @brief Error codes for SDK operations
enum class ErrorCode : uint32_t {
    Success = 0,

    // General errors (1xxx)
    Unknown = 1000,
    InvalidArgument = 1001,
    InvalidState = 1002,
    NotImplemented = 1003,
    NotSupported = 1004,
    NotFound = 1005,
    AlreadyExists = 1006,
    Timeout = 1007,
    Cancelled = 1008,
    OutOfMemory = 1009,
    PermissionDenied = 1010,
    IOError = 1011,
    InvalidData = 1012,
    WouldBlock = 1013,

    // Pipeline errors (2xxx)
    PipelineNotReady = 2000,
    PipelineAlreadyRunning = 2001,
    PipelineInvalidGraph = 2002,
    PipelineBuildFailed = 2003,
    PipelineNodeNotFound = 2004,
    PipelineCyclicGraph = 2005,
    PipelineTypeMismatch = 2006,

    // IO errors (3xxx)
    FileNotFound = 3000,
    FileOpenFailed = 3001,
    FileReadFailed = 3002,
    FileWriteFailed = 3003,
    StreamOpenFailed = 3004,
    StreamReadFailed = 3005,
    StreamConnectionLost = 3006,
    DeviceNotFound = 3007,
    DeviceOpenFailed = 3008,
    DeviceBusy = 3009,
    EndOfStream = 3010,
    InvalidFormat = 3011,

    // Codec errors (4xxx)
    CodecNotFound = 4000,
    CodecOpenFailed = 4001,
    EncodeFailed = 4002,
    DecodeFailed = 4003,
    CodecConfigInvalid = 4004,

    // GPU errors (5xxx)
    GPUNotAvailable = 5000,
    GPUContextFailed = 5001,
    GPUTransferFailed = 5002,
    GPUMemoryFailed = 5003,
    GPUShaderFailed = 5004,

    // IPC errors (6xxx)
    IPCConnectionFailed = 6000,
    IPCConnectionLost = 6001,
    IPCSendFailed = 6002,
    IPCReceiveFailed = 6003,
    IPCTimeout = 6004,
    IPCServerNotRunning = 6005,
    IPCSharedMemoryFailed = 6006,
    IPCProtocolError = 6007,

    // Plugin errors (7xxx)
    PluginLoadFailed = 7000,
    PluginNotFound = 7001,
    PluginVersionMismatch = 7002,
    PluginCrashed = 7003,
    PluginTimeout = 7004,

    // Config errors (8xxx)
    ConfigLoadFailed = 8000,
    ConfigInvalid = 8001,
    ConfigKeyNotFound = 8002,

    // License errors (9xxx)
    LicenseInvalid = 9000,
    LicenseExpired = 9001,
    LicenseFeatureDisabled = 9002,
};

/// @brief Error information struct
struct Error {
    ErrorCode code = ErrorCode::Success;
    std::string message;
    std::string source;     ///< Module/file that generated the error
    int32_t line = 0;       ///< Source line (debug)

    [[nodiscard]] bool IsSuccess() const { return code == ErrorCode::Success; }
    [[nodiscard]] bool IsError() const { return code != ErrorCode::Success; }

    /// @brief Create a success result
    static Error Ok() { return {ErrorCode::Success, "", "", 0}; }

    /// @brief Create an error with message
    static Error Make(ErrorCode code, std::string_view msg, std::string_view src = "", int32_t ln = 0) {
        return {code, std::string(msg), std::string(src), ln};
    }
};

/// @brief Result type — either a value or an error
/// Uses C++23 std::expected
template <typename T>
using Result = std::expected<T, Error>;

/// @brief Void result for operations that don't return a value
using VoidResult = std::expected<void, Error>;

/// @brief Convert ErrorCode to human-readable string
[[nodiscard]] constexpr std::string_view ErrorCodeToString(ErrorCode code) {
    switch (code) {
        case ErrorCode::Success:              return "Success";
        case ErrorCode::Unknown:              return "Unknown error";
        case ErrorCode::InvalidArgument:      return "Invalid argument";
        case ErrorCode::InvalidState:         return "Invalid state";
        case ErrorCode::NotImplemented:       return "Not implemented";
        case ErrorCode::NotSupported:         return "Not supported";
        case ErrorCode::NotFound:             return "Not found";
        case ErrorCode::Timeout:              return "Timeout";
        case ErrorCode::OutOfMemory:          return "Out of memory";
        case ErrorCode::IOError:              return "I/O error";
        case ErrorCode::InvalidData:          return "Invalid data";
        case ErrorCode::WouldBlock:           return "Would block";
        case ErrorCode::PipelineNotReady:     return "Pipeline not ready";
        case ErrorCode::PipelineInvalidGraph: return "Invalid pipeline graph";
        case ErrorCode::FileNotFound:         return "File not found";
        case ErrorCode::EndOfStream:          return "End of stream";
        case ErrorCode::InvalidFormat:        return "Invalid format";
        case ErrorCode::CodecNotFound:        return "Codec not found";
        case ErrorCode::GPUNotAvailable:      return "GPU not available";
        case ErrorCode::IPCConnectionFailed:  return "IPC connection failed";
        case ErrorCode::PluginLoadFailed:     return "Plugin load failed";
        default:                              return "Unknown error code";
    }
}

/// @brief Helper macro to create error with source info
#define OME_ERROR(code, msg) \
    ::openmedia::core::Error::Make(code, msg, __FILE__, __LINE__)

#define OME_OK() ::openmedia::core::Error::Ok()

} // namespace openmedia::core
