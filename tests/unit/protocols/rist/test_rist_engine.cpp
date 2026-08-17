#include <gtest/gtest.h>
#include "openmedia/rist/RISTEngine.h"
#include "openmedia/rist/RISTSource.h"
#include "openmedia/rist/RISTOutput.h"

using namespace openmedia::rist;

class RISTEngineTest : public ::testing::Test {
protected:
    void SetUp() override {}
    void TearDown() override {}
};

TEST_F(RISTEngineTest, InitializeAndShutdown) {
    RISTEngine engine;
    EXPECT_TRUE(engine.Initialize());
    engine.Shutdown();
}

TEST_F(RISTEngineTest, ConnectSource) {
    RISTEngine engine;
    ASSERT_TRUE(engine.Initialize());

    RISTSource source;
    EXPECT_TRUE(source.Connect("rist://@239.0.0.1:5000"));
}

TEST_F(RISTEngineTest, StartOutput) {
    RISTEngine engine;
    ASSERT_TRUE(engine.Initialize());

    RISTOutput output;
    EXPECT_TRUE(output.Start("rist://239.0.0.2:5000"));
    output.Stop();
}
