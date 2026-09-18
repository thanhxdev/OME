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

enum class HLSMode {
    Live = 0,   // Sliding window
    Event = 1   // Growing playlist without deletion
};

struct HLSConfig {
    std::string outputDir = "./hls";
    std::string manifestName = "playlist.m3u8";
    HLSMode mode = HLSMode::Live;
    double segmentDurationSec = 2.0;
    size_t liveWindowSegments = 5;
    bool useFmp4 = false;
};

struct HLSSegmentInfo {
    std::string filename;
    double durationSec;
    uint64_t sequenceNumber;
};

class HLSOutput {
public:
    explicit HLSOutput(const HLSConfig& config = {});
    ~HLSOutput();

    bool Start(const std::string& output_dir, const std::string& manifest_name);
    void Stop();
    bool IsStarted() const;

    void Configure(const HLSConfig& config);
    HLSConfig GetConfig() const;

    /// @brief Push video/audio packet into active HLS segment
    /// @param pts Packet presentation timestamp in 90kHz ticks
    /// @param isKeyframe If true, allows clean segment boundary transition
    bool PushPacket(int64_t pts, std::span<const uint8_t> payload, bool isKeyframe);

    /// @brief Generate current HLS playlist .m3u8 text
    std::string GeneratePlaylist() const;

    std::vector<HLSSegmentInfo> GetSegments() const;

private:
    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

} // namespace ott
} // namespace outputs
} // namespace openmedia
