/// @file DeviceInfo.h
#pragma once

#include <string>

namespace openmedia::io {

enum class DeviceType {
    Unknown = 0,
    VideoInput,
    AudioInput,
    DesktopDuplication,
    WindowCapture
};

struct DeviceInfo {
    std::string name;
    std::string id;
    DeviceType type = DeviceType::Unknown;
};

} // namespace openmedia::io
