#include <gtest/gtest.h>
#include "openmedia/outputs/decklink/DeckLinkOutput.h"
#include "openmedia/outputs/decklink/DeckLinkSource.h"

using namespace openmedia::outputs::decklink;

class DeckLinkTest : public ::testing::Test {
protected:
    void SetUp() override {}
    void TearDown() override {}
};

TEST_F(DeckLinkTest, SourceStartStop) {
    DeckLinkSource source;
    EXPECT_TRUE(source.Start(0, 0));
    source.Stop();
}

TEST_F(DeckLinkTest, OutputStartStop) {
    DeckLinkOutput output;
    EXPECT_TRUE(output.Start(0, 0));
    output.Stop();
}
