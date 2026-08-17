#pragma once

#include <memory>
#include <vector>
#include <openmedia/core/MediaFrame.h>

namespace openmedia::audio {

struct Point2D {
    float x;
    float y;
};

/// @brief Generates vectorscope (L/R phase) data from stereo audio
class AudioVectorscope {
public:
    AudioVectorscope();
    ~AudioVectorscope();

    /// @brief Process frame and extract Lissajous figure points
    /// @return A vector of 2D points representing the phase relationship
    std::vector<Point2D> Process(std::shared_ptr<openmedia::core::MediaFrame> input);
};

} // namespace openmedia::audio
