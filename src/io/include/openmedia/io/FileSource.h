#pragma once
#include <openmedia/core/IMediaObject.h>
#include <openmedia/core/MediaFrame.h>
#include <openmedia/io/MediaReader.h>
#include <memory>
#include <string>

namespace openmedia::io {

class FileSource : public core::IMediaObject {
public:
    FileSource();
    ~FileSource() override;

    std::string GetName() const override;
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

    core::VoidResult Open(const std::string& path);
    void Close();

    const std::vector<StreamInfo>& GetStreams() const;
    core::VoidResult Seek(double timestampSeconds);
    core::VoidResult SeekFrame(int64_t frameIndex);

    void SetLoopMode(bool loop);
    bool GetLoopMode() const;

    double GetDurationSeconds() const;
    uint32_t GetBitrate() const;

    void SetOutputResolution(uint32_t width, uint32_t height);

private:
    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

} // namespace openmedia::io

