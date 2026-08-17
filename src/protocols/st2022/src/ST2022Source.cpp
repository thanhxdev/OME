#include "openmedia/st2022/ST2022Source.h"

namespace openmedia {
namespace st2022 {

ST2022Source::ST2022Source() : connected_(false) {
}

ST2022Source::~ST2022Source() {
    Disconnect();
}

bool ST2022Source::Connect(const std::string& ip, int port) {
    if (connected_) return true;
    
    // TODO: IGMP join and MPEG-TS over IP receiver setup
    
    connected_ = true;
    return true;
}

void ST2022Source::Disconnect() {
    if (!connected_) return;
    
    // TODO: Teardown stream
    
    connected_ = false;
}

} // namespace st2022
} // namespace openmedia
