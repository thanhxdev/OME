/// @file test_hitless_merge.cpp
/// @brief Unit test verifying SMPTE ST 2022-7 Seamless Protection Switching (Hitless Merge).

#include <gtest/gtest.h>
#include "openmedia/st2022/HitlessMerge.h"

#include <vector>
#include <set>

using namespace openmedia::st2022;

class HitlessMergeTest : public ::testing::Test {
protected:
    void SetUp() override {
        HitlessMergeConfig config;
        config.enabled = true;
        config.differentialDelayMs = 20;
        config.maxBufferSizePackets = 1024;
        m_merge = std::make_unique<HitlessMerge>(config);
    }

    std::unique_ptr<HitlessMerge> m_merge;
};

TEST_F(HitlessMergeTest, InitialStateAndConfig) {
    EXPECT_TRUE(m_merge->IsEnabled());
    auto config = m_merge->GetConfig();
    EXPECT_EQ(config.differentialDelayMs, 20u);
    EXPECT_EQ(config.maxBufferSizePackets, 1024u);

    m_merge->Enable(false);
    EXPECT_FALSE(m_merge->IsEnabled());
    m_merge->Enable(true);
    EXPECT_TRUE(m_merge->IsEnabled());
}

TEST_F(HitlessMergeTest, SequenceOrderWithWrapAround) {
    EXPECT_TRUE(HitlessMerge::IsSeqNewer(10, 5));
    EXPECT_FALSE(HitlessMerge::IsSeqNewer(5, 10));
    EXPECT_FALSE(HitlessMerge::IsSeqNewer(10, 10));

    // 16-bit wrap-around: sequence 2 is newer than 65530
    EXPECT_TRUE(HitlessMerge::IsSeqNewer(2, 65530));
    EXPECT_FALSE(HitlessMerge::IsSeqNewer(65530, 2));
}

TEST_F(HitlessMergeTest, DropSimulationTwentyPercentRecovery) {
    // Simulate 200 packets
    constexpr size_t totalPackets = 200;
    std::vector<uint8_t> dummyPayload = {0x80, 0x60, 0x01, 0x02, 0x03, 0x04};

    // Path A drops 20% (every multiple of 5)
    // Path B drops 20% (every multiple of 5 + 2)
    // Combined streams should yield 100% (0% packet loss)
    for (size_t i = 1; i <= totalPackets; ++i) {
        uint16_t seq = static_cast<uint16_t>(i);

        bool dropA = (i % 5 == 0);
        bool dropB = (i % 5 == 2);

        if (!dropA) {
            EXPECT_TRUE(m_merge->PushPacket(NetworkPath::PathA, seq, dummyPayload));
        }
        if (!dropB) {
            // Push to Path B (might be duplicate if Path A already delivered it)
            m_merge->PushPacket(NetworkPath::PathB, seq, dummyPayload);
        }
    }

    // Collect all merged output packets
    std::set<uint16_t> receivedSeqs;
    size_t mergedCount = 0;

    while (auto pkt = m_merge->PopPacket()) {
        mergedCount++;
    }

    HitlessMergeStats stats = m_merge->GetStats();
    EXPECT_GT(stats.pathAPackets, 0u);
    EXPECT_GT(stats.pathBPackets, 0u);
    EXPECT_GT(stats.duplicatesDropped, 0u);
    EXPECT_GT(stats.recoveredFromPathB, 0u);
    EXPECT_EQ(stats.lostPackets, 0u);
    EXPECT_EQ(stats.outputPackets, totalPackets);
}
