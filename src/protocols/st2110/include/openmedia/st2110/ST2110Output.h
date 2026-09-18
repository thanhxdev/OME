#pragma once

#include <string>
#include <cstdint>
#include <vector>
#include <memory>
#include <span>
#include <mutex>
#include <openmedia/core/MediaFrame.h>

namespace openmedia {
namespace st2110 {

enum class EssenceType {
    ST2110_20_Video, // RFC 4175 Uncompressed Video
    ST2110_30_Audio, // RFC 3190 24-bit PCM Audio
    ST2110_40_Ancillary
};

struct ST2110Config {
    std::string destinationIp = "239.255.0.1";
    int port = 20000;
    EssenceType type = EssenceType::ST2110_20_Video;
    uint32_t payloadType = 96;
    uint32_t ssrc = 0x12345678;
    uint16_t mtu = 1460;
};

class ST2110Output {
public:
    explicit ST2110Output(const ST2110Config& config = {});
    ~ST2110Output();

    bool Start(const std::string& destination_ip, int port);
    void Stop();
    bool IsStarted() const;

    void Configure(const ST2110Config& config);
    ST2110Config GetConfig() const;

    /// @brief Encapsulate video frame into a series of RFC 4175 RTP packets
    std::vector<std::vector<uint8_t>> EncapsulateVideoRFC4175(const core::MediaFrame& frame);

    /// @brief Encapsulate audio samples into RFC 3190 24-bit PCM RTP packet
    std::vector<uint8_t> EncapsulateAudioRFC3190(const core::MediaFrame& frame);

private:
    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

} // namespace st2110
} // namespace openmedia
