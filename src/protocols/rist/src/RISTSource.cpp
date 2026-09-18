#include "openmedia/rist/RISTSource.h"
#include <mutex>

namespace openmedia {
namespace rist {

struct RISTSource::Impl {
    RISTConfig config;
    bool connected = false;
    mutable std::mutex mutex;
    RISTStats stats;

    explicit Impl(const RISTConfig& cfg) : config(cfg) {}

    int Receive(std::span<uint8_t> buffer) {
        std::lock_guard<std::mutex> lock(mutex);
        if (!connected || buffer.empty()) return -1;
        stats.packetsReceived++;
        return 0;
    }
};

RISTSource::RISTSource(const RISTConfig& config)
    : m_impl(std::make_unique<Impl>(config)) {
}

RISTSource::~RISTSource() {
    Disconnect();
}

bool RISTSource::Connect(const std::string& url) {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    if (!url.empty()) {
        m_impl->config.peers.push_back({url, 1});
    }
    m_impl->connected = true;
    return true;
}

void RISTSource::Disconnect() {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    m_impl->connected = false;
}

bool RISTSource::IsConnected() const {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    return m_impl->connected;
}

void RISTSource::Configure(const RISTConfig& config) {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    m_impl->config = config;
}

RISTConfig RISTSource::GetConfig() const {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    return m_impl->config;
}

int RISTSource::ReceiveData(std::span<uint8_t> buffer) {
    return m_impl->Receive(buffer);
}

RISTStats RISTSource::GetStats() const {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    return m_impl->stats;
}

} // namespace rist
} // namespace openmedia
