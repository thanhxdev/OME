#include <gtest/gtest.h>
#include "openmedia/st2110/ST2110Engine.h"
#include "openmedia/st2110/ST2110Source.h"
#include "openmedia/st2110/ST2110Output.h"
#include "openmedia/st2110/PTPClock.h"
#include "openmedia/st2110/NMOSEngine.h"

using namespace openmedia::st2110;

class ST2110EngineTest : public ::testing::Test {
protected:
    void SetUp() override {}
    void TearDown() override {}
};

TEST_F(ST2110EngineTest, InitializeAndShutdown) {
    ST2110Engine engine;
    EXPECT_TRUE(engine.Initialize());
    engine.Shutdown();
}

TEST_F(ST2110EngineTest, ConnectSource) {
    ST2110Engine engine;
    ASSERT_TRUE(engine.Initialize());

    ST2110Source source;
    bool connected = source.Connect("239.0.0.1", 5000);
    EXPECT_TRUE(connected);
}

TEST_F(ST2110EngineTest, StartOutput) {
    ST2110Engine engine;
    ASSERT_TRUE(engine.Initialize());

    ST2110Output output;
    bool started = output.Start("239.0.0.2", 5000);
    EXPECT_TRUE(started);
    output.Stop();
}

TEST_F(ST2110EngineTest, PTPSync) {
    PTPClock clock;
    EXPECT_TRUE(clock.Sync());
    EXPECT_GT(clock.GetCurrentTime(), 0.0);
}

TEST_F(ST2110EngineTest, NMOSRegistration) {
    NMOSEngine nmos;
    EXPECT_TRUE(nmos.StartRegistration("http://localhost:3000/x-nmos/registration/v1.2"));
    nmos.StopRegistration();
}
