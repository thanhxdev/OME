#pragma once

#include <openmedia/plugin/IPlugin.h>
#include <openmedia/core/Logger.h>
#include <spdlog/logger.h>
#include <memory>

namespace openmedia::plugins {

class GrayscaleFilterPlugin : public plugin::IPlugin {
public:
    GrayscaleFilterPlugin();
    ~GrayscaleFilterPlugin() override;

    // IPlugin interface
    bool Initialize() override;
    void Shutdown() override;
    const plugin::PluginInfo& GetInfo() const override;
    bool Configure(const char* jsonConfig) override;
    const char* GetDefaultConfig() const override;

private:
    core::Logger& m_logger;
    plugin::PluginInfo m_info;
};

} // namespace openmedia::plugins
