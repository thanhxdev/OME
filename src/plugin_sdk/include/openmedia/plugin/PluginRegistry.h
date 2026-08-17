#pragma once

#include "IPlugin.h"
#include <string>
#include <vector>
#include <memory>
#include <mutex>
#include <unordered_map>

namespace openmedia::plugin {

class PluginRegistry {
public:
    static PluginRegistry& Instance();

    bool RegisterPlugin(const std::string& name, IPlugin* plugin);
    void UnregisterPlugin(const std::string& name);
    void UnregisterAll();

    IPlugin* GetPlugin(const std::string& name) const;
    std::vector<PluginInfo> GetRegisteredPlugins() const;

private:
    PluginRegistry() = default;
    ~PluginRegistry();

    mutable std::mutex m_mutex;
    std::unordered_map<std::string, IPlugin*> m_plugins;
};

} // namespace openmedia::plugin
