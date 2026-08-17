#include <openmedia/cg/CEFAppHandler.h>
#include <openmedia/core/Logger.h>

namespace openmedia::cg {

CEFAppHandler::CEFAppHandler() {}

void CEFAppHandler::OnBeforeCommandLineProcessing(const CefString& process_type, CefRefPtr<CefCommandLine> command_line) {
    // Required for Off-Screen Rendering (OSR)
    command_line->AppendSwitch("disable-gpu");
    command_line->AppendSwitch("disable-gpu-compositing");
    command_line->AppendSwitch("enable-begin-frame-scheduling");
    // Run without sandbox for ease of integration
    command_line->AppendSwitch("no-sandbox");
    
    // Autoplay media if any
    command_line->AppendSwitchWithValue("autoplay-policy", "no-user-gesture-required");
}

void CEFAppHandler::OnContextInitialized() {
    openmedia::core::Logger::SInfo("CEF", "CEF Context Initialized. Ready for OSR rendering.");
}

} // namespace openmedia::cg
