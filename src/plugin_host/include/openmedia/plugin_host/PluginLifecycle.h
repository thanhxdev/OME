#pragma once

/// @file PluginLifecycle.h
/// @brief Plugin lifecycle hooks and entry point types
/// @since 1.0.0

#include <cstdint>
#include <string>

namespace openmedia::plugin_host {

/// @brief Plugin API version for compatibility checking
struct PluginAPIVersion {
    static constexpr uint32_t MAJOR = 1;
    static constexpr uint32_t MINOR = 0;
    static constexpr uint32_t PATCH = 0;
};

/// @brief Plugin capability flags
enum class PluginCapability : uint32_t {
    None = 0,
    VideoFilter = 1 << 0,
    AudioFilter = 1 << 1,
    Encoder = 1 << 2,
    Decoder = 1 << 3,
    NetworkProtocol = 1 << 4,
    Overlay = 1 << 5,
    Transition = 1 << 6,
    AIFilter = 1 << 7,
    Source = 1 << 8,
    Output = 1 << 9,
};

/// @brief Bitwise OR for PluginCapability
constexpr PluginCapability operator|(PluginCapability a, PluginCapability b) {
    return static_cast<PluginCapability>(
        static_cast<uint32_t>(a) | static_cast<uint32_t>(b));
}

/// @brief Bitwise AND for PluginCapability
constexpr PluginCapability operator&(PluginCapability a, PluginCapability b) {
    return static_cast<PluginCapability>(
        static_cast<uint32_t>(a) & static_cast<uint32_t>(b));
}

/// @brief Check if capability flag is set
constexpr bool HasCapability(PluginCapability flags, PluginCapability cap) {
    return (flags & cap) == cap;
}

/// @brief Plugin descriptor returned by the plugin entry point
struct PluginDescriptor {
    const char* name = nullptr;             ///< Plugin name (e.g., "GrayscaleFilter")
    const char* displayName = nullptr;      ///< Human-readable name
    const char* version = nullptr;          ///< Plugin version string
    const char* author = nullptr;           ///< Author / company
    const char* description = nullptr;      ///< Short description
    uint32_t apiVersionMajor = 0;           ///< Required API major version
    uint32_t apiVersionMinor = 0;           ///< Required API minor version
    PluginCapability capabilities = PluginCapability::None;
};

/// @brief Plugin state
enum class PluginState : uint32_t {
    Unloaded = 0,
    Loaded,
    Initialized,
    Configured,
    Running,
    Error,
};

/// @brief Plugin entry point function type
/// The plugin DLL must export a function with this signature named "OME_GetPluginDescriptor"
using GetPluginDescriptorFn = PluginDescriptor* (*)();

/// @brief Plugin initialize function type
/// Called after loading. Returns 0 on success, non-zero on failure.
using PluginInitFn = int32_t (*)();

/// @brief Plugin shutdown function type
/// Called before unloading.
using PluginShutdownFn = void (*)();

/// @brief Plugin configure function type
/// Pass JSON config string. Returns 0 on success.
using PluginConfigureFn = int32_t (*)(const char* jsonConfig);

// --- Entry Point Macros ---

/// @brief Macro to declare a plugin entry point
/// Use in plugin DLLs:
/// @code
/// OME_DECLARE_PLUGIN(MyPlugin, "My Plugin", "1.0.0", "Author", "Description",
///                    PluginCapability::VideoFilter)
/// @endcode
#ifdef _WIN32
#define OME_PLUGIN_EXPORT __declspec(dllexport)
#else
#define OME_PLUGIN_EXPORT __attribute__((visibility("default")))
#endif

#define OME_DECLARE_PLUGIN(NAME, DISPLAY, VER, AUTHOR, DESC, CAPS)              \
    static openmedia::plugin_host::PluginDescriptor s_pluginDesc = {            \
        #NAME, DISPLAY, VER, AUTHOR, DESC,                                       \
        openmedia::plugin_host::PluginAPIVersion::MAJOR,                         \
        openmedia::plugin_host::PluginAPIVersion::MINOR,                         \
        CAPS                                                                     \
    };                                                                           \
    extern "C" OME_PLUGIN_EXPORT                                                 \
    openmedia::plugin_host::PluginDescriptor* OME_GetPluginDescriptor() {       \
        return &s_pluginDesc;                                                    \
    }

} // namespace openmedia::plugin_host
