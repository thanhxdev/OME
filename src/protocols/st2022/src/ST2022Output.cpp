#include "openmedia/st2022/ST2022Output.h"

namespace openmedia {
namespace st2022 {

ST2022Output::ST2022Output() : started_(false) {
}

ST2022Output::~ST2022Output() {
    Stop();
}

bool ST2022Output::Start(const std::string& ip, int port) {
    if (started_) return true;
    
    // TODO: Setup MPEG-TS over IP sender with FEC (ST 2022-1/2)
    
    started_ = true;
    return true;
}

void ST2022Output::Stop() {
    if (!started_) return;
    
    started_ = false;
}

} // namespace st2022
} // namespace openmedia
