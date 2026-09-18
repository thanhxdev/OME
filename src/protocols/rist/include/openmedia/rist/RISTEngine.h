#pragma once

#include <string>
#include <vector>
#include <memory>
#include <cstdint>

namespace openmedia {
namespace rist {

enum class RISTProfile {
    Simple = 0, // VSF TR-06-1: ARQ NACK packet loss recovery
    Main = 1,   // VSF TR-06-2: GRE encapsulation, DTLS encryption, Multi-link bonding
    Advanced = 2
};

struct RISTPeerConfig {
    std::string url;
    uint32_t weight = 1; // Load balancing weight for multi-link bonding
};

struct RISTConfig {
    RISTProfile profile = RISTProfile::Main;
    std::vector<RISTPeerConfig> peers;
    int recoveryBufferMs = 250;
    int maxBitrateKbps = 10000;
    std::string passphrase;
    int keyLength = 256; // AES-128 / AES-256
    bool multiLinkBonding = false;
};

struct RISTStats {
    uint64_t packetsSent = 0;
    uint64_t packetsReceived = 0;
    uint64_t packetsRecovered = 0;
    uint64_t packetsLost = 0;
    double rttMs = 0.0;
    double qualityScore = 100.0;
};

class RISTEngine {
public:
    RISTEngine();
    ~RISTEngine();

    bool Initialize();
    void Shutdown();
    bool IsInitialized() const;

private:
    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

} // namespace rist
} // namespace openmedia
