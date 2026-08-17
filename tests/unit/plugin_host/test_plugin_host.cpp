#include <gtest/gtest.h>
#include <openmedia/plugin_host/PluginHost.h>
#include <filesystem>
#include <fstream>

using namespace openmedia::plugin_host;
namespace fs = std::filesystem;

class PluginHostTest : public ::testing::Test {
protected:
    void SetUp() override {
        // Create a dummy plugins directory
        fs::create_directories("test_plugins");
    }

    void TearDown() override {
        fs::remove_all("test_plugins");
    }
};

TEST_F(PluginHostTest, InitializationAndShutdown) {
    PluginHostConfig config;
    config.pluginDirectory = "test_plugins";
    config.autoScanOnStart = true;
    config.enableHotReload = false;

    PluginHost host(config);
    EXPECT_FALSE(host.IsRunning());

    auto startResult = host.Start();
    EXPECT_TRUE(startResult.has_value());
    EXPECT_TRUE(host.IsRunning());
    
    EXPECT_EQ(host.GetPluginCount(), 0);

    host.Stop();
    EXPECT_FALSE(host.IsRunning());
}

TEST_F(PluginHostTest, ScanDirectory) {
    PluginHostConfig config;
    config.pluginDirectory = "test_plugins";
    
    // Create a dummy dll file
    std::ofstream dummyDll("test_plugins/dummy.dll");
    dummyDll << "dummy content";
    dummyDll.close();

    PluginHost host(config);
    auto files = host.ScanDirectory();
    
    // It should find the dummy file
    EXPECT_EQ(files.size(), 1);
    EXPECT_EQ(files[0].filename().string(), "dummy.dll");
}
