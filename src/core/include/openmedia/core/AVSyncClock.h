#pragma once

#include <openmedia/core/MediaFrame.h>
#include <cstdint>
#include <chrono>

namespace openmedia::core {

/// @brief Audio-Video synchronization clock (audio master)
///
/// Determines when to display video frames based on the audio
/// playback position. The audio device hardware clock is the
/// single source of truth.
class AVSyncClock {
public:
    /// @brief Sync decision for a video frame
    enum class VideoAction {
        Display,    ///< Frame is on-time, display it
        Wait,       ///< Frame is early, hold and retry later
        Drop,       ///< Frame is late, skip and pull next
    };

    struct Config {
        double syncThresholdSec = 0.04;   ///< ±40ms tolerance (1 frame at 25fps)
        double maxDropThresholdSec = 0.1;  ///< Drop if >100ms behind
        double maxWaitThresholdSec = 0.5;  ///< Force display if audio clock stalls
    };

    struct SyncStats {
        uint64_t framesDisplayed = 0;
        uint64_t framesDropped = 0;
        uint64_t framesWaited = 0;
        double currentDriftSec = 0.0;     ///< video_pts - audio_clock
    };

    explicit AVSyncClock(const Config& config = Config{});

    /// @brief Update the audio clock position
    /// @param audioPositionSec Current audio playback position in seconds
    void UpdateAudioClock(double audioPositionSec);

    /// @brief Decide what to do with a video frame
    /// @param frame The video frame to evaluate
    /// @return Action to take (Display, Wait, or Drop)
    VideoAction EvaluateVideoFrame(const MediaFrame& frame) const;

    /// @brief Convert a frame's PTS to seconds using its timebase
    static double PtsToSeconds(const MediaFrame& frame);

    /// @brief Get current audio clock value
    double GetAudioClockSeconds() const;

    /// @brief Get sync statistics
    const SyncStats& GetStats() const;

    /// @brief Record that a frame was displayed/dropped/waited
    void RecordAction(VideoAction action, double driftSec);

    /// @brief Reset clock and statistics
    void Reset();

private:
    Config m_config;
    double m_audioClockSec = 0.0;
    SyncStats m_stats;
};

} // namespace openmedia::core
