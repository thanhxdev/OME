#include <openmedia/overlay/TextOverlay.h>
#include <fmt/format.h>

namespace openmedia::overlay {

TextOverlay::TextOverlay(std::string id, std::string text)
    : m_id(std::move(id)), m_text(std::move(text)) {
}

std::string TextOverlay::GetFilterString(const std::string& inputPadName, const std::string& outputPadName) const {
    std::string escapedText = m_text;
    
    std::string fontStr = m_fontPath.empty() ? "" : fmt::format("fontfile='{}':", m_fontPath);
    
    return fmt::format("[{}]drawtext={}text='{}':x={}:y={}:fontsize={}:fontcolor={}[{}]",
        inputPadName,
        fontStr,
        escapedText,
        m_x, m_y,
        m_fontSize,
        m_fontColor,
        outputPadName);
}

} // namespace openmedia::overlay
