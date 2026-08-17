#include <gtest/gtest.h>
#include "openmedia/ndi/NDIEngine.h"
#include "openmedia/ndi/NDISource.h"
#include "openmedia/ndi/NDIOutput.h"
#include <thread>
#include <chrono>

using namespace openmedia::ndi;

class NDIEngineTest : public ::testing::Test {
protected:
    void SetUp() override {
        engine = std::make_unique<NDIEngine>();
        ASSERT_TRUE(engine->Initialize());
    }

    void TearDown() override {
        engine->Shutdown();
    }

    std::unique_ptr<NDIEngine> engine;
};

TEST_F(NDIEngineTest, InitializeAndShutdown) {
    // Already done in SetUp/TearDown, just checking if it doesn't crash
}

TEST_F(NDIEngineTest, SenderReceiverConnection) {
    NDIOutput output;
    ASSERT_TRUE(output.Start("Test_NDI_Source"));

    std::this_thread::sleep_for(std::chrono::milliseconds(100));

    NDISource source;
    ASSERT_TRUE(source.Connect("Test_NDI_Source"));

    std::this_thread::sleep_for(std::chrono::milliseconds(100));
    
    source.Disconnect();
    output.Stop();
}
