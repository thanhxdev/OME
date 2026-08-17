#include <openmedia/gpu/QuickSyncContext.h>

namespace openmedia::gpu {

QuickSyncContext::QuickSyncContext() = default;

QuickSyncContext::~QuickSyncContext() {
    Shutdown();
}

core::VoidResult QuickSyncContext::Initialize() {
    m_deviceIndex = 0;
    // Stub: Initialize oneVPL session
    return {};
}

void QuickSyncContext::Shutdown() {
    if (m_vplSession) {
        // Stub: Close oneVPL session
        m_vplSession = nullptr;
    }
}

void* QuickSyncContext::GetDeviceHandle() {
    return m_vplSession;
}

} // namespace openmedia::gpu
