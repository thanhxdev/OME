#pragma once

#include <string>
#include <memory>
#include <openmedia/core/MediaFrame.h>

namespace openmedia::overlay {

/// @brief Overlay to render real-time clock or countdown
class ClockOverlay {
public:
    ClockOverlay();
    ~ClockOverlay();

    /// @brief Set the clock format (e.g. "HH:MM:SS", "MM:SS")
    void SetFormat(const std::string& format);

    /// @brief Enable countdown mode
    void SetCountdown(bool enable, int seconds = 0);

    /// @brief Render the clock onto the frame
    bool Render(std::shared_ptr<openmedia::core::MediaFrame> frame);

    /// @brief Set position
    void SetPosition(int x, int y);

    /// @brief Set font size
    void SetFontSize(int size);

private:
    std::string m_format = "HH:MM:SS";
    bool m_isCountdown = false;
    int m_countdownSeconds = 0;
    int m_x = 0;
    int m_y = 0;
    int m_fontSize = 24;

    std::string GetCurrentTimeString() const;
};

} // namespace openmedia::overlay
