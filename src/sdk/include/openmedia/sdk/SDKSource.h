#pragma once

/// @file SDKSource.h
/// @brief Client-side proxy for server-side source operations
/// @since 1.0.0

#include <openmedia/sdk/SDKConfig.h>
#include <openmedia/core/ErrorCodes.h>

#include <cstdint>
#include <memory>
#include <string>

namespace openmedia::ipc { class IPCClient; }

namespace openmedia::sdk {

/// @brief Source info returned by the server
struct SourceInfo {
    std::string url;
    double durationMs = 0.0;
    uint32_t width = 0;
    uint32_t height = 0;
    double frameRate = 0.0;
    std::string videoCodec;
    std::string audioCodec;
    int32_t audioChannels = 0;
    int32_t audioSampleRate = 0;
    int64_t bitrateKbps = 0;
};

/// @brief Client-side proxy for a source running on the server
///
/// Communicates with the server via IPC to control source operations.
/// The actual media handling happens in the server process.
///
/// @code
/// SDKSource source(ipcClient, pipelineId);
/// source.Open("video.mp4");
/// auto info = source.GetInfo();
/// source.Seek(10000);  // seek to 10 seconds
/// source.Close();
/// @endcode
class SDKSource {
public:
    SDKSource(ipc::IPCClient& client, uint32_t pipelineId, uint32_t sourceId);
    ~SDKSource();

    SDKSource(const SDKSource&) = delete;
    SDKSource& operator=(const SDKSource&) = delete;

    // --- Operations ---

    /// @brief Open a source (file path or URL)
    [[nodiscard]] core::VoidResult Open(const SourceConfig& config);

    /// @brief Close the source
    void Close();

    /// @brief Seek to position (milliseconds)
    [[nodiscard]] core::VoidResult Seek(int64_t positionMs);

    // --- Info ---

    /// @brief Get source information
    [[nodiscard]] core::Result<SourceInfo> GetInfo() const;

    /// @brief Get the source ID
    [[nodiscard]] uint32_t GetSourceId() const;

    /// @brief Get the pipeline ID this source belongs to
    [[nodiscard]] uint32_t GetPipelineId() const;

    /// @brief Check if source is open
    [[nodiscard]] bool IsOpen() const;

private:
    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

} // namespace openmedia::sdk
