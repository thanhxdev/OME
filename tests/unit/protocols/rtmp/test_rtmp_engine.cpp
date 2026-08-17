#include <gtest/gtest.h>
#include "openmedia/rtmp/RTMPEngine.h"
#include "openmedia/rtmp/RTMPSource.h"
#include "openmedia/rtmp/RTMPOutput.h"

using namespace openmedia::rtmp;

class RTMPEngineTest : public ::testing::Test {
protected:
    void SetUp() override {}
    void TearDown() override {}
};

TEST_F(RTMPEngineTest, InitializeAndShutdown) {
    RTMPEngine engine;
    EXPECT_TRUE(engine.Initialize());
    engine.Shutdown();
}

TEST_F(RTMPEngineTest, SourceLifecycle) {
    RTMPEngine engine;
    ASSERT_TRUE(engine.Initialize());

    RTMPSource source;
    // Just verify the interface compiles and runs
    bool connected = source.Connect("rtmp://localhost/live/stream");
}

TEST_F(RTMPEngineTest, OutputLifecycle) {
    RTMPEngine engine;
    ASSERT_TRUE(engine.Initialize());

    RTMPOutput output;
    bool started = output.Start("rtmp://localhost/live/stream");
    EXPECT_TRUE(started);
    output.Stop();
}
