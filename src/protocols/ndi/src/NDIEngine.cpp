#include "openmedia/ndi/NDIEngine.h"
#include <Processing.NDI.Lib.h>
#include <spdlog/spdlog.h>

namespace openmedia::ndi {

NDIEngine::NDIEngine() = default;

NDIEngine::~NDIEngine() {
    Shutdown();
}

bool NDIEngine::Initialize() {
    if (m_initialized) return true;

    if (!NDIlib_initialize()) {
        spdlog::error("Failed to initialize NDI SDK");
        return false;
    }

    spdlog::info("NDI SDK initialized");
    m_initialized = true;
    return true;
}

void NDIEngine::Shutdown() {
    if (!m_initialized) return;

    NDIlib_destroy();
    spdlog::info("NDI SDK shutdown");
    m_initialized = false;
}

} // namespace openmedia::ndi
