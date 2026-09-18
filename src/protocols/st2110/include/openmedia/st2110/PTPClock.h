#pragma once

#include <cstdint>
#include <string>
#include <memory>
#include <chrono>
#include <openmedia/core/MediaFrame.h>

namespace openmedia {
namespace st2110 {

enum class PTPState {
    FreeRun = 0,
    Locking = 1,
    Locked = 2,
    Holdover = 3
};

struct PTPClockConfig {
    uint8_t domain = 127;                      // SMPTE ST 2059 default domain
    std::string grandmasterId = "00:11:22:FF:FE:33:44:55";
    int64_t simulatedOffsetNs = 0;             // Offset from grandmaster
    bool useHardwareTimestamp = false;
};

class PTPClock {
public:
    explicit PTPClock(const PTPClockConfig& config = {});
    ~PTPClock();

    bool Sync();
    bool SyncWithGrandmaster(const std::string& grandmasterIp, uint8_t domain);
    
    /// @brief Get current time in seconds (floating-point)
    double GetCurrentTime();

    /// @brief Get high-precision PTP timestamp in nanoseconds since TAI epoch
    uint64_t GetCurrentTimeNs();

    /// @brief Tag a MediaFrame with precise PTP timestamp
    void TagMediaFrame(std::shared_ptr<openmedia::core::MediaFrame> frame);

    PTPState GetState() const;
    bool IsSynchronized() const;
    int64_t GetOffsetFromMasterNs() const;
    std::string GetGrandmasterId() const;

private:
    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

} // namespace st2110
} // namespace openmedia
