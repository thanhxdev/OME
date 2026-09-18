/// @file test_srt_bonding.cpp
/// @brief Unit test verifying SRT multi-interface bonding, local adapter binding, and failover parameters.

#include <gtest/gtest.h>
#include "openmedia/srt/SRTEngine.h"
#include "openmedia/srt/SRTSource.h"
#include "openmedia/srt/SRTOutput.h"
#include "openmedia/srt/SRTUtils.h"

using namespace openmedia::srt;

class SRTBondingTest : public ::testing::Test {
protected:
    void SetUp() override {
        m_engine.Initialize();
    }

    void TearDown() override {
        m_engine.Shutdown();
    }

    SRTEngine m_engine;
};

TEST_F(SRTBondingTest, ParseBondingAndLocalIpUri) {
    SRTUriConfig config;
    const std::string uri = "srt://192.168.1.50:9000?mode=caller&localip=192.168.1.100&bonding=1&latency=150";
    bool parsed = SRTUriConfig::Parse(uri, config);

    EXPECT_TRUE(parsed);
    EXPECT_EQ(config.ip, "192.168.1.50");
    EXPECT_EQ(config.port, 9000);
    EXPECT_EQ(config.mode, SRTMode::Caller);
    EXPECT_EQ(config.bindAddress, "192.168.1.100");
    EXPECT_TRUE(config.broadcastRedundancy);
    EXPECT_EQ(config.latency, 150);
}

TEST_F(SRTBondingTest, DualInterfaceBindingSimulation) {
    // Primary adapter configuration (Fiber / Ethernet)
    SRTUriConfig primaryConfig;
    EXPECT_TRUE(SRTUriConfig::Parse("srt://127.0.0.1:9010?mode=caller&localip=127.0.0.1&bonding=1", primaryConfig));
    EXPECT_EQ(primaryConfig.bindAddress, "127.0.0.1");
    EXPECT_TRUE(primaryConfig.broadcastRedundancy);

    // Secondary backup adapter configuration (4G / 5G Cellular modem)
    SRTUriConfig backupConfig;
    EXPECT_TRUE(SRTUriConfig::Parse("srt://127.0.0.1:9010?mode=caller&localip=127.0.0.1&bonding=1&latency=300", backupConfig));
    EXPECT_EQ(backupConfig.bindAddress, "127.0.0.1");
    EXPECT_TRUE(backupConfig.broadcastRedundancy);
    EXPECT_EQ(backupConfig.latency, 300);
}
