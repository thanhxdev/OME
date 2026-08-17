# OpenMedia SDK — API Design

**Version:** 1.0  
**Date:** July 2026

---

## 1. Design Philosophy

- **Modern C++20** — Không sử dụng COM, không phụ thuộc Windows-specific APIs
- **Fluent API** — Chainable methods cho pipeline construction
- **RAII** — Resource management tự động
- **Smart Pointers** — Dùng `std::unique_ptr` / `std::shared_ptr`
- **Error Handling** — `std::expected` (C++23) hoặc custom `Result<T, Error>`
- **Async** — `std::future`, coroutines cho I/O operations

---

## 2. C++ Core API

### 2.1 Engine (Entry Point)

```cpp
namespace openmedia {

class Engine {
public:
    // Factory
    static std::unique_ptr<Engine> Create();
    static std::unique_ptr<Engine> Create(const EngineConfig& config);

    // Source factories
    std::unique_ptr<FileSource> CreateFileSource(const std::string& path);
    std::unique_ptr<LiveSource> CreateLiveSource(const std::string& url);
    std::unique_ptr<DeviceSource> CreateDeviceSource(const DeviceInfo& device);

    // Processing factories
    std::unique_ptr<Mixer> CreateMixer(const MixerConfig& config = {});
    std::unique_ptr<Playlist> CreatePlaylist();
    std::unique_ptr<OverlayEngine> CreateOverlayEngine();

    // Encoder/Output factories
    std::unique_ptr<Encoder> CreateEncoder(const EncoderConfig& config);
    std::unique_ptr<SRTOutput> CreateSRTOutput(const SRTConfig& config);
    std::unique_ptr<NDIOutput> CreateNDIOutput(const std::string& name);
    std::unique_ptr<RTMPOutput> CreateRTMPOutput(const std::string& url);
    std::unique_ptr<WebRTCOutput> CreateWebRTCOutput(const WebRTCConfig& config);
    std::unique_ptr<FileOutput> CreateFileOutput(const std::string& path, const EncoderConfig& config);

    // Pipeline
    std::unique_ptr<MediaPipeline> CreatePipeline();

    // Lifecycle
    void Run();            // Start all pipelines
    void Stop();           // Stop all pipelines
    bool IsRunning() const;

    // Device enumeration
    std::vector<DeviceInfo> EnumerateDevices(DeviceType type = DeviceType::All);
    std::vector<NDISourceInfo> DiscoverNDISources(int timeoutMs = 5000);

    // Plugin
    PluginManager& GetPluginManager();

    // Environment
    const EnvironmentConfig& GetEnvironment() const;

    // Version
    static const char* GetVersionString();
    static int GetVersionMajor();
};

} // namespace openmedia
```

### 2.2 MediaPipeline (Pipeline Builder)

```cpp
namespace openmedia {

class MediaPipeline {
public:
    // Fluent pipeline construction
    MediaPipeline& SetSource(IMediaObject* source);
    MediaPipeline& AddFilter(IMediaObject* filter);
    MediaPipeline& SetMixer(Mixer* mixer, int layerIndex = 0);
    MediaPipeline& AddOverlay(IMediaObject* overlay);
    MediaPipeline& SetEncoder(Encoder* encoder);
    MediaPipeline& AddOutput(IMediaObject* output);

    // Simplified chain
    MediaPipeline& Connect(IMediaObject* from, IMediaObject* to);

    // Lifecycle
    bool Build();          // Validate and finalize pipeline
    bool Start();
    bool Stop();
    bool Pause();
    bool Resume();

    // State
    PipelineState GetState() const;
    PipelineStats GetStats() const;

    // Events
    void OnError(std::function<void(const Error&)> callback);
    void OnStateChanged(std::function<void(PipelineState)> callback);
    void OnFrameProcessed(std::function<void(const FrameInfo&)> callback);
};

} // namespace openmedia
```

### 2.3 IMediaObject (Base Interface)

```cpp
namespace openmedia {

enum class MediaObjectType {
    Source, Decoder, Filter, Mixer, Overlay,
    Encoder, Output, Preview, Playlist
};

class IMediaObject {
public:
    virtual ~IMediaObject() = default;

    // Connection
    virtual bool Connect(IMediaObject* downstream) = 0;
    virtual bool Disconnect(IMediaObject* downstream) = 0;

    // Lifecycle
    virtual bool Start() = 0;
    virtual bool Stop() = 0;
    virtual bool Pause() = 0;
    virtual bool Resume() = 0;

    // Frame transfer
    virtual MediaFrame* PullFrame() = 0;          // Pull model
    virtual bool PushFrame(MediaFrame* frame) = 0; // Push model

    // Properties
    virtual MediaObjectType GetType() const = 0;
    virtual const char* GetName() const = 0;

    // Configuration
    virtual bool SetProperty(const std::string& key, const std::string& value) = 0;
    virtual std::string GetProperty(const std::string& key) const = 0;
};

} // namespace openmedia
```

### 2.4 WebRTCConfig

```cpp
namespace openmedia::webrtc {

enum class WebRTCMode { Single, Simulcast, MultiDestination };

struct SimulcastLayer {
    int width, height, fps, bitrate;
    std::string rid; 
};

struct WebRTCConfig {
    std::string signalingUri;
    std::vector<std::string> iceServers;
    WebRTCMode mode = WebRTCMode::Single;
    
    // Encoder & ABR settings
    codecs::VideoCodec videoCodec = codecs::VideoCodec::H264;
    bool enableGpuFallback = true;
    bool enableABR = true;
    
    std::vector<SimulcastLayer> simulcastLayers;
};

} // namespace openmedia::webrtc
```

### 2.5 MediaFrame

```cpp
namespace openmedia {

struct VideoFrameInfo {
    int width;
    int height;
    PixelFormat format;     // NV12, BGRA, YUV420P, etc.
    double pts;             // Presentation timestamp (seconds)
    int64_t frameNumber;
    bool isKeyFrame;
    ColorSpace colorSpace;
    TransferFunction transferFunction;  // SDR, PQ (HDR10), HLG
    int bitDepth;           // 8, 10, 12
};

struct AudioFrameInfo {
    int sampleRate;
    int channels;
    SampleFormat format;    // Float32, Int16, Int32
    int sampleCount;
    double pts;
};

class MediaFrame {
public:
    // Access video data
    uint8_t* GetVideoData(int plane = 0);
    const uint8_t* GetVideoData(int plane = 0) const;
    int GetVideoLineSize(int plane = 0) const;
    VideoFrameInfo GetVideoInfo() const;

    // Access audio data
    float* GetAudioData(int channel = 0);
    const float* GetAudioData(int channel = 0) const;
    AudioFrameInfo GetAudioInfo() const;

    // Metadata
    MediaMetadata& GetMetadata();

    // GPU
    bool IsGPUFrame() const;
    void* GetGPUTexture() const;        // D3D11 Texture2D* or CUdeviceptr
    GPUTextureFormat GetGPUFormat() const;

    // Memory
    static std::unique_ptr<MediaFrame> Allocate(const VideoFrameInfo& info);
    static std::unique_ptr<MediaFrame> AllocateAudio(const AudioFrameInfo& info);
    static std::unique_ptr<MediaFrame> WrapExternalMemory(void* data, size_t size, const VideoFrameInfo& info);

    // Copy / Clone
    std::unique_ptr<MediaFrame> Clone() const;
    void CopyTo(MediaFrame& dest) const;

    // Conversion
    std::unique_ptr<MediaFrame> ConvertTo(PixelFormat targetFormat) const;
};

} // namespace openmedia
```

---

## 3. Usage Examples (C++)

### 3.1 Simple File Player

```cpp
#include <openmedia/Engine.h>

int main() {
    auto engine = openmedia::Engine::Create();

    auto source = engine->CreateFileSource("video.mp4");
    auto preview = engine->CreatePreview(windowHandle);

    auto pipeline = engine->CreatePipeline();
    pipeline->SetSource(source.get())
            .AddOutput(preview.get())
            .Build();

    pipeline->Start();

    std::cout << "Press Enter to stop...\n";
    std::cin.get();

    pipeline->Stop();
}
```

### 3.2 Broadcast Pipeline

```cpp
#include <openmedia/Engine.h>

int main() {
    auto engine = openmedia::Engine::Create({
        .environment = "production",
        .gpuPrefer = GPUPreference::CUDA
    });

    // Sources
    auto cam1 = engine->CreateDeviceSource({"Blackmagic", 0});
    auto cam2 = engine->CreateDeviceSource({"Blackmagic", 1});
    auto graphic = engine->CreateFileSource("lower_third.png");

    // Mixer
    auto mixer = engine->CreateMixer({
        .width = 1920, .height = 1080,
        .frameRate = 29.97
    });
    mixer->AddInput(cam1.get(), 0);     // Layer 0: Camera 1
    mixer->AddInput(cam2.get(), 1);     // Layer 1: Camera 2

    // Overlay
    auto overlay = engine->CreateOverlayEngine();
    overlay->AddLogo("logo.png", Position::TopRight, 0.8f);
    overlay->AddTicker("Breaking News: ...", TickerStyle::ScrollLeft);

    // Encoder + Output
    auto encoder = engine->CreateEncoder({
        .codec = "h264",
        .bitrate = 8000,
        .preset = "fast"
    });

    auto srtOut = engine->CreateSRTOutput({
        .host = "srt://broadcast.server.com",
        .port = 9000,
        .latency = 120
    });

    auto rtmpOut = engine->CreateRTMPOutput("rtmp://live.youtube.com/live2/your-key");

    // Pipeline
    auto pipeline = engine->CreatePipeline();
    pipeline->SetSource(mixer.get())
            .AddFilter(overlay.get())
            .SetEncoder(encoder.get())
            .AddOutput(srtOut.get())
            .AddOutput(rtmpOut.get())
            .Build();

    engine->Run();
}
```

### 3.3 NDI ↔ SRT Bridge

```cpp
auto engine = openmedia::Engine::Create();

auto ndiSource = engine->CreateLiveSource("ndi://STUDIO-PC/Camera 1");
auto srtOutput = engine->CreateSRTOutput({
    .host = "0.0.0.0", .port = 9000,
    .mode = SRTMode::Listener
});

auto encoder = engine->CreateEncoder({
    .codec = "h265", .bitrate = 15000,
    .hwAccel = true
});

auto pipeline = engine->CreatePipeline();
pipeline->Connect(ndiSource.get(), encoder.get())
        .Connect(encoder.get(), srtOutput.get())
        .Build();

pipeline->Start();
```

---

## 4. .NET API

### 4.1 Core API

```csharp
using OpenMedia.Core;

// Simple player
var engine = new MediaEngine();
var source = engine.CreateFileSource("video.mp4");
var preview = engine.CreatePreview(this.Handle);

var pipeline = engine.CreatePipeline()
    .SetSource(source)
    .AddOutput(preview)
    .Build();

await pipeline.StartAsync();
```

### 4.2 Broadcast Example

```csharp
using OpenMedia.Core;
using OpenMedia.IO;
using OpenMedia.Mixer;

var engine = new MediaEngine(new EngineConfig {
    Environment = EnvironmentTag.Production,
    GpuPreference = GpuPreference.CUDA
});

var cam1 = engine.CreateDeviceSource(new DeviceInfo("Blackmagic", 0));
var cam2 = engine.CreateDeviceSource(new DeviceInfo("Blackmagic", 1));

var mixer = engine.CreateMixer(new MixerConfig {
    Width = 1920, Height = 1080,
    FrameRate = 29.97
});

mixer.AddInput(cam1, layer: 0);
mixer.AddInput(cam2, layer: 1);

var encoder = engine.CreateEncoder(new EncoderConfig {
    Codec = "h264",
    Bitrate = 8000,
    Preset = "fast"
});

var srtOutput = engine.CreateSRTOutput(new SRTConfig {
    Host = "srt://broadcast.server.com",
    Port = 9000
});

var pipeline = engine.CreatePipeline()
    .SetSource(mixer)
    .SetEncoder(encoder)
    .AddOutput(srtOutput)
    .Build();

// Events
pipeline.OnError += (sender, error) => Console.WriteLine($"Error: {error.Message}");
pipeline.OnStateChanged += (sender, state) => Console.WriteLine($"State: {state}");

await pipeline.StartAsync();
```

### 4.3 Event Model

```csharp
public class MediaPipeline {
    // Events
    public event EventHandler<MediaError> OnError;
    public event EventHandler<PipelineState> OnStateChanged;
    public event EventHandler<FrameInfo> OnFrameProcessed;
    public event EventHandler<MediaMetadata> OnMetadataReceived;

    // Async lifecycle
    public Task StartAsync(CancellationToken ct = default);
    public Task StopAsync(CancellationToken ct = default);
    public Task PauseAsync();
    public Task ResumeAsync();

    // State
    public PipelineState State { get; }
    public PipelineStats Stats { get; }
}
```

---

## 5. Error Handling

### C++ Error Model

```cpp
namespace openmedia {

enum class ErrorCode {
    Success = 0,
    InvalidArgument,
    FileNotFound,
    CodecNotSupported,
    DeviceNotAvailable,
    NetworkError,
    PipelineError,
    GPUError,
    LicenseError,
    PluginError,
    OutOfMemory,
    Timeout,
    Unknown
};

struct Error {
    ErrorCode code;
    std::string message;
    std::string source;     // Module that produced the error
    int line;               // Source line (debug only)
};

// Result type for error handling
template<typename T>
using Result = std::expected<T, Error>;

} // namespace openmedia
```

---

## 6. Naming Conventions

| Element | C++ | .NET |
|---------|-----|------|
| Namespace | `openmedia::core` | `OpenMedia.Core` |
| Class | `MediaPipeline` | `MediaPipeline` |
| Method | `CreateFileSource()` | `CreateFileSource()` |
| Property | `GetWidth()` / `SetWidth()` | `Width { get; set; }` |
| Enum | `PixelFormat::NV12` | `PixelFormat.NV12` |
| Constant | `kMaxFrameQueueSize` | `MaxFrameQueueSize` |
| Event | `OnError(callback)` | `event OnError` |
