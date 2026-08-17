# Plugin Development Guide

The OpenMedia SDK features a highly modular architecture that supports dynamic plugins. You can write your own plugins in C++ or C# to add custom video filters, new input sources, or specialized audio processing without recompiling the core SDK.

## Understanding Plugins

A plugin in OpenMedia SDK is a dynamic library (`.dll` on Windows, `.so` on Linux) that implements specific interfaces defined in the SDK (e.g., `IMediaObject`, `IFilter`, `ISource`). 

The `PluginManager` scans a directory for these libraries, loads them, and registers their capabilities at runtime.

## Writing a C++ Plugin

### 1. Implement the Component
Create a class that inherits from `IMediaObject` (or a more specific interface).

```cpp
#include <openmedia/core/IMediaObject.h>

class MyCustomFilter : public openmedia::core::IMediaObject {
public:
    std::string GetName() const override { return "MyCustomFilter"; }

    bool Initialize() override {
        // Allocate resources, e.g., setup OpenCV context or FFmpeg filter
        return true;
    }

    bool Start() override {
        m_isRunning = true;
        return true;
    }

    bool Stop() override {
        m_isRunning = false;
        return true;
    }
    
    bool Connect(IMediaObject* downstream) override {
        m_downstream = downstream;
        return true;
    }

    bool PushFrame(openmedia::core::MediaFrame* frame) override {
        if (!m_isRunning || !frame) return false;
        
        // Apply custom filter logic here
        // ...

        if (m_downstream) {
            return m_downstream->PushFrame(frame);
        }
        return true;
    }

private:
    bool m_isRunning = false;
    IMediaObject* m_downstream = nullptr;
};
```

### 2. Export the Plugin Factory
Your DLL must expose a specific C-style export function called `ome_plugin_create` so the SDK can instantiate it.

```cpp
#include <openmedia/plugin/IPlugin.h>

extern "C" {
    __declspec(dllexport) openmedia::plugin::IPlugin* ome_plugin_create() {
        // Return a wrapper that provides metadata and factory methods
        return new MyPluginWrapper();
    }
}
```

### 3. Build as a Shared Library
Ensure your CMake output is a shared library (`MODULE` or `SHARED`).

```cmake
add_library(MyCustomPlugin SHARED src/MyCustomFilter.cpp src/PluginExport.cpp)
target_link_libraries(MyCustomPlugin PRIVATE OpenMedia.Core)
```

## Loading Plugins

Once your plugin DLL is built, place it in a designated `plugins/` directory next to your executable. Use the `PluginManager` to load it.

```cpp
#include <openmedia/plugin/PluginManager.h>

int main() {
    auto pluginManager = openmedia::plugin::PluginManager::Instance();
    
    // Load all plugins in the directory
    pluginManager.LoadPluginsFromDirectory("./plugins");
    
    // Instantiate your custom filter by name
    auto myFilter = pluginManager->CreateObject("MyCustomFilter");
    
    // Use it in a pipeline
    auto pipeline = engine->CreatePipeline();
    pipeline->SetSource(source.get())
            .AddFilter(myFilter.get())
            .AddOutput(output.get())
            .Build();

    pipeline->Start();
    
    // Wait for exit
    std::cout << "Running pipeline with custom filter...\n";
    std::cin.get();

    pipeline->Stop();
    return 0;
}
```

## C# Plugins (.NET)

OpenMedia SDK uses C++/CLI (or standard interop) to host the .NET runtime, allowing plugins written in C# to be injected directly into the native pipeline. For details on C# plugin development, refer to the `07_plugin_sdk_spec.md` document.
