#include "openmedia/rist/RISTOutput.h"

namespace openmedia {
namespace rist {

RISTOutput::RISTOutput() : started_(false) {
}

RISTOutput::~RISTOutput() {
    Stop();
}

bool RISTOutput::Start(const std::string& url) {
    if (started_) return true;
    
    // TODO: Setup librist sender
    
    started_ = true;
    return true;
}

void RISTOutput::Stop() {
    if (!started_) return;
    
    // TODO: Stop librist sender
    
    started_ = false;
}

} // namespace rist
} // namespace openmedia
