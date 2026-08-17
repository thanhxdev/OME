#include "NoiseReductionFilterPlugin.h"
#include <iostream>

namespace openmedia::plugins {

NoiseReductionFilterPlugin::NoiseReductionFilterPlugin() {
    m_info.name = "NoiseReductionFilter";
    m_info.displayName = "Noise Reduction Filter Plugin";
    m_info.description = "Applies temporal and spatial denoise to video frames.";
    m_info.author = "OpenMedia Team";
    m_info.version = "1.0.0";
    m_info.url = "https://openmedia.org";
    m_info.apiVersion = OME_PLUGIN_API_VERSION;
    m_info.capabilities = plugin::PluginCapability::VideoFilter;
}

bool NoiseReductionFilterPlugin::Initialize() {
    std::cout << "[NoiseReductionFilter] Initialized.\n";
    return true;
}

void NoiseReductionFilterPlugin::Shutdown() {
    std::cout << "[NoiseReductionFilter] Shutting down.\n";
}

const plugin::PluginInfo& NoiseReductionFilterPlugin::GetInfo() const {
    return m_info;
}

bool NoiseReductionFilterPlugin::Configure(const char* jsonConfig) {
    if (jsonConfig) {
        std::cout << "[NoiseReductionFilter] Configured with: " << jsonConfig << "\n";
    }
    return true;
}

const char* NoiseReductionFilterPlugin::GetDefaultConfig() const {
    return "{ \"spatial_strength\": 0.5, \"temporal_strength\": 0.5 }";
}

bool NoiseReductionFilterPlugin::Setup(const plugin::VideoFilterParams& params) {
    m_params = params;
    std::cout << "[NoiseReductionFilter] Setup with size " << params.width << "x" << params.height << "\n";
    return true;
}

bool NoiseReductionFilterPlugin::ProcessFrame(const core::MediaFrame& /*input*/, core::MediaFrame& /*output*/) {
    // Stub: Process frame in software
    return true;
}

bool NoiseReductionFilterPlugin::ProcessFrameGPU(const void* /*inputTexture*/, void* /*outputTexture*/, int /*textureFormat*/) {
    // Stub: Process frame on GPU
    return true;
}

plugin::VideoFilterParams NoiseReductionFilterPlugin::GetOutputParams() const {
    return m_params;
}

void NoiseReductionFilterPlugin::Reset() {
    std::cout << "[NoiseReductionFilter] State reset.\n";
}

} // namespace openmedia::plugins

OME_DECLARE_PLUGIN(openmedia::plugins::NoiseReductionFilterPlugin)
