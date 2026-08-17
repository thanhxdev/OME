#include <gtest/gtest.h>
#include <openmedia/mixer/Switcher.h>
#include <openmedia/mixer/Transition.h>
#include <openmedia/core/MediaFrame.h>

using namespace openmedia::mixer;
using namespace openmedia::core;

TEST(SwitcherTest, CutTransition) {
    Switcher switcher;
    
    auto frame1 = MediaFrame::CreateVideo(100, 100, PixelFormat::BGRA);
    auto frame2 = MediaFrame::CreateVideo(100, 100, PixelFormat::BGRA);
    
    switcher.AddInput(1, frame1);
    switcher.AddInput(2, frame2);
    
    // By default, first added is program and preview
    auto pgm = switcher.GetProgramOutput(0);
    EXPECT_EQ(pgm, frame1);
    
    switcher.SetPreview(2);
    EXPECT_EQ(switcher.GetPreviewId(), 2);
    
    // Take (Cut)
    switcher.Take();
    
    pgm = switcher.GetProgramOutput(0);
    EXPECT_EQ(pgm, frame2);
}

TEST(SwitcherTest, AutoTransition) {
    Switcher switcher;
    
    auto frame1 = MediaFrame::CreateVideo(100, 100, PixelFormat::BGRA);
    auto frame2 = MediaFrame::CreateVideo(100, 100, PixelFormat::BGRA);
    
    switcher.AddInput(1, frame1);
    switcher.AddInput(2, frame2);
    
    switcher.SetPreview(2);
    
    auto transition = std::make_shared<Transition>(TransitionType::Dissolve, 1000);
    switcher.Auto(transition, 1000);
    
    // Start of transition (T=0)
    auto pgm = switcher.GetProgramOutput(0);
    // At T=0, it should be heavily frame1. In our logic, Transition returns a new frame anyway, so pointer won't match exactly if it's dissolve, but let's just check it doesn't crash.
    ASSERT_NE(pgm, nullptr);
    
    // Middle of transition (T=500)
    auto pgmMid = switcher.GetProgramOutput(500);
    ASSERT_NE(pgmMid, nullptr);
    
    // End of transition (T=1000)
    auto pgmEnd = switcher.GetProgramOutput(1000);
    EXPECT_EQ(pgmEnd, frame2); // Finished transition, returns exact PVW frame
}
