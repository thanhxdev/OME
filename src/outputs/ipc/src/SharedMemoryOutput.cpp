#include "openmedia/outputs/ipc/SharedMemoryOutput.h"

namespace openmedia {
namespace outputs {
namespace ipc {

SharedMemoryOutput::SharedMemoryOutput() : started_(false) {
}

SharedMemoryOutput::~SharedMemoryOutput() {
    Stop();
}

bool SharedMemoryOutput::Start(const std::string& segment_name) {
    if (started_) return true;
    
    // TODO: Create Boost.Interprocess shared memory segment
    
    started_ = true;
    return true;
}

void SharedMemoryOutput::Stop() {
    if (!started_) return;
    
    // TODO: Remove shared memory segment
    
    started_ = false;
}

} // namespace ipc
} // namespace outputs
} // namespace openmedia
