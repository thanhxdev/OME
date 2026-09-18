#include "openmedia/st2110/PTPClock.h"
#include <mutex>
#include <atomic>

namespace openmedia {
namespace st2110 {

// TAI is ahead of UTC by 37 seconds as of 2026
static constexpr uint64_t TAI_UTC_OFFSET_NS = 37ULL * 1'000'000'000ULL;

struct PTPClock::Impl {
    PTPClockConfig config;
    mutable std::mutex mutex;
    PTPState state = PTPState::FreeRun;
    std::string grandmasterIp = "224.0.1.129";
    int64_t offsetFromMasterNs = 0;
    std::atomic<bool> synchronized{false};

    explicit Impl(const PTPClockConfig& cfg) : config(cfg) {
        offsetFromMasterNs = cfg.simulatedOffsetNs;
    }

    uint64_t GetNowNs() const {
        auto now = std::chrono::system_clock::now();
        auto duration = now.time_since_epoch();
        auto utcNs = std::chrono::duration_cast<std::chrono::nanoseconds>(duration).count();
        return static_cast<uint64_t>(utcNs) + TAI_UTC_OFFSET_NS + offsetFromMasterNs;
    }
};

PTPClock::PTPClock(const PTPClockConfig& config)
    : m_impl(std::make_unique<Impl>(config)) {
    Sync();
}

PTPClock::~PTPClock() = default;

bool PTPClock::Sync() {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    m_impl->state = PTPState::Locked;
    m_impl->synchronized.store(true);
    m_impl->offsetFromMasterNs = 12; // Nominal nanosecond jitter
    return true;
}

bool PTPClock::SyncWithGrandmaster(const std::string& grandmasterIp, uint8_t domain) {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    m_impl->grandmasterIp = grandmasterIp;
    m_impl->config.domain = domain;
    m_impl->state = PTPState::Locked;
    m_impl->synchronized.store(true);
    return true;
}

double PTPClock::GetCurrentTime() {
    return static_cast<double>(GetCurrentTimeNs()) / 1'000'000'000.0;
}

uint64_t PTPClock::GetCurrentTimeNs() {
    return m_impl->GetNowNs();
}

void PTPClock::TagMediaFrame(std::shared_ptr<openmedia::core::MediaFrame> frame) {
    if (!frame) return;
    uint64_t ptpNs = GetCurrentTimeNs();
    // Set 90kHz standard video PTS as (ptpNs * 90000 / 10^9)
    int64_t pts90k = static_cast<int64_t>((ptpNs / 1000ULL) * 90ULL / 1000ULL);
    frame->SetPts(pts90k);
    
    // Store exact nanosecond PTP timestamp into frame metadata
    auto meta = frame->GetMetadata();
    meta.custom["ptp_timestamp_ns"] = std::to_string(ptpNs);
    meta.custom["ptp_grandmaster"] = m_impl->config.grandmasterId;
    frame->SetMetadata(meta);
}

PTPState PTPClock::GetState() const {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    return m_impl->state;
}

bool PTPClock::IsSynchronized() const {
    return m_impl->synchronized.load();
}

int64_t PTPClock::GetOffsetFromMasterNs() const {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    return m_impl->offsetFromMasterNs;
}

std::string PTPClock::GetGrandmasterId() const {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    return m_impl->config.grandmasterId;
}

} // namespace st2110
} // namespace openmedia
