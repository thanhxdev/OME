#include "openmedia/rist/RISTEngine.h"
#include <mutex>

namespace openmedia {
namespace rist {

struct RISTEngine::Impl {
    bool initialized = false;
    mutable std::mutex mutex;
};

RISTEngine::RISTEngine() : m_impl(std::make_unique<Impl>()) {
}

RISTEngine::~RISTEngine() {
    Shutdown();
}

bool RISTEngine::Initialize() {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    if (m_impl->initialized) return true;
    m_impl->initialized = true;
    return true;
}

void RISTEngine::Shutdown() {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    m_impl->initialized = false;
}

bool RISTEngine::IsInitialized() const {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    return m_impl->initialized;
}

} // namespace rist
} // namespace openmedia
