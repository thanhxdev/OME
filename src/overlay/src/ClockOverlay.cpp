#include <openmedia/overlay/ClockOverlay.h>
#include <chrono>
#include <iomanip>
#include <sstream>

namespace openmedia::overlay {

ClockOverlay::ClockOverlay() = default;
ClockOverlay::~ClockOverlay() = default;

void ClockOverlay::SetFormat(const std::string& format) {
    m_format = format;
}

void ClockOverlay::SetCountdown(bool enable, int seconds) {
    m_isCountdown = enable;
    m_countdownSeconds = seconds;
}

void ClockOverlay::SetPosition(int x, int y) {
    m_x = x;
    m_y = y;
}

void ClockOverlay::SetFontSize(int size) {
    m_fontSize = size;
}

std::string ClockOverlay::GetCurrentTimeString() const {
    if (m_isCountdown) {
        // Basic countdown logic
        int hours = m_countdownSeconds / 3600;
        int minutes = (m_countdownSeconds % 3600) / 60;
        int seconds = m_countdownSeconds % 60;
        
        std::ostringstream oss;
        oss << std::setfill('0') << std::setw(2) << hours << ":"
            << std::setfill('0') << std::setw(2) << minutes << ":"
            << std::setfill('0') << std::setw(2) << seconds;
        return oss.str();
    } else {
        auto now = std::chrono::system_clock::now();
        auto in_time_t = std::chrono::system_clock::to_time_t(now);
        std::ostringstream oss;
        
        // Simple format support, can be expanded to use m_format
        struct tm buf;
#ifdef _WIN32
        localtime_s(&buf, &in_time_t);
#else
        localtime_r(&in_time_t, &buf);
#endif
        oss << std::put_time(&buf, "%H:%M:%S");
        return oss.str();
    }
}

bool ClockOverlay::Render(std::shared_ptr<openmedia::core::MediaFrame> frame) {
    if (!frame) return false;
    
    std::string timeStr = GetCurrentTimeString();
    
    // TODO: Use TextOverlay or direct rendering to draw timeStr on frame.
    // For now, this is a stub implementation.
    
    return true;
}

} // namespace openmedia::overlay
