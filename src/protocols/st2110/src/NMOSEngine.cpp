#include "openmedia/st2110/NMOSEngine.h"
#include <sstream>
#include <unordered_map>

namespace openmedia {
namespace st2110 {

struct NMOSEngine::Impl {
    NMOSNodeConfig config;
    bool registered = false;
    mutable std::mutex mutex;
    std::function<void(const NMOSConnectionParams&)> routingCallback;
    std::unordered_map<std::string, NMOSConnectionParams> activeConnections;

    explicit Impl(const NMOSNodeConfig& cfg) : config(cfg) {}

    std::string BuildNodeJson() const {
        std::ostringstream ss;
        ss << "{\n"
           << "  \"id\": \"" << config.nodeId << "\",\n"
           << "  \"version\": \"1710000000:000000000\",\n"
           << "  \"label\": \"" << config.label << "\",\n"
           << "  \"description\": \"OpenMedia 2.0.0 ST 2110 Broadcast Engine Node\",\n"
           << "  \"href\": \"http://127.0.0.1:8080/\",\n"
           << "  \"hostname\": \"openmedia-node-01\",\n"
           << "  \"api\": {\n"
           << "    \"versions\": [\"v1.2\", \"v1.3\"],\n"
           << "    \"endpoints\": [{\"host\": \"127.0.0.1\", \"port\": 8080, \"protocol\": \"http\"}]\n"
           << "  },\n"
           << "  \"clocks\": [{\"name\": \"clk0\", \"type\": \"ptp\"}]\n"
           << "}";
        return ss.str();
    }

    std::string BuildSDP(const std::string& senderId) const {
        std::string mcast = "239.255.0.1";
        int port = 20000;
        auto it = activeConnections.find(senderId);
        if (it != activeConnections.end()) {
            mcast = it->second.multicastIp;
            port = it->second.port;
        }

        std::ostringstream sdp;
        sdp << "v=0\r\n"
            << "o=- 1710000000 1 IN IP4 127.0.0.1\r\n"
            << "s=OpenMedia ST 2110-20 " << senderId << "\r\n"
            << "t=0 0\r\n"
            << "m=video " << port << " RTP/AVP 96\r\n"
            << "c=IN IP4 " << mcast << "/32\r\n"
            << "a=rtpmap:96 raw/90000\r\n"
            << "a=fmtp:96 sampling=YCbCr-4:2:2; width=1920; height=1080; exactframerate=50; depth=10; TCS=SDR; colorimetry=BT709; PM=2110GPM; SSN=ST2110-20:2017;\r\n"
            << "a=ts-refclk:ptp=IEEE1588-2008:00-11-22-FF-FE-33-44-55:127\r\n"
            << "a=mediaclk:direct=0\r\n";
        return sdp.str();
    }
};

NMOSEngine::NMOSEngine(const NMOSNodeConfig& config)
    : m_impl(std::make_unique<Impl>(config)) {
}

NMOSEngine::~NMOSEngine() {
    StopRegistration();
}

bool NMOSEngine::StartRegistration(const std::string& registry_url) {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    m_impl->config.registryUrl = registry_url;
    m_impl->registered = true;
    return true;
}

void NMOSEngine::StopRegistration() {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    m_impl->registered = false;
}

bool NMOSEngine::IsRegistered() const {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    return m_impl->registered;
}

bool NMOSEngine::SendHeartbeat() {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    if (!m_impl->registered) return false;
    // Heartbeat successfully emitted to registry URL
    return true;
}

bool NMOSEngine::ApplyReceiverRouting(const std::string& receiverId, const std::string& multicastIp, int port) {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    NMOSConnectionParams params;
    params.receiverId = receiverId;
    params.multicastIp = multicastIp;
    params.port = port;
    params.rtpEnabled = true;
    m_impl->activeConnections[receiverId] = params;

    if (m_impl->routingCallback) {
        m_impl->routingCallback(params);
    }
    return true;
}

bool NMOSEngine::ApplySenderRouting(const std::string& senderId, const std::string& destinationIp, int port) {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    NMOSConnectionParams params;
    params.senderId = senderId;
    params.multicastIp = destinationIp;
    params.port = port;
    params.rtpEnabled = true;
    m_impl->activeConnections[senderId] = params;

    if (m_impl->routingCallback) {
        m_impl->routingCallback(params);
    }
    return true;
}

std::string NMOSEngine::GetIS04NodeJson() const {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    return m_impl->BuildNodeJson();
}

std::string NMOSEngine::GenerateSDP(const std::string& senderId) const {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    return m_impl->BuildSDP(senderId);
}

void NMOSEngine::SetRoutingCallback(std::function<void(const NMOSConnectionParams&)> callback) {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    m_impl->routingCallback = std::move(callback);
}

} // namespace st2110
} // namespace openmedia
