#include "LowerThirdOverlayPlugin.h"
#include <iostream>

namespace openmedia::plugins {

LowerThirdOverlayPlugin::LowerThirdOverlayPlugin() {
    m_info.name = "LowerThirdOverlay";
    m_info.displayName = "Lower Third Overlay Plugin";
    m_info.description = "Renders a lower third graphic over the video frame.";
    m_info.author = "OpenMedia Team";
    m_info.version = "1.0.0";
    m_info.url = "https://openmedia.org";
    m_info.apiVersion = OME_PLUGIN_API_VERSION;
    m_info.capabilities = plugin::PluginCapability::Overlay;
}

bool LowerThirdOverlayPlugin::Initialize() {
    std::cout << "[LowerThirdOverlay] Initialized.\n";
    return true;
}

void LowerThirdOverlayPlugin::Shutdown() {
    std::cout << "[LowerThirdOverlay] Shutting down.\n";
}

const plugin::PluginInfo& LowerThirdOverlayPlugin::GetInfo() const {
    return m_info;
}

bool LowerThirdOverlayPlugin::Configure(const char* jsonConfig) {
    // Basic stub for configuration
    if (jsonConfig) {
        std::cout << "[LowerThirdOverlay] Configured with: " << jsonConfig << "\n";
    }
    return true;
}

const char* LowerThirdOverlayPlugin::GetDefaultConfig() const {
    return "{ \"text\": \"Breaking News\", \"subtitle\": \"Live from OpenMedia\" }";
}

bool LowerThirdOverlayPlugin::RenderOverlay(void* /*renderContext*/) {
    // Stub for rendering overlay
    return true;
}

void LowerThirdOverlayPlugin::UpdateData(const char* jsonData) {
    // Stub for updating dynamic data like text
    if (jsonData) {
        std::cout << "[LowerThirdOverlay] Updating data: " << jsonData << "\n";
    }
}

} // namespace openmedia::plugins

OME_DECLARE_PLUGIN(openmedia::plugins::LowerThirdOverlayPlugin)
