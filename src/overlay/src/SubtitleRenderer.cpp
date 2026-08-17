#include <openmedia/overlay/SubtitleRenderer.h>

namespace openmedia::overlay {

SubtitleRenderer::SubtitleRenderer() = default;
SubtitleRenderer::~SubtitleRenderer() = default;

bool SubtitleRenderer::LoadSubtitleFile(const std::string& filepath) {
    // Stub: Parsing of SRT/VTT would go here
    return true;
}

void SubtitleRenderer::SetCurrentText(const std::string& text) {
    m_currentText = text;
}

void SubtitleRenderer::SetStyle(int fontSize, uint32_t textColor, uint32_t bgColor) {
    m_fontSize = fontSize;
    m_textColor = textColor;
    m_bgColor = bgColor;
}

bool SubtitleRenderer::Render(std::shared_ptr<openmedia::core::MediaFrame> frame) {
    if (!frame || m_currentText.empty()) return false;
    
    // Stub: use TextOverlay or freetype to render m_currentText at bottom center
    
    return true;
}

} // namespace openmedia::overlay
