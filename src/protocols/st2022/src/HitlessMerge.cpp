#include "openmedia/st2022/HitlessMerge.h"

namespace openmedia {
namespace st2022 {

HitlessMerge::HitlessMerge() : enabled_(false) {
}

HitlessMerge::~HitlessMerge() {
}

bool HitlessMerge::Enable(bool enable) {
    enabled_ = enable;
    // TODO: Configure ST 2022-7 Seamless Protection Switching
    return true;
}

bool HitlessMerge::IsEnabled() const {
    return enabled_;
}

} // namespace st2022
} // namespace openmedia
