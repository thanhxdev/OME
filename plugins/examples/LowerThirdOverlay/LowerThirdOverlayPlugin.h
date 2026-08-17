#pragma once

#include <openmedia/plugin/IOverlayPlugin.h>
#include <string>

namespace openmedia::plugins {

class LowerThirdOverlayPlugin : public plugin::IOverlayPlugin {
public:
    LowerThirdOverlayPlugin();
    ~LowerThirdOverlayPlugin() override = default;

    // IPlugin
    bool Initialize() override;
    void Shutdown() override;
    const plugin::PluginInfo& GetInfo() const override;
    bool Configure(const char* jsonConfig) override;
    const char* GetDefaultConfig() const override;

    // IOverlayPlugin
    bool RenderOverlay(void* renderContext) override;
    void UpdateData(const char* jsonData) override;

private:
    plugin::PluginInfo m_info;
    std::string m_text;
    std::string m_subtitle;
};

} // namespace openmedia::plugins
