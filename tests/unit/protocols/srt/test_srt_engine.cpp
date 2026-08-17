#include <gtest/gtest.h>
#include "openmedia/srt/SRTEngine.h"
#include "openmedia/srt/SRTSource.h"
#include "openmedia/srt/SRTOutput.h"
#include "openmedia/srt/SRTUtils.h"
#include <thread>
#include <chrono>

using namespace openmedia::srt;

class SRTEngineTest : public ::testing::Test {
protected:
    void SetUp() override {
        engine.Initialize();
    }

    void TearDown() override {
        engine.Shutdown();
    }

    SRTEngine engine;
};

TEST_F(SRTEngineTest, InitializeAndShutdown) {
    EXPECT_TRUE(engine.Initialize());
}

TEST_F(SRTEngineTest, ParseUriConfig) {
    SRTUriConfig config;
    bool parsed = SRTUriConfig::Parse("srt://127.0.0.1:9000?mode=listener&passphrase=secret1234&pbkeylen=16&latency=200&maxbw=1000000", config);
    EXPECT_TRUE(parsed);
    EXPECT_EQ(config.ip, "127.0.0.1");
    EXPECT_EQ(config.port, 9000);
    EXPECT_EQ(config.mode, SRTMode::Listener);
    EXPECT_EQ(config.passphrase, "secret1234");
    EXPECT_EQ(config.pbkeylen, 16);
    EXPECT_EQ(config.latency, 200);
    EXPECT_EQ(config.maxbw, 1000000);
}

TEST_F(SRTEngineTest, TestConnectionAndStats) {
    SRTSource listener;
    SRTOutput caller;

    // Start listener
    EXPECT_TRUE(listener.Connect("srt://127.0.0.1:9001?mode=listener&latency=50"));

    // Wait a bit to ensure it's listening
    std::this_thread::sleep_for(std::chrono::milliseconds(100));

    // Connect caller
    EXPECT_TRUE(caller.Start("srt://127.0.0.1:9001?mode=caller&latency=50"));

    // Wait for connection to establish and some stats to populate
    std::this_thread::sleep_for(std::chrono::milliseconds(500));

    SRTSource::SRTStatistics sourceStats;
    EXPECT_TRUE(listener.GetStatistics(sourceStats));
    // RTT might be 0 on localhost, but at least we can get it
    EXPECT_GE(sourceStats.msRTT, 0);

    SRTOutput::SRTStatistics outputStats;
    EXPECT_TRUE(caller.GetStatistics(outputStats));
    EXPECT_GE(outputStats.msRTT, 0);

    caller.Stop();
    listener.Disconnect();
}
