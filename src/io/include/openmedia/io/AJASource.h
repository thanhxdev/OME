#pragma once

#include <openmedia/core/IMediaObject.h>
#include <memory>
#include <string>

namespace openmedia::io {

class AJASource : public core::IMediaObject {
public:
    explicit AJASource(int deviceIndex = 0);
    ~AJASource() override;

    // IMediaObject
    std::string GetName() const override;
    core::PipelineState GetState() const override;
    core::VoidResult Initialize() override;
    core::VoidResult Start() override;
    core::VoidResult Stop() override;
    core::VoidResult PushFrame(std::shared_ptr<core::MediaFrame> frame) override;
    core::Result<std::shared_ptr<core::MediaFrame>> PullFrame() override;
    core::VoidResult Connect(std::shared_ptr<core::IMediaObject> downstream) override;
    core::VoidResult Disconnect() override;
    void OnStateChange(core::StateChangeCallback callback) override;
    void OnError(core::ErrorCallback callback) override;

private:
    int m_deviceIndex;
    core::PipelineState m_state = core::PipelineState::Idle;
    core::StateChangeCallback m_stateCallback;
    core::ErrorCallback m_errorCallback;
    std::shared_ptr<core::IMediaObject> m_downstream;

    // NTV2 specific card context would go here
    // CNTV2Card m_ntv2Card;
    bool m_isOpen = false;
};

} // namespace openmedia::io
