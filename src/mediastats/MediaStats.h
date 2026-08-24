#pragma once

#include <cstdint>
#include <memory>
#include <functional>

// Forward declarations for external dependencies
namespace webrtc {
    class PeerConnectionInterface;
}

namespace ome {
namespace stats {

/**
 * @brief Plain Old Data struct representing a snapshot of media network statistics.
 * 
 * Designed to be zero-overhead to copy.
 */
struct Snapshot {
    uint64_t timestamp_ms{0};
    
    // Common metrics
    uint64_t packets_sent{0};
    uint64_t packets_recv{0};
    
    // Split loss metrics
    uint64_t packets_snd_lost{0}; // Sender side loss
    uint64_t packets_rcv_lost{0}; // Receiver side loss
    double   packet_loss_pct{0.0};
    
    // Split bitrate metrics
    double   mbps_send_rate{0.0};
    double   mbps_recv_rate{0.0};
    
    double   frame_rate{0.0};
    double   rtt_ms{0.0};
    
    // SRT specific
    uint64_t retransmission_count{0};
    
    // WebRTC specific
    uint64_t nack_count{0};
    uint64_t pli_count{0};
};

using StatsCallback = std::function<void(const Snapshot&)>;

/**
 * @brief High-performance, low-overhead interface for media stats collection.
 */
class IMediaStats {
public:
    virtual ~IMediaStats() = default;

    /**
     * @brief Retrieve the latest stats snapshot.
     * 
     * This method is thread-safe, non-blocking, and lock-free (or very low contention).
     * @return Snapshot The latest recorded statistics.
     */
    [[nodiscard]] virtual Snapshot getSnapshot() const = 0;
    
    /**
     * @brief Optional callback mode. The callback is invoked when stats change significantly.
     * 
     * @param cb The callback function.
     * @param change_threshold_pct The minimum percentage change to trigger the callback (e.g. 5.0%).
     */
    virtual void setCallback(StatsCallback cb, double change_threshold_pct = 5.0) = 0;
    
    /**
     * @brief Create a stats collector for an SRT socket.
     * 
     * @param srt_socket The active SRT socket ID.
     * @return std::unique_ptr<IMediaStats> An instance to collect SRT stats.
     */
    static std::unique_ptr<IMediaStats> createSRT(int srt_socket);
    
    /**
     * @brief Create a stats collector for a WebRTC PeerConnection.
     * 
     * @param pc Pointer to the WebRTC PeerConnectionInterface.
     * @return std::unique_ptr<IMediaStats> An instance to collect WebRTC stats.
     */
    static std::unique_ptr<IMediaStats> createWebRTC(webrtc::PeerConnectionInterface* pc);
};

} // namespace stats
} // namespace ome
