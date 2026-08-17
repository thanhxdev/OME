#include <openmedia/cg/CGRenderer.h>
#include <openmedia/cg/CGEngine.h>
#include <openmedia/core/Logger.h>

namespace openmedia::cg {

struct CGRenderer::Impl {
    std::shared_ptr<CGEngine> engine;
    double accumulatedTimeMs = 0.0;
};

CGRenderer::CGRenderer(std::shared_ptr<CGEngine> engine) : m_impl(std::make_unique<Impl>()) {
    m_impl->engine = engine;
}

CGRenderer::~CGRenderer() = default;

bool CGRenderer::Initialize() {
    OME_LOG_INFO(core::Logger::Get("CGRenderer"), "Initializing CGRenderer");
    if (!m_impl->engine) {
        OME_LOG_ERROR(core::Logger::Get("CGRenderer"), "CGEngine is null in CGRenderer::Initialize");
        return false;
    }
    return true;
}

bool CGRenderer::Render(std::shared_ptr<core::MediaFrame> frame) {
    if (!frame) return false;
    
    // In a full implementation, we would extract texture data from CEF (via CGEngine)
    // and blend it onto the MediaFrame using Alpha Blending or GPU composition.
    
    // Currently acting as a pass-through/stub for animation frame
    return true;
}

void CGRenderer::Update(double deltaTimeMs) {
    m_impl->accumulatedTimeMs += deltaTimeMs;
    // Update JS animations or trigger ticks in CEF if needed
}

} // namespace openmedia::cg
