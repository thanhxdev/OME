#include <openmedia/codecs/H264Encoder_QSV.h>
#include <openmedia/core/Logger.h>

namespace openmedia::codecs {

H264Encoder_QSV::H264Encoder_QSV() {
    m_logger = core::Logger::Get("H264Encoder_QSV");
}

H264Encoder_QSV::~H264Encoder_QSV() {
    (void)Stop();
}

core::VoidResult H264Encoder_QSV::Initialize() {
    std::lock_guard<std::mutex> lock(m_mutex);
    if (m_initialized) return {};
    
    OME_LOG_INFO(m_logger, "Initializing Intel QuickSync H264 Encoder...");
    // TODO: Initialize MFX session and MFXVideoENCODE
    
    m_initialized = true;
    return {};
}

core::VoidResult H264Encoder_QSV::Start() {
    return {};
}

core::VoidResult H264Encoder_QSV::Stop() {
    std::lock_guard<std::mutex> lock(m_mutex);
    if (m_initialized) {
        OME_LOG_INFO(m_logger, "Cleaning up QuickSync resources");
        // TODO: MFXVideoENCODE_Close
        m_mfxSession = nullptr;
        m_initialized = false;
    }
    return {};
}

core::VoidResult H264Encoder_QSV::Configure(const EncoderConfig& config) {
    return {};
}

core::VoidResult H264Encoder_QSV::PushFrame(std::shared_ptr<core::MediaFrame> frame) {
    if (!m_initialized || !frame) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidArgument, "Invalid frame or not initialized"));
    }
    
    OME_LOG_INFO(m_logger, "Pushing frame to QuickSync encoder");
    // TODO: MFXVideoENCODE_EncodeFrameAsync
    
    return {};
}

core::Result<std::shared_ptr<core::MediaFrame>> H264Encoder_QSV::PullFrame() {
    // TODO: Sync operation and extract bitstream
    
    auto packetFrame = core::MediaFrame::CreateVideo(1920, 1080, core::PixelFormat::H264);
    if (!packetFrame) {
        return std::unexpected(core::Error::Make(core::ErrorCode::OutOfMemory, "Failed to allocate output frame"));
    }
    
    // Copy bitstream...
    OME_LOG_INFO(m_logger, "Successfully encoded frame with QuickSync");
    
    return packetFrame;
}

} // namespace openmedia::codecs
