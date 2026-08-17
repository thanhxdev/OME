#pragma once

/// @file FrameNotification.h
/// @brief Event notification structures for frame-ready and pipeline state events
/// @since 1.0.0

#include <openmedia/core/Types.h>

#include <cstdint>
#include <string>

namespace openmedia::ipc {

/// @brief Notification type
enum class NotificationType : uint32_t {
    FrameReady = 0x0001,        ///< A new frame is available
    FrameDropped = 0x0002,      ///< A frame was dropped
    PipelineStateChanged = 0x0010,
    PipelineError = 0x0011,
    SourceEOF = 0x0020,         ///< Source reached end of file
    SourceReconnecting = 0x0021,
    EncoderStats = 0x0030,      ///< Encoder statistics update
    ServerHealthUpdate = 0x0040,
};

/// @brief Frame ready notification payload
struct FrameReadyNotification {
    uint32_t pipelineId = 0;    ///< Which pipeline produced this frame
    uint32_t slotIndex = 0;     ///< SharedMemory or D3D11 texture slot index
    uint64_t pts = 0;           ///< Presentation timestamp (microseconds)
    uint64_t frameNumber = 0;   ///< Sequential frame counter
    uint32_t width = 0;
    uint32_t height = 0;
    core::PixelFormat format = core::PixelFormat::Unknown;
    bool isKeyFrame = false;
    bool isGPUFrame = false;    ///< true = D3D11SharedTexture, false = SharedMemory
};

/// @brief Frame dropped notification
struct FrameDroppedNotification {
    uint32_t pipelineId = 0;
    uint64_t droppedCount = 0;  ///< Cumulative dropped frames
    std::string reason;         ///< Reason for drop
};

/// @brief Pipeline state change notification
struct PipelineStateNotification {
    uint32_t pipelineId = 0;
    uint32_t previousState = 0;
    uint32_t newState = 0;
};

/// @brief Pipeline error notification
struct PipelineErrorNotification {
    uint32_t pipelineId = 0;
    uint32_t errorCode = 0;
    std::string message;
    std::string source;         ///< Module that produced the error
};

/// @brief Encoder stats notification
struct EncoderStatsNotification {
    uint32_t pipelineId = 0;
    uint32_t encoderId = 0;
    double fps = 0.0;
    double bitrateKbps = 0.0;
    uint64_t totalFrames = 0;
    uint64_t totalBytes = 0;
    double avgEncodingTimeMs = 0.0;
};

/// @brief Server health update
struct ServerHealthNotification {
    double cpuUsagePercent = 0.0;
    double memoryUsageMB = 0.0;
    double gpuUsagePercent = 0.0;
    uint32_t activePipelines = 0;
    uint32_t connectedClients = 0;
    uint64_t uptimeSeconds = 0;
};

} // namespace openmedia::ipc
