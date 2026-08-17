/// @file SnapshotOutput.h
#pragma once

#include <openmedia/core/IMediaObject.h>

#include <string>
#include <memory>
#include <vector>

namespace openmedia::io {

class SnapshotOutput : public core::IMediaObject {
public:
    SnapshotOutput();
    ~SnapshotOutput() override;

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

    // Custom APIs
    core::VoidResult SaveNextFrame(const std::string& path);

private:
    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

} // namespace openmedia::io
