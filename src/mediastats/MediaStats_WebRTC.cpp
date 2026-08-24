#include "MediaStats.h"

// Forward declaration of WebRTC types to avoid heavy includes in header.
// Assuming WebRTC is available in the include path.
#ifdef OME_HAS_WEBRTC
#include <api/peer_connection_interface.h>
#include <api/stats/rtc_stats_collector_callback.h>
#include <api/stats/rtc_stats_report.h>
#include <api/stats/rtcstats_objects.h>
#endif

#include <atomic>
#include <chrono>

namespace ome {
namespace stats {

#ifdef OME_HAS_WEBRTC

class WebRTCStatsCollector : public webrtc::RTCStatsCollectorCallback {
public:
    explicit WebRTCStatsCollector(std::atomic<Snapshot>& cached_snap)
        : m_cached_snap(cached_snap) {}

    void OnStatsDelivered(const rtc::scoped_refptr<const webrtc::RTCStatsReport>& report) override {
        Snapshot snap;
        
        auto now = std::chrono::steady_clock::now();
        snap.timestamp_ms = std::chrono::duration_cast<std::chrono::milliseconds>(
            now.time_since_epoch()).count();

        // Very basic parsing for demonstration. 
        // In reality, one would iterate over report->GetStatsOfType<webrtc::RTCInboundRtpStreamStats>()
        // and webrtc::RTCOutboundRtpStreamStats() etc.
        for (const auto& stat : *report) {
            if (stat.type() == webrtc::RTCInboundRtpStreamStats::kType) {
                const auto& inbound = stat.cast_to<webrtc::RTCInboundRtpStreamStats>();
                if (inbound.packets_received.is_defined()) snap.packets_recv += *inbound.packets_received;
                if (inbound.packets_lost.is_defined()) snap.packets_rcv_lost += *inbound.packets_lost;
                if (inbound.nack_count.is_defined()) snap.nack_count += *inbound.nack_count;
                if (inbound.pli_count.is_defined()) snap.pli_count += *inbound.pli_count;
                if (inbound.frames_per_second.is_defined()) snap.frame_rate = *inbound.frames_per_second;
            } else if (stat.type() == webrtc::RTCOutboundRtpStreamStats::kType) {
                const auto& outbound = stat.cast_to<webrtc::RTCOutboundRtpStreamStats>();
                if (outbound.packets_sent.is_defined()) snap.packets_sent += *outbound.packets_sent;
                if (outbound.nack_count.is_defined()) snap.nack_count += *outbound.nack_count;
                if (outbound.pli_count.is_defined()) snap.pli_count += *outbound.pli_count;
            } else if (stat.type() == webrtc::RTCRemoteInboundRtpStreamStats::kType) {
                const auto& remote_inbound = stat.cast_to<webrtc::RTCRemoteInboundRtpStreamStats>();
                if (remote_inbound.round_trip_time.is_defined()) snap.rtt_ms = *remote_inbound.round_trip_time * 1000.0;
                if (remote_inbound.packets_lost.is_defined()) snap.packets_snd_lost += *remote_inbound.packets_lost;
            }
        }
        
        uint64_t total_pkts = snap.packets_sent + snap.packets_recv;
        if (total_pkts > 0) {
            uint64_t total_lost = snap.packets_snd_lost + snap.packets_rcv_lost;
            snap.packet_loss_pct = static_cast<double>(total_lost) / total_pkts * 100.0;
        }

        m_cached_snap.store(snap, std::memory_order_relaxed);
        
        if (m_callback && std::abs(snap.packet_loss_pct - m_last_notified_loss) >= m_threshold) {
            m_last_notified_loss = snap.packet_loss_pct;
            m_callback(snap);
        }
    }

    void setCallback(StatsCallback cb, double threshold) {
        m_callback = std::move(cb);
        m_threshold = threshold;
    }

private:
    std::atomic<Snapshot>& m_cached_snap;
    StatsCallback m_callback;
    double m_threshold{5.0};
    double m_last_notified_loss{-1.0};
};

class MediaStatsWebRTC : public IMediaStats {
public:
    explicit MediaStatsWebRTC(webrtc::PeerConnectionInterface* pc) : m_pc(pc) {
        m_cached_snap.store(Snapshot{}, std::memory_order_relaxed);
        m_collector = rtc::make_ref_counted<WebRTCStatsCollector>(m_cached_snap);
    }
    
    ~MediaStatsWebRTC() override = default;

    void setCallback(StatsCallback cb, double change_threshold_pct = 5.0) override {
        m_collector->setCallback(std::move(cb), change_threshold_pct);
    }

    [[nodiscard]] Snapshot getSnapshot() const override {
        // Trigger an async update for the next time, but return the currently cached snapshot instantly (zero-copy, lock-free)
        if (m_pc) {
            m_pc->GetStats(m_collector.get());
        }
        return m_cached_snap.load(std::memory_order_relaxed);
    }

private:
    webrtc::PeerConnectionInterface* m_pc;
    rtc::scoped_refptr<WebRTCStatsCollector> m_collector;
    mutable std::atomic<Snapshot> m_cached_snap;
};

std::unique_ptr<IMediaStats> IMediaStats::createWebRTC(webrtc::PeerConnectionInterface* pc) {
    return std::make_unique<MediaStatsWebRTC>(pc);
}

#else

// Dummy implementation if WebRTC is not available
std::unique_ptr<IMediaStats> IMediaStats::createWebRTC(webrtc::PeerConnectionInterface* /*pc*/) {
    return nullptr; 
}

#endif

} // namespace stats
} // namespace ome
