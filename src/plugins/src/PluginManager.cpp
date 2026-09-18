/// @file PluginManager.cpp
/// @brief Dynamic library loader and plugin lifecycle management with crash isolation.

#include "openmedia/plugins/PluginManager.h"
#include <openmedia/core/Logger.h>

#include <iostream>

#ifdef _WIN32
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#else
#include <dlfcn.h>
#endif

namespace openmedia {
namespace plugins {

namespace {

#ifdef _MSC_VER

static IPlugin* SafeCallCreatePlugin(CreatePluginFunc func) {
    __try {
        return func ? func() : nullptr;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return nullptr;
    }
}

static bool SafeCallInitialize(IPlugin* plugin) {
    __try {
        return plugin ? plugin->Initialize() : false;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return false;
    }
}

static void SafeCallShutdown(IPlugin* plugin) {
    __try {
        if (plugin) plugin->Shutdown();
    } __except (EXCEPTION_EXECUTE_HANDLER) {
    }
}

static void SafeCallDestroyPlugin(DestroyPluginFunc func, IPlugin* plugin) {
    __try {
        if (func && plugin) func(plugin);
    } __except (EXCEPTION_EXECUTE_HANDLER) {
    }
}

#else

static IPlugin* SafeCallCreatePlugin(CreatePluginFunc func) {
    try {
        return func ? func() : nullptr;
    } catch (...) {
        return nullptr;
    }
}

static bool SafeCallInitialize(IPlugin* plugin) {
    try {
        return plugin ? plugin->Initialize() : false;
    } catch (...) {
        return false;
    }
}

static void SafeCallShutdown(IPlugin* plugin) {
    try {
        if (plugin) plugin->Shutdown();
    } catch (...) {
    }
}

static void SafeCallDestroyPlugin(DestroyPluginFunc func, IPlugin* plugin) {
    try {
        if (func && plugin) func(plugin);
    } catch (...) {
    }
}

#endif

void* NativeLoadLibrary(const char* path) {
#ifdef _WIN32
    return reinterpret_cast<void*>(LoadLibraryExA(path, nullptr, LOAD_WITH_ALTERED_SEARCH_PATH));
#else
    return dlopen(path, RTLD_NOW | RTLD_LOCAL);
#endif
}

void* NativeGetProcAddress(void* handle, const char* symbol) {
#ifdef _WIN32
    return reinterpret_cast<void*>(GetProcAddress(reinterpret_cast<HMODULE>(handle), symbol));
#else
    return dlsym(handle, symbol);
#endif
}

void NativeFreeLibrary(void* handle) {
    if (!handle) return;
#ifdef _WIN32
    FreeLibrary(reinterpret_cast<HMODULE>(handle));
#else
    dlclose(handle);
#endif
}

} // namespace

PluginManager::PluginManager() {
}

PluginManager::~PluginManager() {
    UnloadAll();
}

bool PluginManager::LoadPlugin(const std::string& path) {
    void* handle = NativeLoadLibrary(path.c_str());
    if (!handle) {
        return false;
    }

    auto createFunc = reinterpret_cast<CreatePluginFunc>(NativeGetProcAddress(handle, "CreatePlugin"));
    if (!createFunc) {
        NativeFreeLibrary(handle);
        return false;
    }

    IPlugin* plugin = SafeCallCreatePlugin(createFunc);
    if (!plugin) {
        NativeFreeLibrary(handle);
        return false;
    }

    if (!SafeCallInitialize(plugin)) {
        auto destroyFunc = reinterpret_cast<DestroyPluginFunc>(NativeGetProcAddress(handle, "DestroyPlugin"));
        if (destroyFunc) {
            SafeCallDestroyPlugin(destroyFunc, plugin);
        } else {
            delete plugin;
        }
        NativeFreeLibrary(handle);
        return false;
    }

    loaded_plugins_.push_back(plugin);
    library_handles_.push_back(handle);
    return true;
}

void PluginManager::UnloadAll() {
    for (size_t i = 0; i < loaded_plugins_.size(); ++i) {
        IPlugin* plugin = loaded_plugins_[i];
        void* handle = (i < library_handles_.size()) ? library_handles_[i] : nullptr;

        if (plugin) {
            SafeCallShutdown(plugin);

            if (handle) {
                auto destroyFunc = reinterpret_cast<DestroyPluginFunc>(NativeGetProcAddress(handle, "DestroyPlugin"));
                if (destroyFunc) {
                    SafeCallDestroyPlugin(destroyFunc, plugin);
                } else {
                    delete plugin;
                }
            } else {
                delete plugin;
            }
        }

        if (handle) {
            NativeFreeLibrary(handle);
        }
    }
    loaded_plugins_.clear();
    library_handles_.clear();
}

const std::vector<IPlugin*>& PluginManager::GetLoadedPlugins() const {
    return loaded_plugins_;
}

} // namespace plugins
} // namespace openmedia
