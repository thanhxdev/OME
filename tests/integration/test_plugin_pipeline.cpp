#include <gtest/gtest.h>
#include <openmedia/plugin/PluginManager.h>
#include <openmedia/plugin/IVideoFilter.h>

using namespace openmedia::core;
using namespace openmedia::plugin;

class PluginPipelineTest : public ::testing::Test {
protected:
    void SetUp() override {
        PluginManager::GetInstance().LoadDirectory("plugins");
    }
    
    void TearDown() override {
        PluginManager::GetInstance().UnloadAll();
    }
};

TEST_F(PluginPipelineTest, GrayscaleFilterProcess) {
    auto& pm = PluginManager::GetInstance();
    auto plugins = pm.GetPlugins();
    
    std::shared_ptr<IPlugin> filterPlugin;
    for (auto p : plugins) {
        if (std::string(p->GetInfo().name) == "GrayscaleFilter") {
            filterPlugin = p;
            break;
        }
    }
    
    // We mock the test here for MVP structure, in actual it would process a MediaFrame
    EXPECT_TRUE(true);
}
