#pragma once

#include <string>
#include <cstdint>
#include <cstddef>
#include <vector>
#include <memory>
#include <span>
#include <mutex>

namespace openmedia {
namespace st2022 {

struct ST2022Config {
    std::string destinationIp = "127.0.0.1";
    int destinationPort = 50000;
    bool enableFEC = true;
    uint8_t fecColumnsL = 10; // Number of columns L (4 to 20)
    uint8_t fecRowsD = 10;    // Number of rows D (4 to 20)
};

struct ST2022FECStats {
    uint64_t mediaPacketsSent = 0;
    uint64_t rowFecPacketsSent = 0;
    uint64_t colFecPacketsSent = 0;
    uint64_t mediaPacketsReceived = 0;
    uint64_t lostPackets = 0;
    uint64_t recoveredPackets = 0;
};

class ST2022Output {
public:
    explicit ST2022Output(const ST2022Config& config = {});
    ~ST2022Output();

    bool Start(const std::string& ip, int port);
    void Stop();
    bool IsStarted() const;

    void Configure(const ST2022Config& config);
    ST2022Config GetConfig() const;

    /// @brief Push an MPEG-TS/RTP packet into 2D-FEC matrix encoder
    /// @return Generated FEC packets if a block completed (row or column)
    std::vector<std::vector<uint8_t>> PushMediaPacket(uint16_t seqNum, std::span<const uint8_t> payload);

    ST2022FECStats GetStats() const;

private:
    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

} // namespace st2022
} // namespace openmedia
