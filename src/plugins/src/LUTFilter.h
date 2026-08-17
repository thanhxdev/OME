#pragma once

#include <openmedia/plugin/IVideoFilter.h>
#include <openmedia/plugin/IPlugin.h>
#include <string>
#include <nlohmann/json.hpp>

namespace openmedia::plugins {

class LUTFilter : public openmedia::plugin::IVideoFilter {
public:
    LUTFilter() = default;
    ~LUTFilter() override;

    // IPlugin implementation
    bool Initialize() override;
    void Shutdown() override;
    const openmedia::plugin::PluginInfo& GetInfo() const override;
    bool Configure(const char* jsonConfig) override;
    const char* GetDefaultConfig() const override;

    // IVideoFilter implementation
    bool Setup(const openmedia::plugin::VideoFilterParams& params) override;
    bool ProcessFrame(
        const openmedia::core::MediaFrame& input,
        openmedia::core::MediaFrame& output
    ) override;

    openmedia::plugin::VideoFilterParams GetOutputParams() const override {
        return m_params;
    }
    void Reset() override;

private:
    openmedia::plugin::VideoFilterParams m_params;
    openmedia::plugin::PluginInfo m_info;
    std::string m_defaultConfig;

    // FFmpeg filter graph context
    struct AVFilterGraph* m_filterGraph = nullptr;
    struct AVFilterContext* m_buffersrcCtx = nullptr;
    struct AVFilterContext* m_buffersinkCtx = nullptr;
    bool m_graphNeedsRebuild = true;

    // Properties
    std::string m_lutFilePath = "";

    bool RebuildGraph();
};

} // namespace openmedia::plugins
