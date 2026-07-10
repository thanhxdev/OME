# OpenMedia SDK — Plugin SDK Specification

**Version:** 1.0  
**Date:** July 2026

---

## 1. Tổng quan

Plugin SDK cho phép third-party developers mở rộng OpenMedia SDK bằng cách viết các plugin:
- **Video Filter** — Xử lý/biến đổi video frames
- **Audio Filter** — Xử lý/biến đổi audio samples
- **Encoder** — Custom encoder implementations
- **Decoder** — Custom decoder implementations
- **Network Protocol** — Giao thức mạng tùy chỉnh
- **Overlay** — Hiển thị overlay tùy chỉnh
- **CG Template** — Character Generator templates
- **Transition** — Hiệu ứng chuyển cảnh
- **AI Filter** — Bộ lọc AI (denoise, upscale, segmentation)

Thiết kế lấy cảm hứng từ FFmpeg AVFilter, OBS Plugin, nhưng với API hiện đại C++20.

---

## 2. Plugin Interface

### 2.1 Base Plugin Interface

```cpp
// include/openmedia/plugin/IPlugin.h
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
```

### 2.2 Video Filter Interface

```cpp
// include/openmedia/plugin/IVideoFilter.h
#pragma once

#include "IPlugin.h"
#include <openmedia/core/MediaFrame.h>

namespace openmedia::plugin {

struct VideoFilterParams {
    int width;
    int height;
    int pixelFormat;    // OME pixel format enum
    double frameRate;
    bool gpuEnabled;
};

class IVideoFilter : public IPlugin {
public:
    // Setup filter with input parameters
    virtual bool Setup(const VideoFilterParams& params) = 0;

    // Process a video frame (in-place or allocate new)
    virtual bool ProcessFrame(
        const core::MediaFrame& input,
        core::MediaFrame& output
    ) = 0;

    // GPU variant (optional)
    virtual bool ProcessFrameGPU(
        const void* inputTexture,
        void* outputTexture,
        int textureFormat
    ) { return false; }

    // Get output parameters (may differ from input)
    virtual VideoFilterParams GetOutputParams() const = 0;

    // Reset filter state
    virtual void Reset() = 0;
};

} // namespace openmedia::plugin
```

### 2.3 Audio Filter Interface

```cpp
// include/openmedia/plugin/IAudioFilter.h
#pragma once

#include "IPlugin.h"
#include <cstddef>

namespace openmedia::plugin {

struct AudioFilterParams {
    int sampleRate;
    int channels;
    int sampleFormat;   // float32, int16, etc.
    int samplesPerFrame;
};

class IAudioFilter : public IPlugin {
public:
    virtual bool Setup(const AudioFilterParams& params) = 0;

    // Process audio samples
    virtual bool ProcessSamples(
        const float* input,
        float* output,
        size_t sampleCount,
        int channels
    ) = 0;

    virtual AudioFilterParams GetOutputParams() const = 0;
    virtual void Reset() = 0;
};

} // namespace openmedia::plugin
```

### 2.4 Encoder / Decoder Plugin Interface

```cpp
// include/openmedia/plugin/IEncoderPlugin.h
#pragma once

#include "IPlugin.h"
#include <openmedia/core/MediaFrame.h>
#include <vector>

namespace openmedia::plugin {

struct EncoderConfig {
    int width, height;
    double frameRate;
    int bitrate;        // kbps
    int gopSize;
    int bFrames;
    const char* preset; // "ultrafast", "medium", "slow"
    const char* profile;
    const char* pixelFormat;
};

class IEncoderPlugin : public IPlugin {
public:
    virtual bool Open(const EncoderConfig& config) = 0;
    virtual bool EncodeFrame(
        const core::MediaFrame& frame,
        std::vector<uint8_t>& encodedData
    ) = 0;
    virtual bool Flush(std::vector<std::vector<uint8_t>>& remaining) = 0;
    virtual void Close() = 0;

    virtual const char* GetCodecName() const = 0;
    virtual const char* GetCodecFourCC() const = 0;
};

class IDecoderPlugin : public IPlugin {
public:
    virtual bool Open(const char* codecName) = 0;
    virtual bool DecodePacket(
        const uint8_t* data,
        size_t size,
        core::MediaFrame& outputFrame
    ) = 0;
    virtual void Close() = 0;
};

} // namespace openmedia::plugin
```

### 2.5 Network Protocol Plugin

```cpp
// include/openmedia/plugin/INetworkPlugin.h
#pragma once

#include "IPlugin.h"
#include <cstddef>
#include <cstdint>
#include <functional>

namespace openmedia::plugin {

enum class NetworkRole { Source, Output, Bidirectional };

struct NetworkConfig {
    const char* url;
    int port;
    int latency;        // ms
    int timeout;        // ms
    int bufferSize;     // bytes
    const char* encryption;
    const char* passphrase;
};

class INetworkPlugin : public IPlugin {
public:
    virtual bool Connect(const NetworkConfig& config, NetworkRole role) = 0;
    virtual bool Disconnect() = 0;
    virtual bool IsConnected() const = 0;

    // Source mode
    virtual int Receive(uint8_t* buffer, size_t maxSize) = 0;

    // Output mode
    virtual bool Send(const uint8_t* data, size_t size) = 0;

    // Statistics
    virtual const char* GetStatisticsJson() const = 0;

    // Async callback (optional)
    using DataCallback = std::function<void(const uint8_t*, size_t)>;
    virtual void SetDataCallback(DataCallback callback) {}
};

} // namespace openmedia::plugin
```

### 2.6 AI Filter Plugin

```cpp
// include/openmedia/plugin/IAIFilterPlugin.h
#pragma once

#include "IVideoFilter.h"

namespace openmedia::plugin {

enum class AIFilterType {
    Denoise,
    Upscale,
    Segmentation,
    BackgroundRemoval,
    FaceDetection,
    ObjectDetection,
    StyleTransfer,
    FrameInterpolation,
    Custom
};

class IAIFilterPlugin : public IVideoFilter {
public:
    virtual AIFilterType GetAIFilterType() const = 0;

    // Model management
    virtual bool LoadModel(const char* modelPath) = 0;
    virtual bool IsModelLoaded() const = 0;

    // Inference device
    virtual bool SetInferenceDevice(const char* device) = 0; // "cpu", "cuda:0", etc.

    // Confidence threshold (for detection)
    virtual void SetConfidence(float threshold) {}
};

} // namespace openmedia::plugin
```

---

## 3. Plugin Manager

### 3.1 Plugin Loading

```cpp
// include/openmedia/plugin/PluginManager.h
#pragma once

#include "IPlugin.h"
#include <string>
#include <vector>
#include <memory>

namespace openmedia::plugin {

class PluginManager {
public:
    static PluginManager& Instance();

    // Scan and load plugins from directory
    bool LoadPluginsFromDirectory(const std::string& path);

    // Load single plugin
    bool LoadPlugin(const std::string& dllPath);

    // Unload
    void UnloadPlugin(const std::string& name);
    void UnloadAll();

    // Query
    std::vector<PluginInfo> GetLoadedPlugins() const;
    IPlugin* GetPlugin(const std::string& name) const;

    // Factory
    template<typename T>
    T* CreateInstance(const std::string& name) const;

    // Hot reload (demo mode only)
    bool ReloadPlugin(const std::string& name);

private:
    PluginManager() = default;
    struct PluginEntry;
    std::vector<std::unique_ptr<PluginEntry>> m_plugins;
};

} // namespace openmedia::plugin
```

### 3.2 Plugin Discovery Flow

```
1. PluginManager scans plugin directory
2. For each .dll/.so file:
   a. LoadLibrary / dlopen
   b. GetProcAddress("ome_plugin_create")
   c. Call ome_plugin_create() → IPlugin*
   d. Verify apiVersion compatibility
   e. Call plugin->Initialize()
   f. Register in PluginRegistry
3. Plugins available via GetPlugin() / CreateInstance<T>()
```

---

## 4. Example Plugin

### Grayscale Video Filter Plugin

```cpp
// plugins/examples/SampleVideoFilter/GrayscaleFilter.h
#pragma once

#include <openmedia/plugin/IVideoFilter.h>

class GrayscaleFilter : public openmedia::plugin::IVideoFilter {
public:
    // IPlugin
    bool Initialize() override { return true; }
    void Shutdown() override {}
    const openmedia::plugin::PluginInfo& GetInfo() const override;
    bool Configure(const char* jsonConfig) override { return true; }
    const char* GetDefaultConfig() const override { return "{}"; }

    // IVideoFilter
    bool Setup(const openmedia::plugin::VideoFilterParams& params) override;
    bool ProcessFrame(
        const openmedia::core::MediaFrame& input,
        openmedia::core::MediaFrame& output
    ) override;
    openmedia::plugin::VideoFilterParams GetOutputParams() const override;
    void Reset() override {}

private:
    openmedia::plugin::VideoFilterParams m_params{};
};

OME_DECLARE_PLUGIN(GrayscaleFilter)
```

---

## 5. Plugin Directory Structure

```
plugins/
├── examples/
│   ├── SampleVideoFilter/
│   │   ├── CMakeLists.txt
│   │   ├── GrayscaleFilter.h
│   │   ├── GrayscaleFilter.cpp
│   │   └── plugin.json           # Plugin metadata
│   ├── SampleAudioFilter/
│   │   ├── CMakeLists.txt
│   │   ├── GainFilter.h
│   │   └── GainFilter.cpp
│   └── SampleOverlay/
│       ├── CMakeLists.txt
│       ├── LowerThirdOverlay.h
│       └── LowerThirdOverlay.cpp
└── builtin/
    ├── ColorCorrectionFilter/
    ├── LUTFilter/
    └── NoiseReductionFilter/
```

### plugin.json example:

```json
{
    "name": "GrayscaleFilter",
    "displayName": "Grayscale Video Filter",
    "description": "Converts video frames to grayscale",
    "author": "OpenMedia",
    "version": "1.0.0",
    "apiVersion": 1,
    "capabilities": ["VideoFilter"],
    "dependencies": [],
    "config": {
        "intensity": {
            "type": "float",
            "default": 1.0,
            "min": 0.0,
            "max": 1.0,
            "description": "Grayscale intensity (0=color, 1=full grayscale)"
        }
    }
}
```
