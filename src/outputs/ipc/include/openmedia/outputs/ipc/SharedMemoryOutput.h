#pragma once

#include <string>

namespace openmedia {
namespace outputs {
namespace ipc {

class SharedMemoryOutput {
public:
    SharedMemoryOutput();
    ~SharedMemoryOutput();

    bool Start(const std::string& segment_name);
    void Stop();

private:
    bool started_;
};

} // namespace ipc
} // namespace outputs
} // namespace openmedia
