#include "openmedia/srt/SRTSource.h"
#include "SRTUtils.h"
#include <srt/srt.h>
#include <spdlog/spdlog.h>
#include <cstring>
#include <thread>
#include <chrono>

#ifdef _WIN32
#include <winsock2.h>
#include <ws2tcpip.h>
#else
#include <arpa/inet.h>
#endif

namespace openmedia::srt {

SRTSource::SRTSource() {
    srt_startup();
}

SRTSource::~SRTSource() {
    Disconnect();
    srt_cleanup();
}

bool SRTSource::Connect(const std::string& uri) {
    if (m_socket != -1) return true;
    
    SRTUriConfig config;
    if (!SRTUriConfig::Parse(uri, config)) {
        spdlog::error("Invalid SRT URI format: {}", uri);
        return false;
    }
    
    m_socket = srt_create_socket();
    if (m_socket == SRT_INVALID_SOCK) {
        spdlog::error("Failed to create SRT socket: {}", srt_getlasterror_str());
        return false;
    }
    
    // Apply options for optimal Live Stream Reception
    SRT_TRANSTYPE tt = SRTT_LIVE;
    srt_setsockopt(m_socket, 0, SRTO_TRANSTYPE, &tt, sizeof(tt));

    int payload_size = 1316; // 7 * 188 bytes MPEG-TS standard
    srt_setsockopt(m_socket, 0, SRTO_PAYLOADSIZE, &payload_size, sizeof(payload_size));

    int sndbuf = 8192 * 1316; // ~10.7 MB buffer
    int rcvbuf = 8192 * 1316;
    srt_setsockopt(m_socket, 0, SRTO_SNDBUF, &sndbuf, sizeof(sndbuf));
    srt_setsockopt(m_socket, 0, SRTO_RCVBUF, &rcvbuf, sizeof(rcvbuf));

    int fc = 25600; // Flow control window size
    srt_setsockopt(m_socket, 0, SRTO_FC, &fc, sizeof(fc));

    int tlpktdrop = 1; // Enable Too-Late Packet Drop for Live Stream
    srt_setsockopt(m_socket, 0, SRTO_TLPKTDROP, &tlpktdrop, sizeof(tlpktdrop));

    int tsbpdmode = 1; // Enable Timestamp-Based Packet Delivery
    srt_setsockopt(m_socket, 0, SRTO_TSBPDMODE, &tsbpdmode, sizeof(tsbpdmode));

    if (!config.passphrase.empty()) {
        srt_setsockopt(m_socket, 0, SRTO_PASSPHRASE, config.passphrase.c_str(), (int)config.passphrase.length());
        int pbkeylen = config.pbkeylen;
        srt_setsockopt(m_socket, 0, SRTO_PBKEYLEN, &pbkeylen, sizeof(pbkeylen));
    }

    int latency = config.latency;
    srt_setsockopt(m_socket, 0, SRTO_LATENCY, &latency, sizeof(latency));
    srt_setsockopt(m_socket, 0, SRTO_PEERLATENCY, &latency, sizeof(latency));

    if (config.maxbw > 0) {
        int64_t maxbw = config.maxbw;
        srt_setsockopt(m_socket, 0, SRTO_MAXBW, &maxbw, sizeof(maxbw));
    }

    sockaddr_in sa = {};
    sa.sin_family = AF_INET;
    sa.sin_port = htons(config.port);
    if (config.ip.empty() || config.ip == "0.0.0.0") {
        sa.sin_addr.s_addr = INADDR_ANY;
    } else {
        inet_pton(AF_INET, config.ip.c_str(), &sa.sin_addr);
    }

    m_isListener = (config.mode == SRTMode::Listener);
    m_running = true;

    if (m_isListener) {
        // Listener mode: set non-blocking accept to not freeze the thread
        int rcvSyn = 0; // non-blocking for accept polling
        srt_setsockopt(m_socket, 0, SRTO_RCVSYN, &rcvSyn, sizeof(rcvSyn));

        if (srt_bind(m_socket, (sockaddr*)&sa, sizeof(sa)) == SRT_ERROR) {
            spdlog::error("srt_bind failed: {}", srt_getlasterror_str());
            Disconnect();
            return false;
        }

        if (srt_listen(m_socket, 5) == SRT_ERROR) {
            spdlog::error("srt_listen failed: {}", srt_getlasterror_str());
            Disconnect();
            return false;
        }

        spdlog::info("SRTSource listening on {}:{}", config.ip, config.port);
        m_acceptThread = std::thread(&SRTSource::AcceptLoop, this);
    } else {
        // Caller mode
        if (srt_connect(m_socket, (sockaddr*)&sa, sizeof(sa)) == SRT_ERROR) {
            spdlog::error("srt_connect failed: {}", srt_getlasterror_str());
            Disconnect();
            return false;
        }
        spdlog::info("SRTSource connected to {}:{}", config.ip, config.port);
    }
    
    return true;
}

void SRTSource::AcceptLoop() {
    while (m_running && m_socket != -1) {
        if (m_clientSocket == -1) {
            sockaddr_in client_sa;
            int client_sa_len = sizeof(client_sa);
            int client = srt_accept(m_socket, (sockaddr*)&client_sa, &client_sa_len);
            if (client != SRT_INVALID_SOCK) {
                spdlog::info("SRT client connected to source listener");
                m_clientSocket = client;
            }
        }
        std::this_thread::sleep_for(std::chrono::milliseconds(50));
    }
}

void SRTSource::Disconnect() {
    m_running = false;
    if (m_acceptThread.joinable()) {
        m_acceptThread.join();
    }

    int client = m_clientSocket.exchange(-1);
    if (client != -1) {
        srt_close(client);
    }
    if (m_socket != -1) {
        srt_close(m_socket);
        m_socket = -1;
    }
}

bool SRTSource::IsConnected() const {
    if (m_isListener) {
        return m_clientSocket != -1;
    }
    return m_socket != -1 && srt_getsockstate(m_socket) == SRTS_CONNECTED;
}

int SRTSource::Receive(uint8_t* buffer, size_t size) {
    if (!buffer || size == 0) return -1;
    int targetSocket = m_isListener ? m_clientSocket.load() : m_socket;
    if (targetSocket == -1 || targetSocket == SRT_INVALID_SOCK) {
        return -1;
    }

    int bytesRead = srt_recv(targetSocket, (char*)buffer, (int)size);
    return bytesRead;
}

bool SRTSource::GetStatistics(SRTStatistics& stats) const {
    int targetSocket = m_isListener ? m_clientSocket.load() : m_socket;
    if (targetSocket == -1 || targetSocket == SRT_INVALID_SOCK) {
        return false;
    }

    SRT_TRACEBSTATS bstats;
    int clear = 1; // 1 means clear the stats after reading
    if (srt_bistats(targetSocket, &bstats, clear, 1) < 0) {
        return false;
    }

    stats.msRTT = bstats.msRTT;
    stats.pktLossTotal = (int)bstats.pktRcvLossTotal;
    stats.mbpsBandwidth = (int)bstats.mbpsBandwidth;
    stats.pktRetransmitTotal = (int)bstats.pktRetransTotal;
    stats.pktSentTotal = (int)bstats.pktSentTotal;
    stats.pktRecvTotal = (int)bstats.pktRecvTotal;
    stats.pktDropTotal = (int)bstats.pktRcvDropTotal;
    stats.bytesSentTotal = (uint64_t)bstats.byteSentTotal;
    stats.bytesRecvTotal = (uint64_t)bstats.byteRecvTotal;

    return true;
}

} // namespace openmedia::srt
