#pragma once

#include <memory>
#include <string>

namespace openmedia::ndi {

class NDIEngine {
public:
    NDIEngine();
    ~NDIEngine();

    bool Initialize();
    void Shutdown();

private:
    bool m_initialized = false;
};

} // namespace openmedia::ndi
