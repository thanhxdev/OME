/// @file test_st2022_fec.cpp
/// @brief Unit test verifying SMPTE ST 2022-1 2D-FEC Matrix generation and iterative recovery.

#include <gtest/gtest.h>
#include "openmedia/st2022/ST2022Output.h"
#include "openmedia/st2022/ST2022Source.h"

#include <vector>

using namespace openmedia::st2022;

class ST2022FECTest : public ::testing::Test {
protected:
    void SetUp() override {
        ST2022Config config;
        config.enableFEC = true;
        config.fecColumnsL = 4;
        config.fecRowsD = 4;

        m_output = std::make_unique<ST2022Output>(config);
        m_source = std::make_unique<ST2022Source>(config);
    }

    std::unique_ptr<ST2022Output> m_output;
    std::unique_ptr<ST2022Source> m_source;
};

TEST_F(ST2022FECTest, MatrixEncodingAndSingleLossRecovery) {
    constexpr size_t L = 4;
    constexpr size_t D = 4;
    constexpr size_t totalPackets = L * D;

    std::vector<std::vector<uint8_t>> originalPackets(totalPackets);
    std::vector<std::vector<uint8_t>> rowFecPackets;
    std::vector<std::vector<uint8_t>> colFecPackets;

    // Generate 16 test packets (each 188 bytes MPEG-TS)
    for (size_t i = 0; i < totalPackets; ++i) {
        originalPackets[i].resize(188);
        originalPackets[i][0] = 0x47; // TS sync byte
        for (size_t b = 1; b < 188; ++b) {
            originalPackets[i][b] = static_cast<uint8_t>((i * 13 + b * 7) & 0xFF);
        }

        auto fecs = m_output->PushMediaPacket(static_cast<uint16_t>(i + 1), originalPackets[i]);
        for (auto& fec : fecs) {
            // First 4 FECs are row FECs, subsequent 4 are column FECs
            if (rowFecPackets.size() < D) {
                rowFecPackets.push_back(std::move(fec));
            } else {
                colFecPackets.push_back(std::move(fec));
            }
        }
    }

    EXPECT_EQ(rowFecPackets.size(), D);
    EXPECT_EQ(colFecPackets.size(), L);

    // Simulate sending packets to Source, but drop packet at row=1, col=2 (index 1*4 + 2 = 6)
    size_t droppedRow = 1;
    size_t droppedCol = 2;
    size_t droppedIdx = droppedRow * L + droppedCol;

    for (size_t r = 0; r < D; ++r) {
        for (size_t c = 0; c < L; ++c) {
            size_t idx = r * L + c;
            if (idx != droppedIdx) {
                m_source->ReceiveMediaPacket(r, c, originalPackets[idx]);
            }
        }
    }

    // Packet is missing before recovery
    EXPECT_FALSE(m_source->HasPacket(droppedRow, droppedCol));

    // Deliver row FEC for row 1
    m_source->ReceiveRowFEC(droppedRow, rowFecPackets[droppedRow]);

    // Deliver all col FECs as well
    for (size_t c = 0; c < L; ++c) {
        m_source->ReceiveColFEC(c, colFecPackets[c]);
    }

    // Trigger recovery
    size_t recovered = m_source->DecodeAndRecover();
    EXPECT_GE(recovered, 1u);
    EXPECT_TRUE(m_source->HasPacket(droppedRow, droppedCol));

    // Verify bit-for-bit accuracy of recovered packet
    auto recoveredPkt = m_source->GetPacket(droppedRow, droppedCol);
    ASSERT_TRUE(recoveredPkt.has_value());
    EXPECT_EQ(recoveredPkt.value(), originalPackets[droppedIdx]);
}
