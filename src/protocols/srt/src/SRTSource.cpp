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
#ifdef _WIN32
    WSADATA wsaData;
    WSAStartup(MAKEWORD(2, 2), &wsaData);
#endif
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

    linger ling = { 1, 0 }; // Zero linger: abortive close, frees port & multiplexer instantly
    srt_setsockopt(m_socket, 0, SRTO_LINGER, &ling, sizeof(ling));

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
    sa.sin_port = htons((u_short)config.port);
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

        int reuse = 1;
        srt_setsockopt(m_socket, 0, SRTO_REUSEADDR, &reuse, sizeof(reuse));

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
        // Caller mode: bind to specific local network adapter if requested
        if (!config.bindAddress.empty()) {
            sockaddr_in localSa = {};
            localSa.sin_family = AF_INET;
            localSa.sin_port = 0;
            inet_pton(AF_INET, config.bindAddress.c_str(), &localSa.sin_addr);
            if (srt_bind(m_socket, (sockaddr*)&localSa, sizeof(localSa)) == SRT_ERROR) {
                spdlog::warn("SRTSource failed to bind to local interface {}: {}", config.bindAddress, srt_getlasterror_str());
            } else {
                spdlog::info("SRTSource bound to interface {}", config.bindAddress);
            }
        }

        // Configure receive timeout (250ms) to allow responsive cancellation
        int rcvTimeo = 250;
        srt_setsockopt(m_socket, 0, SRTO_RCVTIMEO, &rcvTimeo, sizeof(rcvTimeo));

        if (srt_connect(m_socket, (sockaddr*)&sa, sizeof(sa)) == SRT_ERROR) {
            spdlog::error("srt_connect failed: {}", srt_getlasterror_str());
            Disconnect();
            return false;
        }
        spdlog::info("SRTSource connected to {}:{} (Bonding redundancy: {})", config.ip, config.port, config.broadcastRedundancy);
    }
    
    return true;
}

void SRTSource::AcceptLoop() {
    while (m_running && m_socket != -1) {
        SRT_SOCKSTATUS serverSt = srt_getsockstate(m_socket);
        if (serverSt != SRTS_LISTENING) {
            spdlog::warn("SRT listener socket state changed to {} (not listening). Breaking accept loop.", (int)serverSt);
            break;
        }

        int curClient = m_clientSocket.load();
        if (curClient != -1 && curClient != SRT_INVALID_SOCK) {
            SRT_SOCKSTATUS st = srt_getsockstate(curClient);
            if (st != SRTS_CONNECTED && st != SRTS_CONNECTING) {
                spdlog::warn("SRT client socket state changed to {} (disconnected). Resetting listener client.", (int)st);
                int oldClient = m_clientSocket.exchange(-1);
                if (oldClient != -1 && oldClient != SRT_INVALID_SOCK) {
                    srt_close(oldClient);
                }
            }
        }

        if (m_clientSocket == -1) {
            sockaddr_in client_sa;
            int client_sa_len = sizeof(client_sa);
            int client = srt_accept(m_socket, (sockaddr*)&client_sa, &client_sa_len);
            if (client != SRT_INVALID_SOCK) {
                spdlog::info("SRT client connected to source listener");
                int rcvTimeo = 250;
                srt_setsockopt(client, 0, SRTO_RCVTIMEO, &rcvTimeo, sizeof(rcvTimeo));
                linger clientLing = { 1, 0 };
                srt_setsockopt(client, 0, SRTO_LINGER, &clientLing, sizeof(clientLing));
                m_clientSocket = client;
            }
        }
        std::this_thread::sleep_for(std::chrono::milliseconds(50));
    }
}

void SRTSource::Disconnect() {
    m_running = false;

    int client = m_clientSocket.exchange(-1);
    if (client != -1) {
        srt_close(client);
    }
    if (m_socket != -1) {
        int sock = m_socket;
        m_socket = -1;
        srt_close(sock);
    }

    if (m_acceptThread.joinable()) {
        m_acceptThread.join();
    }
}

bool SRTSource::IsConnected() const {
    if (m_isListener) {
        int client = m_clientSocket.load();
        return client != -1 && client != SRT_INVALID_SOCK && srt_getsockstate(client) == SRTS_CONNECTED;
    }
    return m_socket != -1 && srt_getsockstate(m_socket) == SRTS_CONNECTED;
}

int SRTSource::Receive(uint8_t* buffer, size_t size) {
    if (!m_running || !buffer || size == 0) return -1;
    int targetSocket = m_isListener ? m_clientSocket.load() : m_socket;
    if (targetSocket == -1 || targetSocket == SRT_INVALID_SOCK) {
        return -1;
    }

    int bytesRead = srt_recv(targetSocket, (char*)buffer, (int)size);
    if (bytesRead < 0) {
        SRT_SOCKSTATUS st = srt_getsockstate(targetSocket);
        if (st != SRTS_CONNECTED && st != SRTS_CONNECTING) {
            if (m_isListener) {
                int oldClient = m_clientSocket.exchange(-1);
                if (oldClient != -1 && oldClient != SRT_INVALID_SOCK) {
                    srt_close(oldClient);
                }
            }
        }
    }
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
