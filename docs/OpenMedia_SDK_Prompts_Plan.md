# OpenMedia SDK Development Prompts Plan for Antigravity

## Project Overview
This document provides a structured set of prompts and development plans to build **OpenMedia SDK** — a high-performance, independently developed C++20 + .NET 8/9 Media Engine SDK. It mirrors the capabilities of Medialooks SDK but with a modern, clean, plugin-based architecture without copying any code or violating copyrights.

**Key Principles:**
- Design independent architecture and APIs.
- Use modern C++20 features and .NET 8/9.
- Modular, extensible via plugins.
- High-performance media pipeline.
- Support for broadcast/production workflows.

**Target Architecture:**
- **C++ Core Engine** (performance-critical).
- **.NET Wrappers** (for Windows apps).
- Plugin SDK for extensions.

---

## Phase 1: Project Setup & Core Architecture

### Prompt 1: Initialize Repository and Core Structure
```
You are an expert C++20 and CMake architect. Create a complete project skeleton for OpenMedia SDK.

Project name: OpenMediaSDK
Structure:
- OpenMedia.Core (base classes, MediaObject, Pipeline)
- OpenMedia.IO (sources, readers, outputs)
- OpenMedia.Codecs (decoders/encoders)
- OpenMedia.Rendering, OpenMedia.Mixer, OpenMedia.Audio, etc. (as per spec)
- OpenMedia.PluginSDK
- CMakeLists.txt for multi-platform build
- .NET 8 solution with C++/CLI or P/Invoke wrappers

Include:
- Modern CMake with C++20 standard
- Logging, error handling, threading utilities
- IMediaObject base interface
- Pipeline builder class
- Versioning and build scripts

Output the full directory tree and key CMake files.
```

### Prompt 2: Define Core MediaObject and Pipeline
```
Design the core C++ classes for OpenMedia SDK following this hierarchy:

class IMediaObject {
public:
    virtual ~IMediaObject() = default;
    virtual bool Connect(IMediaObject* downstream) = 0;
    virtual bool Start() = 0;
    virtual bool Stop() = 0;
    virtual MediaFrame* PullFrame() = 0; // or Push model
};

Implement:
- MediaPipeline class for connecting Source -> Decoder -> Filters -> Mixer -> Encoder -> Output
- Frame queue with thread-safe buffering
- Metadata handling
- Clock synchronization

Provide header files and basic implementations with extensive comments.
Ensure thread-safety and zero-copy where possible.
```

---

## Phase 2: Input/Output Modules

### Prompt 3: File and Stream Sources
```
Implement MediaReader and FileSource for OpenMedia.IO:

Supported formats: MP4, MOV, MXF, AVI, MKV, HLS, MPEG-TS, etc.

Use FFmpeg libraries (libavformat, libavcodec) as backend but wrap with clean API.
Provide:
- MediaFileSource
- LiveSource (RTMP, RTSP, SRT, UDP, etc.)
- Network protocols abstraction layer

Include demuxing, seeking, metadata extraction.
```

### Prompt 4: Output and Encoding
```
Create Encoder and Output modules:

- Support H.264, H.265, AV1, AAC, Opus, etc.
- Outputs: MP4, SRT, RTMP, NDI, WebRTC, SDI/HDMI (via Blackmagic/AJA SDKs)
- MFWriter equivalent

Implement push/pull frame model and async encoding.
```

### Prompt 5: Device Capture (SDI, HDMI, NDI, etc.)
```
Implement sources for:
- Blackmagic DeckLink
- AJA
- Magewell
- NDI
- DirectShow / MediaFoundation
- Webcam, Desktop, Window capture

Create unified DeviceSource factory.
```

---

## Phase 3: Processing Pipeline

### Prompt 6: Mixer and Overlay
```
Design MMixer equivalent:

- Multi-layer video/audio mixing
- Transitions, switcher
- Overlay (text, logo, ticker, clock)
- HTML Overlay renderer (using Chromium Embedded Framework or similar)
- Character Generator (CG)

Support chroma/luma key, crop, scale, rotate, color correction, LUTs.
```

### Prompt 7: Audio Engine
```
Build Audio Engine:
- Mixer, delay, timeshift, slow motion
- Audio meters (LUFS, VU, RMS, waveform, vectorscope)
- Support for PCM, resampling, channel mapping
```

### Prompt 8: Playlist and Advanced Features
```
Implement MPlaylist:
- Playlist management with transitions
- Replay, slow-motion
- SCTE35/104, CC608/CEA708 subtitle support
- Metadata processing
```

---

## Phase 4: GPU Acceleration

### Prompt 9: GPU Pipeline
```
Integrate GPU acceleration:
- CUDA / NVDEC / NVENC
- Intel QuickSync
- D3D11 / D3D12
- Vulkan / OpenCL

Create GPUFrame and unified GPUContext.
Support zero-copy transfers between CPU/GPU.
```

### Prompt 9.1: Native NVENC Hardware Encoder (Commercial-Grade)
```
Implement a Native NVENC Encoder module using the NVIDIA Video Codec SDK directly
(NOT through FFmpeg wrapper) for commercial-grade hardware encoding:

Architecture:
- NVENCEncoder class implementing IEncoder interface
- Supports H.264 and HEVC via NVENCCodec enum
- NVENC presets P1-P7, tuning modes (HighQuality, LowLatency, UltraLowLatency, Lossless)
- Rate control: CBR, VBR, CQP, target quality
- B-frame support, lookahead buffer, temporal AQ

Native SDK Flow:
1. cuInit → cuDeviceGet → cuCtxCreate (CUDA context)
2. NvEncodeAPICreateInstance → load function table
3. nvEncOpenEncodeSessionEx → encoder session
4. nvEncGetEncodePresetConfigEx → preset config
5. nvEncInitializeEncoder → configure resolution/fps/bitrate/GOP
6. nvEncCreateInputBuffer + nvEncCreateBitstreamBuffer → I/O buffer pool
7. PushFrame: nvEncLockInputBuffer → copy NV12 → nvEncEncodePicture → nvEncLockBitstream → output
8. Flush: EOS → drain remaining frames

Additional:
- NVENCCapabilities.h for runtime GPU caps query
- CUDAContext full implementation (cuInit, Upload/Download)
- CodecFactory auto-selection: NVENC Native > FFmpeg NVENC > QSV > Software
- Graceful stub fallback when NVIDIA GPU is unavailable

Performance targets:
- H.264 1080p30: > 240 fps
- HEVC 1080p60: > 120 fps
- CPU usage: < 5% (vs ~50% software)
- Encode latency: < 5ms per frame
```


---

## Phase 5: Protocol Engines

### Prompt 10: Network Protocols
```
Implement dedicated engines:
- OpenMedia.SRT
- OpenMedia.NDI
- OpenMedia.WebRTC
- OpenMedia.RTMP
- OpenMedia.ST2110
- OpenMedia.ST2022

Each as pluggable modules with clean APIs.
```

---

## Phase 6: .NET Wrapper and API

### Prompt 11: .NET 8 Wrapper
```
Create C#/.NET 8 managed wrappers:

public class MediaPlayer {
    public void Open(string url);
    public void Play();
    // etc.
}

Use P/Invoke or C++/CLI.
Provide high-level fluent API matching the C++ example:

var source = engine.CreateFileSource();
source.Connect(mixer);
...
```

### Prompt 12: Modern C++ API Examples
```
Generate comprehensive usage examples for C++:

auto engine = CreateEngine();
auto source = engine->CreateFileSource("video.mp4");
auto mixer = engine->CreateMixer();
...
engine->Run();
```

---

## Phase 7: Plugin System

### Prompt 13: Plugin SDK
```
Design PluginSDK similar to OBS/FFmpeg AVFilter:

- Interface for VideoFilter, AudioFilter, Encoder, Decoder, NetworkProtocol, Overlay, Transition, AI Filter
- Dynamic loading (dlopen / LoadLibrary)
- Registration system
- Example plugins
```

---

## Phase 8: Testing, Documentation & Packaging

### Prompt 14: Unit Tests and Samples
```
Create comprehensive test suite and sample applications:
- Simple player
- Broadcast pipeline
- Mixer demo
- NDI/SRT output

Use Google Test or Catch2.
```

### Prompt 15: Documentation
```
Generate detailed documentation:
- API reference (Doxygen)
- Architecture overview
- Getting started guide
- Comparison with Medialooks (feature mapping)
```

---

## Implementation Strategy in Antigravity
1. Start with Phase 1 prompts to set up repository.
2. Proceed module by module.
3. Use iterative refinement: Implement → Test → Optimize.
4. Maintain strict separation from any proprietary code.
5. Focus on performance, stability, and extensibility.

**Next Step:** Execute Prompt 1 to bootstrap the project structure.

---

**Version:** 1.0
**Date:** July 2026
**License:** Plan for commercial OpenMedia SDK development
```

This file is ready for download. You can copy the content above or use the generated file in the workspace.