#include "openmedia/st2110/ST2110Source.h"
#include <cstring>

namespace openmedia {
namespace st2110 {

static inline uint16_t ReadBE16(const uint8_t* src) {
    return static_cast<uint16_t>((src[0] << 8) | src[1]);
}

static inline uint32_t ReadBE32(const uint8_t* src) {
    return (static_cast<uint32_t>(src[0]) << 24) |
           (static_cast<uint32_t>(src[1]) << 16) |
           (static_cast<uint32_t>(src[2]) << 8)  |
            static_cast<uint32_t>(src[3]);
}

struct ST2110Source::Impl {
    ST2110Config config;
    bool connected = false;
    mutable std::mutex mutex;
    std::function<void(std::shared_ptr<core::MediaFrame>)> frameCallback;

    std::shared_ptr<core::MediaFrame> currentVideoFrame;
    uint32_t currentFramePts = 0;

    explicit Impl(const ST2110Config& cfg) : config(cfg) {
        // Default 1080p frame buffer for assembly
        currentVideoFrame = core::MediaFrame::CreateVideo(1920, 1080, core::PixelFormat::BGRA);
    }

    std::shared_ptr<core::MediaFrame> ProcessRTP(std::span<const uint8_t> packetData) {
        if (packetData.size() < 12) return nullptr;

        const uint8_t* data = packetData.data();
        bool marker = (data[1] & 0x80) != 0;
        uint32_t timestamp = ReadBE32(&data[4]);

        if (config.type == EssenceType::ST2110_30_Audio) {
            // RFC 3190 PCM Audio
            size_t audioPayloadSize = packetData.size() - 12;
            uint32_t channels = 2;
            uint32_t bytesPerSample = 3; // 24-bit
            uint32_t samples = static_cast<uint32_t>(audioPayloadSize / (channels * bytesPerSample));
            if (samples == 0) return nullptr;

            auto audioFrame = core::MediaFrame::CreateAudio(samples, channels, core::SampleFormat::S24, 48000);
            if (audioFrame && audioFrame->GetAudioData()) {
                std::memcpy(audioFrame->GetAudioData(), data + 12, audioPayloadSize);
                audioFrame->SetPts(timestamp);
                if (frameCallback) frameCallback(audioFrame);
                return audioFrame;
            }
            return nullptr;
        }

        // ST 2110-20 Video (RFC 4175)
        if (packetData.size() < 20) return nullptr;

        uint16_t length = ReadBE16(&data[14]);
        uint16_t line = ReadBE16(&data[16]) & 0x7FFF;
        uint16_t offset = ReadBE16(&data[18]) & 0x7FFF;

        if (packetData.size() < 20ULL + length) return nullptr;

        if (!currentVideoFrame) {
            currentVideoFrame = core::MediaFrame::CreateVideo(1920, 1080, core::PixelFormat::BGRA);
        }

        if (line < currentVideoFrame->GetHeight()) {
            uint8_t* plane = currentVideoFrame->GetVideoPlane(0);
            int stride = currentVideoFrame->GetLineSize(0);
            if (plane && stride > 0) {
                size_t destOffset = static_cast<size_t>(line) * stride + offset;
                size_t maxCopy = static_cast<size_t>(stride) > offset ? static_cast<size_t>(stride) - offset : 0;
                size_t actualCopy = std::min(static_cast<size_t>(length), maxCopy);
                std::memcpy(plane + destOffset, data + 20, actualCopy);
            }
        }

        currentFramePts = timestamp;

        if (marker) {
            // Complete frame arrived
            currentVideoFrame->SetPts(currentFramePts);
            auto completedFrame = currentVideoFrame;
            currentVideoFrame = core::MediaFrame::CreateVideo(1920, 1080, core::PixelFormat::BGRA);
            if (frameCallback) frameCallback(completedFrame);
            return completedFrame;
        }

        return nullptr;
    }
};

ST2110Source::ST2110Source(const ST2110Config& config)
    : m_impl(std::make_unique<Impl>(config)) {
}

ST2110Source::~ST2110Source() {
    Disconnect();
}

bool ST2110Source::Connect(const std::string& multicast_ip, int port) {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    m_impl->config.destinationIp = multicast_ip;
    m_impl->config.port = port;
    m_impl->connected = true;
    return true;
}

void ST2110Source::Disconnect() {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    m_impl->connected = false;
}

bool ST2110Source::IsConnected() const {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    return m_impl->connected;
}

void ST2110Source::Configure(const ST2110Config& config) {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    m_impl->config = config;
}

ST2110Config ST2110Source::GetConfig() const {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    return m_impl->config;
}

std::shared_ptr<core::MediaFrame> ST2110Source::IngestRTPPacket(std::span<const uint8_t> packetData) {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    return m_impl->ProcessRTP(packetData);
}

void ST2110Source::SetFrameReadyCallback(std::function<void(std::shared_ptr<core::MediaFrame>)> callback) {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    m_impl->frameCallback = std::move(callback);
}

} // namespace st2110
} // namespace openmedia
