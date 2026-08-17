#include <gtest/gtest.h>
#include <openmedia/overlay/TickerOverlay.h>
#include <openmedia/core/MediaFrame.h>

using namespace OpenMedia::Overlay;
using namespace openmedia::core;

TEST(TickerOverlayTest, Initialization) {
    TickerOverlay overlay;
    TickerConfig config;
    config.text = "Breaking News: OpenMedia SDK is awesome!";
    config.speed = 5;
    EXPECT_NO_THROW(overlay.SetConfig(config));
}

TEST(TickerOverlayTest, Update) {
    TickerOverlay overlay;
    TickerConfig config;
    config.text = "Test";
    config.speed = 10;
    overlay.SetConfig(config);
    
    auto frame = MediaFrame::CreateVideo(1920, 1080, PixelFormat::BGRA);
    EXPECT_TRUE(overlay.RenderToFrame(frame.get()).has_value());
}
