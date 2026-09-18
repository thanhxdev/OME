#pragma once

#include <string>
#include <cstdint>
#include <cstddef>
#include <vector>
#include <memory>
#include <span>
#include <optional>
#include <mutex>
#include "openmedia/st2022/ST2022Output.h"

namespace openmedia {
namespace st2022 {

class ST2022Source {
public:
    explicit ST2022Source(const ST2022Config& config = {});
    ~ST2022Source();

    bool Connect(const std::string& ip, int port);
    void Disconnect();
    bool IsConnected() const;

    void Configure(const ST2022Config& config);
    ST2022Config GetConfig() const;

    /// @brief Push received media packet at matrix position (row, col)
    void ReceiveMediaPacket(size_t row, size_t col, std::span<const uint8_t> payload);

    /// @brief Push received Row FEC packet for row r
    void ReceiveRowFEC(size_t row, std::span<const uint8_t> fecPayload);

    /// @brief Push received Column FEC packet for column c
    void ReceiveColFEC(size_t col, std::span<const uint8_t> fecPayload);

    /// @brief Trigger 2D-FEC matrix decoding and recover lost packets
    /// @return Number of packets successfully recovered in this cycle
    size_t DecodeAndRecover();

    /// @brief Retrieve recovered or received packet at (row, col)
    std::optional<std::vector<uint8_t>> GetPacket(size_t row, size_t col) const;

    /// @brief Check if packet at (row, col) is available
    bool HasPacket(size_t row, size_t col) const;

    ST2022FECStats GetStats() const;

private:
    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

} // namespace st2022
} // namespace openmedia
