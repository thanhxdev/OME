#pragma once

#include <openmedia/codecs/IEncoder.h>
#include <openmedia/core/MediaFrame.h>
#include <openmedia/core/Config.h>
#include <mutex>

namespace openmedia::codecs {

class H264Encoder_QSV : public IEncoder {
public:
    H264Encoder_QSV();
    ~H264Encoder_QSV() override;

    std::string GetName() const override { return "H264Encoder_QSV"; }
    core::PipelineState GetState() const override { return m_initialized ? core::PipelineState::Running : core::PipelineState::Stopped; }

    core::VoidResult Initialize() override;
    core::VoidResult Start() override;
    core::VoidResult Stop() override;
    core::VoidResult Configure(const EncoderConfig& config) override;
    core::VoidResult PushFrame(std::shared_ptr<core::MediaFrame> frame) override;
    [[nodiscard]] core::Result<std::shared_ptr<core::MediaFrame>> PullFrame() override;

    core::VoidResult Connect(std::shared_ptr<core::IMediaObject> /*downstream*/) override { return {}; }
    core::VoidResult Disconnect() override { return {}; }
    void OnStateChange(core::StateChangeCallback /*callback*/) override {}
    void OnError(core::ErrorCallback /*callback*/) override {}

private:
    bool m_initialized = false;
    std::mutex m_mutex;
    std::shared_ptr<spdlog::logger> m_logger;
    void* m_mfxSession = nullptr; // mfxSession
};

} // namespace openmedia::codecs
