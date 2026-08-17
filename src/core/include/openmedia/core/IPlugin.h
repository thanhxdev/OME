#pragma once

#include <openmedia/core/ErrorCodes.h>
#include <string>

#ifdef _WIN32
#define OME_PLUGIN_EXPORT extern "C" __declspec(dllexport)
#else
#define OME_PLUGIN_EXPORT extern "C" __attribute__((visibility("default")))
#endif

namespace openmedia::core {

class IPlugin {
public:
    virtual ~IPlugin() = default;

    virtual std::string GetName() const = 0;
    virtual std::string GetVersion() const = 0;
    virtual std::string GetDescription() const = 0;

    virtual VoidResult Initialize() = 0;
    virtual VoidResult Shutdown() = 0;
};

} // namespace openmedia::core

// Typedef for the export function that every plugin DLL must implement
typedef openmedia::core::IPlugin* (*OmePluginCreateFunc)();
