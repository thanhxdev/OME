#include "openmedia/outputs/ott/DASHOutput.h"

namespace openmedia {
namespace outputs {
namespace ott {

DASHOutput::DASHOutput() : started_(false) {
}

DASHOutput::~DASHOutput() {
    Stop();
}

bool DASHOutput::Start(const std::string& output_dir, const std::string& manifest_name) {
    if (started_) return true;
    
    // TODO: Initialize FFmpeg libavformat for DASH muxing
    
    started_ = true;
    return true;
}

void DASHOutput::Stop() {
    if (!started_) return;
    
    started_ = false;
}

} // namespace ott
} // namespace outputs
} // namespace openmedia
