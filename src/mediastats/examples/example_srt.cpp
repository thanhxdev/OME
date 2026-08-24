#include "../MediaStats.h"
#include <iostream>
#include <thread>
#include <chrono>

int main() {
    // In a real application, you would create and connect an SRT socket.
    int srt_socket = 1; // Dummy socket ID

    // Create the stats collector
    auto stats = ome::stats::IMediaStats::createSRT(srt_socket);

    // Simulate a media loop
    for (int i = 0; i < 5; ++i) {
        std::this_thread::sleep_for(std::chrono::milliseconds(500));

        // Get snapshot (lock-free, zero-copy, extremely low overhead)
        auto snap = stats->getSnapshot();

        std::cout << "Timestamp: " << snap.timestamp_ms << " ms\n"
                  << "Packets Sent: " << snap.packets_sent << "\n"
                  << "Packets Recv: " << snap.packets_recv << "\n"
                  << "Packets Snd Lost: " << snap.packets_snd_lost << "\n"
                  << "Packets Rcv Lost: " << snap.packets_rcv_lost << "\n"
                  << "Packet Loss: " << snap.packet_loss_pct << "%\n"
                  << "RTT: " << snap.rtt_ms << " ms\n"
                  << "Send Rate: " << snap.mbps_send_rate << " Mbps\n"
                  << "Recv Rate: " << snap.mbps_recv_rate << " Mbps\n"
                  << "Retransmissions: " << snap.retransmission_count << "\n"
                  << "-----------------------------------\n";
    }

    return 0;
}
