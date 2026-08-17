#pragma once

#include <openmedia/plugin/IVideoFilter.h>
#include <string>

namespace openmedia::plugins {

class NoiseReductionFilterPlugin : public plugin::IVideoFilter {
public:
    NoiseReductionFilterPlugin();
    ~NoiseReductionFilterPlugin() override = default;

    // IPlugin
    bool Initialize() override;
    void Shutdown() override;
    const plugin::PluginInfo& GetInfo() const override;
    bool Configure(const char* jsonConfig) override;
    const char* GetDefaultConfig() const override;

    // IVideoFilter
    bool Setup(const plugin::VideoFilterParams& params) override;
    bool ProcessFrame(const core::MediaFrame& input, core::MediaFrame& output) override;
    bool ProcessFrameGPU(const void* inputTexture, void* outputTexture, int textureFormat) override;
    plugin::VideoFilterParams GetOutputParams() const override;
    void Reset() override;

private:
    plugin::PluginInfo m_info;
    plugin::VideoFilterParams m_params;
};

} // namespace openmedia::plugins
