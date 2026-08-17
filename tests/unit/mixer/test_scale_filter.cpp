#include <gtest/gtest.h>
#include <openmedia/mixer/ScaleFilter.h>
#include <openmedia/core/MediaFrame.h>

using namespace openmedia;
using namespace openmedia::mixer;

class ScaleFilterTest : public ::testing::Test {
protected:
    void SetUp() override {
        scaleFilter = std::make_unique<ScaleFilter>();
    }

    std::unique_ptr<ScaleFilter> scaleFilter;
};

TEST_F(ScaleFilterTest, ScalingChangesDimensions) {
    auto inputFrame = core::MediaFrame::CreateVideo(1920, 1080, core::PixelFormat::BGRA);
    
    // Scale down to 1280x720
    scaleFilter->SetOutputSize(1280, 720);
    auto resultDown = scaleFilter->Process(inputFrame);
    
    ASSERT_TRUE(resultDown.has_value());
    EXPECT_EQ(resultDown.value()->GetWidth(), 1280);
    EXPECT_EQ(resultDown.value()->GetHeight(), 720);
    
    // Scale up to 3840x2160
    scaleFilter->SetOutputSize(3840, 2160);
    auto resultUp = scaleFilter->Process(inputFrame);
    
    ASSERT_TRUE(resultUp.has_value());
    EXPECT_EQ(resultUp.value()->GetWidth(), 3840);
    EXPECT_EQ(resultUp.value()->GetHeight(), 2160);
}
