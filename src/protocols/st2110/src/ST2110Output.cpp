#include "openmedia/st2110/ST2110Output.h"

namespace openmedia {
namespace st2110 {

ST2110Output::ST2110Output() : started_(false) {
}

ST2110Output::~ST2110Output() {
    Stop();
}

bool ST2110Output::Start(const std::string& destination_ip, int port) {
    if (started_) return true;
    
    // TODO: Setup RTP packetization for ST 2110
    
    started_ = true;
    return true;
}

void ST2110Output::Stop() {
    if (!started_) return;
    
    started_ = false;
}

} // namespace st2110
} // namespace openmedia
