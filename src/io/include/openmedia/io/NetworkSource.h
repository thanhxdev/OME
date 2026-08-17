#pragma once
#include <openmedia/core/IMediaObject.h>
#include <openmedia/core/MediaFrame.h>
#include <memory>
#include <string>
#include <vector>

namespace openmedia::io {

enum class NetworkProtocol {
    Unknown,
    RTMP,
    RTSP,
    HLS,
    MPEG_TS,
    SRT,
    HTTP
};

class NetworkSource : public core::IMediaObject {
public:
    NetworkSource();
    ~NetworkSource() override;

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

    core::VoidResult OpenURL(const std::string& url);
    void Close();

    NetworkProtocol GetDetectedProtocol() const;

private:
    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

} // namespace openmedia::io
