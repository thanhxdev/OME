/// @file DeviceFactory.h
#pragma once

#include <openmedia/core/IMediaObject.h>
#include <openmedia/io/DeviceInfo.h>

#include <vector>
#include <memory>
#include <expected>

namespace openmedia::io {

class DeviceFactory {
public:
    static std::vector<DeviceInfo> EnumerateDevices(DeviceType filter = DeviceType::Unknown);
    
    static core::Result<std::shared_ptr<core::IMediaObject>> CreateDeviceSource(const DeviceInfo& info);
};

} // namespace openmedia::io
