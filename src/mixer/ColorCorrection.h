#ifndef OPENMEDIA_COLOR_CORRECTION_H
#define OPENMEDIA_COLOR_CORRECTION_H

#include "core/IMediaObject.h"
#include "core/MediaFrame.h"

namespace OpenMedia {
namespace Mixer {

struct ColorConfig {
    float brightness = 1.0f; // 0.0 to 2.0
    float contrast = 1.0f;   // 0.0 to 2.0
    float saturation = 1.0f; // 0.0 to 2.0
    float hue = 0.0f;        // -180.0 to +180.0 degrees
    bool enable_hdr = false;
};

class ColorCorrection {
public:
    ColorCorrection();
    ~ColorCorrection();

    void SetConfig(const ColorConfig& config);
    ColorConfig GetConfig() const;

    // Apply color correction to the given video frame in-place or return a new frame
    Result<void> ProcessFrame(MediaFrame* frame);

private:
    ColorConfig m_config;
};

} // namespace Mixer
} // namespace OpenMedia

#endif // OPENMEDIA_COLOR_CORRECTION_H
