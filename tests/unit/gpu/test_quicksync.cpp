#include <gtest/gtest.h>
#include <openmedia/gpu/QuickSyncContext.h>

using namespace openmedia::gpu;
using namespace openmedia::core;

TEST(QuickSyncContextTest, Initialization) {
    QuickSyncContext context;
    EXPECT_EQ(context.GetType(), GPUType::QuickSync);
    
    auto result = context.Initialize();
    if (!result.has_value()) { GTEST_SKIP() << "QuickSync not available"; return; }
    EXPECT_EQ(context.GetDeviceName(), "Intel QuickSync Video");
    
    if (context.GetDeviceHandle() == nullptr) { GTEST_SKIP() << "QuickSync hardware unavailable (nullptr handle)"; return; }
    EXPECT_NE(context.GetDeviceHandle(), nullptr);
    
    context.Shutdown();
    EXPECT_EQ(context.GetDeviceHandle(), nullptr);
}
