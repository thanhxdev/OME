#pragma once

#include <string>

namespace openmedia::srt {

class SRTOutput {
public:
    SRTOutput();
    ~SRTOutput();

    bool Start(const std::string& uri);
    void Stop();

    struct SRTStatistics {
        int64_t msRTT = 0;
        int pktLossTotal = 0;
        int mbpsBandwidth = 0;
        int pktRetransmitTotal = 0;
    };

    bool GetStatistics(SRTStatistics& stats) const;

private:
    int m_socket = -1;
    int m_clientSocket = -1; // For listener mode
    bool m_isListener = false;
};

} // namespace openmedia::srt
