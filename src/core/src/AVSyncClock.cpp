#include <openmedia/core/AVSyncClock.h>
#include <openmedia/core/Logger.h>

namespace openmedia::core {

AVSyncClock::AVSyncClock(const Config& config)
    : m_config(config) {}

void AVSyncClock::UpdateAudioClock(double audioPositionSec) {
    if (audioPositionSec > 0.0) {
        m_audioClockSec = audioPositionSec;
        m_audioClockUpdated = true;
    }
}

AVSyncClock::VideoAction AVSyncClock::EvaluateVideoFrame(const MediaFrame& frame, double effectiveOffsetSec) const {
    double rawVideoPts = PtsToSeconds(frame);
    if (m_firstVideoPts < 0.0 || (m_firstVideoPts >= 0.0 && rawVideoPts < m_firstVideoPts - 0.5)) {
        const_cast<AVSyncClock*>(this)->Reset();
        m_firstVideoPts = rawVideoPts;
    }
    double videoPts = rawVideoPts - m_firstVideoPts + effectiveOffsetSec;

    double currentClock = 0.0;
    if (m_audioClockUpdated && m_audioClockSec > 0.0) {
        currentClock = m_audioClockSec;
    } else {
        auto now = std::chrono::steady_clock::now();
        currentClock = std::chrono::duration<double>(now - m_startTime).count();
    }

    double drift = videoPts - currentClock;  // positive = video ahead

    if (drift < -m_config.maxDropThresholdSec) {
        // Video is significantly behind audio -> drop
        return VideoAction::Drop;
    }

    if (drift > m_config.syncThresholdSec) {
        // Video is ahead of audio -> wait
        // Unless we've been waiting too long and audio clock stalled
        if (drift > m_config.maxWaitThresholdSec) {
            return VideoAction::Display;
        }
        return VideoAction::Wait;
    }

    // Within tolerance -> display
    return VideoAction::Display;
}

double AVSyncClock::PtsToSeconds(const MediaFrame& frame) {
    auto tb = frame.GetTimeBase();
    if (tb.den == 0) return 0.0;
    return static_cast<double>(frame.GetPts()) * tb.num / tb.den;
}

double AVSyncClock::GetAudioClockSeconds() const {
    return m_audioClockSec;
}

const AVSyncClock::SyncStats& AVSyncClock::GetStats() const {
    return m_stats;
}

void AVSyncClock::RecordAction(VideoAction action, double driftSec) {
    m_stats.currentDriftSec = driftSec;
    switch (action) {
        case VideoAction::Display:
            m_stats.framesDisplayed++;
            break;
        case VideoAction::Wait:
            m_stats.framesWaited++;
            break;
        case VideoAction::Drop:
            m_stats.framesDropped++;
            break;
    }
}

void AVSyncClock::Reset() {
    m_audioClockSec = 0.0;
    m_audioClockUpdated = false;
    m_startTime = std::chrono::steady_clock::now();
    m_firstVideoPts = -1.0;
    m_stats = SyncStats{};
}

} // namespace openmedia::core
