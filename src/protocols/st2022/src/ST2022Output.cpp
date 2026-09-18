#include "openmedia/st2022/ST2022Output.h"
#include <algorithm>

namespace openmedia {
namespace st2022 {

static void XORBuffers(std::vector<uint8_t>& dst, std::span<const uint8_t> src) {
    if (dst.empty()) {
        dst.assign(src.begin(), src.end());
        return;
    }
    if (dst.size() < src.size()) {
        dst.resize(src.size(), 0);
    }
    for (size_t i = 0; i < src.size(); ++i) {
        dst[i] ^= src[i];
    }
}

struct ST2022Output::Impl {
    ST2022Config config;
    bool started = false;
    mutable std::mutex mutex;
    ST2022FECStats stats;

    std::vector<std::vector<uint8_t>> rowAccumulators;
    std::vector<std::vector<uint8_t>> colAccumulators;
    size_t currentMatrixIndex = 0;

    explicit Impl(const ST2022Config& cfg) : config(cfg) {
        ResetMatrix();
    }

    void ResetMatrix() {
        rowAccumulators.assign(config.fecRowsD, {});
        colAccumulators.assign(config.fecColumnsL, {});
        currentMatrixIndex = 0;
    }

    std::vector<std::vector<uint8_t>> Push(uint16_t /*seqNum*/, std::span<const uint8_t> payload) {
        std::lock_guard<std::mutex> lock(mutex);
        std::vector<std::vector<uint8_t>> generatedFEC;

        stats.mediaPacketsSent++;
        if (!config.enableFEC || config.fecColumnsL == 0 || config.fecRowsD == 0) {
            return generatedFEC;
        }

        size_t matrixSize = static_cast<size_t>(config.fecColumnsL) * config.fecRowsD;
        size_t r = (currentMatrixIndex / config.fecColumnsL) % config.fecRowsD;
        size_t c = currentMatrixIndex % config.fecColumnsL;

        // XOR into row and col accumulators
        XORBuffers(rowAccumulators[r], payload);
        XORBuffers(colAccumulators[c], payload);

        // Row FEC packet ready at the end of each row
        if (c == config.fecColumnsL - 1) {
            std::vector<uint8_t> rowFecPacket = rowAccumulators[r];
            // Format FEC Header (Type 0x01: Row FEC, row index r)
            std::vector<uint8_t> fecHeader = { 0x80, 0x60, static_cast<uint8_t>(r), static_cast<uint8_t>(config.fecColumnsL) };
            rowFecPacket.insert(rowFecPacket.begin(), fecHeader.begin(), fecHeader.end());
            generatedFEC.push_back(std::move(rowFecPacket));
            stats.rowFecPacketsSent++;
        }

        currentMatrixIndex++;

        // When matrix is full, emit Column FEC packets
        if (currentMatrixIndex >= matrixSize) {
            for (size_t col = 0; col < config.fecColumnsL; ++col) {
                std::vector<uint8_t> colFecPacket = colAccumulators[col];
                // Format FEC Header (Type 0x02: Col FEC, col index col)
                std::vector<uint8_t> fecHeader = { 0x80, 0x61, static_cast<uint8_t>(col), static_cast<uint8_t>(config.fecRowsD) };
                colFecPacket.insert(colFecPacket.begin(), fecHeader.begin(), fecHeader.end());
                generatedFEC.push_back(std::move(colFecPacket));
                stats.colFecPacketsSent++;
            }
            ResetMatrix();
        }

        return generatedFEC;
    }
};

ST2022Output::ST2022Output(const ST2022Config& config)
    : m_impl(std::make_unique<Impl>(config)) {
}

ST2022Output::~ST2022Output() {
    Stop();
}

bool ST2022Output::Start(const std::string& ip, int port) {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    m_impl->config.destinationIp = ip;
    m_impl->config.destinationPort = port;
    m_impl->started = true;
    return true;
}

void ST2022Output::Stop() {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    m_impl->started = false;
    m_impl->ResetMatrix();
}

bool ST2022Output::IsStarted() const {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    return m_impl->started;
}

void ST2022Output::Configure(const ST2022Config& config) {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    m_impl->config = config;
    m_impl->ResetMatrix();
}

ST2022Config ST2022Output::GetConfig() const {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    return m_impl->config;
}

std::vector<std::vector<uint8_t>> ST2022Output::PushMediaPacket(uint16_t seqNum, std::span<const uint8_t> payload) {
    return m_impl->Push(seqNum, payload);
}

ST2022FECStats ST2022Output::GetStats() const {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    return m_impl->stats;
}

} // namespace st2022
} // namespace openmedia
