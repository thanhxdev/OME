#include <openmedia/cg/CGEngine.h>
#include <openmedia/core/Logger.h>
#include <cstring>

#ifdef OME_ENABLE_CEF
#include <include/cef_app.h>
#include <openmedia/cg/CEFAppHandler.h>
#include <openmedia/cg/CEFClientHandler.h>
#include <openmedia/cg/CEFRenderHandler.h>
#ifdef _WIN32
#include <windows.h>
#endif
#endif

namespace openmedia::cg {

struct CGEngine::Impl {
    int width;
    int height;
    std::string templatePath;

#ifdef OME_ENABLE_CEF
    CefRefPtr<CEFAppHandler> appHandler;
    CefRefPtr<CEFRenderHandler> renderHandler;
    CefRefPtr<CEFClientHandler> clientHandler;
    bool cefInitialized = false;
#endif

    Impl(int w, int h) : width(w), height(h) {}
};

int CGEngine::InitializeSubProcess() {
#if defined(OME_ENABLE_CEF) && defined(_WIN32)
    CefMainArgs main_args(GetModuleHandle(NULL));
    return CefExecuteProcess(main_args, nullptr, nullptr);
#else
    return -1;
#endif
}

CGEngine::CGEngine(int width, int height) : pImpl(std::make_unique<Impl>(width, height)) {
#ifdef OME_ENABLE_CEF
    CefMainArgs main_args; // Windows-specific args usually require HINSTANCE, empty for basic setup
    pImpl->appHandler = new CEFAppHandler();
    
    CefSettings settings;
    settings.windowless_rendering_enabled = true;
    settings.no_sandbox = true;
    
    // Initialize CEF
    if (CefInitialize(main_args, settings, pImpl->appHandler.get(), nullptr)) {
        pImpl->cefInitialized = true;
        pImpl->renderHandler = new CEFRenderHandler(width, height);
        pImpl->clientHandler = new CEFClientHandler(pImpl->renderHandler);
        openmedia::core::Logger::SInfo("CGEngine", "CEF Initialized successfully.");
    } else {
        openmedia::core::Logger::SError("CGEngine", "Failed to initialize CEF.");
    }
#else
    openmedia::core::Logger::SInfo("CGEngine", "CGEngine initialized without CEF support.");
#endif
}

CGEngine::~CGEngine() {
#ifdef OME_ENABLE_CEF
    if (pImpl->cefInitialized) {
        if (pImpl->clientHandler && pImpl->clientHandler->GetBrowser()) {
            pImpl->clientHandler->GetBrowser()->GetHost()->CloseBrowser(true);
        }
        pImpl->clientHandler = nullptr;
        pImpl->renderHandler = nullptr;
        pImpl->appHandler = nullptr;
        CefShutdown();
    }
#endif
}

core::VoidResult CGEngine::LoadTemplate(const std::string& templatePath) {
    pImpl->templatePath = templatePath;
    openmedia::core::Logger::SInfo("CGEngine", "Loading CG template: {}", templatePath);

#ifdef OME_ENABLE_CEF
    if (pImpl->cefInitialized) {
        CefWindowInfo window_info;
        window_info.SetAsWindowless(0); // Off-screen rendering

        CefBrowserSettings browser_settings;
        browser_settings.windowless_frame_rate = 60; // Render at 60fps
        browser_settings.background_color = 0x00000000; // Transparent background (Alpha = 0)

        // Ensure we handle absolute file paths correctly for local templates
        std::string finalPath = templatePath;
        if (templatePath.find("http://") != 0 && templatePath.find("https://") != 0 && templatePath.find("file://") != 0) {
            finalPath = "file:///" + templatePath;
        }

        CefBrowserHost::CreateBrowser(window_info, pImpl->clientHandler.get(), 
                                      CefString(finalPath), browser_settings, nullptr, nullptr);
    }
#endif
    return {};
}

core::VoidResult CGEngine::BindData(const std::string& key, const std::string& value) {
#ifdef OME_ENABLE_CEF
    if (pImpl->clientHandler && pImpl->clientHandler->GetBrowser()) {
        auto browser = pImpl->clientHandler->GetBrowser();
        auto frame = browser->GetMainFrame();
        
        // Very basic data binding via executing javascript
        // Assuming there is a JS function: updateData(key, value)
        std::string js = "if (typeof updateData === 'function') { updateData('" + key + "', '" + value + "'); }";
        frame->ExecuteJavaScript(CefString(js), frame->GetURL(), 0);
    }
#endif
    return {};
}

core::VoidResult CGEngine::Render(std::shared_ptr<core::MediaFrame> frame) {
    if (!frame) return std::unexpected(openmedia::core::Error::Make(core::ErrorCode::InvalidArgument, "Frame is null"));

#ifdef OME_ENABLE_CEF
    if (pImpl->renderHandler) {
        std::vector<uint8_t> bgraBuffer;
        int w, h;
        if (pImpl->renderHandler->GetLatestFrame(bgraBuffer, w, h)) {
            // Write the BGRA buffer to the MediaFrame's video plane
            // Note: In reality, we need to convert BGRA to whatever format the MediaFrame uses,
            // or ensure the MediaFrame is created as BGRA.
            if (bgraBuffer.size() > 0) {
                auto plane = frame->GetVideoPlane(0);
                if (plane && frame->GetTotalSize() >= bgraBuffer.size()) {
                    std::memcpy(plane, bgraBuffer.data(), bgraBuffer.size());
                }
            }
        }
    }
#endif
    return {};
}

void CGEngine::DoMessageLoopWork() {
#ifdef OME_ENABLE_CEF
    if (pImpl->cefInitialized) {
        CefDoMessageLoopWork();
    }
#endif
}

} // namespace openmedia::cg
