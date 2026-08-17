#include <gtest/gtest.h>
#include "openmedia/io/DeviceFactory.h"
#include "openmedia/io/DeviceSource.h"
#include "openmedia/core/MediaFrame.h"
#include "openmedia/io/DeviceInfo.h"

using namespace openmedia;
using namespace openmedia::io;

TEST(DeviceSourceTest, EnumerateDevices) {
    auto devices = DeviceFactory::EnumerateDevices();
    
    // We expect at least one device, since DesktopCapture is always available
    EXPECT_FALSE(devices.empty());

    bool foundDesktop = false;
    for (const auto& dev : devices) {
        if (dev.type == DeviceType::DesktopDuplication) {
            foundDesktop = true;
        }
    }
    EXPECT_TRUE(foundDesktop);
}

TEST(DeviceSourceTest, CreateDesktopCapture) {
    DeviceInfo info{"Primary Monitor", "Monitor0", DeviceType::DesktopDuplication};
    auto result = DeviceFactory::CreateDeviceSource(info);
    ASSERT_TRUE(result.has_value());
    
    auto deviceSource = std::dynamic_pointer_cast<DeviceSource>(result.value());
    ASSERT_NE(deviceSource, nullptr);

    auto devInfo = deviceSource->GetDeviceInfo();
    EXPECT_EQ(devInfo.type, DeviceType::DesktopDuplication);

    // Test formats
    auto formats = deviceSource->GetSupportedFormats();
    EXPECT_FALSE(formats.empty());

    // Start
    auto startResult = deviceSource->Start();
    if (!startResult.has_value()) {
        GTEST_SKIP() << "Failed to start Desktop Duplication, skipping test.";
        return;
    }

    // Pull frame
    auto frameResult = deviceSource->PullFrame();
    
    // It might return WouldBlock or a frame
    if (!frameResult.has_value()) {
        EXPECT_TRUE(frameResult.error().code == core::ErrorCode::WouldBlock);
    } else {
        auto frame = frameResult.value();
        EXPECT_NE(frame, nullptr);
        EXPECT_TRUE(frame->GetMediaType() == core::MediaType::Video);
    }

    // Stop
    EXPECT_TRUE(deviceSource->Stop().has_value());
}

TEST(DeviceSourceTest, CreateDeckLinkMock) {
    DeviceInfo info{"DeckLink Video Capture", "DeckLink0", DeviceType::VideoInput};
    auto result = DeviceFactory::CreateDeviceSource(info);
    
    // It will likely return NotSupported because it's a mock that returns an error when QueryInterface fails.
    if (result.has_value()) {
        auto deviceSource = std::dynamic_pointer_cast<DeviceSource>(result.value());
        ASSERT_NE(deviceSource, nullptr);

        auto devInfo = deviceSource->GetDeviceInfo();
        EXPECT_EQ(devInfo.type, DeviceType::VideoInput);
        
        // We do not test Connect() since it takes a downstream argument
    } else {
        EXPECT_TRUE(result.error().code == core::ErrorCode::FileNotFound);
    }
}
