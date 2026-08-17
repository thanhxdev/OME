#include <gtest/gtest.h>
#include "openmedia/webrtc/WebRTCEngine.h"
#include "openmedia/webrtc/WebRTCSource.h"
#include "openmedia/webrtc/WebRTCOutput.h"

using namespace openmedia::webrtc;

class WebRTCEngineTest : public ::testing::Test {
protected:
    void SetUp() override {}
    void TearDown() override {}
};

TEST_F(WebRTCEngineTest, InitializeAndShutdown) {
    WebRTCEngine engine;
    EXPECT_TRUE(engine.Initialize());
    engine.Shutdown();
}

TEST_F(WebRTCEngineTest, SourceLifecycle) {
    WebRTCEngine engine;
    ASSERT_TRUE(engine.Initialize());

    WebRTCSource source;
    // Just verify the interface compiles and runs
    bool connected = source.Connect("ws://localhost:8080/whip");
}

TEST_F(WebRTCEngineTest, OutputLifecycle) {
    WebRTCEngine engine;
    ASSERT_TRUE(engine.Initialize());

    WebRTCOutput output;
    bool started = output.Start("ws://localhost:8080/whip");
    EXPECT_TRUE(started);
    output.Stop();
}
