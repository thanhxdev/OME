#include <openmedia/cg/CEFClientHandler.h>
#include <openmedia/core/Logger.h>

namespace openmedia::cg {

CEFClientHandler::CEFClientHandler(CefRefPtr<CEFRenderHandler> renderHandler)
    : m_renderHandler(renderHandler) {}

void CEFClientHandler::OnAfterCreated(CefRefPtr<CefBrowser> browser) {
    m_browser = browser;
    openmedia::core::Logger::SInfo("CEF", "Browser window created (OSR).");
}

bool CEFClientHandler::DoClose(CefRefPtr<CefBrowser> browser) {
    return false;
}

void CEFClientHandler::OnBeforeClose(CefRefPtr<CefBrowser> browser) {
    m_browser = nullptr;
    openmedia::core::Logger::SInfo("CEF", "Browser window closed.");
}

void CEFClientHandler::OnLoadEnd(CefRefPtr<CefBrowser> browser, CefRefPtr<CefFrame> frame, int httpStatusCode) {
    if (frame->IsMain()) {
        openmedia::core::Logger::SInfo("CEF", "Main frame loaded successfully.");
    }
}

void CEFClientHandler::OnLoadError(CefRefPtr<CefBrowser> browser, CefRefPtr<CefFrame> frame, ErrorCode errorCode, const CefString& errorText, const CefString& failedUrl) {
    openmedia::core::Logger::SError("CEF", "Load error: {}, URL: {}", errorText.ToString(), failedUrl.ToString());
}

} // namespace openmedia::cg
