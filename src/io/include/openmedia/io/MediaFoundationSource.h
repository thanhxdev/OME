/// @file MediaFoundationSource.h
#pragma once

#include <openmedia/io/DeviceSource.h>
#include <memory>
#include <string>

namespace openmedia::io {

class MediaFoundationSource : public DeviceSource {
public:
    MediaFoundationSource();
    ~MediaFoundationSource() override;

    // MediaFoundationSource specific
    core::VoidResult Open(const std::string& deviceSymbolicLink);

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

private:
    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

} // namespace openmedia::io
