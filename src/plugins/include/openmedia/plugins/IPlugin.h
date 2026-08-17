#pragma once

#include <string>

namespace openmedia {
namespace plugins {

class IPlugin {
public:
    virtual ~IPlugin() = default;

    virtual const char* GetName() const = 0;
    virtual const char* GetVersion() const = 0;
    
    virtual bool Initialize() = 0;
    virtual void Shutdown() = 0;
};

// Typedef for the exported creation function in the dynamic library
typedef IPlugin* (*CreatePluginFunc)();
typedef void (*DestroyPluginFunc)(IPlugin*);

} // namespace plugins
} // namespace openmedia
