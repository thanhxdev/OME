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

struct CMAFChunk {
    uint64_t chunkIndex = 0;
    uint64_t segmentIndex = 0;
    int64_t pts = 0;
    double durationMs = 0.0;
    bool isIndependent = false; // Keyframe chunk
    std::vector<uint8_t> data;   // moof + mdat
};

struct CMAFConfig {
    std::string outputDir = "./cmaf";
    std::string trackId = "video_1";
    uint32_t chunkDurationMs = 333;   // ~20 frames at 60fps (Low-Latency)
    uint32_t segmentDurationMs = 2000;
    bool enableLLHLS = true;
};

class CMAFOutput {
public:
    explicit CMAFOutput(const CMAFConfig& config = {});
    ~CMAFOutput();

    bool Start(const std::string& output_dir, const std::string& manifest_name);
    void Stop();
    bool IsStarted() const;

    void Configure(const CMAFConfig& config);
    CMAFConfig GetConfig() const;

    /// @brief Package video NALU into a Low-Latency CMAF chunk (moof + mdat)
    std::vector<uint8_t> CreateChunk(int64_t pts, std::span<const uint8_t> naluPayload, bool isKeyframe);

    /// @brief Push packet and trigger chunk delivery
    bool PushPacket(int64_t pts, std::span<const uint8_t> payload, bool isKeyframe);

    std::vector<CMAFChunk> GetRecentChunks() const;

private:
    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

} // namespace ott
} // namespace outputs
} // namespace openmedia
