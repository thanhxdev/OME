#pragma once
#include <openmedia/core/ErrorCodes.h>
#include <openmedia/core/MediaFrame.h>
#include <openmedia/core/IMediaObject.h>
#include <memory>
#include <vector>
#include <mutex>
#include <thread>
#include <atomic>
#include <string>

#include <openmedia/gpu/GPUContext.h>

namespace openmedia::mixer {

class MixerLayer;

class Mixer : public core::IMediaObject {
public:
    Mixer();
    ~Mixer() override;

    core::Result<void> SetOutputFormat(int outputWidth, int outputHeight, int fps);
    
    // GPU Context
    void SetGPUContext(std::shared_ptr<gpu::IGPUContext> context) { m_gpuContext = context; }
    std::shared_ptr<gpu::IGPUContext> GetGPUContext() const { return m_gpuContext; }

    core::Result<void> AddLayer(std::shared_ptr<MixerLayer> layer);
    core::Result<void> RemoveLayer(int layerId);
    
    // IMediaObject implementation
    std::string GetName() const override;
    core::PipelineState GetState() const override;
    core::VoidResult Initialize() override;
    core::VoidResult Start() override;
    core::VoidResult Stop() override;
    
    // Pushing frame to Mixer is not supported (it acts as a source by pulling from layers)
    core::VoidResult PushFrame(std::shared_ptr<core::MediaFrame> frame) override;
    [[nodiscard]] core::Result<std::shared_ptr<core::MediaFrame>> PullFrame() override;
    
    core::VoidResult Connect(std::shared_ptr<core::IMediaObject> downstream) override;
    core::VoidResult Disconnect() override;
    void OnStateChange(core::StateChangeCallback callback) override;
    void OnError(core::ErrorCallback callback) override;

private:
    void MixThreadLoop();
    void RenderFrame(std::shared_ptr<core::MediaFrame>& outputFrame);
    void RenderFrameGPU(std::shared_ptr<core::MediaFrame>& outputFrame);

    int m_outputWidth;
    int m_outputHeight;
    int m_fps;
    
    mutable std::mutex m_mutex;
    std::vector<std::shared_ptr<MixerLayer>> m_layers;
    std::shared_ptr<gpu::IGPUContext> m_gpuContext;
    
    core::PipelineState m_state;
    std::shared_ptr<core::IMediaObject> m_downstream;
    core::StateChangeCallback m_onStateChange;
    core::ErrorCallback m_onError;

    std::thread m_mixThread;
    std::atomic<bool> m_stopRequested;
};

} // namespace openmedia::mixer
