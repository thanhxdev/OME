/// @file FFmpegNVENCEncoder.h
#pragma once

#include <openmedia/codecs/IEncoder.h>

#include <string>
#include <memory>
#include <vector>

namespace openmedia::codecs {

class FFmpegNVENCEncoder : public IEncoder {
public:
    explicit FFmpegNVENCEncoder(bool isHevc = false);
    ~FFmpegNVENCEncoder() override;

    // IMediaObject
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

    // IEncoder
    core::VoidResult Configure(const EncoderConfig& config) override;

private:
    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

} // namespace openmedia::codecs
