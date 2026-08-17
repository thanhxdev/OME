#include "openmedia/st2022/ST2022Engine.h"

namespace openmedia {
namespace st2022 {

ST2022Engine::ST2022Engine() : initialized_(false) {
}

ST2022Engine::~ST2022Engine() {
    Shutdown();
}

bool ST2022Engine::Initialize() {
    if (initialized_) return true;
    
    // TODO: Initialize backend for ST 2022 (e.g. DPDK, Rivermax)
    
    initialized_ = true;
    return true;
}

void ST2022Engine::Shutdown() {
    if (!initialized_) return;
    
    initialized_ = false;
}

} // namespace st2022
} // namespace openmedia
