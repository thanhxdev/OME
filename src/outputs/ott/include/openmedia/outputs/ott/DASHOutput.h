#pragma once

#include <string>
#include <vector>
#include <memory>
#include <span>
#include <cstdint>
#include <mutex>

namespace openmedia {
namespace outputs {
namespace ott {

struct DASHRepresentation {
    std::string id = "video_1080p";
    uint32_t width = 1920;
    uint32_t height = 1080;
    uint32_t bandwidth = 6000000; // bps
    std::string codec = "avc1.640028";
    std::string frameRate = "60";
};

struct DASHConfig {
    std::string outputDir = "./dash";
    std::string manifestName = "manifest.mpd";
    double segmentDurationSec = 2.0;
    std::vector<DASHRepresentation> representations;
    bool isLive = true;
};

class DASHOutput {
public:
    explicit DASHOutput(const DASHConfig& config = {});
    ~DASHOutput();

    bool Start(const std::string& output_dir, const std::string& manifest_name);
    void Stop();
    bool IsStarted() const;

    void Configure(const DASHConfig& config);
    DASHConfig GetConfig() const;

    void AddRepresentation(const DASHRepresentation& rep);

    /// @brief Push video payload for a specific representation
    bool PushPacket(const std::string& repId, int64_t pts, std::span<const uint8_t> payload, bool isKeyframe);

    /// @brief Generate MPEG-DASH XML MPD manifest
    std::string GenerateManifest() const;

private:
    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

} // namespace ott
} // namespace outputs
} // namespace openmedia
