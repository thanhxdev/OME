#include "openmedia/st2022/ST2022Source.h"
#include <algorithm>

namespace openmedia {
namespace st2022 {

static std::vector<uint8_t> XORCompute(const std::vector<uint8_t>& a, const std::vector<uint8_t>& b) {
    size_t maxLen = std::max(a.size(), b.size());
    std::vector<uint8_t> res(maxLen, 0);
    for (size_t i = 0; i < a.size(); ++i) res[i] ^= a[i];
    for (size_t i = 0; i < b.size(); ++i) res[i] ^= b[i];
    return res;
}

struct ST2022Source::Impl {
    ST2022Config config;
    bool connected = false;
    mutable std::mutex mutex;
    ST2022FECStats stats;

    std::vector<std::vector<std::optional<std::vector<uint8_t>>>> mediaMatrix;
    std::vector<std::optional<std::vector<uint8_t>>> rowFEC;
    std::vector<std::optional<std::vector<uint8_t>>> colFEC;

    explicit Impl(const ST2022Config& cfg) : config(cfg) {
        Reset();
    }

    void Reset() {
        mediaMatrix.assign(config.fecRowsD, std::vector<std::optional<std::vector<uint8_t>>>(config.fecColumnsL, std::nullopt));
        rowFEC.assign(config.fecRowsD, std::nullopt);
        colFEC.assign(config.fecColumnsL, std::nullopt);
    }

    size_t Decode() {
        size_t totalRecovered = 0;
        bool progress = true;

        while (progress) {
            progress = false;

            // Row pass
            for (size_t r = 0; r < config.fecRowsD; ++r) {
                if (!rowFEC[r].has_value()) continue;

                size_t missingCount = 0;
                size_t missingCol = 0;
                for (size_t c = 0; c < config.fecColumnsL; ++c) {
                    if (!mediaMatrix[r][c].has_value()) {
                        missingCount++;
                        missingCol = c;
                    }
                }

                if (missingCount == 1) {
                    // Strip 4-byte FEC header if present
                    const auto& rawFec = *rowFEC[r];
                    std::vector<uint8_t> fecPayload = (rawFec.size() > 4 && rawFec[0] == 0x80) 
                        ? std::vector<uint8_t>(rawFec.begin() + 4, rawFec.end()) 
                        : rawFec;

                    std::vector<uint8_t> recovered = fecPayload;
                    for (size_t c = 0; c < config.fecColumnsL; ++c) {
                        if (c != missingCol && mediaMatrix[r][c].has_value()) {
                            recovered = XORCompute(recovered, *mediaMatrix[r][c]);
                        }
                    }

                    mediaMatrix[r][missingCol] = std::move(recovered);
                    stats.recoveredPackets++;
                    totalRecovered++;
                    progress = true;
                }
            }

            // Column pass
            for (size_t c = 0; c < config.fecColumnsL; ++c) {
                if (!colFEC[c].has_value()) continue;

                size_t missingCount = 0;
                size_t missingRow = 0;
                for (size_t r = 0; r < config.fecRowsD; ++r) {
                    if (!mediaMatrix[r][c].has_value()) {
                        missingCount++;
                        missingRow = r;
                    }
                }

                if (missingCount == 1) {
                    const auto& rawFec = *colFEC[c];
                    std::vector<uint8_t> fecPayload = (rawFec.size() > 4 && rawFec[0] == 0x80)
                        ? std::vector<uint8_t>(rawFec.begin() + 4, rawFec.end())
                        : rawFec;

                    std::vector<uint8_t> recovered = fecPayload;
                    for (size_t r = 0; r < config.fecRowsD; ++r) {
                        if (r != missingRow && mediaMatrix[r][c].has_value()) {
                            recovered = XORCompute(recovered, *mediaMatrix[r][c]);
                        }
                    }

                    mediaMatrix[missingRow][c] = std::move(recovered);
                    stats.recoveredPackets++;
                    totalRecovered++;
                    progress = true;
                }
            }
        }

        return totalRecovered;
    }
};

ST2022Source::ST2022Source(const ST2022Config& config)
    : m_impl(std::make_unique<Impl>(config)) {
}

ST2022Source::~ST2022Source() {
    Disconnect();
}

bool ST2022Source::Connect(const std::string& ip, int port) {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    m_impl->config.destinationIp = ip;
    m_impl->config.destinationPort = port;
    m_impl->connected = true;
    return true;
}

void ST2022Source::Disconnect() {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    m_impl->connected = false;
    m_impl->Reset();
}

bool ST2022Source::IsConnected() const {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    return m_impl->connected;
}

void ST2022Source::Configure(const ST2022Config& config) {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    m_impl->config = config;
    m_impl->Reset();
}

ST2022Config ST2022Source::GetConfig() const {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    return m_impl->config;
}

void ST2022Source::ReceiveMediaPacket(size_t row, size_t col, std::span<const uint8_t> payload) {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    if (row < m_impl->config.fecRowsD && col < m_impl->config.fecColumnsL) {
        m_impl->mediaMatrix[row][col] = std::vector<uint8_t>(payload.begin(), payload.end());
        m_impl->stats.mediaPacketsReceived++;
    }
}

void ST2022Source::ReceiveRowFEC(size_t row, std::span<const uint8_t> fecPayload) {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    if (row < m_impl->config.fecRowsD) {
        m_impl->rowFEC[row] = std::vector<uint8_t>(fecPayload.begin(), fecPayload.end());
    }
}

void ST2022Source::ReceiveColFEC(size_t col, std::span<const uint8_t> fecPayload) {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    if (col < m_impl->config.fecColumnsL) {
        m_impl->colFEC[col] = std::vector<uint8_t>(fecPayload.begin(), fecPayload.end());
    }
}

size_t ST2022Source::DecodeAndRecover() {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    return m_impl->Decode();
}

std::optional<std::vector<uint8_t>> ST2022Source::GetPacket(size_t row, size_t col) const {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    if (row < m_impl->config.fecRowsD && col < m_impl->config.fecColumnsL) {
        return m_impl->mediaMatrix[row][col];
    }
    return std::nullopt;
}

bool ST2022Source::HasPacket(size_t row, size_t col) const {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    if (row < m_impl->config.fecRowsD && col < m_impl->config.fecColumnsL) {
        return m_impl->mediaMatrix[row][col].has_value();
    }
    return false;
}

ST2022FECStats ST2022Source::GetStats() const {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    return m_impl->stats;
}

} // namespace st2022
} // namespace openmedia
