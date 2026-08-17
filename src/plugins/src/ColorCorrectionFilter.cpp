#include "ColorCorrectionFilter.h"
#include <fmt/format.h>

extern "C" {
#include <libavfilter/avfilter.h>
#include <libavfilter/buffersrc.h>
#include <libavfilter/buffersink.h>
#include <libavutil/opt.h>
}

namespace openmedia::plugins {

ColorCorrectionFilter::~ColorCorrectionFilter() {
    Shutdown();
}

bool ColorCorrectionFilter::Initialize() {
    m_info.name = "ColorCorrectionFilter";
    m_info.displayName = "Color Correction Filter";
    m_info.description = "Adjust brightness, contrast, saturation, and gamma using FFmpeg eq filter";
    m_info.author = "OpenMedia SDK";
    m_info.version = "1.0.0";
    m_info.url = "https://openmedia.org/plugins/colorcorrection";
    m_info.apiVersion = OME_PLUGIN_API_VERSION;
    m_info.capabilities = openmedia::plugin::PluginCapability::VideoFilter;

    nlohmann::json defConfig;
    defConfig["brightness"] = 0.0f;
    defConfig["contrast"] = 1.0f;
    defConfig["saturation"] = 1.0f;
    defConfig["gamma"] = 1.0f;
    m_defaultConfig = defConfig.dump();

    return true;
}

void ColorCorrectionFilter::Shutdown() {
    if (m_filterGraph) {
        avfilter_graph_free(&m_filterGraph);
        m_filterGraph = nullptr;
    }
}

const openmedia::plugin::PluginInfo& ColorCorrectionFilter::GetInfo() const {
    return m_info;
}

bool ColorCorrectionFilter::Configure(const char* jsonConfig) {
    if (!jsonConfig) return true;
    try {
        auto config = nlohmann::json::parse(jsonConfig);
        if (config.contains("brightness")) m_brightness = config["brightness"].get<float>();
        if (config.contains("contrast")) m_contrast = config["contrast"].get<float>();
        if (config.contains("saturation")) m_saturation = config["saturation"].get<float>();
        if (config.contains("gamma")) m_gamma = config["gamma"].get<float>();
        m_graphNeedsRebuild = true;
        return true;
    } catch (...) {
        return false;
    }
}

const char* ColorCorrectionFilter::GetDefaultConfig() const {
    return m_defaultConfig.c_str();
}

bool ColorCorrectionFilter::Setup(const openmedia::plugin::VideoFilterParams& params) {
    m_params = params;
    m_graphNeedsRebuild = true;
    return true;
}

bool ColorCorrectionFilter::ProcessFrame(
    const openmedia::core::MediaFrame& input,
    openmedia::core::MediaFrame& output)
{
    // A mock implementation representing the FFmpeg graph flow
    // In actual implementation, we would push frame to buffersrc and pop from buffersink.
    // For MVP structure we bypass processing.
    
    openmedia::core::VideoFrameInfo info = input.GetVideoInfo();
    
    // Y-plane copy
    int ySize = info.width * info.height;
    if (const uint8_t* inY = input.GetVideoData(0)) {
        if (uint8_t* outY = output.GetVideoData(0)) {
            std::memcpy(outY, inY, ySize);
        }
    }

    // U/V-planes copy
    int uvSize = ySize / 2; // approximation for NV12
    if (const uint8_t* inUV = input.GetVideoData(1)) {
        if (uint8_t* outUV = output.GetVideoData(1)) {
            std::memcpy(outUV, inUV, uvSize);
        }
    }
    
    if (const uint8_t* inV = input.GetVideoData(2)) {
        if (uint8_t* outV = output.GetVideoData(2)) {
            std::memcpy(outV, inV, uvSize / 2);
        }
    }

    return true;
}

bool ColorCorrectionFilter::RebuildGraph() {
    if (m_filterGraph) {
        avfilter_graph_free(&m_filterGraph);
        m_filterGraph = nullptr;
    }
    
    m_filterGraph = avfilter_graph_alloc();
    if (!m_filterGraph) return false;

    // FFmpeg integration logic would be here
    // Example eq string: eq=brightness=0:contrast=1:saturation=1:gamma=1
    
    m_graphNeedsRebuild = false;
    return true;
}

void ColorCorrectionFilter::Reset() {
    m_graphNeedsRebuild = true;
}

} // namespace openmedia::plugins

// Standard Plugin Export
extern "C" {
#ifdef _WIN32
    __declspec(dllexport)
#else
    __attribute__((visibility("default")))
#endif
    openmedia::plugin::IPlugin* ome_plugin_create() {
        return new openmedia::plugins::ColorCorrectionFilter();
    }
}
