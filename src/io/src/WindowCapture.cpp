/// @file WindowCapture.cpp
#include <openmedia/io/WindowCapture.h>
#include <openmedia/core/Logger.h>

#include <mutex>
#include <chrono>
#define WIN32_LEAN_AND_MEAN
#include <windows.h>

namespace openmedia::io {

struct WindowCapture::Impl {
    std::mutex mutex;
    core::PipelineState state{core::PipelineState::Stopped};
    std::shared_ptr<core::IMediaObject> downstream;
    core::StateChangeCallback onStateChange;
    core::ErrorCallback onError;

    std::string targetTitle;
    HWND targetHwnd = nullptr;
    int64_t frameCount = 0;
};

WindowCapture::WindowCapture() : m_impl(new Impl()) {}

WindowCapture::~WindowCapture() {
    (void)Stop();
}

std::string WindowCapture::GetName() const { return "WindowCapture"; }

core::PipelineState WindowCapture::GetState() const { return m_impl->state; }

core::VoidResult WindowCapture::Initialize() {
    return {};
}

core::VoidResult WindowCapture::Start() {
    std::lock_guard lock(m_impl->mutex);
    if (m_impl->targetTitle.empty() && m_impl->targetHwnd == nullptr) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "No target window specified"));
    }
    
    if (m_impl->targetHwnd == nullptr) {
        m_impl->targetHwnd = FindWindowA(nullptr, m_impl->targetTitle.c_str());
        if (!m_impl->targetHwnd) {
            core::Logger::SError("WindowCapture", "Could not find window with title: {}", m_impl->targetTitle);
            return std::unexpected(core::Error::Make(core::ErrorCode::NotFound, "Window not found"));
        }
    }

    m_impl->state = core::PipelineState::Running;
    m_impl->frameCount = 0;
    return {};
}

core::VoidResult WindowCapture::Stop() {
    std::lock_guard lock(m_impl->mutex);
    m_impl->state = core::PipelineState::Stopped;
    return {};
}

core::VoidResult WindowCapture::PushFrame(std::shared_ptr<core::MediaFrame> /*frame*/) {
    return std::unexpected(core::Error::Make(core::ErrorCode::NotImplemented, "WindowCapture does not accept incoming frames"));
}

core::Result<std::shared_ptr<core::MediaFrame>> WindowCapture::PullFrame() {
    std::lock_guard lock(m_impl->mutex);
    if (m_impl->state != core::PipelineState::Running) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "WindowCapture is not running"));
    }
    
    if (!IsWindow(m_impl->targetHwnd)) {
        return std::unexpected(core::Error::Make(core::ErrorCode::NotFound, "Target window was closed"));
    }

    RECT rc;
    GetClientRect(m_impl->targetHwnd, &rc);
    int width = rc.right - rc.left;
    int height = rc.bottom - rc.top;

    if (width <= 0 || height <= 0) {
        // Window might be minimized
        return std::unexpected(core::Error::Make(core::ErrorCode::Unknown, "Invalid window size"));
    }

    HDC hdcWindow = GetDC(m_impl->targetHwnd);
    HDC hdcMemDC = CreateCompatibleDC(hdcWindow);
    HBITMAP hbmScreen = CreateCompatibleBitmap(hdcWindow, width, height);
    SelectObject(hdcMemDC, hbmScreen);

    // Use PrintWindow or BitBlt
    // PrintWindow(m_impl->targetHwnd, hdcMemDC, PW_CLIENTONLY);
    BitBlt(hdcMemDC, 0, 0, width, height, hdcWindow, 0, 0, SRCCOPY);

    BITMAPINFOHEADER bi;
    bi.biSize = sizeof(BITMAPINFOHEADER);
    bi.biWidth = width;
    bi.biHeight = -height; // Top-down
    bi.biPlanes = 1;
    bi.biBitCount = 32;
    bi.biCompression = BI_RGB;
    bi.biSizeImage = 0;
    bi.biXPelsPerMeter = 0;
    bi.biYPelsPerMeter = 0;
    bi.biClrUsed = 0;
    bi.biClrImportant = 0;

    auto frame = core::MediaFrame::CreateVideo(width, height, core::PixelFormat::BGRA);
    uint8_t* dst = frame->GetVideoPlane(0);
    int dstStride = frame->GetLineSize(0);

    // If strides match exactly, we can read directly.
    if (dstStride == width * 4) {
        GetDIBits(hdcWindow, hbmScreen, 0, height, dst, (BITMAPINFO*)&bi, DIB_RGB_COLORS);
    } else {
        std::vector<uint8_t> tempBuffer(width * height * 4);
        GetDIBits(hdcWindow, hbmScreen, 0, height, tempBuffer.data(), (BITMAPINFO*)&bi, DIB_RGB_COLORS);
        for (int y = 0; y < height; ++y) {
            memcpy(dst + y * dstStride, tempBuffer.data() + y * width * 4, width * 4);
        }
    }

    DeleteObject(hbmScreen);
    DeleteDC(hdcMemDC);
    ReleaseDC(m_impl->targetHwnd, hdcWindow);

    int64_t pts = m_impl->frameCount * 3000; // 30fps
    frame->SetPts(pts);
    m_impl->frameCount++;

    return frame;
}

core::VoidResult WindowCapture::Connect(std::shared_ptr<core::IMediaObject> downstream) {
    std::lock_guard lock(m_impl->mutex);
    m_impl->downstream = downstream;
    return {};
}

core::VoidResult WindowCapture::Disconnect() {
    std::lock_guard lock(m_impl->mutex);
    m_impl->downstream.reset();
    return {};
}

void WindowCapture::OnStateChange(core::StateChangeCallback callback) {
    m_impl->onStateChange = callback;
}

void WindowCapture::OnError(core::ErrorCallback callback) {
    m_impl->onError = callback;
}

void WindowCapture::SetTargetWindowTitle(const std::string& title) {
    std::lock_guard lock(m_impl->mutex);
    m_impl->targetTitle = title;
    m_impl->targetHwnd = nullptr; // Reset to find it again on start
}

const DeviceInfo& WindowCapture::GetDeviceInfo() const {
    static DeviceInfo info{"Window Capture", "WinCap0", DeviceType::WindowCapture};
    return info;
}

std::vector<DeviceFormat> WindowCapture::GetSupportedFormats() const {
    // Dynamic based on window, returning a dummy list
    std::vector<DeviceFormat> formats;
    formats.push_back({1920, 1080, 30.0f, core::PixelFormat::BGRA, core::SampleFormat::Unknown, 0, 0});
    return formats;
}

core::VoidResult WindowCapture::SetFormat(const DeviceFormat& /*format*/) {
    return {};
}

const DeviceFormat& WindowCapture::GetCurrentFormat() const {
    static DeviceFormat fmt{1920, 1080, 30.0f, core::PixelFormat::BGRA, core::SampleFormat::Unknown, 0, 0};
    return fmt;
}

} // namespace openmedia::io
