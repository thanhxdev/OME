#include <gtest/gtest.h>
#include <openmedia/plugin/PluginManager.h>

using namespace openmedia::core;

class PluginLoaderTest : public ::testing::Test {
protected:
    void SetUp() override {}
    void TearDown() override {
        PluginManager::GetInstance().UnloadAll();
    }
};

TEST_F(PluginLoaderTest, LoadAndUnload) {
    auto& pm = PluginManager::GetInstance();
    // Attempt loading a non-existent dll — should fail gracefully
    auto result = pm.LoadPlugin("dummy_plugin.dll");
    EXPECT_FALSE(result.has_value());

    pm.UnloadAll();
    EXPECT_EQ(pm.GetPlugins().size(), 0);
}
