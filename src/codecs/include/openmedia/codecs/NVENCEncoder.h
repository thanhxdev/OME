/// @file NVENCEncoder.h
/// @brief Native NVIDIA Video Codec SDK hardware encoder (H.264 / HEVC)
///
/// Uses the NVENC API directly for maximum performance and zero-copy GPU encoding.
/// Falls back gracefully when NVIDIA hardware is unavailable.
#pragma once

#include <openmedia/codecs/IEncoder.h>
#include <openmedia/codecs/NVENCCapabilities.h>

#include <string>
#include <memory>

namespace openmedia::codecs {

/// @brief Codec selection for NVENCEncoder
enum class NVENCCodec {
    H264,
    HEVC
};

/// @brief NVENC tuning mode
enum class NVENCTuning {
    HighQuality,   ///< Optimised for visual quality (file recording)
    LowLatency,    ///< Minimal latency (live streaming)
    UltraLowLatency, ///< Sub-frame latency (interactive / cloud gaming)
    Lossless       ///< Mathematically lossless (archival)
};

/// @brief Native NVENC hardware encoder
///
/// Directly calls nvEncodeAPI for H.264 or HEVC hardware encoding on
/// NVIDIA GPUs.  Accepts NV12 CPU frames or CUDA device pointers for
/// zero-copy GPU-to-encode pipelines.
///
/// @code
/// auto enc = std::make_shared<NVENCEncoder>(NVENCCodec::H264);
/// EncoderConfig cfg;
/// cfg.width = 1920; cfg.height = 1080; cfg.fps = 30;
/// cfg.bitrate = 5'000'000;
/// enc->Configure(cfg);
/// enc->Initialize();
/// enc->Start();
/// enc->PushFrame(nv12Frame);
/// auto pkt = enc->PullFrame();
/// @endcode
class NVENCEncoder : public IEncoder {
public:
    /// @param codec  H.264 or HEVC
    /// @param deviceId  CUDA device ordinal (default 0)
    explicit NVENCEncoder(NVENCCodec codec, int deviceId = 0);
    ~NVENCEncoder() override;

    // Prevent copy
    NVENCEncoder(const NVENCEncoder&) = delete;
    NVENCEncoder& operator=(const NVENCEncoder&) = delete;

    // --- IMediaObject ---
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
    void Flush() override;

    // --- IEncoder ---
    core::VoidResult Configure(const EncoderConfig& config) override;

    /// @brief Set tuning mode (call before Initialize)
    void SetTuning(NVENCTuning tuning);

    /// @brief Set the number of B-frames (0 = disabled, default)
    void SetBFrames(int count);

    /// @brief Enable NVENC lookahead (increases quality, adds latency)
    void SetLookahead(int frames);

    /// @brief Query whether a specific NVENC codec is available on the current GPU
    static bool IsAvailable(NVENCCodec codec, int deviceId = 0);

private:
    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

} // namespace openmedia::codecs
