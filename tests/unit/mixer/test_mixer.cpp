#include <gtest/gtest.h>
#include <openmedia/mixer/Mixer.h>
#include <openmedia/mixer/MixerLayer.h>

using namespace openmedia::mixer;
using namespace openmedia::core;

// Mock downstream object to receive mixed frames
class MockDownstream : public IMediaObject {
public:
    int m_frameCount = 0;
    
    std::string GetName() const override { return "MockDownstream"; }
    PipelineState GetState() const override { return PipelineState::Running; }
    VoidResult Initialize() override { return {}; }
    VoidResult Start() override { return {}; }
    VoidResult Stop() override { return {}; }
    
    VoidResult PushFrame(std::shared_ptr<MediaFrame> frame) override {
        m_frameCount++;
        return {};
    }
    
    Result<std::shared_ptr<MediaFrame>> PullFrame() override { return std::unexpected(Error::Make(ErrorCode::NotImplemented, "")); }
    VoidResult Connect(std::shared_ptr<IMediaObject> downstream) override { return {}; }
    VoidResult Disconnect() override { return {}; }
    void OnStateChange(StateChangeCallback callback) override {}
    void OnError(ErrorCallback callback) override {}
};

TEST(MixerTest, Initialization) {
    auto mixer = std::make_shared<Mixer>();
    
    EXPECT_EQ(mixer->GetName(), "Mixer");
    EXPECT_EQ(mixer->GetState(), PipelineState::Stopped);
    
    ASSERT_TRUE(mixer->SetOutputFormat(1280, 720, 30).has_value());
    ASSERT_TRUE(mixer->Initialize().has_value());
}

TEST(MixerTest, AddRemoveLayers) {
    auto mixer = std::make_shared<Mixer>();
    mixer->Initialize();
    
    auto layer1 = std::make_shared<MixerLayer>(1);
    auto layer2 = std::make_shared<MixerLayer>(2);
    
    EXPECT_TRUE(mixer->AddLayer(layer1).has_value());
    EXPECT_TRUE(mixer->AddLayer(layer2).has_value());
    
    EXPECT_TRUE(mixer->RemoveLayer(1).has_value());
    EXPECT_TRUE(mixer->RemoveLayer(2).has_value());
}

TEST(MixerTest, RenderLoop) {
    auto mixer = std::make_shared<Mixer>();
    mixer->SetOutputFormat(640, 480, 60);
    mixer->Initialize();
    
    auto mockDownstream = std::make_shared<MockDownstream>();
    mixer->Connect(mockDownstream);
    
    auto layer = std::make_shared<MixerLayer>(1);
    layer->SetSize(640, 480);
    layer->Start();
    mixer->AddLayer(layer);
    
    auto frame = MediaFrame::CreateVideo(640, 480, PixelFormat::BGRA);
    layer->PushFrame(frame);
    
    mixer->Start();
    
    // Wait briefly for the mixer thread to produce some frames
    std::this_thread::sleep_for(std::chrono::milliseconds(100));
    
    mixer->Stop();
    
    // At 60fps, 100ms should produce roughly 6 frames
    EXPECT_GT(mockDownstream->m_frameCount, 0);
}
