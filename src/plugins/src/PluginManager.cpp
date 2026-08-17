#include "openmedia/plugins/PluginManager.h"

// #ifdef _WIN32
// #include <windows.h>
// #else
// #include <dlfcn.h>
// #endif

namespace openmedia {
namespace plugins {

PluginManager::PluginManager() {
}

PluginManager::~PluginManager() {
    UnloadAll();
}

bool PluginManager::LoadPlugin(const std::string& path) {
    // TODO: Implement cross-platform dynamic library loading
    // void* handle = dlopen(path.c_str(), RTLD_LAZY);
    // if (!handle) return false;
    // 
    // CreatePluginFunc createFunc = (CreatePluginFunc)dlsym(handle, "CreatePlugin");
    // if (!createFunc) {
    //     dlclose(handle);
    //     return false;
    // }
    // 
    // IPlugin* plugin = createFunc();
    // if (plugin && plugin->Initialize()) {
    //     loaded_plugins_.push_back(plugin);
    //     library_handles_.push_back(handle);
    //     return true;
    // }
    
    // Stub success
    return true;
}

void PluginManager::UnloadAll() {
    for (size_t i = 0; i < loaded_plugins_.size(); ++i) {
        loaded_plugins_[i]->Shutdown();
        
        // TODO: call DestroyPluginFunc
        // DestroyPluginFunc destroyFunc = (DestroyPluginFunc)dlsym(library_handles_[i], "DestroyPlugin");
        // if (destroyFunc) destroyFunc(loaded_plugins_[i]);
        
        // TODO: dlclose(library_handles_[i]);
    }
    loaded_plugins_.clear();
    library_handles_.clear();
}

const std::vector<IPlugin*>& PluginManager::GetLoadedPlugins() const {
    return loaded_plugins_;
}

} // namespace plugins
} // namespace openmedia
