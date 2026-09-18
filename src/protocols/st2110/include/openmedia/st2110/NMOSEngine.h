#pragma once

#include <string>
#include <vector>
#include <memory>
#include <mutex>
#include <functional>

namespace openmedia {
namespace st2110 {

struct NMOSNodeConfig {
    std::string nodeId = "urn:uuid:11111111-2222-3333-4444-555555555555";
    std::string deviceId = "urn:uuid:66666666-7777-8888-9999-000000000000";
    std::string label = "OpenMedia Broadcast Node";
    std::string registryUrl = "http://nmos-registry.local:8235";
    int heartbeatIntervalSec = 5;
};

struct NMOSConnectionParams {
    std::string senderId;
    std::string receiverId;
    std::string multicastIp = "239.255.0.1";
    int port = 20000;
    bool rtpEnabled = true;
};

class NMOSEngine {
public:
    explicit NMOSEngine(const NMOSNodeConfig& config = {});
    ~NMOSEngine();

    bool StartRegistration(const std::string& registry_url);
    void StopRegistration();
    bool IsRegistered() const;

    /// @brief Send periodic IS-04 heartbeat to keep node alive in registry
    bool SendHeartbeat();

    /// @brief AMWA NMOS IS-05 Connection Management: Route IP stream to receiver
    bool ApplyReceiverRouting(const std::string& receiverId, const std::string& multicastIp, int port);

    /// @brief AMWA NMOS IS-05 Connection Management: Configure sender target
    bool ApplySenderRouting(const std::string& senderId, const std::string& destinationIp, int port);

    /// @brief Generate AMWA IS-04 Node JSON document
    std::string GetIS04NodeJson() const;

    /// @brief Generate RFC 4566 SDP (Session Description Protocol) manifest for NMOS
    std::string GenerateSDP(const std::string& senderId) const;

    void SetRoutingCallback(std::function<void(const NMOSConnectionParams&)> callback);

private:
    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

} // namespace st2110
} // namespace openmedia
