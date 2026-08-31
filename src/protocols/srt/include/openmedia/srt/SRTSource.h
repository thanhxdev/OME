#include <string>
#include <cstdint>
#include <atomic>
#include <thread>

namespace openmedia::srt {

class SRTSource {
public:
    SRTSource();
    ~SRTSource();

    bool Connect(const std::string& uri);
    void Disconnect();
    int Receive(uint8_t* buffer, size_t size);
    bool IsConnected() const;

    struct SRTStatistics {
        int64_t msRTT = 0;
        int pktLossTotal = 0;
        int mbpsBandwidth = 0;
        int pktRetransmitTotal = 0;
        int pktSentTotal = 0;
        int pktRecvTotal = 0;
        int pktDropTotal = 0;
        uint64_t bytesSentTotal = 0;
        uint64_t bytesRecvTotal = 0;
    };

    bool GetStatistics(SRTStatistics& stats) const;

private:
    void AcceptLoop();

    int m_socket = -1;
    std::atomic<int> m_clientSocket = -1; // For listener mode
    bool m_isListener = false;
    std::atomic<bool> m_running = false;
    std::thread m_acceptThread;
};

} // namespace openmedia::srt
