#include "openmedia/st2110/ST2110Source.h"

namespace openmedia {
namespace st2110 {

ST2110Source::ST2110Source() : connected_(false) {
}

ST2110Source::~ST2110Source() {
    Disconnect();
}

bool ST2110Source::Connect(const std::string& multicast_ip, int port) {
    if (connected_) return true;
    
    // TODO: IGMP join and essence stream setup
    
    connected_ = true;
    return true;
}

void ST2110Source::Disconnect() {
    if (!connected_) return;
    
    // TODO: Teardown stream
    
    connected_ = false;
}

} // namespace st2110
} // namespace openmedia
