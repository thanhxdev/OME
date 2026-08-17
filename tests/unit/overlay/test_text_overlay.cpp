#include <gtest/gtest.h>
#include <openmedia/overlay/TextOverlay.h>

using namespace openmedia::overlay;

TEST(TextOverlayTest, Initialization) {
    TextOverlay overlay("text1", "Hello World");
    EXPECT_EQ(overlay.GetText(), "Hello World");
    EXPECT_NO_THROW(overlay.SetFontSize(24));
}

TEST(TextOverlayTest, GetFilterString) {
    TextOverlay overlay("text1", "Test");
    overlay.SetPosition(10, 10);
    std::string filter = overlay.GetFilterString("in", "out");
    EXPECT_FALSE(filter.empty());
}
