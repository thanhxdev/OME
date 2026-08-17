/// @file DesktopCapture.h
#pragma once

#include <openmedia/io/DeviceSource.h>

#include <string>
#include <memory>
#include <vector>

namespace openmedia::io {

class DesktopCapture : public DeviceSource {
public:
    DesktopCapture();
    ~DesktopCapture() override;

    // IMediaObject
    std::string GetName() const override;
    core::PipelineState GetState() const override;
    core::VoidResult Initialize() override;
    core::VoidResult Start() override;
    core::VoidResult Stop() override;
    core::VoidResult PushFrame(std::shared_ptr<core::MediaFrame> frame) override;
    [[nodiscard]] core::Result<std::shared_ptr<core::MediaFrame>> PullFrame() override;
    core::VoidResult Connect(std::shared_ptr<core::IMediaObject> downstream) override;
    core::VoidResult Disconnect() override;
    void OnStateChange(core::StateChangeCallback callback) override;
    void OnError(core::ErrorCallback callback) override;

    // DeviceSource
    const DeviceInfo& GetDeviceInfo() const override;
    std::vector<DeviceFormat> GetSupportedFormats() const override;
    core::VoidResult SetFormat(const DeviceFormat& format) override;
    const DeviceFormat& GetCurrentFormat() const override;

    // Custom APIs
    void SetDisplayIndex(int index);
    void SetCaptureRegion(int x, int y, int width, int height);

private:
    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

} // namespace openmedia::io
