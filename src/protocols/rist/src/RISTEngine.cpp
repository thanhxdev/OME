#include "openmedia/rist/RISTEngine.h"

namespace openmedia {
namespace rist {

RISTEngine::RISTEngine() : initialized_(false) {
}

RISTEngine::~RISTEngine() {
    Shutdown();
}

bool RISTEngine::Initialize() {
    if (initialized_) return true;
    
    // TODO: Initialize librist ctx
    
    initialized_ = true;
    return true;
}

void RISTEngine::Shutdown() {
    if (!initialized_) return;
    
    initialized_ = false;
}

} // namespace rist
} // namespace openmedia
