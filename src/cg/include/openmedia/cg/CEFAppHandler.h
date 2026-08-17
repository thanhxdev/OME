#pragma once

#include <include/cef_app.h>

namespace openmedia::cg {

class CEFAppHandler : public CefApp, public CefBrowserProcessHandler {
public:
    CEFAppHandler();

    // CefApp methods:
    CefRefPtr<CefBrowserProcessHandler> GetBrowserProcessHandler() override {
        return this;
    }

    void OnBeforeCommandLineProcessing(const CefString& process_type, CefRefPtr<CefCommandLine> command_line) override;

    // CefBrowserProcessHandler methods:
    void OnContextInitialized() override;

private:
    IMPLEMENT_REFCOUNTING(CEFAppHandler);
};

} // namespace openmedia::cg
