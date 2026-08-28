#include "openmedia/audio/AudioPlayer.h"
#include <openmedia/core/Logger.h>
#include <xaudio2.h>
#include <wrl/client.h>
#include <mutex>
#include <queue>

using Microsoft::WRL::ComPtr;

namespace openmedia::audio {

class AudioPlayerCallback : public IXAudio2VoiceCallback {
public:
    void OnVoiceProcessingPassStart(UINT32 BytesRequired) override {}
    void OnVoiceProcessingPassEnd() override {}
    void OnStreamEnd() override {}
    void OnBufferStart(void* pBufferContext) override {}
    void OnBufferEnd(void* pBufferContext) override {
        if (pBufferContext) {
            delete[] reinterpret_cast<uint8_t*>(pBufferContext);
        }
    }
    void OnLoopEnd(void* pBufferContext) override {}
    void OnVoiceError(void* pBufferContext, HRESULT Error) override {}
};

static AudioPlayerCallback g_voiceCallback;

AudioPlayer::AudioPlayer() {
}

AudioPlayer::~AudioPlayer() {
    Stop();
}

core::VoidResult AudioPlayer::Initialize(int sampleRate, int channels) {
    if (m_initialized) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "AudioPlayer already initialized"));
    }

    HRESULT hr = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    if (FAILED(hr) && hr != RPC_E_CHANGED_MODE) {
        return std::unexpected(core::Error::Make(core::ErrorCode::Unknown, "Failed to initialize COM"));
    }

    hr = XAudio2Create(&m_xaudio2, 0, XAUDIO2_DEFAULT_PROCESSOR);
    if (FAILED(hr)) {
        return std::unexpected(core::Error::Make(core::ErrorCode::Unknown, "Failed to create XAudio2 engine"));
    }

    hr = m_xaudio2->CreateMasteringVoice(&m_masteringVoice);
    if (FAILED(hr)) {
        m_xaudio2->Release();
        m_xaudio2 = nullptr;
        return std::unexpected(core::Error::Make(core::ErrorCode::Unknown, "Failed to create MasteringVoice"));
    }

    WAVEFORMATEX wfx = {};
    wfx.wFormatTag = WAVE_FORMAT_IEEE_FLOAT;
    wfx.nChannels = static_cast<WORD>(channels);
    wfx.nSamplesPerSec = sampleRate;
    wfx.wBitsPerSample = 32;
    wfx.nBlockAlign = (wfx.nChannels * wfx.wBitsPerSample) / 8;
    wfx.nAvgBytesPerSec = wfx.nSamplesPerSec * wfx.nBlockAlign;
    wfx.cbSize = 0;

    hr = m_xaudio2->CreateSourceVoice(&m_sourceVoice, &wfx, 0, XAUDIO2_DEFAULT_FREQ_RATIO, &g_voiceCallback);
    if (FAILED(hr)) {
        m_masteringVoice->DestroyVoice();
        m_masteringVoice = nullptr;
        m_xaudio2->Release();
        m_xaudio2 = nullptr;
        return std::unexpected(core::Error::Make(core::ErrorCode::Unknown, "Failed to create SourceVoice"));
    }

    m_sourceVoice->Start(0);
    
    m_sampleRate = sampleRate;
    m_channels = channels;
    m_initialized = true;
    
    core::Logger::SInfo("AudioPlayer", "Initialized XAudio2 playback: {}Hz, {} channels", sampleRate, channels);

    return {};
}

core::VoidResult AudioPlayer::PlayFrame(std::shared_ptr<core::MediaFrame> frame) {
    if (!m_initialized || !m_sourceVoice) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "AudioPlayer not initialized"));
    }

    if (!frame) {
        return {};
    }

    // Audio format expectation: float32, interleaved
    uint8_t* dataPtr = frame->GetAudioData(0); 
    size_t dataSize = frame->GetAudioBufferSize();
    if (!dataPtr || dataSize == 0) return {};

    // Copy data to a dynamic array that the callback will free
    uint8_t* pData = new uint8_t[dataSize];
    std::memcpy(pData, dataPtr, dataSize);

    XAUDIO2_BUFFER buffer = {};
    buffer.AudioBytes = static_cast<UINT32>(dataSize);
    buffer.pAudioData = pData;
    buffer.pContext = pData; // Pass pointer as context to free it in OnBufferEnd

    HRESULT hr = m_sourceVoice->SubmitSourceBuffer(&buffer);
    if (FAILED(hr)) {
        delete[] pData;
        return std::unexpected(core::Error::Make(core::ErrorCode::Unknown, "Failed to submit XAudio2 buffer"));
    }

    return {};
}

void AudioPlayer::Stop() {
    if (m_sourceVoice) {
        m_sourceVoice->Stop();
        m_sourceVoice->FlushSourceBuffers();
        m_sourceVoice->DestroyVoice();
        m_sourceVoice = nullptr;
    }
    if (m_masteringVoice) {
        m_masteringVoice->DestroyVoice();
        m_masteringVoice = nullptr;
    }
    if (m_xaudio2) {
        m_xaudio2->Release();
        m_xaudio2 = nullptr;
    }
    m_initialized = false;
}

void AudioPlayer::Pause() {
    if (m_sourceVoice) {
        m_sourceVoice->Stop();
    }
}

void AudioPlayer::Resume() {
    if (m_sourceVoice) {
        m_sourceVoice->Start(0);
    }
}

void AudioPlayer::SetVolume(float volume) {
    if (m_sourceVoice) {
        m_sourceVoice->SetVolume(volume);
    }
}

void AudioPlayer::SetMuted(bool muted) {
    if (m_sourceVoice) {
        m_sourceVoice->SetVolume(muted ? 0.0f : 1.0f);
    }
}

double AudioPlayer::GetPlaybackPositionSeconds() const {
    if (!m_initialized || !m_sourceVoice || m_sampleRate == 0) return 0.0;

    XAUDIO2_VOICE_STATE state;
    m_sourceVoice->GetState(&state);
    return static_cast<double>(state.SamplesPlayed) / m_sampleRate;
}

uint32_t AudioPlayer::GetQueuedBufferCount() const {
    if (!m_initialized || !m_sourceVoice) return 0;

    XAUDIO2_VOICE_STATE state;
    m_sourceVoice->GetState(&state, XAUDIO2_VOICE_NOSAMPLESPLAYED);
    return state.BuffersQueued;
}

double AudioPlayer::GetQueuedDurationSeconds() const {
    if (!m_initialized || !m_sourceVoice || m_sampleRate == 0) return 0.0;

    XAUDIO2_VOICE_STATE state;
    m_sourceVoice->GetState(&state, XAUDIO2_VOICE_NOSAMPLESPLAYED);
    return static_cast<double>(state.BuffersQueued * 1024) / m_sampleRate;
}

} // namespace openmedia::audio

