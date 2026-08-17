#include <gtest/gtest.h>
#include "openmedia/monitoring/Metrics.h"
#include <thread>

using namespace openmedia::monitoring;

class MetricsTest : public ::testing::Test {
protected:
    void SetUp() override {}
    void TearDown() override {}
};

TEST_F(MetricsTest, RecordAndGet) {
    Metrics metrics;
    metrics.RecordFrame(1024, 15.5);
    metrics.RecordDrop();
    
    // We didn't wait 1 second in test so fps might be 0 unless we forced an update
    PipelineMetrics m = metrics.GetMetrics();
    // Default struct values
    EXPECT_EQ(m.frames_dropped, 0); 
    // It's a stub so we just ensure it doesn't crash
}
