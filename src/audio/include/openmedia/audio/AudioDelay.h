#pragma once

#include <memory>
#include <vector>
#include <openmedia/core/MediaFrame.h>

namespace openmedia::audio {

/// @brief Applies a fixed delay to audio frames (useful for A/V sync)
class AudioDelay {
public:
    AudioDelay();
    ~AudioDelay();

    void SetDelayMs(int milliseconds);
    
    std::shared_ptr<openmedia::core::MediaFrame> Process(std::shared_ptr<openmedia::core::MediaFrame> input);

private:
    int m_delayMs = 0;
    std::vector<uint8_t> m_delayBuffer;
};

} // namespace openmedia::audio
