#include "openmedia/st2110/NMOSEngine.h"

namespace openmedia {
namespace st2110 {

NMOSEngine::NMOSEngine() : running_(false) {
}

NMOSEngine::~NMOSEngine() {
    StopRegistration();
}

bool NMOSEngine::StartRegistration(const std::string& registry_url) {
    if (running_) return true;
    
    // TODO: Send IS-04 Node API announcement
    
    running_ = true;
    return true;
}

void NMOSEngine::StopRegistration() {
    if (!running_) return;
    
    // TODO: Send IS-04 Node offline
    
    running_ = false;
}

} // namespace st2110
} // namespace openmedia
