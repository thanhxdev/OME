#pragma once

/// @file SDKConfig.h
/// @brief SDK configuration structures
/// @since 1.0.0

#include <cstdint>
#include <string>

namespace openmedia::sdk {

/// @brief SDK configuration
struct SDKConfig {
    std::string serverExePath = "OpenMediaServer.exe";  ///< Path to server executable
    std::string pipeName;                                ///< Named pipe name (empty = default)
    bool autoLaunchServer = true;                        ///< Auto-launch server if not running
    uint32_t connectionTimeoutMs = 10000;                ///< Connection timeout
    uint32_t maxReconnectAttempts = 5;                   ///< Max reconnection attempts
    uint32_t reconnectDelayMs = 2000;                    ///< Delay between reconnect attempts
    uint32_t commandTimeoutMs = 5000;                    ///< Default command timeout

    // Shared memory settings
    uint32_t sharedMemoryFrameCount = 8;                 ///< Number of frame slots
    uint32_t sharedMemoryFrameSize = 1920 * 1080 * 4;    ///< Max frame size (bytes)

    // GPU settings
    bool enableGPUSharing = true;                        ///< Enable D3D11 shared textures
    uint32_t gpuTexturePoolSize = 4;                     ///< D3D11 shared texture pool size
};

/// @brief Pipeline configuration (sent to server)
struct PipelineConfig {
    std::string name;               ///< Pipeline display name
    uint32_t width = 1920;          ///< Output width
    uint32_t height = 1080;         ///< Output height
    double frameRate = 29.97;       ///< Output frame rate
    bool enableAudio = true;        ///< Enable audio processing
    int32_t audioSampleRate = 48000; ///< Audio sample rate
    int32_t audioChannels = 2;      ///< Audio channels
};

/// @brief Source configuration
struct SourceConfig {
    std::string url;                ///< File path or stream URL
    bool enableLoop = false;        ///< Loop playback
    int64_t startTimeMs = 0;        ///< Start time offset
};

/// @brief Encoder configuration (sent to server)
struct EncoderConfig {
    std::string codec = "h264";     ///< Codec name
    uint32_t bitrate = 8000;        ///< Bitrate in kbps
    std::string preset = "fast";    ///< Encoder preset
    std::string profile;            ///< Codec profile
    bool hardwareAccel = true;      ///< Use hardware acceleration
};

/// @brief Output configuration
struct OutputConfig {
    std::string url;                ///< Output URL or file path
    std::string format;             ///< Container format (mp4, ts, etc.)
    EncoderConfig videoEncoder;     ///< Video encoder settings
    EncoderConfig audioEncoder;     ///< Audio encoder settings
};

} // namespace openmedia::sdk
