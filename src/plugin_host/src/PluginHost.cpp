/// @file PluginHost.cpp
/// @brief Plugin loading and lifecycle manager implementation

#include <openmedia/plugin_host/PluginHost.h>
#include <openmedia/core/Logger.h>

#include <algorithm>
#include <mutex>
#include <unordered_map>

#ifdef _WIN32
#include <windows.h>
#else
#include <dlfcn.h>
#endif

namespace openmedia::plugin_host {

namespace {
auto& Log() { return core::Logger::Get("plugin.host"); }
}

/// @brief Native library handle
struct NativeLibrary {
#ifdef _WIN32
    HMODULE handle = nullptr;
#else
    void* handle = nullptr;
#endif
    std::filesystem::path path;

    [[nodiscard]] bool IsLoaded() const { return handle != nullptr; }

    template <typename Fn>
    [[nodiscard]] Fn GetSymbol(const char* name) const {
#ifdef _WIN32
        return reinterpret_cast<Fn>(GetProcAddress(handle, name));
#else
        return reinterpret_cast<Fn>(dlsym(handle, name));
#endif
    }
};

struct PluginHost::Impl {
    PluginHostConfig config;
    PluginSandbox sandbox;
    bool running = false;
    uint32_t nextId = 1;

    struct PluginEntry {
        NativeLibrary library;
        LoadedPluginInfo info;
        PluginDescriptor* descriptor = nullptr;
        PluginInitFn initFn = nullptr;
        PluginShutdownFn shutdownFn = nullptr;
        PluginConfigureFn configureFn = nullptr;
    };

    std::unordered_map<uint32_t, PluginEntry> plugins;
    mutable std::mutex mutex;

    Impl(const PluginHostConfig& cfg)
        : config(cfg), sandbox(cfg.sandboxConfig) {}
};

PluginHost::PluginHost(const PluginHostConfig& config)
    : m_impl(std::make_unique<Impl>(config)) {}

PluginHost::~PluginHost() {
    Stop();
}

core::VoidResult PluginHost::Start() {
    std::lock_guard lock(m_impl->mutex);

    if (m_impl->running) {
        return std::unexpected(core::Error{
            core::ErrorCode::InvalidState,
            "PluginHost already running",
            "PluginHost"});
    }

    Log().Info("PluginHost starting...");

    if (m_impl->config.autoScanOnStart &&
        !m_impl->config.pluginDirectory.empty()) {

        auto paths = ScanDirectory();
        for (const auto& path : paths) {
            auto result = LoadPlugin(path);
            if (!result) {
                Log().Warn("Failed to load plugin {}: {}",
                           path.string(), result.error().message);
            }
        }
    }

    m_impl->running = true;
    Log().Info("PluginHost started with {} plugins", m_impl->plugins.size());

    return {};
}

void PluginHost::Stop() {
    std::lock_guard lock(m_impl->mutex);

    if (!m_impl->running) return;

    Log().Info("PluginHost stopping...");
    UnloadAll();
    m_impl->running = false;
    Log().Info("PluginHost stopped");
}

bool PluginHost::IsRunning() const {
    return m_impl->running;
}

std::vector<std::filesystem::path> PluginHost::ScanDirectory() {
    std::vector<std::filesystem::path> result;

    if (m_impl->config.pluginDirectory.empty() ||
        !std::filesystem::exists(m_impl->config.pluginDirectory)) {
        return result;
    }

    std::error_code ec;
    for (const auto& entry :
         std::filesystem::directory_iterator(m_impl->config.pluginDirectory, ec)) {

        if (!entry.is_regular_file()) continue;

        auto ext = entry.path().extension().string();
#ifdef _WIN32
        if (ext == ".dll") {
            result.push_back(entry.path());
        }
#else
        if (ext == ".so" || ext == ".dylib") {
            result.push_back(entry.path());
        }
#endif
    }

    Log().Info("Scanned plugin directory: {} — found {} candidates",
              m_impl->config.pluginDirectory.string(), result.size());

    return result;
}

core::Result<uint32_t> PluginHost::LoadPlugin(const std::filesystem::path& path) {
    NativeLibrary lib;
    lib.path = path;

#ifdef _WIN32
    lib.handle = LoadLibraryW(path.wstring().c_str());
    if (!lib.handle) {
        return std::unexpected(core::Error{
            core::ErrorCode::PluginLoadFailed,
            "Failed to load DLL: " + path.string() + " (error " + std::to_string(GetLastError()) + ")",
            "PluginHost"});
    }
#else
    lib.handle = dlopen(path.string().c_str(), RTLD_NOW | RTLD_LOCAL);
    if (!lib.handle) {
        return std::unexpected(core::Error{
            core::ErrorCode::PluginLoadFailed,
            "Failed to load library: " + path.string() + " (" + dlerror() + ")",
            "PluginHost"});
    }
#endif

    // Resolve entry point
    auto getDescriptor = lib.GetSymbol<GetPluginDescriptorFn>("OME_GetPluginDescriptor");
    if (!getDescriptor) {
#ifdef _WIN32
        FreeLibrary(lib.handle);
#else
        dlclose(lib.handle);
#endif
        return std::unexpected(core::Error{
            core::ErrorCode::PluginLoadFailed,
            "Missing OME_GetPluginDescriptor in: " + path.string(),
            "PluginHost"});
    }

    PluginDescriptor* descriptor = getDescriptor();
    if (!descriptor || !descriptor->name) {
#ifdef _WIN32
        FreeLibrary(lib.handle);
#else
        dlclose(lib.handle);
#endif
        return std::unexpected(core::Error{
            core::ErrorCode::PluginLoadFailed,
            "Invalid plugin descriptor in: " + path.string(),
            "PluginHost"});
    }

    // API version compatibility check
    if (descriptor->apiVersionMajor != PluginAPIVersion::MAJOR) {
#ifdef _WIN32
        FreeLibrary(lib.handle);
#else
        dlclose(lib.handle);
#endif
        return std::unexpected(core::Error{
            core::ErrorCode::PluginVersionMismatch,
            "API version mismatch: plugin requires v" +
                std::to_string(descriptor->apiVersionMajor) + "." +
                std::to_string(descriptor->apiVersionMinor) +
                ", host provides v" +
                std::to_string(PluginAPIVersion::MAJOR) + "." +
                std::to_string(PluginAPIVersion::MINOR),
            "PluginHost"});
    }

    // Resolve optional entry points
    auto initFn = lib.GetSymbol<PluginInitFn>("OME_PluginInit");
    auto shutdownFn = lib.GetSymbol<PluginShutdownFn>("OME_PluginShutdown");
    auto configureFn = lib.GetSymbol<PluginConfigureFn>("OME_PluginConfigure");

    uint32_t pluginId = m_impl->nextId++;

    Impl::PluginEntry entry;
    entry.library = std::move(lib);
    entry.descriptor = descriptor;
    entry.initFn = initFn;
    entry.shutdownFn = shutdownFn;
    entry.configureFn = configureFn;

    entry.info.id = pluginId;
    entry.info.name = descriptor->name ? descriptor->name : "";
    entry.info.displayName = descriptor->displayName ? descriptor->displayName : entry.info.name;
    entry.info.version = descriptor->version ? descriptor->version : "0.0.0";
    entry.info.author = descriptor->author ? descriptor->author : "";
    entry.info.description = descriptor->description ? descriptor->description : "";
    entry.info.filePath = path.string();
    entry.info.capabilities = descriptor->capabilities;
    entry.info.state = PluginState::Loaded;

    // Call init if available
    if (initFn) {
        auto report = m_impl->sandbox.Execute(entry.info.name, [&]() {
            int32_t result = initFn();
            if (result != 0) {
                throw std::runtime_error("Plugin init returned " + std::to_string(result));
            }
        });

        if (report.result != SandboxResult::Success) {
#ifdef _WIN32
            FreeLibrary(entry.library.handle);
#else
            dlclose(entry.library.handle);
#endif
            return std::unexpected(core::Error{
                core::ErrorCode::PluginLoadFailed,
                "Plugin init failed: " + report.errorMessage,
                "PluginHost"});
        }

        entry.info.state = PluginState::Initialized;
    }

    Log().Info("Loaded plugin: {} v{} (ID={}, caps=0x{:X})",
              entry.info.name, entry.info.version, pluginId,
              static_cast<uint32_t>(entry.info.capabilities));

    m_impl->plugins[pluginId] = std::move(entry);

    return pluginId;
}

core::VoidResult PluginHost::UnloadPlugin(uint32_t pluginId) {
    auto it = m_impl->plugins.find(pluginId);
    if (it == m_impl->plugins.end()) {
        return std::unexpected(core::Error{
            core::ErrorCode::PluginNotFound,
            "Plugin not found: " + std::to_string(pluginId),
            "PluginHost"});
    }

    auto& entry = it->second;

    if (entry.shutdownFn) {
        m_impl->sandbox.Execute(entry.info.name, [&]() {
            entry.shutdownFn();
        });
    }

    if (entry.library.handle) {
#ifdef _WIN32
        FreeLibrary(entry.library.handle);
#else
        dlclose(entry.library.handle);
#endif
    }

    Log().Info("Unloaded plugin: {} (ID={})", entry.info.name, pluginId);
    m_impl->plugins.erase(it);

    return {};
}

void PluginHost::UnloadAll() {
    std::vector<uint32_t> ids;
    for (const auto& [id, entry] : m_impl->plugins) {
        ids.push_back(id);
    }
    for (auto id : ids) {
        UnloadPlugin(id);
    }
}

core::VoidResult PluginHost::ReloadPlugin(uint32_t pluginId) {
    auto it = m_impl->plugins.find(pluginId);
    if (it == m_impl->plugins.end()) {
        return std::unexpected(core::Error{
            core::ErrorCode::PluginNotFound,
            "Plugin not found: " + std::to_string(pluginId),
            "PluginHost"});
    }

    auto path = std::filesystem::path(it->second.info.filePath);
    auto unloadResult = UnloadPlugin(pluginId);
    if (!unloadResult) return unloadResult;

    auto loadResult = LoadPlugin(path);
    if (!loadResult) {
        return std::unexpected(loadResult.error());
    }

    return {};
}

std::vector<LoadedPluginInfo> PluginHost::GetLoadedPlugins() const {
    std::lock_guard lock(m_impl->mutex);
    std::vector<LoadedPluginInfo> result;
    result.reserve(m_impl->plugins.size());
    for (const auto& [id, entry] : m_impl->plugins) {
        result.push_back(entry.info);
    }
    return result;
}

core::Result<LoadedPluginInfo> PluginHost::GetPluginInfo(uint32_t pluginId) const {
    std::lock_guard lock(m_impl->mutex);
    auto it = m_impl->plugins.find(pluginId);
    if (it == m_impl->plugins.end()) {
        return std::unexpected(core::Error{
            core::ErrorCode::PluginNotFound,
            "Plugin not found: " + std::to_string(pluginId),
            "PluginHost"});
    }
    return it->second.info;
}

std::vector<LoadedPluginInfo> PluginHost::FindByCapability(PluginCapability cap) const {
    std::lock_guard lock(m_impl->mutex);
    std::vector<LoadedPluginInfo> result;
    for (const auto& [id, entry] : m_impl->plugins) {
        if (HasCapability(entry.info.capabilities, cap)) {
            result.push_back(entry.info);
        }
    }
    return result;
}

uint32_t PluginHost::GetPluginCount() const {
    std::lock_guard lock(m_impl->mutex);
    return static_cast<uint32_t>(m_impl->plugins.size());
}

PluginSandbox& PluginHost::GetSandbox() {
    return m_impl->sandbox;
}

} // namespace openmedia::plugin_host
