#include <openmedia/plugin/PluginRegistry.h>

namespace openmedia::plugin {

PluginRegistry& PluginRegistry::Instance() {
    static PluginRegistry instance;
    return instance;
}

PluginRegistry::~PluginRegistry() {
    UnregisterAll();
}

bool PluginRegistry::RegisterPlugin(const std::string& name, IPlugin* plugin) {
    if (!plugin) return false;
    
    std::lock_guard<std::mutex> lock(m_mutex);
    if (m_plugins.find(name) != m_plugins.end()) {
        return false; // Already registered
    }
    
    m_plugins[name] = plugin;
    return true;
}

void PluginRegistry::UnregisterPlugin(const std::string& name) {
    std::lock_guard<std::mutex> lock(m_mutex);
    m_plugins.erase(name);
}

void PluginRegistry::UnregisterAll() {
    std::lock_guard<std::mutex> lock(m_mutex);
    m_plugins.clear();
}

IPlugin* PluginRegistry::GetPlugin(const std::string& name) const {
    std::lock_guard<std::mutex> lock(m_mutex);
    auto it = m_plugins.find(name);
    if (it != m_plugins.end()) {
        return it->second;
    }
    return nullptr;
}

std::vector<PluginInfo> PluginRegistry::GetRegisteredPlugins() const {
    std::lock_guard<std::mutex> lock(m_mutex);
    std::vector<PluginInfo> infos;
    infos.reserve(m_plugins.size());
    
    for (const auto& pair : m_plugins) {
        if (pair.second) {
            infos.push_back(pair.second->GetInfo());
        }
    }
    return infos;
}

} // namespace openmedia::plugin
