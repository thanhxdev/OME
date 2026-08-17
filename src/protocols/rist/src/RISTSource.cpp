#include "openmedia/rist/RISTSource.h"

namespace openmedia {
namespace rist {

RISTSource::RISTSource() : connected_(false) {
}

RISTSource::~RISTSource() {
    Disconnect();
}

bool RISTSource::Connect(const std::string& url) {
    if (connected_) return true;
    
    // TODO: Connect via librist
    
    connected_ = true;
    return true;
}

void RISTSource::Disconnect() {
    if (!connected_) return;
    
    connected_ = false;
}

} // namespace rist
} // namespace openmedia
