#include <gtest/gtest.h>
#include "openmedia/outputs/ipc/SharedMemoryOutput.h"

using namespace openmedia::outputs::ipc;

class IPCOutputTest : public ::testing::Test {
protected:
    void SetUp() override {}
    void TearDown() override {}
};

TEST_F(IPCOutputTest, SharedMemoryStartStop) {
    SharedMemoryOutput shm;
    EXPECT_TRUE(shm.Start("OpenMedia_SHM_1"));
    shm.Stop();
}
