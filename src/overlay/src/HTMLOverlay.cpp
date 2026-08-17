#include <openmedia/overlay/HTMLOverlay.h>
#include <fmt/format.h>

namespace openmedia::overlay {

HTMLOverlay::HTMLOverlay(std::string id, int width, int height)
    : m_id(std::move(id)), m_width(width), m_height(height) {
    m_cgEngine = std::make_shared<cg::CGEngine>(width, height);
    m_frameBuffer = core::MediaFrame::CreateVideo(width, height, core::PixelFormat::BGRA);
}

HTMLOverlay::~HTMLOverlay() {
}

core::VoidResult HTMLOverlay::LoadTemplate(const std::string& templatePath) {
    return m_cgEngine->LoadTemplate(templatePath);
}

core::VoidResult HTMLOverlay::BindData(const std::string& key, const std::string& value) {
    return m_cgEngine->BindData(key, value);
}

void HTMLOverlay::Update() {
    if (m_cgEngine) {
        m_cgEngine->DoMessageLoopWork();
        (void)m_cgEngine->Render(m_frameBuffer);
    }
}

std::shared_ptr<core::MediaFrame> HTMLOverlay::GetRenderedFrame() {
    return m_frameBuffer;
}

std::string HTMLOverlay::GetFilterString(const std::string& inputPadName, const std::string& outputPadName) const {
    // In a full FFmpeg graph, this would use a 'buffer' source.
    // For now, return a passthrough since dynamic frame injection requires graph re-wiring.
    return fmt::format("[{}]copy[{}]", inputPadName, outputPadName);
}

} // namespace openmedia::overlay
