#include "openmedia/st2110/ST2110Output.h"
#include <cstring>
#include <algorithm>

namespace openmedia {
namespace st2110 {

static inline void WriteBE16(uint8_t* dst, uint16_t val) {
    dst[0] = static_cast<uint8_t>((val >> 8) & 0xFF);
    dst[1] = static_cast<uint8_t>(val & 0xFF);
}

static inline void WriteBE32(uint8_t* dst, uint32_t val) {
    dst[0] = static_cast<uint8_t>((val >> 24) & 0xFF);
    dst[1] = static_cast<uint8_t>((val >> 16) & 0xFF);
    dst[2] = static_cast<uint8_t>((val >> 8) & 0xFF);
    dst[3] = static_cast<uint8_t>(val & 0xFF);
}

struct ST2110Output::Impl {
    ST2110Config config;
    bool started = false;
    mutable std::mutex mutex;
    uint16_t seqNum = 0;

    explicit Impl(const ST2110Config& cfg) : config(cfg) {}
};

ST2110Output::ST2110Output(const ST2110Config& config)
    : m_impl(std::make_unique<Impl>(config)) {
}

ST2110Output::~ST2110Output() {
    Stop();
}

bool ST2110Output::Start(const std::string& destination_ip, int port) {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    m_impl->config.destinationIp = destination_ip;
    m_impl->config.port = port;
    m_impl->started = true;
    return true;
}

void ST2110Output::Stop() {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    m_impl->started = false;
}

bool ST2110Output::IsStarted() const {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    return m_impl->started;
}

void ST2110Output::Configure(const ST2110Config& config) {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    m_impl->config = config;
}

ST2110Config ST2110Output::GetConfig() const {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    return m_impl->config;
}

std::vector<std::vector<uint8_t>> ST2110Output::EncapsulateVideoRFC4175(const core::MediaFrame& frame) {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    std::vector<std::vector<uint8_t>> packets;

    uint32_t width = frame.GetWidth();
    uint32_t height = frame.GetHeight();
    if (width == 0 || height == 0) return packets;

    const uint8_t* rawData = frame.GetVideoPlane(0);
    int stride = frame.GetLineSize(0);
    if (!rawData || stride <= 0) return packets;

    uint32_t timestamp = static_cast<uint32_t>(frame.GetPts());
    size_t maxPayloadBytes = m_impl->config.mtu > 40 ? (m_impl->config.mtu - 32) : 1200;

    for (uint32_t line = 0; line < height; ++line) {
        const uint8_t* linePtr = rawData + (line * stride);
        size_t lineRemaining = static_cast<size_t>(stride);
        size_t lineOffset = 0;

        while (lineRemaining > 0) {
            size_t chunkSize = std::min(lineRemaining, maxPayloadBytes);
            bool isLastPacketOfFrame = (line == height - 1) && (lineRemaining == chunkSize);

            // 12 bytes RTP Header + 2 bytes Extended Seq + 6 bytes Scanline Header + chunkSize
            std::vector<uint8_t> pkt(12 + 2 + 6 + chunkSize);

            // 1. RTP Header
            pkt[0] = 0x80; // V=2
            pkt[1] = static_cast<uint8_t>((isLastPacketOfFrame ? 0x80 : 0x00) | (m_impl->config.payloadType & 0x7F));
            WriteBE16(&pkt[2], m_impl->seqNum++);
            WriteBE32(&pkt[4], timestamp);
            WriteBE32(&pkt[8], m_impl->config.ssrc);

            // 2. RFC 4175 Header
            WriteBE16(&pkt[12], 0x0000); // Extended sequence number
            WriteBE16(&pkt[14], static_cast<uint16_t>(chunkSize)); // Length
            WriteBE16(&pkt[16], static_cast<uint16_t>(line & 0x7FFF)); // Line number (field 0)
            WriteBE16(&pkt[18], static_cast<uint16_t>(lineOffset & 0x7FFF)); // Offset in line

            // 3. Pixel payload
            std::memcpy(&pkt[20], linePtr + lineOffset, chunkSize);

            packets.push_back(std::move(pkt));

            lineOffset += chunkSize;
            lineRemaining -= chunkSize;
        }
    }

    return packets;
}

std::vector<uint8_t> ST2110Output::EncapsulateAudioRFC3190(const core::MediaFrame& frame) {
    std::lock_guard<std::mutex> lock(m_impl->mutex);

    uint32_t samples = frame.GetAudioSampleCount();
    uint32_t channels = frame.GetAudioChannelCount();
    if (samples == 0 || channels == 0) return {};

    const uint8_t* audioData = frame.GetAudioData();
    if (!audioData) return {};

    uint32_t timestamp = static_cast<uint32_t>(frame.GetPts());
    size_t pcmPayloadSize = static_cast<size_t>(samples) * channels * 3; // 24-bit = 3 bytes per sample

    std::vector<uint8_t> pkt(12 + pcmPayloadSize);

    // 1. RTP Header
    pkt[0] = 0x80;
    pkt[1] = static_cast<uint8_t>(m_impl->config.payloadType & 0x7F); // Audio typical PT = 97
    WriteBE16(&pkt[2], m_impl->seqNum++);
    WriteBE32(&pkt[4], timestamp);
    WriteBE32(&pkt[8], m_impl->config.ssrc);

    // 2. RFC 3190 L24 big-endian PCM payload
    // If incoming audio is already raw PCM, copy into place
    size_t copyLen = std::min(pcmPayloadSize, static_cast<size_t>(frame.GetDataSize()));
    std::memcpy(&pkt[12], audioData, copyLen);

    return pkt;
}

} // namespace st2110
} // namespace openmedia
