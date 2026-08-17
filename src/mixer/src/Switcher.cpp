#include <openmedia/mixer/Switcher.h>

namespace openmedia::mixer {

Switcher::Switcher() 
    : m_programId(-1), m_previewId(-1), m_inTransition(false), m_transitionStartMs(0), m_transitionDurationMs(0) {}

Switcher::~Switcher() {}

core::Result<void> Switcher::AddInput(int inputId, std::shared_ptr<core::MediaFrame> frame) {
    for (auto& input : m_inputs) {
        if (input.first == inputId) {
            input.second = frame;
            return {};
        }
    }
    m_inputs.push_back({inputId, frame});
    if (m_programId == -1) {
        m_programId = inputId; // Auto select first input as PGM
    }
    if (m_previewId == -1) {
        m_previewId = inputId; // Auto select first input as PVW
    }
    return {};
}

core::Result<void> Switcher::SwitchTo(int inputId) {
    m_programId = inputId;
    m_previewId = inputId;
    m_inTransition = false;
    return {};
}

core::Result<void> Switcher::SetPreview(int inputId) {
    m_previewId = inputId;
    return {};
}

core::Result<void> Switcher::Take() {
    std::swap(m_programId, m_previewId);
    m_inTransition = false;
    return {};
}

core::Result<void> Switcher::Auto(std::shared_ptr<Transition> transition, int durationMs) {
    if (m_inTransition) return {}; // Already in transition
    if (m_programId == m_previewId) return {}; // Same source
    if (!transition) return Take(); // Fallback to cut if no transition
    
    m_transition = transition;
    m_transitionDurationMs = (durationMs == -1) ? transition->GetDurationMs() : durationMs;
    // We start the transition on the next GetProgramOutput call where we have the current time
    m_transitionStartMs = -1; 
    m_inTransition = true;
    return {};
}

std::shared_ptr<core::MediaFrame> Switcher::GetProgramOutput(int64_t currentTimeMs) {
    std::shared_ptr<core::MediaFrame> pgmFrame = nullptr;
    std::shared_ptr<core::MediaFrame> pvwFrame = nullptr;
    
    for (const auto& input : m_inputs) {
        if (input.first == m_programId) pgmFrame = input.second;
        if (input.first == m_previewId) pvwFrame = input.second;
    }
    
    if (!m_inTransition) {
        return pgmFrame;
    }
    
    if (m_transitionStartMs == -1) {
        m_transitionStartMs = currentTimeMs;
    }
    
    int64_t elapsed = currentTimeMs - m_transitionStartMs;
    if (elapsed >= m_transitionDurationMs) {
        // Transition finished
        std::swap(m_programId, m_previewId);
        m_inTransition = false;
        m_transition = nullptr;
        return pvwFrame; // previous preview is now program
    }
    
    float progress = static_cast<float>(elapsed) / static_cast<float>(m_transitionDurationMs);
    
    // During transition: frameA is PGM, frameB is PVW
    auto out = m_transition->Process(pgmFrame, pvwFrame, progress);
    return out.has_value() ? out.value() : pgmFrame;
}

} // namespace openmedia::mixer
