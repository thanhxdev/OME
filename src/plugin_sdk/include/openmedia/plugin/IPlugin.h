#pragma once

#include <string>
#include <cstdint>

namespace openmedia::plugin {

// Plugin capability flags
enum class PluginCapability : uint32_t {
    None            = 0,
    VideoFilter     = 1 << 0,
    AudioFilter     = 1 << 1,
    Encoder         = 1 << 2,
    Decoder         = 1 << 3,
    NetworkProtocol = 1 << 4,
    Overlay         = 1 << 5,
    CGTemplate      = 1 << 6,
    Transition      = 1 << 7,
    AIFilter        = 1 << 8,
};

inline PluginCapability operator|(PluginCapability a, PluginCapability b) {
    return static_cast<PluginCapability>(static_cast<uint32_t>(a) | static_cast<uint32_t>(b));
}

inline PluginCapability operator&(PluginCapability a, PluginCapability b) {
    return static_cast<PluginCapability>(static_cast<uint32_t>(a) & static_cast<uint32_t>(b));
}

struct PluginInfo {
    const char* name;
    const char* displayName;
    const char* description;
    const char* author;
    const char* version;
    const char* url;
    uint32_t    apiVersion;     // OME_PLUGIN_API_VERSION
    PluginCapability capabilities;
};

// Base plugin interface — tất cả plugins phải implement
class IPlugin {
public:
    virtual ~IPlugin() = default;

    // Lifecycle
    virtual bool Initialize() = 0;
    virtual void Shutdown() = 0;

    // Metadata
    virtual const PluginInfo& GetInfo() const = 0;

    // Configuration
    virtual bool Configure(const char* jsonConfig) = 0;
    virtual const char* GetDefaultConfig() const = 0;
};

// Plugin entry point macros
#define OME_PLUGIN_API_VERSION 1

#ifdef _WIN32
    #define OME_PLUGIN_EXPORT extern "C" __declspec(dllexport)
#else
    #define OME_PLUGIN_EXPORT extern "C" __attribute__((visibility("default")))
#endif

// Mỗi plugin DLL phải export 2 functions này:
// OME_PLUGIN_EXPORT IPlugin* ome_plugin_create();
// OME_PLUGIN_EXPORT void ome_plugin_destroy(IPlugin* plugin);

#define OME_DECLARE_PLUGIN(PluginClass)                          \
    OME_PLUGIN_EXPORT openmedia::plugin::IPlugin* ome_plugin_create() { \
        return new PluginClass();                                 \
    }                                                             \
    OME_PLUGIN_EXPORT void ome_plugin_destroy(openmedia::plugin::IPlugin* p) { \
        delete p;                                                 \
    }

} // namespace openmedia::plugin
