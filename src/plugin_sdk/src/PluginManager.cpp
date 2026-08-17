#include <openmedia/plugin/PluginManager.h>
#include <openmedia/core/Logger.h>
#include <filesystem>
#include <fmt/format.h>
#include <chrono>

#ifdef _WIN32
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#else
#include <dlfcn.h>
#endif

namespace fs = std::filesystem;

namespace openmedia::core {

PluginManager& PluginManager::GetInstance() {
    static PluginManager instance;
    return instance;
}

PluginManager::PluginManager() : m_logger(Logger::Get("PluginManager")) {
}

PluginManager::~PluginManager() {
    UnloadAll();
}

VoidResult PluginManager::LoadDirectory(const std::string& directoryPath) {
    if (!fs::exists(directoryPath) || !fs::is_directory(directoryPath)) {
        OME_LOG_WARN(m_logger, "Plugin directory does not exist: {}", directoryPath);
        return {};
    }

    for (const auto& entry : fs::directory_iterator(directoryPath)) {
        if (entry.is_regular_file()) {
            auto ext = entry.path().extension().string();
#ifdef _WIN32
            if (ext == ".dll") {
#else
            if (ext == ".so" || ext == ".dylib") {
#endif
                auto res = LoadPlugin(entry.path().string());
                if (!res.has_value()) {
                    OME_LOG_ERROR(m_logger, "Failed to load plugin {}: {}", entry.path().string(), res.error().message);
                }
            }
        }
    }

    return {};
}

VoidResult PluginManager::LoadPlugin(const std::string& pluginPath) {
    OME_LOG_INFO(m_logger, "Loading plugin: {}", pluginPath);

    // 1. Shadow Copying
    auto now = std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::system_clock::now().time_since_epoch()).count();
    std::string tempPath = fmt::format("{}.{}.tmp", pluginPath, now);

    try {
        fs::copy_file(pluginPath, tempPath, fs::copy_options::overwrite_existing);
    } catch (const std::exception& e) {
        return std::unexpected(Error::Make(ErrorCode::Unknown, fmt::format("Failed to copy plugin DLL: {}", e.what())));
    }

#ifdef _WIN32
    void* handle = LoadLibraryA(tempPath.c_str());
#else
    void* handle = dlopen(tempPath.c_str(), RTLD_NOW | RTLD_LOCAL);
#endif

    if (!handle) {
        std::error_code ec;
        fs::remove(tempPath, ec);
#ifdef _WIN32
        return std::unexpected(Error::Make(ErrorCode::Unknown, "LoadLibraryA failed"));
#else
        return std::unexpected(Error::Make(ErrorCode::Unknown, dlerror()));
#endif
    }

    typedef plugin::IPlugin* (*OmePluginCreateFunc)();
    typedef void (*OmePluginDestroyFunc)(plugin::IPlugin*);

#ifdef _WIN32
    OmePluginCreateFunc createFunc = (OmePluginCreateFunc)GetProcAddress((HMODULE)handle, "ome_plugin_create");
    OmePluginDestroyFunc destroyFunc = (OmePluginDestroyFunc)GetProcAddress((HMODULE)handle, "ome_plugin_destroy");
#else
    OmePluginCreateFunc createFunc = (OmePluginCreateFunc)dlsym(handle, "ome_plugin_create");
    OmePluginDestroyFunc destroyFunc = (OmePluginDestroyFunc)dlsym(handle, "ome_plugin_destroy");
#endif

    if (!createFunc || !destroyFunc) {
#ifdef _WIN32
        FreeLibrary((HMODULE)handle);
#else
        dlclose(handle);
#endif
        std::error_code ec;
        fs::remove(tempPath, ec);
        return std::unexpected(Error::Make(ErrorCode::Unknown, "ome_plugin_create or ome_plugin_destroy entry point not found"));
    }

    plugin::IPlugin* rawPlugin = createFunc();
    if (!rawPlugin) {
#ifdef _WIN32
        FreeLibrary((HMODULE)handle);
#else
        dlclose(handle);
#endif
        std::error_code ec;
        fs::remove(tempPath, ec);
        return std::unexpected(Error::Make(ErrorCode::Unknown, "ome_plugin_create returned null"));
    }

    // 2. Custom Deleter
    std::shared_ptr<plugin::IPlugin> plugin(rawPlugin, [handle, destroyFunc, tempPath](plugin::IPlugin* p) {
        if (p) {
            p->Shutdown();
            destroyFunc(p);
        }
#ifdef _WIN32
        FreeLibrary((HMODULE)handle);
#else
        dlclose(handle);
#endif
        std::error_code ec;
        fs::remove(tempPath, ec);
    });
    
    auto& info = plugin->GetInfo();
    if (info.apiVersion != OME_PLUGIN_API_VERSION) {
        return std::unexpected(Error::Make(ErrorCode::Unknown, fmt::format("Plugin API version mismatch: expected {}, got {}", OME_PLUGIN_API_VERSION, info.apiVersion)));
    }

    auto initRes = plugin->Initialize();
    if (!initRes) {
        return std::unexpected(Error::Make(ErrorCode::Unknown, "Plugin Initialize() failed"));
    }

    OME_LOG_INFO(m_logger, "Successfully loaded plugin: {} v{}", std::string(info.name), std::string(info.version));

    PluginContext ctx;
    ctx.instance = plugin;
    ctx.libraryHandle = handle;
    ctx.originalPath = pluginPath;
    ctx.tempPath = tempPath;

    std::lock_guard<std::mutex> lock(m_mutex);
    m_plugins.push_back(ctx);

    return {};
}

VoidResult PluginManager::ReloadPlugin(const std::string& pluginName) {
    std::string originalPath;
    {
        std::lock_guard<std::mutex> lock(m_mutex);
        auto it = std::find_if(m_plugins.begin(), m_plugins.end(), [&](const PluginContext& ctx) {
            return ctx.instance && std::string(ctx.instance->GetInfo().name) == pluginName;
        });

        if (it == m_plugins.end()) {
            return std::unexpected(Error::Make(ErrorCode::NotFound, "Plugin not found for reload"));
        }
        originalPath = it->originalPath;
        
        // Remove the old plugin from the list. The shared_ptr will keep the old library alive
        // until all pipeline references are dropped.
        m_plugins.erase(it);
    }

    // Load the new version (this will acquire the lock again inside LoadPlugin)
    return LoadPlugin(originalPath);
}

void PluginManager::UnloadAll() {
    std::lock_guard<std::mutex> lock(m_mutex);
    // Clearing the vector will drop the shared_ptrs, invoking the custom deleters,
    // which will cleanly shutdown and unload the libraries.
    m_plugins.clear();
}

std::vector<std::shared_ptr<plugin::IPlugin>> PluginManager::GetPlugins() const {
    std::lock_guard<std::mutex> lock(m_mutex);
    std::vector<std::shared_ptr<plugin::IPlugin>> list;
    for (const auto& ctx : m_plugins) {
        list.push_back(ctx.instance);
    }
    return list;
}

std::shared_ptr<plugin::IPlugin> PluginManager::GetPlugin(const std::string& pluginName) const {
    std::lock_guard<std::mutex> lock(m_mutex);
    for (const auto& ctx : m_plugins) {
        if (ctx.instance && std::string(ctx.instance->GetInfo().name) == pluginName) {
            return ctx.instance;
        }
    }
    return nullptr;
}

} // namespace openmedia::core
