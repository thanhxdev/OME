#pragma once

#include <string>
#include <memory>
#include <openmedia/core/MediaFrame.h>

#include <vector>
#include <cstdint>

namespace openmedia::overlay {

struct SubtitleCue {
    int64_t startMs = 0;
    int64_t endMs = 0;
    std::string text;
};

/// @brief Overlay to render subtitles (SRT, WebVTT, live 608/708)
class SubtitleRenderer {
public:
    SubtitleRenderer();
    ~SubtitleRenderer();

    /// @brief Load subtitle content from file (.srt or .vtt)
    bool LoadSubtitleFile(const std::string& filepath);

    /// @brief Load subtitle content directly from string
    bool LoadSubtitleContent(const std::string& content);

    /// @brief Set current subtitle text directly (for live closed-captions)
    void SetCurrentText(const std::string& text);

    /// @brief Get subtitle text active at specified time in milliseconds
    std::string GetTextAtTimeMs(int64_t timeMs) const;

    /// @brief Render subtitles for the current frame PTS
    bool Render(std::shared_ptr<openmedia::core::MediaFrame> frame);

    /// @brief Set basic styling
    void SetStyle(int fontSize, uint32_t textColor, uint32_t bgColor);

    const std::vector<SubtitleCue>& GetCues() const;

private:
    std::string m_currentText;
    int m_fontSize = 32;
    uint32_t m_textColor = 0xFFFFFFFF; // RGBA
    uint32_t m_bgColor = 0x80000000;   // Semi-transparent black
    std::vector<SubtitleCue> m_cues;
};

} // namespace openmedia::overlay
