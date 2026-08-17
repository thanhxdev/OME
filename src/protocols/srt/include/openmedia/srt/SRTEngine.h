#pragma once

#include <memory>
#include <string>

namespace openmedia::srt {

class SRTEngine {
public:
    SRTEngine();
    ~SRTEngine();

    bool Initialize();
    void Shutdown();

private:
    bool m_initialized = false;
};

} // namespace openmedia::srt
