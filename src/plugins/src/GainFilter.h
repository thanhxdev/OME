#pragma once

#include <openmedia/plugin/IAudioFilter.h>
#include <openmedia/plugin/IPlugin.h>

namespace openmedia::plugins {

class GainFilter : public openmedia::plugin::IAudioFilter {
public:
    GainFilter() = default;
    ~GainFilter() override = default;

    // IPlugin implementation
    const char* GetName() const override { return "GainFilter"; }
    const char* GetVersion() const override { return "1.0.0"; }
    const char* GetAuthor() const override { return "OpenMedia SDK"; }
    const char* GetDescription() const override { return "Applies a gain factor to audio samples"; }
    
    openmedia::plugin::PluginInfo GetInfo() const override {
        openmedia::plugin::PluginInfo info;
        info.name = GetName();
        info.displayName = "Gain Filter";
        info.description = GetDescription();
        info.author = GetAuthor();
        info.version = GetVersion();
        info.url = "https://openmedia.org/plugins/gain";
        info.apiVersion = OME_PLUGIN_API_VERSION;
        info.capabilities = openmedia::plugin::PluginCapability::AudioFilter;
        return info;
    }

    // IAudioFilter implementation
    bool Setup(const openmedia::plugin::AudioFilterParams& params) override {
        m_params = params;
        return true;
    }

    bool ProcessSamples(
        const openmedia::core::MediaFrame& input,
        openmedia::core::MediaFrame& output
    ) override;

    openmedia::plugin::AudioFilterParams GetOutputParams() const override {
        return m_params;
    }

    void Reset() override {}

    // Custom property
    void SetGain(float gain) { m_gain = gain; }
    float GetGain() const { return m_gain; }

private:
    openmedia::plugin::AudioFilterParams m_params;
    float m_gain = 1.5f; // default gain
};

} // namespace openmedia::plugins
