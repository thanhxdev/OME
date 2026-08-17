#include <openmedia/io/MediaFoundationSource.h>
#include <openmedia/core/Logger.h>
#include <openmedia/core/ErrorCodes.h>

#include <windows.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mfreadwrite.h>

#pragma comment(lib, "mfplat.lib")
#pragma comment(lib, "mfreadwrite.lib")
#pragma comment(lib, "mfuuid.lib")

namespace openmedia::io {

struct MediaFoundationSource::Impl {
    DeviceInfo deviceInfo;
    DeviceFormat currentFormat;
    std::vector<DeviceFormat> supportedFormats;
    core::PipelineState state{core::PipelineState::Idle};

    // COM pointers
    IMFMediaSource* mediaSource = nullptr;
    IMFSourceReader* sourceReader = nullptr;

    ~Impl() {
        if (sourceReader) {
            sourceReader->Release();
            sourceReader = nullptr;
        }
        if (mediaSource) {
            mediaSource->Shutdown();
            mediaSource->Release();
            mediaSource = nullptr;
        }
        MFShutdown();
    }
};

MediaFoundationSource::MediaFoundationSource() : m_impl(std::make_unique<Impl>()) {
    HRESULT hr = MFStartup(MF_VERSION);
    if (FAILED(hr)) {
        auto& logger = core::Logger::Get("IO");
        OME_LOG_ERROR(logger, "Failed to initialize Media Foundation");
    }
}

MediaFoundationSource::~MediaFoundationSource() = default;

core::VoidResult MediaFoundationSource::Open(const std::string& deviceSymbolicLink) {
    auto& logger = core::Logger::Get("IO");
    OME_LOG_INFO(logger, "Opening MediaFoundation source: {}", deviceSymbolicLink);
    // Placeholder for actual device enumeration and creation via MFEnumDeviceSources
    
    m_impl->deviceInfo.id = deviceSymbolicLink;
    m_impl->deviceInfo.name = "MediaFoundation Device";
    m_impl->deviceInfo.type = DeviceType::VideoInput;
    
    return {};
}

std::string MediaFoundationSource::GetName() const {
    return "MediaFoundationSource";
}

core::PipelineState MediaFoundationSource::GetState() const {
    return m_impl->state;
}

core::VoidResult MediaFoundationSource::Initialize() {
    return core::Result<void>();
}

core::VoidResult MediaFoundationSource::Start() {
    m_impl->state = core::PipelineState::Running;
    return {};
}

core::VoidResult MediaFoundationSource::Stop() {
    m_impl->state = core::PipelineState::Stopped;
    return {};
}

core::VoidResult MediaFoundationSource::PushFrame(std::shared_ptr<core::MediaFrame> frame) {
    return std::unexpected(core::Error{core::ErrorCode::NotImplemented, "Not implemented"});
}

core::Result<std::shared_ptr<core::MediaFrame>> MediaFoundationSource::PullFrame() {
    // Return empty frame or timeout
    return std::unexpected(core::Error{core::ErrorCode::Timeout, "Timeout"});
}

core::VoidResult MediaFoundationSource::Connect(std::shared_ptr<core::IMediaObject> downstream) {
    return {};
}

core::VoidResult MediaFoundationSource::Disconnect() {
    return {};
}

void MediaFoundationSource::OnStateChange(core::StateChangeCallback callback) {
    // To be implemented
}

void MediaFoundationSource::OnError(core::ErrorCallback callback) {
    // To be implemented
}

const DeviceInfo& MediaFoundationSource::GetDeviceInfo() const {
    return m_impl->deviceInfo;
}

std::vector<DeviceFormat> MediaFoundationSource::GetSupportedFormats() const {
    return m_impl->supportedFormats;
}

core::VoidResult MediaFoundationSource::SetFormat(const DeviceFormat& format) {
    m_impl->currentFormat = format;
    return {};
}

const DeviceFormat& MediaFoundationSource::GetCurrentFormat() const {
    return m_impl->currentFormat;
}

} // namespace openmedia::io
