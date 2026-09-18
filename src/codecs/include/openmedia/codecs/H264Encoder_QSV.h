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

    core::VoidResult Connect(std::shared_ptr<core::IMediaObject> downstream) override;
    core::VoidResult Disconnect() override;
    void OnStateChange(core::StateChangeCallback callback) override;
    void OnError(core::ErrorCallback callback) override;

private:
    bool m_initialized = false;
    bool m_running = false;
    bool m_hardwareAvailable = false;
    uint64_t m_frameCounter = 0;
    EncoderConfig m_config;
    std::mutex m_mutex;
    std::shared_ptr<spdlog::logger> m_logger;
    void* m_mfxSession = nullptr; // mfxSession
    void* m_qsvModule = nullptr;  // Dynamic library handle
    std::queue<std::shared_ptr<core::MediaFrame>> m_encodedQueue;
    std::shared_ptr<core::IMediaObject> m_downstream;
    core::StateChangeCallback m_onStateChange;
    core::ErrorCallback m_onError;
};

} // namespace openmedia::codecs
