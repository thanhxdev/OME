#pragma once

#include <include/cef_render_handler.h>
#include <mutex>
#include <vector>

namespace openmedia::cg {

class CEFRenderHandler : public CefRenderHandler {
public:
    CEFRenderHandler(int width, int height);

    // CefRenderHandler methods:
    void GetViewRect(CefRefPtr<CefBrowser> browser, CefRect& rect) override;
    
    void OnPaint(CefRefPtr<CefBrowser> browser,
                 PaintElementType type,
                 const RectList& dirtyRects,
                 const void* buffer,
                 int width,
                 int height) override;

    // Custom methods to retrieve the rendered frame
    bool GetLatestFrame(std::vector<uint8_t>& outBuffer, int& outWidth, int& outHeight);

private:
    int m_width;
    int m_height;
    std::vector<uint8_t> m_pixelBuffer;
    std::mutex m_mutex;
    bool m_isDirty = false;

    IMPLEMENT_REFCOUNTING(CEFRenderHandler);
};

} // namespace openmedia::cg
