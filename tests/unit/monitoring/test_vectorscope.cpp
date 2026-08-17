#include <gtest/gtest.h>
#include "openmedia/monitoring/Vectorscope.h"
#include <vector>

using namespace openmedia::monitoring;

class VectorscopeTest : public ::testing::Test {
protected:
    void SetUp() override {}
    void TearDown() override {}
};

TEST_F(VectorscopeTest, ProcessDummyFrame) {
    int width = 1920;
    int height = 1080;
    std::vector<uint8_t> dummy_u(width * height, 64);
    std::vector<uint8_t> dummy_v(width * height, 192);
    
    Vectorscope vs(256);
    vs.ProcessFrame(dummy_u.data(), dummy_v.data(), width, width, height);
    
    const auto& buf = vs.GetBuffer();
    EXPECT_EQ(buf.size(), 256 * 256);
}
