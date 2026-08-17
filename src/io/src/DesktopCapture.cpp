/// @file DesktopCapture.cpp
#include <openmedia/io/DesktopCapture.h>
#include <openmedia/core/Logger.h>

#include <mutex>
#include <chrono>
#include <d3d11.h>
#include <dxgi1_2.h>
#include <wrl/client.h>

#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "dxgi.lib")

using Microsoft::WRL::ComPtr;

namespace openmedia::io {

struct DesktopCapture::Impl {
    std::mutex mutex;
    core::PipelineState state{core::PipelineState::Stopped};
    std::shared_ptr<core::IMediaObject> downstream;
    core::StateChangeCallback onStateChange;
    core::ErrorCallback onError;

    int displayIndex = 0;
    int regionX = 0;
    int regionY = 0;
    int regionW = 0;
    int regionH = 0;

    int64_t frameCount = 0;
    
    ComPtr<ID3D11Device> d3dDevice;
    ComPtr<ID3D11DeviceContext> d3dContext;
    ComPtr<IDXGIOutputDuplication> desktopDuplication;
    ComPtr<ID3D11Texture2D> stagingTexture;
    
    DXGI_OUTPUT_DESC outputDesc{};
    
    bool InitDXGI();
    void CleanupDXGI();
};

bool DesktopCapture::Impl::InitDXGI() {
    HRESULT hr = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, 0, nullptr, 0, D3D11_SDK_VERSION, &d3dDevice, nullptr, &d3dContext);
    if (FAILED(hr)) {
        core::Logger::SError("DesktopCapture", "Failed to create D3D11 device.");
        return false;
    }
    
    ComPtr<IDXGIDevice> dxgiDevice;
    hr = d3dDevice.As(&dxgiDevice);
    if (FAILED(hr)) return false;
    
    ComPtr<IDXGIAdapter> dxgiAdapter;
    hr = dxgiDevice->GetAdapter(&dxgiAdapter);
    if (FAILED(hr)) return false;
    
    ComPtr<IDXGIOutput> dxgiOutput;
    hr = dxgiAdapter->EnumOutputs(displayIndex, &dxgiOutput);
    if (FAILED(hr)) {
        core::Logger::SError("DesktopCapture", "Failed to get DXGI output index {}", displayIndex);
        return false;
    }
    
    hr = dxgiOutput->GetDesc(&outputDesc);
    
    ComPtr<IDXGIOutput1> dxgiOutput1;
    hr = dxgiOutput.As(&dxgiOutput1);
    if (FAILED(hr)) return false;
    
    hr = dxgiOutput1->DuplicateOutput(d3dDevice.Get(), &desktopDuplication);
    if (FAILED(hr)) {
        core::Logger::SError("DesktopCapture", "Failed to create Desktop Duplication. (HR=0x{:X})", (uint32_t)hr);
        return false;
    }
    
    // Create staging texture
    D3D11_TEXTURE2D_DESC texDesc = {};
    texDesc.Width = outputDesc.DesktopCoordinates.right - outputDesc.DesktopCoordinates.left;
    texDesc.Height = outputDesc.DesktopCoordinates.bottom - outputDesc.DesktopCoordinates.top;
    texDesc.MipLevels = 1;
    texDesc.ArraySize = 1;
    texDesc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
    texDesc.SampleDesc.Count = 1;
    texDesc.Usage = D3D11_USAGE_STAGING;
    texDesc.BindFlags = 0;
    texDesc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    texDesc.MiscFlags = 0;
    
    hr = d3dDevice->CreateTexture2D(&texDesc, nullptr, &stagingTexture);
    if (FAILED(hr)) return false;
    
    return true;
}

void DesktopCapture::Impl::CleanupDXGI() {
    stagingTexture.Reset();
    desktopDuplication.Reset();
    d3dContext.Reset();
    d3dDevice.Reset();
}

DesktopCapture::DesktopCapture() : m_impl(new Impl()) {}

DesktopCapture::~DesktopCapture() {
    (void)Stop();
}

std::string DesktopCapture::GetName() const { return "DesktopCapture"; }

core::PipelineState DesktopCapture::GetState() const { return m_impl->state; }

core::VoidResult DesktopCapture::Initialize() {
    return {};
}

core::VoidResult DesktopCapture::Start() {
    std::lock_guard lock(m_impl->mutex);
    if (!m_impl->InitDXGI()) {
        return std::unexpected(core::Error::Make(core::ErrorCode::DeviceOpenFailed, "Failed to initialize DXGI Duplication"));
    }
    m_impl->state = core::PipelineState::Running;
    m_impl->frameCount = 0;
    return {};
}

core::VoidResult DesktopCapture::Stop() {
    std::lock_guard lock(m_impl->mutex);
    m_impl->CleanupDXGI();
    m_impl->state = core::PipelineState::Stopped;
    return {};
}

core::VoidResult DesktopCapture::PushFrame(std::shared_ptr<core::MediaFrame> /*frame*/) {
    return std::unexpected(core::Error::Make(core::ErrorCode::NotImplemented, "DesktopCapture does not accept incoming frames"));
}

core::Result<std::shared_ptr<core::MediaFrame>> DesktopCapture::PullFrame() {
    std::lock_guard lock(m_impl->mutex);
    if (m_impl->state != core::PipelineState::Running) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "DesktopCapture is not running"));
    }
    if (!m_impl->desktopDuplication) {
        return std::unexpected(core::Error::Make(core::ErrorCode::DeviceOpenFailed, "DXGI not initialized"));
    }

    DXGI_OUTDUPL_FRAME_INFO frameInfo;
    ComPtr<IDXGIResource> desktopResource;
    
    HRESULT hr = m_impl->desktopDuplication->AcquireNextFrame(100, &frameInfo, &desktopResource);
    if (hr == DXGI_ERROR_WAIT_TIMEOUT) {
        return std::unexpected(core::Error::Make(core::ErrorCode::Unknown, "Timeout"));
    }
    if (FAILED(hr)) {
        // Access lost, needs re-init
        m_impl->CleanupDXGI();
        m_impl->InitDXGI();
        return std::unexpected(core::Error::Make(core::ErrorCode::DeviceOpenFailed, "DXGI Acquire Error"));
    }
    
    ComPtr<ID3D11Texture2D> acquiredTexture;
    hr = desktopResource.As(&acquiredTexture);
    if (FAILED(hr)) {
        m_impl->desktopDuplication->ReleaseFrame();
        return std::unexpected(core::Error::Make(core::ErrorCode::DeviceOpenFailed, "Failed to get texture"));
    }
    
    m_impl->d3dContext->CopyResource(m_impl->stagingTexture.Get(), acquiredTexture.Get());
    m_impl->desktopDuplication->ReleaseFrame();
    
    D3D11_MAPPED_SUBRESOURCE mapped;
    hr = m_impl->d3dContext->Map(m_impl->stagingTexture.Get(), 0, D3D11_MAP_READ, 0, &mapped);
    if (FAILED(hr)) {
        return std::unexpected(core::Error::Make(core::ErrorCode::DeviceOpenFailed, "Failed to map staging texture"));
    }
    
    int desktopWidth = m_impl->outputDesc.DesktopCoordinates.right - m_impl->outputDesc.DesktopCoordinates.left;
    int desktopHeight = m_impl->outputDesc.DesktopCoordinates.bottom - m_impl->outputDesc.DesktopCoordinates.top;
    
    int capX = (m_impl->regionW > 0) ? m_impl->regionX : 0;
    int capY = (m_impl->regionH > 0) ? m_impl->regionY : 0;
    int capW = (m_impl->regionW > 0) ? m_impl->regionW : desktopWidth;
    int capH = (m_impl->regionH > 0) ? m_impl->regionH : desktopHeight;
    
    if (capX + capW > desktopWidth) capW = desktopWidth - capX;
    if (capY + capH > desktopHeight) capH = desktopHeight - capY;
    if (capW <= 0 || capH <= 0) capW = capH = 1; // Fallback
    
    auto frame = core::MediaFrame::CreateVideo(capW, capH, core::PixelFormat::BGRA);
    
    uint8_t* dst = frame->GetVideoPlane(0);
    const uint8_t* src = static_cast<const uint8_t*>(mapped.pData);
    int dstStride = frame->GetLineSize(0);
    int srcStride = mapped.RowPitch;
    
    for (int y = 0; y < capH; ++y) {
        memcpy(dst + y * dstStride, src + (capY + y) * srcStride + capX * 4, capW * 4);
    }
    
    m_impl->d3dContext->Unmap(m_impl->stagingTexture.Get(), 0);
    
    int64_t pts = m_impl->frameCount * 3000; // Assuming 30fps at 90kHz timebase
    frame->SetPts(pts);
    m_impl->frameCount++;

    return frame;
}

core::VoidResult DesktopCapture::Connect(std::shared_ptr<core::IMediaObject> downstream) {
    std::lock_guard lock(m_impl->mutex);
    m_impl->downstream = downstream;
    return {};
}

core::VoidResult DesktopCapture::Disconnect() {
    std::lock_guard lock(m_impl->mutex);
    m_impl->downstream.reset();
    return {};
}

void DesktopCapture::OnStateChange(core::StateChangeCallback callback) {
    m_impl->onStateChange = callback;
}

void DesktopCapture::OnError(core::ErrorCallback callback) {
    m_impl->onError = callback;
}

void DesktopCapture::SetDisplayIndex(int index) {
    std::lock_guard lock(m_impl->mutex);
    m_impl->displayIndex = index;
}

void DesktopCapture::SetCaptureRegion(int x, int y, int width, int height) {
    std::lock_guard lock(m_impl->mutex);
    m_impl->regionX = x;
    m_impl->regionY = y;
    m_impl->regionW = width;
    m_impl->regionH = height;
}

const DeviceInfo& DesktopCapture::GetDeviceInfo() const {
    static DeviceInfo info{"Primary Monitor", "Monitor0", DeviceType::DesktopDuplication};
    return info;
}

std::vector<DeviceFormat> DesktopCapture::GetSupportedFormats() const {
    std::vector<DeviceFormat> formats;
    formats.push_back({1920, 1080, 60.0f, core::PixelFormat::BGRA, core::SampleFormat::Unknown, 0, 0});
    formats.push_back({1920, 1080, 30.0f, core::PixelFormat::BGRA, core::SampleFormat::Unknown, 0, 0});
    return formats;
}

core::VoidResult DesktopCapture::SetFormat(const DeviceFormat& /*format*/) {
    return {};
}

const DeviceFormat& DesktopCapture::GetCurrentFormat() const {
    static DeviceFormat fmt{1920, 1080, 30.0f, core::PixelFormat::BGRA, core::SampleFormat::Unknown, 0, 0};
    return fmt;
}

} // namespace openmedia::io
