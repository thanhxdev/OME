#include <gtest/gtest.h>
#include "openmedia/ndi/NDIEngine.h"
#include "openmedia/ndi/NDISource.h"
#include "openmedia/ndi/NDIOutput.h"

using namespace openmedia::ndi;

class NDIEngineTest : public ::testing::Test {
protected:
    void SetUp() override {}
    void TearDown() override {}
};

TEST_F(NDIEngineTest, InitializeAndShutdown) {
    NDIEngine engine;
    EXPECT_TRUE(engine.Initialize());
    engine.Shutdown();
}

TEST_F(NDIEngineTest, ConnectSource) {
    NDIEngine engine;
    ASSERT_TRUE(engine.Initialize());

    NDISource source;
    // Attempt to connect to a dummy source. Might fail depending on NDI runtime.
    bool connected = source.Connect("Test_Source");
    // We just want to ensure it doesn't crash
}

TEST_F(NDIEngineTest, StartOutput) {
    NDIEngine engine;
    ASSERT_TRUE(engine.Initialize());

    NDIOutput output;
    bool started = output.Start("Test_Output");
    EXPECT_TRUE(started);
    output.Stop();
}
