#include <gtest/gtest.h>
#include "openmedia/monitoring/Waveform.h"
#include <vector>

using namespace openmedia::monitoring;

class WaveformTest : public ::testing::Test {
protected:
    void SetUp() override {}
    void TearDown() override {}
};

TEST_F(WaveformTest, ProcessDummyFrame) {
    int width = 1920;
    int height = 1080;
    std::vector<uint8_t> dummy_y(width * height, 128); // Mid-gray
    
    Waveform wf(640, 256);
    wf.ProcessFrame(dummy_y.data(), width, width, height);
    
    const auto& buf = wf.GetBuffer();
    EXPECT_EQ(buf.size(), 640 * 256);
    // 255-128 = 127
    EXPECT_EQ(buf[127 * 640 + 0], 255);
}
