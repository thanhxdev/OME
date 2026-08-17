#include "openmedia/st2110/PTPClock.h"
#include <chrono>

namespace openmedia {
namespace st2110 {

PTPClock::PTPClock() : synchronized_(false) {
}

PTPClock::~PTPClock() {
}

bool PTPClock::Sync() {
    // TODO: Connect to PTP grandmaster
    synchronized_ = true;
    return true;
}

double PTPClock::GetCurrentTime() {
    // Return system time as fallback
    auto now = std::chrono::system_clock::now();
    return std::chrono::duration<double>(now.time_since_epoch()).count();
}

} // namespace st2110
} // namespace openmedia
