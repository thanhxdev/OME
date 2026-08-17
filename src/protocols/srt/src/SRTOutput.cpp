#include "openmedia/srt/SRTOutput.h"
#include "SRTUtils.h"
#include <srt/srt.h>
#include <spdlog/spdlog.h>
#include <cstring>
#include <thread>

#ifdef _WIN32
#include <winsock2.h>
#include <ws2tcpip.h>
#else
#include <arpa/inet.h>
#endif

namespace openmedia::srt {

SRTOutput::SRTOutput() {
}

SRTOutput::~SRTOutput() {
    Stop();
}

bool SRTOutput::Start(const std::string& uri) {
    if (m_socket != -1) return true;
    
    SRTUriConfig config;
    if (!SRTUriConfig::Parse(uri, config)) {
        spdlog::error("Invalid SRT URI format: {}", uri);
        return false;
    }
    
    m_socket = srt_create_socket();
    if (m_socket == SRT_INVALID_SOCK) {
        spdlog::error("Failed to create SRT socket");
        return false;
    }
    
    // Apply options
    if (!config.passphrase.empty()) {
        srt_setsockopt(m_socket, 0, SRTO_PASSPHRASE, config.passphrase.c_str(), config.passphrase.length());
        int pbkeylen = config.pbkeylen;
        srt_setsockopt(m_socket, 0, SRTO_PBKEYLEN, &pbkeylen, sizeof(pbkeylen));
    }

    int latency = config.latency;
    srt_setsockopt(m_socket, 0, SRTO_LATENCY, &latency, sizeof(latency));

    if (config.maxbw > 0) {
        int64_t maxbw = config.maxbw;
        srt_setsockopt(m_socket, 0, SRTO_MAXBW, &maxbw, sizeof(maxbw));
    }

    sockaddr_in sa = {};
    sa.sin_family = AF_INET;
    sa.sin_port = htons(config.port);
    inet_pton(AF_INET, config.ip.c_str(), &sa.sin_addr);

    m_isListener = (config.mode == SRTMode::Listener);

    if (m_isListener) {
        // Listener mode
        int blocking = 1; // 1 = blocking, 0 = non-blocking
        srt_setsockopt(m_socket, 0, SRTO_RCVSYN, &blocking, sizeof(blocking));
        srt_setsockopt(m_socket, 0, SRTO_SNDSYN, &blocking, sizeof(blocking));

        if (srt_bind(m_socket, (sockaddr*)&sa, sizeof(sa)) == SRT_ERROR) {
            spdlog::error("srt_bind failed: {}", srt_getlasterror_str());
            Stop();
            return false;
        }

        if (srt_listen(m_socket, 5) == SRT_ERROR) {
            spdlog::error("srt_listen failed: {}", srt_getlasterror_str());
            Stop();
            return false;
        }

        spdlog::info("SRTOutput listening on {}:{}", config.ip, config.port);
        
        sockaddr_in client_sa;
        int client_sa_len = sizeof(client_sa);
        m_clientSocket = srt_accept(m_socket, (sockaddr*)&client_sa, &client_sa_len);
        if (m_clientSocket == SRT_INVALID_SOCK) {
            spdlog::error("srt_accept failed: {}", srt_getlasterror_str());
            Stop();
            return false;
        }
        spdlog::info("SRT client connected for output");
    } else {
        // Caller mode
        if (srt_connect(m_socket, (sockaddr*)&sa, sizeof(sa)) == SRT_ERROR) {
            spdlog::error("srt_connect failed: {}", srt_getlasterror_str());
            Stop();
            return false;
        }
        spdlog::info("SRTOutput connected to {}:{}", config.ip, config.port);
    }
    
    return true;
}

void SRTOutput::Stop() {
    if (m_clientSocket != -1) {
        srt_close(m_clientSocket);
        m_clientSocket = -1;
    }
    if (m_socket != -1) {
        srt_close(m_socket);
        m_socket = -1;
    }
}

bool SRTOutput::GetStatistics(SRTStatistics& stats) const {
    int targetSocket = m_isListener ? m_clientSocket : m_socket;
    if (targetSocket == -1 || targetSocket == SRT_INVALID_SOCK) {
        return false;
    }

    SRT_TRACEBSTATS bstats;
    int clear = 1; // 1 means clear the stats after reading
    if (srt_bistats(targetSocket, &bstats, clear, 1) < 0) {
        return false;
    }

    stats.msRTT = bstats.msRTT;
    stats.pktLossTotal = bstats.pktSndLossTotal;
    stats.mbpsBandwidth = bstats.mbpsBandwidth;
    // stats.pktRetransmitTotal = bstats.pktSndRetransTotal;

    return true;
}

} // namespace openmedia::srt
