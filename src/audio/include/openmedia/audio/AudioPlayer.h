#pragma once

#include <openmedia/core/MediaFrame.h>
#include <openmedia/core/ErrorCodes.h>
#include <memory>
#include <vector>

// Forward declarations for XAudio2 to avoid including <xaudio2.h> in header
struct IXAudio2;
struct IXAudio2MasteringVoice;
struct IXAudio2SourceVoice;

namespace openmedia::audio {

/// @class AudioPlayer
/// @brief Simple XAudio2-based audio playback for MediaFrames
class AudioPlayer {
public:
    AudioPlayer();
    ~AudioPlayer();

    // Delete copy and move constructors
    AudioPlayer(const AudioPlayer&) = delete;
    AudioPlayer& operator=(const AudioPlayer&) = delete;

    /// @brief Initialize XAudio2 engine and voices
    /// @param sampleRate Sampling rate (default 48000)
    /// @param channels Number of channels (default 2)
    /// @return Success or error
    core::VoidResult Initialize(int sampleRate = 48000, int channels = 2);

    /// @brief Submit a single audio frame to the playback queue
    /// @param frame The audio frame (must be AV_SAMPLE_FMT_FLT, interleaved or planar)
    /// @return Success or error
    core::VoidResult PlayFrame(std::shared_ptr<core::MediaFrame> frame);

    /// @brief Stop playback and clear queues
    void Stop();

    /// @brief Get current playback position in seconds (from XAudio2 SamplesPlayed)
    /// @return Playback position in seconds, or 0.0 if not initialized
    double GetPlaybackPositionSeconds() const;

    /// @brief Get number of buffers currently queued in XAudio2
    uint32_t GetQueuedBufferCount() const;

private:
    IXAudio2* m_xaudio2 = nullptr;
    IXAudio2MasteringVoice* m_masteringVoice = nullptr;
    IXAudio2SourceVoice* m_sourceVoice = nullptr;

    bool m_initialized = false;
    int m_sampleRate = 48000;
    int m_channels = 2;

    // A simple queue to keep the audio data alive while XAudio2 plays it
    std::vector<std::vector<uint8_t>> m_bufferQueue;
    size_t m_maxQueueSize = 10;
};

} // namespace openmedia::audio
