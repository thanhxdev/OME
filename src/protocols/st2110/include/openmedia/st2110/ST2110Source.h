#pragma once

#include <string>
#include <cstdint>
#include <memory>
#include <span>
#include <functional>
#include <mutex>
#include <openmedia/core/MediaFrame.h>
#include "openmedia/st2110/ST2110Output.h"

namespace openmedia {
namespace st2110 {

class ST2110Source {
public:
    explicit ST2110Source(const ST2110Config& config = {});
    ~ST2110Source();

    bool Connect(const std::string& multicast_ip, int port);
    void Disconnect();
    bool IsConnected() const;

    void Configure(const ST2110Config& config);
    ST2110Config GetConfig() const;

    /// @brief Ingest a raw RTP packet and parse RFC 4175 / RFC 3190
    /// @return Complete MediaFrame if end of frame (marker bit) reached, nullptr otherwise
    std::shared_ptr<core::MediaFrame> IngestRTPPacket(std::span<const uint8_t> packetData);

    void SetFrameReadyCallback(std::function<void(std::shared_ptr<core::MediaFrame>)> callback);

private:
    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

} // namespace st2110
} // namespace openmedia
