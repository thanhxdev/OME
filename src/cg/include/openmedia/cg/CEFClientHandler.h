#pragma once

#include <include/cef_client.h>
#include <openmedia/cg/CEFRenderHandler.h>

namespace openmedia::cg {

class CEFClientHandler : public CefClient, public CefLifeSpanHandler, public CefLoadHandler {
public:
    CEFClientHandler(CefRefPtr<CEFRenderHandler> renderHandler);

    // CefClient methods:
    CefRefPtr<CefLifeSpanHandler> GetLifeSpanHandler() override { return this; }
    CefRefPtr<CefLoadHandler> GetLoadHandler() override { return this; }
    CefRefPtr<CefRenderHandler> GetRenderHandler() override { return m_renderHandler; }

    // CefLifeSpanHandler methods:
    void OnAfterCreated(CefRefPtr<CefBrowser> browser) override;
    bool DoClose(CefRefPtr<CefBrowser> browser) override;
    void OnBeforeClose(CefRefPtr<CefBrowser> browser) override;

    // CefLoadHandler methods:
    void OnLoadEnd(CefRefPtr<CefBrowser> browser, CefRefPtr<CefFrame> frame, int httpStatusCode) override;
    void OnLoadError(CefRefPtr<CefBrowser> browser, CefRefPtr<CefFrame> frame, ErrorCode errorCode, const CefString& errorText, const CefString& failedUrl) override;

    CefRefPtr<CefBrowser> GetBrowser() const { return m_browser; }

private:
    CefRefPtr<CEFRenderHandler> m_renderHandler;
    CefRefPtr<CefBrowser> m_browser;

    IMPLEMENT_REFCOUNTING(CEFClientHandler);
};

} // namespace openmedia::cg
