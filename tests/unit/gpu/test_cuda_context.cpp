#include <gtest/gtest.h>
#include <openmedia/gpu/CUDAContext.h>

using namespace openmedia::gpu;
using namespace openmedia::core;

TEST(CUDAContextTest, DISABLED_Initialization) {
    GTEST_SKIP() << "CUDA tests skipped to prevent driver hang on exit";
}
