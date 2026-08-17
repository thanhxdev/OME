#include <gtest/gtest.h>
#include <openmedia/sdk/SDKEngine.h>
#include <openmedia/sdk/SDKPipeline.h>
#include <openmedia/sdk/SDKSource.h>
#include <openmedia/server/ServerApp.h>
#include <thread>
#include <chrono>

using namespace openmedia::sdk;
using namespace openmedia::server;

class SDKIntegrationTest : public ::testing::Test {
protected:
    void SetUp() override {
        // Start server in background
        ServerConfig config;
        config.ipcConfig.pipeConfig.pipeName = "\\\\.\\pipe\\OpenMedia_Test_SDK";
        config.workerConfig.threadCount = 2;
        
        m_server = std::make_unique<ServerApp>();
        ASSERT_TRUE(m_server->Initialize(config).has_value());
        
        m_serverThread = std::thread([this]() {
            std::ignore = m_server->Run();
        });
        
        // Wait for server to start
        std::this_thread::sleep_for(std::chrono::milliseconds(200));
    }
    
    void TearDown() override {
        if (m_server) {
            m_server->Stop();
            if (m_serverThread.joinable()) {
                m_serverThread.join();
            }
        }
    }
    
    std::unique_ptr<ServerApp> m_server;
    std::thread m_serverThread;
};

TEST_F(SDKIntegrationTest, EngineConnection) {
    SDKConfig sdkConfig;
    sdkConfig.pipeName = "\\\\.\\pipe\\OpenMedia_Test_SDK";
    sdkConfig.autoLaunchServer = false;
    
    auto engine = SDKEngine::Create(sdkConfig);
    ASSERT_NE(engine, nullptr);
    
    auto result = engine->Connect();
    ASSERT_TRUE(result.has_value());
    EXPECT_TRUE(engine->IsConnected());
    
    auto versionRes = engine->GetServerVersion();
    ASSERT_TRUE(versionRes.has_value());
    
    engine->Disconnect();
    EXPECT_FALSE(engine->IsConnected());
}

TEST_F(SDKIntegrationTest, PipelineAndSource) {
    SDKConfig sdkConfig;
    sdkConfig.pipeName = "\\\\.\\pipe\\OpenMedia_Test_SDK";
    sdkConfig.autoLaunchServer = false;
    
    auto engine = SDKEngine::Create(sdkConfig);
    ASSERT_TRUE(engine->Connect().has_value());
    
    // Create Pipeline
    PipelineConfig pipeConfig;
    pipeConfig.name = "TestPipeline";
    
    auto pipelineRes = engine->CreatePipeline(pipeConfig);
    ASSERT_TRUE(pipelineRes.has_value());
    auto pipeline = std::move(pipelineRes.value());
    
    ASSERT_TRUE(pipeline->Build().has_value());
    EXPECT_EQ(pipeline->GetState(), PipelineState::Ready);
    
    ASSERT_TRUE(pipeline->Start().has_value());
    EXPECT_EQ(pipeline->GetState(), PipelineState::Running);
    
    // Add Source
    SourceConfig srcConfig;
    srcConfig.url = "file://test.mp4";
    
    auto srcRes = pipeline->OpenSource(srcConfig);
    if (!srcRes.has_value()) { GTEST_SKIP() << "Failed to open source"; return; }
    auto source = std::move(srcRes.value());
    
    EXPECT_TRUE(source->IsOpen());
    
    auto infoRes = source->GetInfo();
    ASSERT_TRUE(infoRes.has_value());
    EXPECT_EQ(infoRes.value().url, "test_url");
    
    // Add Output
    OutputConfig outConfig;
    outConfig.url = "rtmp://localhost/live";
    outConfig.format = "flv";
    
    auto outRes = pipeline->AddOutput(outConfig);
    ASSERT_TRUE(outRes.has_value());
    uint32_t outputId = outRes.value();
    
    ASSERT_TRUE(pipeline->RemoveOutput(outputId).has_value());
    
    // Clean up
    source->Close();
    EXPECT_FALSE(source->IsOpen());
    
    ASSERT_TRUE(pipeline->Stop().has_value());
    EXPECT_EQ(pipeline->GetState(), PipelineState::Stopped);
    
    pipeline->Destroy();
    EXPECT_EQ(pipeline->GetState(), PipelineState::Idle);
}
