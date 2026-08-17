#pragma once
#include <openmedia/core/IMediaObject.h>
#include <openmedia/overlay/IOverlay.h>
#include <memory>
#include <vector>
#include <mutex>
#include <map>

// Forward declarations for FFmpeg structures
struct AVFilterGraph;
struct AVFilterContext;

namespace openmedia::overlay {

class OverlayEngine : public core::IMediaObject {
public:
    OverlayEngine();
    ~OverlayEngine() override;

    // IMediaObject implementation
    std::string GetName() const override { return "OverlayEngine"; }
    core::PipelineState GetState() const override { return m_state; }
    core::VoidResult Initialize() override;
    core::VoidResult Start() override;
    core::VoidResult Stop() override;
    core::VoidResult PushFrame(std::shared_ptr<core::MediaFrame> frame) override;
    core::Result<std::shared_ptr<core::MediaFrame>> PullFrame() override;
    core::VoidResult Connect(std::shared_ptr<IMediaObject> downstream) override;
    core::VoidResult Disconnect() override;
    void OnStateChange(core::StateChangeCallback callback) override { m_stateCallback = std::move(callback); }
    void OnError(core::ErrorCallback callback) override { m_errorCallback = std::move(callback); }
    void Flush() override;

    // Overlay Management
    void AddOverlay(std::shared_ptr<IOverlay> overlay);
    void RemoveOverlay(const std::string& id);
    void ClearOverlays();
    std::shared_ptr<IOverlay> GetOverlay(const std::string& id);
    
    // Updates the FFmpeg filter graph based on current overlays. 
    // Usually called internally when overlays are added/removed/modified.
    core::VoidResult RebuildFilterGraph(uint32_t width, uint32_t height, int format, int timebaseNum, int timebaseDen);

private:
    void ChangeState(core::PipelineState newState);
    core::VoidResult ProcessFrame(std::shared_ptr<core::MediaFrame> frame);

    core::PipelineState m_state = core::PipelineState::Idle;
    core::StateChangeCallback m_stateCallback;
    core::ErrorCallback m_errorCallback;
    std::shared_ptr<IMediaObject> m_downstream;

    std::mutex m_mutex;
    std::vector<std::shared_ptr<IOverlay>> m_overlays;
    bool m_graphNeedsRebuild = true;

    // FFmpeg state
    AVFilterGraph* m_filterGraph = nullptr;
    AVFilterContext* m_buffersrcCtx = nullptr;
    AVFilterContext* m_buffersinkCtx = nullptr;
    
    uint32_t m_currentWidth = 0;
    uint32_t m_currentHeight = 0;
    int m_currentFormat = -1;
};

} // namespace openmedia::overlay
