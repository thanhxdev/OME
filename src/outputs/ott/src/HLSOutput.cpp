#include "openmedia/outputs/ott/HLSOutput.h"

namespace openmedia {
namespace outputs {
namespace ott {

HLSOutput::HLSOutput() : started_(false) {
}

HLSOutput::~HLSOutput() {
    Stop();
}

bool HLSOutput::Start(const std::string& output_dir, const std::string& manifest_name) {
    if (started_) return true;
    
    // TODO: Initialize FFmpeg libavformat for HLS muxing
    
    started_ = true;
    return true;
}

void HLSOutput::Stop() {
    if (!started_) return;
    
    started_ = false;
}

} // namespace ott
} // namespace outputs
} // namespace openmedia
