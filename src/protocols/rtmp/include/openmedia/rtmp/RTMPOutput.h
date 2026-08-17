#pragma once
#include <openmedia/core/IMediaObject.h>
#include <openmedia/core/MediaFrame.h>
#include <memory>
#include <string>

namespace openmedia::rtmp {

class RTMPOutput : public core::IMediaObject {
public:
    RTMPOutput();
    ~RTMPOutput() override;

    std::string GetName() const override { return "RTMPOutput"; }
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

    core::VoidResult AddVideoStream(int width, int height, int fps, const std::string& codecName = "libx264");
    core::VoidResult AddAudioStream(int sampleRate, int channels, const std::string& codecName = "aac");

    core::VoidResult Open(const std::string& rtmpUrl);
    void Close();

private:
    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

} // namespace openmedia::rtmp

