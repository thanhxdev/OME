#include <gtest/gtest.h>
#include "openmedia/st2022/ST2022Engine.h"
#include "openmedia/st2022/ST2022Source.h"
#include "openmedia/st2022/ST2022Output.h"
#include "openmedia/st2022/HitlessMerge.h"

using namespace openmedia::st2022;

class ST2022EngineTest : public ::testing::Test {
protected:
    void SetUp() override {}
    void TearDown() override {}
};

TEST_F(ST2022EngineTest, InitializeAndShutdown) {
    ST2022Engine engine;
    EXPECT_TRUE(engine.Initialize());
    engine.Shutdown();
}

TEST_F(ST2022EngineTest, ConnectSource) {
    ST2022Engine engine;
    ASSERT_TRUE(engine.Initialize());

    ST2022Source source;
    bool connected = source.Connect("239.1.0.1", 5000);
    EXPECT_TRUE(connected);
}

TEST_F(ST2022EngineTest, StartOutput) {
    ST2022Engine engine;
    ASSERT_TRUE(engine.Initialize());

    ST2022Output output;
    bool started = output.Start("239.1.0.2", 5000);
    EXPECT_TRUE(started);
    output.Stop();
}

TEST_F(ST2022EngineTest, HitlessMergeToggle) {
    HitlessMerge merge;
    EXPECT_FALSE(merge.IsEnabled());
    EXPECT_TRUE(merge.Enable(true));
    EXPECT_TRUE(merge.IsEnabled());
}
