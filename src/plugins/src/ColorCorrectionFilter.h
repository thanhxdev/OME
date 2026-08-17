#pragma once

#include <openmedia/plugin/IVideoFilter.h>
#include <openmedia/plugin/IPlugin.h>
#include <string>
#include <nlohmann/json.hpp>

namespace openmedia::plugins {

class ColorCorrectionFilter : public openmedia::plugin::IVideoFilter {
public:
    ColorCorrectionFilter() = default;
    ~ColorCorrectionFilter() override;

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
    float m_brightness = 0.0f; // -1.0 to 1.0
    float m_contrast = 1.0f;   // -2.0 to 2.0
    float m_saturation = 1.0f; // 0.0 to 3.0
    float m_gamma = 1.0f;      // 0.1 to 10.0

    bool RebuildGraph();
};

} // namespace openmedia::plugins
