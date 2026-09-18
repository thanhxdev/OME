#include "openmedia/rist/RISTOutput.h"
#include <mutex>

namespace openmedia {
namespace rist {

struct RISTOutput::Impl {
    RISTConfig config;
    bool started = false;
    mutable std::mutex mutex;
    RISTStats stats;

    explicit Impl(const RISTConfig& cfg) : config(cfg) {}

    bool Send(std::span<const uint8_t> data) {
        std::lock_guard<std::mutex> lock(mutex);
        if (!started || data.empty()) return false;

        stats.packetsSent++;
        return true;
    }
};

RISTOutput::RISTOutput(const RISTConfig& config)
    : m_impl(std::make_unique<Impl>(config)) {
}

RISTOutput::~RISTOutput() {
    Stop();
}

bool RISTOutput::Start(const std::string& url) {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    if (!url.empty()) {
        m_impl->config.peers.push_back({url, 1});
    }
    m_impl->started = true;
    return true;
}

void RISTOutput::Stop() {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    m_impl->started = false;
}

bool RISTOutput::IsStarted() const {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    return m_impl->started;
}

void RISTOutput::Configure(const RISTConfig& config) {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    m_impl->config = config;
}

RISTConfig RISTOutput::GetConfig() const {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    return m_impl->config;
}

bool RISTOutput::AddPeer(const std::string& url, uint32_t weight) {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    m_impl->config.peers.push_back({url, weight});
    return true;
}

bool RISTOutput::SendData(std::span<const uint8_t> data) {
    return m_impl->Send(data);
}

RISTStats RISTOutput::GetStats() const {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    return m_impl->stats;
}

} // namespace rist
} // namespace openmedia
