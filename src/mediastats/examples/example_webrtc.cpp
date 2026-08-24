#include "../MediaStats.h"
#include <iostream>
#include <thread>
#include <chrono>

// Forward declare for the sake of example without pulling in webrtc headers here.
namespace webrtc {
    class PeerConnectionInterface;
}

int main() {
    // In a real application, you would create a WebRTC PeerConnection factory and a PeerConnection.
    webrtc::PeerConnectionInterface* pc = nullptr;

    // Create the stats collector
    auto stats = ome::stats::IMediaStats::createWebRTC(pc);
    if (!stats) {
        std::cout << "WebRTC stats not enabled in build.\n";
        return 0;
    }

    // Simulate a media loop
    for (int i = 0; i < 5; ++i) {
        std::this_thread::sleep_for(std::chrono::milliseconds(500));

        // Get snapshot (lock-free, zero-copy, extremely low overhead)
        auto snap = stats->getSnapshot();

        std::cout << "Timestamp: " << snap.timestamp_ms << " ms\n"
                  << "Packets Sent: " << snap.packets_sent << "\n"
                  << "Packets Recv: " << snap.packets_recv << "\n"
                  << "Packet Loss: " << snap.packet_loss_pct << "%\n"
                  << "RTT: " << snap.rtt_ms << " ms\n"
                  << "Framerate: " << snap.frame_rate << " fps\n"
                  << "NACKs: " << snap.nack_count << "\n"
                  << "PLIs: " << snap.pli_count << "\n"
                  << "-----------------------------------\n";
    }

    return 0;
}
