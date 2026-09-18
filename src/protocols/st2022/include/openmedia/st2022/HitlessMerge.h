#pragma once

#include <cstdint>
#include <cstddef>
#include <vector>
#include <memory>
#include <mutex>
#include <chrono>
#include <optional>
#include <span>
#include <functional>

namespace openmedia {
namespace st2022 {

enum class NetworkPath {
    PathA = 0,
    PathB = 1
};

struct HitlessMergeConfig {
    bool enabled = false;
    uint32_t differentialDelayMs = 50;     // Buffer depth 10ms - 500ms
    uint32_t maxBufferSizePackets = 4096;  // Maximum ring buffer capacity
};

struct HitlessMergeStats {
    uint64_t pathAPackets = 0;
    uint64_t pathBPackets = 0;
    uint64_t duplicatesDropped = 0;
    uint64_t recoveredFromPathB = 0;
    uint64_t outputPackets = 0;
    uint64_t lostPackets = 0;
    double estimatedSkewMs = 0.0;
};

/// @brief SMPTE ST 2022-7 Seamless Protection Switching (Hitless Merging)
/// Implements differential delay compensation (10ms - 500ms) and RTP sequence deduplication.
class HitlessMerge {
public:
    explicit HitlessMerge(const HitlessMergeConfig& config = {});
    ~HitlessMerge();

    bool Enable(bool enable);
    bool IsEnabled() const;

    void Configure(const HitlessMergeConfig& config);
    HitlessMergeConfig GetConfig() const;

    /// @brief Push an RTP packet from either Path A or Path B
    /// @param path Source network interface
    /// @param packetData Raw RTP packet data
    /// @return true if packet accepted, false if discarded as duplicate or invalid
    bool PushRTPPacket(NetworkPath path, std::span<const uint8_t> packetData);

    /// @brief Push a packet with explicit sequence number
    bool PushPacket(NetworkPath path, uint16_t seqNum, std::span<const uint8_t> payload);

    /// @brief Pop the next ordered, deduplicated packet from the delay buffer
    std::optional<std::vector<uint8_t>> PopPacket();

    /// @brief Retrieve real-time telemetry metrics
    HitlessMergeStats GetStats() const;

    /// @brief Reset all internal state and buffers
    void Reset();

    /// @brief Helper to check sequence number order with 16-bit wrap-around
    static bool IsSeqNewer(uint16_t s1, uint16_t s2) {
        return (static_cast<uint16_t>(s1 - s2) < 32768) && (s1 != s2);
    }

private:
    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

} // namespace st2022
} // namespace openmedia
