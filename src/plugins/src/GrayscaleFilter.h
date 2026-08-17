#pragma once

#include <openmedia/plugin/IVideoFilter.h>
#include <openmedia/plugin/IPlugin.h>

namespace openmedia::plugins {

class GrayscaleFilter : public openmedia::plugin::IVideoFilter {
public:
    GrayscaleFilter() = default;
    ~GrayscaleFilter() override = default;

    // IPlugin implementation
    const char* GetName() const override { return "GrayscaleFilter"; }
    const char* GetVersion() const override { return "1.0.0"; }
    const char* GetAuthor() const override { return "OpenMedia SDK"; }
    const char* GetDescription() const override { return "Converts video frames to grayscale"; }
    
    openmedia::plugin::PluginInfo GetInfo() const override {
        openmedia::plugin::PluginInfo info;
        info.name = GetName();
        info.displayName = "Grayscale Filter";
        info.description = GetDescription();
        info.author = GetAuthor();
        info.version = GetVersion();
        info.url = "https://openmedia.org/plugins/grayscale";
        info.apiVersion = OME_PLUGIN_API_VERSION;
        info.capabilities = openmedia::plugin::PluginCapability::VideoFilter;
        return info;
    }

    // IVideoFilter implementation
    bool Setup(const openmedia::plugin::VideoFilterParams& params) override {
        m_params = params;
        return true;
    }

    bool ProcessFrame(
        const openmedia::core::MediaFrame& input,
        openmedia::core::MediaFrame& output
    ) override;

    openmedia::plugin::VideoFilterParams GetOutputParams() const override {
        return m_params;
    }

    void Reset() override {}

private:
    openmedia::plugin::VideoFilterParams m_params;
};

} // namespace openmedia::plugins
