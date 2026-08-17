#pragma once

#include "openmedia/plugins/IPlugin.h"
#include <string>
#include <vector>
#include <memory>

namespace openmedia {
namespace plugins {

class PluginManager {
public:
    PluginManager();
    ~PluginManager();

    bool LoadPlugin(const std::string& path);
    void UnloadAll();
    
    const std::vector<IPlugin*>& GetLoadedPlugins() const;

private:
    std::vector<IPlugin*> loaded_plugins_;
    std::vector<void*> library_handles_;
};

} // namespace plugins
} // namespace openmedia
