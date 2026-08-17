#include "openmedia/srt/SRTEngine.h"
#include <srt/srt.h>

namespace openmedia::srt {

SRTEngine::SRTEngine() {
}

SRTEngine::~SRTEngine() {
    Shutdown();
}

bool SRTEngine::Initialize() {
    if (m_initialized) return true;
    
    if (srt_startup() < 0) {
        return false;
    }
    
    m_initialized = true;
    return true;
}

void SRTEngine::Shutdown() {
    if (m_initialized) {
        srt_cleanup();
        m_initialized = false;
    }
}

} // namespace openmedia::srt
