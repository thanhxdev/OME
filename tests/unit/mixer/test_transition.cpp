#include <gtest/gtest.h>
#include <openmedia/mixer/Transition.h>
#include <openmedia/core/MediaFrame.h>
#include <memory>
#include <cstring>

using namespace openmedia::mixer;
using namespace openmedia::core;

TEST(TransitionTest, CutTransition) {
    Transition transition(TransitionType::Cut, 1000);
    
    auto frameA = MediaFrame::CreateVideo(100, 100, PixelFormat::BGRA);
    auto frameB = MediaFrame::CreateVideo(100, 100, PixelFormat::BGRA);
    
    auto out0 = transition.Process(frameA, frameB, 0.0f);
    EXPECT_EQ(out0.value(), frameA);
    
    auto out1 = transition.Process(frameA, frameB, 1.0f);
    EXPECT_EQ(out1.value(), frameB);
}

TEST(TransitionTest, DissolveTransition) {
    Transition transition(TransitionType::Dissolve, 1000);
    
    auto frameA = MediaFrame::CreateVideo(100, 100, PixelFormat::BGRA);
    auto frameB = MediaFrame::CreateVideo(100, 100, PixelFormat::BGRA);
    
    std::memset(frameA->GetVideoPlane(0), 0, 100 * 100 * 4); // Black
    std::memset(frameB->GetVideoPlane(0), 255, 100 * 100 * 4); // White
    
    auto out = transition.Process(frameA, frameB, 0.5f);
    ASSERT_TRUE(out.has_value());
    
    auto outFrame = out.value();
    uint8_t* pOut = outFrame->GetVideoPlane(0);
    
    // Check first pixel to see if it's blended (~127)
    EXPECT_NEAR(pOut[0], 127, 1);
}

TEST(TransitionTest, WipeTransition) {
    Transition transition(TransitionType::Wipe, 1000);
    
    auto frameA = MediaFrame::CreateVideo(10, 10, PixelFormat::BGRA);
    auto frameB = MediaFrame::CreateVideo(10, 10, PixelFormat::BGRA);
    
    std::memset(frameA->GetVideoPlane(0), 0, 10 * 10 * 4); // Black
    std::memset(frameB->GetVideoPlane(0), 255, 10 * 10 * 4); // White
    
    auto out = transition.Process(frameA, frameB, 0.5f);
    ASSERT_TRUE(out.has_value());
    
    uint8_t* pOut = out.value()->GetVideoPlane(0);
    
    // Pixel (0,0) is less than boundary (width*0.5 = 5), so it should be B (255)
    EXPECT_EQ(pOut[0], 255);
    
    // Pixel (9,0) is greater than boundary, so it should be A (0)
    EXPECT_EQ(pOut[9 * 4], 0);
}
