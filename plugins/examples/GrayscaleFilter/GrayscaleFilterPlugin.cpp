#include "GrayscaleFilterPlugin.h"

namespace openmedia::plugins {

GrayscaleFilterPlugin::GrayscaleFilterPlugin() : m_logger(core::Logger::Get("GrayscaleFilterPlugin")) {
    m_info.name = "GrayscaleFilter";
    m_info.displayName = "Grayscale Filter";
    m_info.description = "Converts video frames to grayscale.";
    m_info.author = "OpenMedia";
    m_info.version = "1.0.0";
    m_info.url = "";
    m_info.apiVersion = OME_PLUGIN_API_VERSION;
    m_info.capabilities = plugin::PluginCapability::VideoFilter;
}

GrayscaleFilterPlugin::~GrayscaleFilterPlugin() {
    Shutdown();
}

bool GrayscaleFilterPlugin::Initialize() {
    OME_LOG_INFO(m_logger, "GrayscaleFilterPlugin initializing...");
    // Register filter node in a real implementation
    return true;
}

void GrayscaleFilterPlugin::Shutdown() {
    OME_LOG_INFO(m_logger, "GrayscaleFilterPlugin shutting down...");
}

const plugin::PluginInfo& GrayscaleFilterPlugin::GetInfo() const {
    return m_info;
}

bool GrayscaleFilterPlugin::Configure(const char* /*jsonConfig*/) {
    // Accept any configuration for now
    return true;
}

const char* GrayscaleFilterPlugin::GetDefaultConfig() const {
    return "{}";
}

} // namespace openmedia::plugins

OME_DECLARE_PLUGIN(openmedia::plugins::GrayscaleFilterPlugin)
