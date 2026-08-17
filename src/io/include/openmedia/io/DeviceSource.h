#pragma once

/// @file DeviceSource.h
/// @brief Base abstract class for hardware device capture
/// @since 1.0.0

#include <openmedia/core/IMediaObject.h>
#include <openmedia/io/DeviceInfo.h>

#include <vector>

namespace openmedia::io {

/// @brief Describes the format configuration of a hardware device
struct DeviceFormat {
    uint32_t width = 0;
    uint32_t height = 0;
    float fps = 0.0f;
    core::PixelFormat pixelFormat = core::PixelFormat::Unknown;

    // Audio specific fields
    core::SampleFormat sampleFormat = core::SampleFormat::Unknown;
    uint32_t sampleRate = 0;
    uint32_t channels = 0;
};

/// @brief Base class for any hardware capture source (Webcam, DeckLink, Screen, etc.)
class DeviceSource : public core::IMediaObject {
public:
    virtual ~DeviceSource() = default;

    /// @brief Get the device hardware information
    virtual const DeviceInfo& GetDeviceInfo() const = 0;

    /// @brief Retrieve a list of formats that the device supports
    virtual std::vector<DeviceFormat> GetSupportedFormats() const = 0;

    /// @brief Set the format (resolution/framerate) of the device
    virtual core::VoidResult SetFormat(const DeviceFormat& format) = 0;

    /// @brief Get the currently active format
    virtual const DeviceFormat& GetCurrentFormat() const = 0;
};

} // namespace openmedia::io
