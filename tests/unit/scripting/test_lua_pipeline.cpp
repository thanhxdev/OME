#include <gtest/gtest.h>
#include "openmedia/scripting/LuaEngine.h"

using namespace openmedia::scripting;

class LuaEngineTest : public ::testing::Test {
protected:
    void SetUp() override {}
    void TearDown() override {}
};

TEST_F(LuaEngineTest, InitializationAndExecution) {
    LuaEngine engine;
    EXPECT_TRUE(engine.Initialize());
    EXPECT_TRUE(engine.ExecuteScript("print('Hello OpenMedia')"));
    engine.Shutdown();
}
