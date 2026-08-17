#pragma once
#include <openmedia/core/ErrorCodes.h>
#include <openmedia/core/MediaFrame.h>
#include <memory>
#include <string>

namespace openmedia::mixer {

enum class TransitionType {
    Cut,
    Dissolve,
    Wipe,
    Push,
    Slide
};

class Transition {
public:
    Transition(TransitionType type, int durationMs);
    ~Transition();

    void SetType(TransitionType type);
    void SetDuration(int durationMs);
    int GetDurationMs() const;
    
    // Perform transition between frameA and frameB based on progress (0.0 to 1.0)
    core::Result<std::shared_ptr<core::MediaFrame>> Process(
        const std::shared_ptr<core::MediaFrame>& frameA,
        const std::shared_ptr<core::MediaFrame>& frameB,
        float progress);

private:
    TransitionType m_type;
    int m_durationMs;
};

} // namespace openmedia::mixer
