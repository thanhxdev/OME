#pragma once
#include <openmedia/core/ErrorCodes.h>
#include <openmedia/core/MediaFrame.h>
#include <memory>

namespace openmedia::mixer {

struct ColorRange {
    int hMin, hMax;
    int sMin, sMax;
    int vMin, vMax;
};

class ChromaKey {
public:
    ChromaKey();
    ~ChromaKey();

    void SetKeyColor(int r, int g, int b);
    void SetTolerance(float tolerance);
    void SetSmoothing(float smoothing);

    core::Result<std::shared_ptr<core::MediaFrame>> Process(const std::shared_ptr<core::MediaFrame>& input);

private:
    int m_r, m_g, m_b;
    float m_tolerance;
    float m_smoothing;
};

} // namespace openmedia::mixer
