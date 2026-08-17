#pragma once
#include <openmedia/core/IMediaObject.h>
#include <openmedia/audio/AudioEngine.h>
#include <vector>
#include <memory>
#include <mutex>
#include <string>
#include <map>

struct AVFilterGraph;
struct AVFilterContext;

namespace openmedia::audio {

class AudioMixer : public core::IMediaObject {
public:
    AudioMixer(std::shared_ptr<AudioEngine> engine);
    ~AudioMixer() override;

    // IMediaObject implementation
    std::string GetName() const override { return "AudioMixer"; }
    core::PipelineState GetState() const override { return m_state; }
    core::VoidResult Initialize() override;
    core::VoidResult Start() override;
    core::VoidResult Stop() override;
    
    core::VoidResult PushFrame(std::shared_ptr<core::MediaFrame> frame) override;
    core::VoidResult PushFrame(std::shared_ptr<core::MediaFrame> frame, int streamId);
    
    core::Result<std::shared_ptr<core::MediaFrame>> PullFrame() override;
    core::VoidResult Connect(std::shared_ptr<IMediaObject> downstream) override;
    core::VoidResult Disconnect() override;
    void OnStateChange(core::StateChangeCallback callback) override { m_stateCallback = std::move(callback); }
    void OnError(core::ErrorCallback callback) override { m_errorCallback = std::move(callback); }
    void Flush() override;

    // Mixer Management
    void AddInput(int streamId);
    void RemoveInput(int streamId);
    void SetInputVolume(int streamId, float volume);
    void SetInputMute(int streamId, bool mute);
    void SetInputPan(int streamId, float pan);
    void SetInputSolo(int streamId, bool solo);

private:
    void ChangeState(core::PipelineState newState);
    core::VoidResult RebuildFilterGraph();
    core::VoidResult ProcessOutput();

    std::shared_ptr<AudioEngine> m_engine;
    core::PipelineState m_state = core::PipelineState::Idle;
    core::StateChangeCallback m_stateCallback;
    core::ErrorCallback m_errorCallback;
    std::shared_ptr<IMediaObject> m_downstream;

    struct InputNode {
        int streamId;
        float volume = 1.0f;
        float pan = 0.0f; // -1.0 (left) to 1.0 (right)
        bool muted = false;
        bool solo = false;
        AVFilterContext* buffersrcCtx = nullptr;
    };

    std::mutex m_mutex;
    std::map<int, InputNode> m_inputs;
    bool m_graphNeedsRebuild = true;

    AVFilterGraph* m_filterGraph = nullptr;
    AVFilterContext* m_buffersinkCtx = nullptr;
};

} // namespace openmedia::audio
