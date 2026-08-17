#include <gtest/gtest.h>
#include <openmedia/mixer/MixerLayer.h>

using namespace openmedia::mixer;
using namespace openmedia::core;

TEST(MixerLayerTest, Initialization) {
    auto layer = std::make_shared<MixerLayer>(1, "Layer1");
    
    EXPECT_EQ(layer->GetId(), 1);
    EXPECT_EQ(layer->GetName(), "Layer1");
    EXPECT_EQ(layer->GetState(), PipelineState::Stopped);
    EXPECT_EQ(layer->GetX(), 0);
    EXPECT_EQ(layer->GetY(), 0);
    EXPECT_EQ(layer->GetZIndex(), 0);
    EXPECT_FLOAT_EQ(layer->GetOpacity(), 1.0f);
    EXPECT_TRUE(layer->IsVisible());
    EXPECT_EQ(layer->GetCurrentFrame(), nullptr);
}

TEST(MixerLayerTest, SetProperties) {
    auto layer = std::make_shared<MixerLayer>(2);
    
    layer->SetPosition(100, 200);
    EXPECT_EQ(layer->GetX(), 100);
    EXPECT_EQ(layer->GetY(), 200);
    
    layer->SetSize(640, 480);
    EXPECT_EQ(layer->GetWidth(), 640);
    EXPECT_EQ(layer->GetHeight(), 480);
    
    layer->SetZIndex(5);
    EXPECT_EQ(layer->GetZIndex(), 5);
    
    layer->SetOpacity(0.5f);
    EXPECT_FLOAT_EQ(layer->GetOpacity(), 0.5f);
    
    layer->SetVisible(false);
    EXPECT_FALSE(layer->IsVisible());
}

TEST(MixerLayerTest, FramePushing) {
    auto layer = std::make_shared<MixerLayer>(3);
    
    auto frame = MediaFrame::CreateVideo(1920, 1080, PixelFormat::BGRA);
    
    // Should fail if stopped
    EXPECT_FALSE(layer->PushFrame(frame).has_value());
    
    ASSERT_TRUE(layer->Start().has_value());
    EXPECT_TRUE(layer->PushFrame(frame).has_value());
    
    auto cached = layer->GetCurrentFrame();
    EXPECT_EQ(cached, frame);
}
