#include <gtest/gtest.h>
#include <openmedia/plugin/PluginManager.h>
#include <openmedia/plugin/IPlugin.h>

using namespace openmedia::core;
using namespace openmedia::plugin;

class PluginManagerTest : public ::testing::Test {
protected:
    void SetUp() override {}
    void TearDown() override {
        PluginManager::GetInstance().UnloadAll();
    }
};

TEST_F(PluginManagerTest, SingletonInstance) {
    auto& pm1 = PluginManager::GetInstance();
    auto& pm2 = PluginManager::GetInstance();
    EXPECT_EQ(&pm1, &pm2);
}

TEST_F(PluginManagerTest, LoadInvalidPlugin) {
    auto& pm = PluginManager::GetInstance();
    auto result = pm.LoadPlugin("non_existent_plugin.dll");
    EXPECT_FALSE(result.has_value());
    EXPECT_EQ(pm.GetPlugins().size(), 0);
}

TEST_F(PluginManagerTest, LoadInvalidDirectory) {
    auto& pm = PluginManager::GetInstance();
    auto result = pm.LoadDirectory("non_existent_directory");
    // LoadDirectory returns VoidResult without error if dir does not exist, just logs warning
    EXPECT_TRUE(result.has_value());
    EXPECT_EQ(pm.GetPlugins().size(), 0);
}
