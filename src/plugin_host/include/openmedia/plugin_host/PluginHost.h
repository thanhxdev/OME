#pragma once

/// @file PluginHost.h
/// @brief Plugin loading manager for the server process
/// @since 1.0.0

#include <openmedia/plugin_host/PluginLifecycle.h>
#include <openmedia/plugin_host/PluginSandbox.h>
#include <openmedia/core/ErrorCodes.h>

#include <cstdint>
#include <filesystem>
#include <memory>
#include <string>
#include <vector>

namespace openmedia::plugin_host {

/// @brief Information about a loaded plugin
struct LoadedPluginInfo {
    std::string name;               ///< Plugin name from descriptor
    std::string displayName;        ///< Human-readable name
    std::string version;            ///< Plugin version
    std::string author;             ///< Plugin author
    std::string description;        ///< Plugin description
    std::string filePath;           ///< Path to the DLL
    PluginCapability capabilities = PluginCapability::None;
    PluginState state = PluginState::Unloaded;
    uint32_t id = 0;                ///< Unique plugin ID assigned by host
};

/// @brief Plugin host configuration
struct PluginHostConfig {
    std::filesystem::path pluginDirectory;      ///< Directory to scan for plugins
    std::string pluginFilePattern = "*.dll";     ///< Glob pattern for plugin files
    bool autoScanOnStart = true;                ///< Scan directory on Start()
    bool enableHotReload = false;               ///< Enable hot-reload (demo mode only)
    SandboxConfig sandboxConfig;                ///< Sandbox settings
};

/// @brief Plugin loading and lifecycle manager for the server process
///
/// Scans a directory for plugin DLLs, loads them, resolves entry points,
/// manages lifecycle (init, configure, shutdown), and provides crash
/// isolation via PluginSandbox.
///
/// @code
/// PluginHost host({
///     .pluginDirectory = "plugins/",
///     .enableHotReload = false
/// });
/// host.Start();
///
/// // List loaded plugins
/// for (const auto& info : host.GetLoadedPlugins()) {
///     LOG_INFO("Plugin: {} v{}", info.name, info.version);
/// }
///
/// host.Stop();
/// @endcode
class PluginHost {
public:
    explicit PluginHost(const PluginHostConfig& config = {});
    ~PluginHost();

    PluginHost(const PluginHost&) = delete;
    PluginHost& operator=(const PluginHost&) = delete;

    // --- Lifecycle ---

    /// @brief Start the plugin host (scans and loads plugins if autoScan)
    [[nodiscard]] core::VoidResult Start();

    /// @brief Stop and unload all plugins
    void Stop();

    /// @brief Check if host is running
    [[nodiscard]] bool IsRunning() const;

    // --- Plugin Management ---

    /// @brief Scan the plugin directory for DLLs
    [[nodiscard]] std::vector<std::filesystem::path> ScanDirectory();

    /// @brief Load a single plugin from file path
    [[nodiscard]] core::Result<uint32_t> LoadPlugin(const std::filesystem::path& path);

    /// @brief Unload a plugin by ID
    [[nodiscard]] core::VoidResult UnloadPlugin(uint32_t pluginId);

    /// @brief Unload all plugins
    void UnloadAll();

    /// @brief Reload a plugin (unload + load)
    [[nodiscard]] core::VoidResult ReloadPlugin(uint32_t pluginId);

    // --- Query ---

    /// @brief Get list of loaded plugins
    [[nodiscard]] std::vector<LoadedPluginInfo> GetLoadedPlugins() const;

    /// @brief Get plugin info by ID
    [[nodiscard]] core::Result<LoadedPluginInfo> GetPluginInfo(uint32_t pluginId) const;

    /// @brief Find plugins by capability
    [[nodiscard]] std::vector<LoadedPluginInfo> FindByCapability(PluginCapability cap) const;

    /// @brief Get total number of loaded plugins
    [[nodiscard]] uint32_t GetPluginCount() const;

    // --- Sandbox ---

    /// @brief Get the sandbox for executing plugin functions
    [[nodiscard]] PluginSandbox& GetSandbox();

private:
    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

} // namespace openmedia::plugin_host
