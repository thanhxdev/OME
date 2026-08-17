#pragma once

#include <string>
#include <memory>
#include <openmedia/core/MediaFrame.h>

namespace openmedia::overlay {

/// @brief Overlay to render subtitles (e.g. CEA-608/708, SRT/VTT stubs)
class SubtitleRenderer {
public:
    SubtitleRenderer();
    ~SubtitleRenderer();

    /// @brief Load subtitle content from file
    bool LoadSubtitleFile(const std::string& filepath);

    /// @brief Set current subtitle text directly (for live closed-captions)
    void SetCurrentText(const std::string& text);

    /// @brief Render subtitles for the current frame PTS
    bool Render(std::shared_ptr<openmedia::core::MediaFrame> frame);

    /// @brief Set basic styling
    void SetStyle(int fontSize, uint32_t textColor, uint32_t bgColor);

private:
    std::string m_currentText;
    int m_fontSize = 32;
    uint32_t m_textColor = 0xFFFFFFFF; // RGBA
    uint32_t m_bgColor = 0x80000000;   // Semi-transparent black

    // TODO: Subtitle track parsed data structures
};

} // namespace openmedia::overlay
