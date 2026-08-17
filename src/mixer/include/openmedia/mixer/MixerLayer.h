#pragma once
#include <openmedia/core/ErrorCodes.h>
#include <openmedia/core/MediaFrame.h>
#include <openmedia/core/IMediaObject.h>
#include <memory>
#include <mutex>
#include <string>

namespace openmedia::mixer {

class MixerLayer : public core::IMediaObject {
public:
    MixerLayer(int id, const std::string& name = "MixerLayer");
    ~MixerLayer() override;

    void SetPosition(int x, int y);
    void SetSize(int width, int height);
    void SetOpacity(float opacity);
    void SetVisible(bool visible);
    void SetZIndex(int zIndex);

    int GetId() const { return m_id; }
    std::shared_ptr<core::MediaFrame> GetCurrentFrame() const;
    float GetOpacity() const { return m_opacity; }
    bool IsVisible() const { return m_visible; }
    int GetZIndex() const { return m_zIndex; }
    int GetX() const { return m_x; }
    int GetY() const { return m_y; }
    int GetWidth() const { return m_width; }
    int GetHeight() const { return m_height; }
    
    // IMediaObject implementation
    std::string GetName() const override;
    core::PipelineState GetState() const override;
    core::VoidResult Initialize() override;
    core::VoidResult Start() override;
    core::VoidResult Stop() override;
    core::VoidResult PushFrame(std::shared_ptr<core::MediaFrame> frame) override;
    [[nodiscard]] core::Result<std::shared_ptr<core::MediaFrame>> PullFrame() override;
    core::VoidResult Connect(std::shared_ptr<core::IMediaObject> downstream) override;
    core::VoidResult Disconnect() override;
    void OnStateChange(core::StateChangeCallback callback) override;
    void OnError(core::ErrorCallback callback) override;

private:
    int m_id;
    std::string m_name;
    int m_x, m_y;
    int m_width, m_height;
    int m_zIndex;
    float m_opacity;
    bool m_visible;
    
    mutable std::mutex m_mutex;
    std::shared_ptr<core::MediaFrame> m_currentFrame;
    core::PipelineState m_state;
    core::StateChangeCallback m_onStateChange;
    core::ErrorCallback m_onError;
};

} // namespace openmedia::mixer
