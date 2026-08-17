#ifndef OPENMEDIA_TICKER_OVERLAY_H
#define OPENMEDIA_TICKER_OVERLAY_H

#include <openmedia/overlay/IOverlay.h>
#include <openmedia/core/IMediaObject.h>
#include <openmedia/core/MediaFrame.h>
#include <string>

namespace OpenMedia {
namespace Overlay {

enum class TickerDirection {
    LeftToRight,
    RightToLeft,
    UpToDown,
    DownToUp
};

struct TickerConfig {
    std::string text;
    std::string font_family = "Arial";
    int font_size = 24;
    uint32_t color_argb = 0xFFFFFFFF; // White
    uint32_t background_argb = 0x80000000; // Semi-transparent black
    int speed = 5; // pixels per frame
    TickerDirection direction = TickerDirection::RightToLeft;
    int y_position = 0; // vertical position if scrolling horizontally
};

class TickerOverlay {
public:
    TickerOverlay();
    ~TickerOverlay();

    void SetConfig(const TickerConfig& config);
    void UpdateText(const std::string& text);

    openmedia::core::Result<void> RenderToFrame(openmedia::core::MediaFrame* frame);

private:
    TickerConfig m_config;
    int m_currentOffset;
};

} // namespace Overlay
} // namespace OpenMedia

#endif // OPENMEDIA_TICKER_OVERLAY_H
