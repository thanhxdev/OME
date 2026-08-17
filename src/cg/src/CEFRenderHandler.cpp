#include <openmedia/cg/CEFRenderHandler.h>
#include <cstring>

namespace openmedia::cg {

CEFRenderHandler::CEFRenderHandler(int width, int height)
    : m_width(width), m_height(height) {
    // CEF OSR returns BGRA 32-bit pixels (4 bytes per pixel)
    m_pixelBuffer.resize(width * height * 4, 0);
}

void CEFRenderHandler::GetViewRect(CefRefPtr<CefBrowser> browser, CefRect& rect) {
    rect = CefRect(0, 0, m_width, m_height);
}

void CEFRenderHandler::OnPaint(CefRefPtr<CefBrowser> browser,
                               PaintElementType type,
                               const RectList& dirtyRects,
                               const void* buffer,
                               int width,
                               int height) {
    if (type == PET_VIEW && width == m_width && height == m_height) {
        std::lock_guard<std::mutex> lock(m_mutex);
        // Copy the raw BGRA buffer provided by CEF to our local buffer
        std::memcpy(m_pixelBuffer.data(), buffer, width * height * 4);
        m_isDirty = true;
    }
}

bool CEFRenderHandler::GetLatestFrame(std::vector<uint8_t>& outBuffer, int& outWidth, int& outHeight) {
    std::lock_guard<std::mutex> lock(m_mutex);
    if (!m_isDirty) {
        return false;
    }
    
    outWidth = m_width;
    outHeight = m_height;
    if (outBuffer.size() != m_pixelBuffer.size()) {
        outBuffer.resize(m_pixelBuffer.size());
    }
    std::memcpy(outBuffer.data(), m_pixelBuffer.data(), m_pixelBuffer.size());
    
    m_isDirty = false;
    return true;
}

} // namespace openmedia::cg
