#include "MediaStats.h"
#include <srt/srt.h>
#include <chrono>

namespace ome {
namespace stats {

class MediaStatsSRT : public IMediaStats {
public:
    explicit MediaStatsSRT(int srt_socket) : m_socket(srt_socket) {}
    
    ~MediaStatsSRT() override = default;

    void setCallback(StatsCallback cb, double change_threshold_pct = 5.0) override {
        m_callback = std::move(cb);
        m_threshold = change_threshold_pct;
    }

    [[nodiscard]] Snapshot getSnapshot() const override {
        Snapshot snap;
        
        CBytePerfMon perf;
        // srt_bistats with clear=1 to get intervals, or clear=0 for cumulative.
        // Usually, clear=0 (cumulative) is better and we calculate diffs if needed, 
        // but here we can just return cumulative stats or instant stats.
        // Let's use clear=0 (cumulative) and instant=1 for current RTT.
        if (srt_bistats(m_socket, &perf, 0, 1) == 0) {
            auto now = std::chrono::steady_clock::now();
            snap.timestamp_ms = std::chrono::duration_cast<std::chrono::milliseconds>(
                now.time_since_epoch()).count();

            snap.packets_sent = perf.pktSentTotal;
            snap.packets_recv = perf.pktRecvTotal;
            
            // Sender side loss (reported by receiver) vs Receiver side loss (detected missing)
            snap.packets_snd_lost = perf.pktSndLossTotal;
            snap.packets_rcv_lost = perf.pktRcvLossTotal;
            
            uint64_t total_pkts = snap.packets_sent + snap.packets_recv;
            if (total_pkts > 0) {
                uint64_t total_lost = snap.packets_snd_lost + snap.packets_rcv_lost;
                snap.packet_loss_pct = static_cast<double>(total_lost) / total_pkts * 100.0;
            }

            snap.mbps_send_rate = perf.mbpsSendRate;
            snap.mbps_recv_rate = perf.mbpsRecvRate;
            snap.rtt_ms = perf.msRTT;
            snap.retransmission_count = perf.pktRetransTotal;
        }
        
        if (m_callback && std::abs(snap.packet_loss_pct - m_last_notified_loss) >= m_threshold) {
            m_last_notified_loss = snap.packet_loss_pct;
            m_callback(snap);
        }
        
        return snap;
    }

private:
    int m_socket;
    StatsCallback m_callback;
    double m_threshold{5.0};
    mutable double m_last_notified_loss{-1.0};
};

std::unique_ptr<IMediaStats> IMediaStats::createSRT(int srt_socket) {
    return std::make_unique<MediaStatsSRT>(srt_socket);
}

} // namespace stats
} // namespace ome
