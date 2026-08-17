#include <gtest/gtest.h>
#include "openmedia/outputs/ott/HLSOutput.h"
#include "openmedia/outputs/ott/DASHOutput.h"
#include "openmedia/outputs/ott/CMAFOutput.h"

using namespace openmedia::outputs::ott;

class OTTOutputsTest : public ::testing::Test {
protected:
    void SetUp() override {}
    void TearDown() override {}
};

TEST_F(OTTOutputsTest, HLSStartStop) {
    HLSOutput hls;
    EXPECT_TRUE(hls.Start("/tmp", "playlist.m3u8"));
    hls.Stop();
}

TEST_F(OTTOutputsTest, DASHStartStop) {
    DASHOutput dash;
    EXPECT_TRUE(dash.Start("/tmp", "manifest.mpd"));
    dash.Stop();
}

TEST_F(OTTOutputsTest, CMAFStartStop) {
    CMAFOutput cmaf;
    EXPECT_TRUE(cmaf.Start("/tmp", "cmaf.m3u8"));
    cmaf.Stop();
}
