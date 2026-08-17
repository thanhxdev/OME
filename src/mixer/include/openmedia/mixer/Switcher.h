#pragma once
#include <openmedia/core/ErrorCodes.h>
#include <openmedia/core/MediaFrame.h>
#include <openmedia/mixer/Transition.h>
#include <memory>
#include <vector>

namespace openmedia::mixer {

class Switcher {
public:
    Switcher();
    ~Switcher();

    core::Result<void> AddInput(int inputId, std::shared_ptr<core::MediaFrame> frame);
    
    // Set what is on Preview (PVW)
    core::Result<void> SetPreview(int inputId);
    int GetPreviewId() const { return m_previewId; }
    
    // Cut instantly (PVW becomes PGM)
    core::Result<void> Take();
    
    // Transition with duration (PVW becomes PGM over time)
    core::Result<void> Auto(std::shared_ptr<Transition> transition, int durationMs = -1);

    // Old method for backward compatibility
    core::Result<void> SwitchTo(int inputId); 
    
    // Process step for 'Auto' transition
    std::shared_ptr<core::MediaFrame> GetProgramOutput(int64_t currentTimeMs);

private:
    int m_programId;
    int m_previewId;
    std::vector<std::pair<int, std::shared_ptr<core::MediaFrame>>> m_inputs;
    
    // Transition state
    bool m_inTransition;
    int64_t m_transitionStartMs;
    int m_transitionDurationMs;
    std::shared_ptr<Transition> m_transition;
};

} // namespace openmedia::mixer
