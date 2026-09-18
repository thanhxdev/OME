#include "openmedia/st2022/HitlessMerge.h"
#include <map>
#include <deque>
#include <cmath>
#include <algorithm>

namespace openmedia {
namespace st2022 {

struct SeqLess {
    bool operator()(uint16_t a, uint16_t b) const {
        return static_cast<uint16_t>(b - a) < 32768 && a != b;
    }
};

struct PacketEntry {
    uint16_t seq = 0;
    std::vector<uint8_t> payload;
    std::chrono::steady_clock::time_point arrivalTime;
    NetworkPath path = NetworkPath::PathA;
};

struct HitlessMerge::Impl {
    HitlessMergeConfig config;
    mutable std::mutex mutex;
    HitlessMergeStats stats;

    bool initialized = false;
    uint16_t nextExpectedSeq = 0;
    std::map<uint16_t, PacketEntry, SeqLess> delayBuffer;
    std::deque<std::vector<uint8_t>> directQueue; // For pass-through when disabled

    explicit Impl(const HitlessMergeConfig& cfg) : config(cfg) {}

    bool Push(NetworkPath path, uint16_t seqNum, std::span<const uint8_t> data) {
        std::lock_guard<std::mutex> lock(mutex);
        auto now = std::chrono::steady_clock::now();

        if (path == NetworkPath::PathA) {
            stats.pathAPackets++;
        } else {
            stats.pathBPackets++;
        }

        // Pass-through when disabled
        if (!config.enabled) {
            directQueue.emplace_back(data.begin(), data.end());
            stats.outputPackets++;
            return true;
        }

        if (!initialized) {
            initialized = true;
            nextExpectedSeq = seqNum;
        }

        // Check if packet is stale (older than nextExpectedSeq)
        if (SeqLess()(seqNum, nextExpectedSeq)) {
            stats.duplicatesDropped++;
            return false;
        }

        // Check if already in buffer (duplicate)
        auto it = delayBuffer.find(seqNum);
        if (it != delayBuffer.end()) {
            stats.duplicatesDropped++;
            double skew = std::chrono::duration<double, std::milli>(now - it->second.arrivalTime).count();
            stats.estimatedSkewMs = (stats.estimatedSkewMs * 0.9) + (std::abs(skew) * 0.1);
            return false;
        }

        // New packet
        PacketEntry entry;
        entry.seq = seqNum;
        entry.payload.assign(data.begin(), data.end());
        entry.arrivalTime = now;
        entry.path = path;

        if (path == NetworkPath::PathB) {
            stats.recoveredFromPathB++;
        }

        delayBuffer[seqNum] = std::move(entry);

        // Cap buffer size if needed
        if (delayBuffer.size() > config.maxBufferSizePackets) {
            auto oldest = delayBuffer.begin();
            nextExpectedSeq = static_cast<uint16_t>(oldest->first + 1);
            stats.lostPackets++;
            delayBuffer.erase(oldest);
        }

        return true;
    }

    std::optional<std::vector<uint8_t>> Pop() {
        std::lock_guard<std::mutex> lock(mutex);
        auto now = std::chrono::steady_clock::now();

        if (!config.enabled) {
            if (directQueue.empty()) return std::nullopt;
            auto pkt = std::move(directQueue.front());
            directQueue.pop_front();
            return pkt;
        }

        if (delayBuffer.empty()) {
            return std::nullopt;
        }

        // Look for nextExpectedSeq
        auto it = delayBuffer.find(nextExpectedSeq);
        if (it != delayBuffer.end()) {
            double waitTimeMs = std::chrono::duration<double, std::milli>(now - it->second.arrivalTime).count();
            if (waitTimeMs >= config.differentialDelayMs || delayBuffer.size() > (config.maxBufferSizePackets / 2)) {
                auto result = std::move(it->second.payload);
                delayBuffer.erase(it);
                nextExpectedSeq++;
                stats.outputPackets++;
                return result;
            }
            // Still waiting for differential delay window to smooth out skew
            return std::nullopt;
        }

        // If nextExpectedSeq is missing, check if the oldest packet in buffer has timed out
        auto oldestIt = delayBuffer.begin();
        double oldestWaitTimeMs = std::chrono::duration<double, std::milli>(now - oldestIt->second.arrivalTime).count();
        if (oldestWaitTimeMs >= (config.differentialDelayMs * 2) || delayBuffer.size() > (config.maxBufferSizePackets * 3 / 4)) {
            // Gap detected and timed out: advance nextExpectedSeq to skip lost packets
            stats.lostPackets += static_cast<uint16_t>(oldestIt->first - nextExpectedSeq);
            nextExpectedSeq = oldestIt->first;
            auto result = std::move(oldestIt->second.payload);
            delayBuffer.erase(oldestIt);
            nextExpectedSeq++;
            stats.outputPackets++;
            return result;
        }

        return std::nullopt;
    }
};

HitlessMerge::HitlessMerge(const HitlessMergeConfig& config)
    : m_impl(std::make_unique<Impl>(config)) {
}

HitlessMerge::~HitlessMerge() = default;

bool HitlessMerge::Enable(bool enable) {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    m_impl->config.enabled = enable;
    return true;
}

bool HitlessMerge::IsEnabled() const {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    return m_impl->config.enabled;
}

void HitlessMerge::Configure(const HitlessMergeConfig& config) {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    m_impl->config = config;
}

HitlessMergeConfig HitlessMerge::GetConfig() const {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    return m_impl->config;
}

bool HitlessMerge::PushRTPPacket(NetworkPath path, std::span<const uint8_t> packetData) {
    if (packetData.size() < 12) {
        return false;
    }
    // Extract RTP 16-bit sequence number (bytes 2 & 3 in big-endian)
    uint16_t seqNum = static_cast<uint16_t>((packetData[2] << 8) | packetData[3]);
    return m_impl->Push(path, seqNum, packetData);
}

bool HitlessMerge::PushPacket(NetworkPath path, uint16_t seqNum, std::span<const uint8_t> payload) {
    return m_impl->Push(path, seqNum, payload);
}

std::optional<std::vector<uint8_t>> HitlessMerge::PopPacket() {
    return m_impl->Pop();
}

HitlessMergeStats HitlessMerge::GetStats() const {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    return m_impl->stats;
}

void HitlessMerge::Reset() {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    m_impl->delayBuffer.clear();
    m_impl->directQueue.clear();
    m_impl->initialized = false;
    m_impl->nextExpectedSeq = 0;
    m_impl->stats = HitlessMergeStats{};
}

} // namespace st2022
} // namespace openmedia
