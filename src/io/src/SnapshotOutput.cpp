/// @file SnapshotOutput.cpp
#include <openmedia/io/SnapshotOutput.h>
#include <openmedia/core/Logger.h>

#include <mutex>

namespace openmedia::io {

struct SnapshotOutput::Impl {
    std::mutex mutex;
    core::PipelineState state{core::PipelineState::Stopped};
    std::shared_ptr<core::IMediaObject> downstream;
    core::StateChangeCallback onStateChange;
    core::ErrorCallback onError;

    bool captureNextFrame = false;
    std::string capturePath;
};

SnapshotOutput::SnapshotOutput() : m_impl(new Impl()) {}

SnapshotOutput::~SnapshotOutput() {
    (void)Stop();
}

std::string SnapshotOutput::GetName() const { return "SnapshotOutput"; }

core::PipelineState SnapshotOutput::GetState() const { return m_impl->state; }

core::VoidResult SnapshotOutput::Initialize() { return {}; }

core::VoidResult SnapshotOutput::Start() {
    std::lock_guard lock(m_impl->mutex);
    m_impl->state = core::PipelineState::Running;
    return {};
}

core::VoidResult SnapshotOutput::Stop() {
    std::lock_guard lock(m_impl->mutex);
    m_impl->state = core::PipelineState::Stopped;
    return {};
}

core::VoidResult SnapshotOutput::PushFrame(std::shared_ptr<core::MediaFrame> frame) {
    std::lock_guard lock(m_impl->mutex);
    if (m_impl->state != core::PipelineState::Running) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "SnapshotOutput is not running"));
    }

    if (m_impl->captureNextFrame && !m_impl->capturePath.empty()) {
        // Encode and save frame to m_impl->capturePath as PNG/JPEG
        // Mock implementation
        m_impl->captureNextFrame = false;
        m_impl->capturePath.clear();
    }

    // Propagate to downstream if connected
    if (m_impl->downstream) {
        return m_impl->downstream->PushFrame(frame);
    }
    return {};
}

core::Result<std::shared_ptr<core::MediaFrame>> SnapshotOutput::PullFrame() {
    return std::unexpected(core::Error::Make(core::ErrorCode::NotImplemented, "SnapshotOutput does not produce frames"));
}

core::VoidResult SnapshotOutput::Connect(std::shared_ptr<core::IMediaObject> downstream) {
    std::lock_guard lock(m_impl->mutex);
    m_impl->downstream = downstream;
    return {};
}

core::VoidResult SnapshotOutput::Disconnect() {
    std::lock_guard lock(m_impl->mutex);
    m_impl->downstream.reset();
    return {};
}

void SnapshotOutput::OnStateChange(core::StateChangeCallback callback) {
    m_impl->onStateChange = callback;
}

void SnapshotOutput::OnError(core::ErrorCallback callback) {
    m_impl->onError = callback;
}

core::VoidResult SnapshotOutput::SaveNextFrame(const std::string& path) {
    std::lock_guard lock(m_impl->mutex);
    m_impl->capturePath = path;
    m_impl->captureNextFrame = true;
    return {};
}

} // namespace openmedia::io
