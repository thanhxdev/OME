#include <gtest/gtest.h>
#include <openmedia/mixer/LumaKey.h>
#include <openmedia/core/MediaFrame.h>
#include <cstring>

using namespace openmedia;
using namespace openmedia::mixer;

class LumaKeyTest : public ::testing::Test {
protected:
    void SetUp() override {
        lumaKey = std::make_unique<LumaKey>();
    }

    std::shared_ptr<core::MediaFrame> CreateTestFrame(uint8_t r, uint8_t g, uint8_t b, uint8_t a) {
        auto frame = core::MediaFrame::CreateVideo(10, 10, core::PixelFormat::BGRA);
        uint8_t* pixels = frame->GetVideoPlane(0);
        int stride = frame->GetLineSize(0);
        
        for (int y = 0; y < 10; ++y) {
            for (int x = 0; x < 10; ++x) {
                int offset = y * stride + x * 4;
                pixels[offset + 0] = b;
                pixels[offset + 1] = g;
                pixels[offset + 2] = r;
                pixels[offset + 3] = a;
            }
        }
        return frame;
    }

    std::unique_ptr<LumaKey> lumaKey;
};

TEST_F(LumaKeyTest, KeyBlackBackground) {
    // Threshold set to 0.1 (10% luminance)
    lumaKey->SetThreshold(0.1f);
    lumaKey->SetSoftness(0.0f);
    
    // Pure black should be keyed out (Y = 0)
    auto blackFrame = CreateTestFrame(0, 0, 0, 255);
    auto resultBlack = lumaKey->Process(blackFrame);
    ASSERT_TRUE(resultBlack.has_value());
    EXPECT_EQ(resultBlack.value()->GetVideoPlane(0)[3], 0); // Alpha should be 0
    
    // Pure white should remain (Y = 255)
    auto whiteFrame = CreateTestFrame(255, 255, 255, 255);
    auto resultWhite = lumaKey->Process(whiteFrame);
    ASSERT_TRUE(resultWhite.has_value());
    EXPECT_EQ(resultWhite.value()->GetVideoPlane(0)[3], 255); // Alpha should be 255
}

TEST_F(LumaKeyTest, KeyWhiteBackgroundInverted) {
    // Threshold set to 0.1 (10% distance from white)
    lumaKey->SetThreshold(0.1f);
    lumaKey->SetSoftness(0.0f);
    lumaKey->SetInvert(true); // Key out bright things
    
    // Pure white should be keyed out (dist = 0 <= threshold)
    auto whiteFrame = CreateTestFrame(255, 255, 255, 255);
    auto resultWhite = lumaKey->Process(whiteFrame);
    ASSERT_TRUE(resultWhite.has_value());
    EXPECT_EQ(resultWhite.value()->GetVideoPlane(0)[3], 0); // Alpha should be 0
    
    // Pure black should remain
    auto blackFrame = CreateTestFrame(0, 0, 0, 255);
    auto resultBlack = lumaKey->Process(blackFrame);
    ASSERT_TRUE(resultBlack.has_value());
    EXPECT_EQ(resultBlack.value()->GetVideoPlane(0)[3], 255); // Alpha should be 255
}
