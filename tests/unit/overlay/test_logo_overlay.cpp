#include <gtest/gtest.h>
#include <openmedia/overlay/LogoOverlay.h>

using namespace openmedia::overlay;

TEST(LogoOverlayTest, Initialization) {
    LogoOverlay overlay("logo1", "dummy.png");
    EXPECT_EQ(overlay.GetImagePath(), "dummy.png");
    EXPECT_NO_THROW(overlay.SetPosition(100, 100));
}

TEST(LogoOverlayTest, GetFilterString) {
    LogoOverlay overlay("logo1", "dummy.png");
    overlay.SetPosition(100, 100);
    std::string filter = overlay.GetFilterString("in", "out");
    EXPECT_FALSE(filter.empty());
}
