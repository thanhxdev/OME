#include <gtest/gtest.h>
#include <openmedia/mixer/ChromaKey.h>
#include <openmedia/core/MediaFrame.h>
#include <cstring>

using namespace openmedia::mixer;
using namespace openmedia::core;

TEST(ChromaKeyTest, KeyGreenScreen) {
    ChromaKey chroma;
    chroma.SetKeyColor(0, 255, 0); // Green
    chroma.SetTolerance(0.1f);
    chroma.SetSmoothing(0.1f);
    
    auto frame = MediaFrame::CreateVideo(10, 10, PixelFormat::BGRA);
    uint8_t* pixels = frame->GetVideoPlane(0);
    
    // Fill with Green and full Alpha
    for (int i = 0; i < 100; ++i) {
        pixels[i * 4 + 0] = 0;   // B
        pixels[i * 4 + 1] = 255; // G
        pixels[i * 4 + 2] = 0;   // R
        pixels[i * 4 + 3] = 255; // A
    }
    
    auto out = chroma.Process(frame);
    ASSERT_TRUE(out.has_value());
    
    auto outFrame = out.value();
    uint8_t* outPixels = outFrame->GetVideoPlane(0);
    
    // Alpha should be 0 because it perfectly matches the key color
    EXPECT_EQ(outPixels[3], 0);
}

TEST(ChromaKeyTest, KeepOtherColors) {
    ChromaKey chroma;
    chroma.SetKeyColor(0, 255, 0); // Green
    
    auto frame = MediaFrame::CreateVideo(10, 10, PixelFormat::BGRA);
    uint8_t* pixels = frame->GetVideoPlane(0);
    
    // Fill with Red and full Alpha
    for (int i = 0; i < 100; ++i) {
        pixels[i * 4 + 0] = 0;   // B
        pixels[i * 4 + 1] = 0;   // G
        pixels[i * 4 + 2] = 255; // R
        pixels[i * 4 + 3] = 255; // A
    }
    
    auto out = chroma.Process(frame);
    ASSERT_TRUE(out.has_value());
    
    auto outFrame = out.value();
    uint8_t* outPixels = outFrame->GetVideoPlane(0);
    
    // Alpha should remain 255 because it's red, not green
    EXPECT_EQ(outPixels[3], 255);
}
