#pragma once
#include <openmedia/core/IMediaObject.h>
#include <openmedia/core/MediaFrame.h>
#include <openmedia/io/MediaReader.h>
#include <memory>
#include <string>

namespace openmedia::rtmp {

class RTMPSource : public core::IMediaObject {
public:
    RTMPSource();
    ~RTMPSource() override;

    std::string GetName() const override { return "RTMPSource"; }
    core::PipelineState GetState() const override;

    core::VoidResult Initialize() override;
    core::VoidResult Start() override;
    core::VoidResult Stop() override;

    core::VoidResult PushFrame(std::shared_ptr<core::MediaFrame> frame) override;
    core::Result<std::shared_ptr<core::MediaFrame>> PullFrame() override;

    core::Result<std::shared_ptr<core::MediaFrame>> PullVideoFrame();
    core::Result<std::shared_ptr<core::MediaFrame>> PullAudioFrame();

    core::VoidResult Connect(std::shared_ptr<core::IMediaObject> downstream) override;
    core::VoidResult Disconnect() override;
    void OnStateChange(core::StateChangeCallback callback) override;
    void OnError(core::ErrorCallback callback) override;

    core::VoidResult Open(const std::string& rtmpUrl);
    void Close();

    const std::vector<io::StreamInfo>& GetStreams() const;

private:
    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

} // namespace openmedia::rtmp

