#pragma once

#include <openmedia/core/IMediaObject.h>
#include <openmedia/core/MediaFrame.h>
#include <memory>
#include <mutex>
#include <spdlog/spdlog.h>

namespace openmedia::webrtc {

class WebRTCOutput : public core::IMediaObject {
public:
    WebRTCOutput();
    ~WebRTCOutput() override;

    std::string GetName() const override { return "WebRTCOutput"; }
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

    core::VoidResult Open(const std::string& signalingUri);
    void Close();

private:
    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

} // namespace openmedia::webrtc
