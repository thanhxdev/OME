#include <openmedia/overlay/LogoOverlay.h>
#include <fmt/format.h>

namespace openmedia::overlay {

LogoOverlay::LogoOverlay(std::string id, std::string imagePath)
    : m_id(std::move(id)), m_imagePath(std::move(imagePath)) {
}

std::string LogoOverlay::GetFilterString(const std::string& inputPadName, const std::string& outputPadName) const {
    std::string escapedPath = m_imagePath;
    for (char& c : escapedPath) {
        if (c == '\\') c = '/';
    }

    if (m_scale != 1.0f) {
        return fmt::format("movie='{}'[logo_{}]; [logo_{}]scale=iw*{}:ih*{}[logo_scaled_{}]; [{}][logo_scaled_{}]overlay=x={}:y={}[{}]",
            escapedPath, m_id, m_id, m_scale, m_scale, m_id, inputPadName, m_id, m_x, m_y, outputPadName);
    } else {
        return fmt::format("movie='{}'[logo_{}]; [{}][logo_{}]overlay=x={}:y={}[{}]",
            escapedPath, m_id, inputPadName, m_id, m_x, m_y, outputPadName);
    }
}

} // namespace openmedia::overlay
