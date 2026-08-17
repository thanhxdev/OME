#include <openmedia/rendering/Preview.h>
#include <openmedia/rendering/D3D11Renderer.h>
#include <openmedia/core/Logger.h>

namespace openmedia::rendering {

Preview::Preview(void* hwnd) : m_hwnd(hwnd) {
    m_renderer = std::make_unique<D3D11Renderer>();
}

Preview::~Preview() {}

core::Result<void> Preview::Initialize() {
    openmedia::core::Logger::SInfo("OME", "Initializing Preview renderer");
    auto res = m_renderer->Initialize(m_hwnd);
    if (res.has_value()) {
        m_renderer->SetScaleMode(ScaleMode::AspectRatioFit); // Default Preview to aspect ratio fit
    }
    return res;
}

core::Result<void> Preview::DisplayFrame(const std::shared_ptr<core::MediaFrame>& frame) {
    return m_renderer->Render(frame);
}

void Preview::OnResize(int width, int height) {
    if (m_renderer) {
        m_renderer->Resize(width, height);
    }
}

void Preview::SetScaleMode(ScaleMode mode) {
    if (m_renderer) {
        m_renderer->SetScaleMode(mode);
    }
}

} // namespace openmedia::rendering
