#include "LUTFilter.h"
#include <fmt/format.h>

extern "C" {
#include <libavfilter/avfilter.h>
#include <libavfilter/buffersrc.h>
#include <libavfilter/buffersink.h>
#include <libavutil/opt.h>
}

namespace openmedia::plugins {

LUTFilter::~LUTFilter() {
    Shutdown();
}

bool LUTFilter::Initialize() {
    m_info.name = "LUTFilter";
    m_info.displayName = "3D LUT Filter";
    m_info.description = "Apply a 3D LUT (Look-Up Table) to video using FFmpeg lut3d filter";
    m_info.author = "OpenMedia SDK";
    m_info.version = "1.0.0";
    m_info.url = "https://openmedia.org/plugins/lut";
    m_info.apiVersion = OME_PLUGIN_API_VERSION;
    m_info.capabilities = openmedia::plugin::PluginCapability::VideoFilter;

    nlohmann::json defConfig;
    defConfig["lut_file"] = "";
    m_defaultConfig = defConfig.dump();

    return true;
}

void LUTFilter::Shutdown() {
    if (m_filterGraph) {
        avfilter_graph_free(&m_filterGraph);
        m_filterGraph = nullptr;
    }
}

const openmedia::plugin::PluginInfo& LUTFilter::GetInfo() const {
    return m_info;
}

bool LUTFilter::Configure(const char* jsonConfig) {
    if (!jsonConfig) return true;
    try {
        auto config = nlohmann::json::parse(jsonConfig);
        if (config.contains("lut_file")) m_lutFilePath = config["lut_file"].get<std::string>();
        m_graphNeedsRebuild = true;
        return true;
    } catch (...) {
        return false;
    }
}

const char* LUTFilter::GetDefaultConfig() const {
    return m_defaultConfig.c_str();
}

bool LUTFilter::Setup(const openmedia::plugin::VideoFilterParams& params) {
    m_params = params;
    m_graphNeedsRebuild = true;
    return true;
}

bool LUTFilter::ProcessFrame(
    const openmedia::core::MediaFrame& input,
    openmedia::core::MediaFrame& output)
{
    // A mock implementation representing the FFmpeg graph flow
    
    openmedia::core::VideoFrameInfo info = input.GetVideoInfo();
    
    // Y-plane copy
    int ySize = info.width * info.height;
    if (const uint8_t* inY = input.GetVideoData(0)) {
        if (uint8_t* outY = output.GetVideoData(0)) {
            std::memcpy(outY, inY, ySize);
        }
    }

    // U/V-planes copy
    int uvSize = ySize / 2;
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

bool LUTFilter::RebuildGraph() {
    if (m_filterGraph) {
        avfilter_graph_free(&m_filterGraph);
        m_filterGraph = nullptr;
    }
    
    m_filterGraph = avfilter_graph_alloc();
    if (!m_filterGraph) return false;

    // Example lut3d string: lut3d=file='path/to/file.cube'
    
    m_graphNeedsRebuild = false;
    return true;
}

void LUTFilter::Reset() {
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
        return new openmedia::plugins::LUTFilter();
    }
}
