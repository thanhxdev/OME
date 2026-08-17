#pragma once

#include <openmedia/plugin/IPlugin.h>
#include <openmedia/core/ErrorCodes.h>
#include <memory>
#include <vector>
#include <string>
#include <mutex>
#include <openmedia/core/Logger.h>

namespace openmedia::core {

class PluginManager {
public:
    static PluginManager& GetInstance();

    VoidResult LoadDirectory(const std::string& directoryPath);
    VoidResult LoadPlugin(const std::string& pluginPath);
    VoidResult ReloadPlugin(const std::string& pluginName);
    void UnloadAll();

    std::vector<std::shared_ptr<plugin::IPlugin>> GetPlugins() const;
    std::shared_ptr<plugin::IPlugin> GetPlugin(const std::string& pluginName) const;

private:
    PluginManager();
    ~PluginManager();

    struct PluginContext {
        std::shared_ptr<plugin::IPlugin> instance;
        void* libraryHandle;
        std::string originalPath;
        std::string tempPath;
    };

    std::vector<PluginContext> m_plugins;
    mutable std::mutex m_mutex;
    Logger& m_logger;
};

} // namespace openmedia::core
