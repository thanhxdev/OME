# OpenMedia SDK — Development Phases

**Version:** 2.0  
**Date:** July 2026 — Updated with Exhand Architecture

---

## Tổng quan các Phase

| Phase | Tên | Ước tính | Ưu tiên |
|-------|-----|----------|---------|
| 1 | Project Setup & Core Architecture | 2–3 tuần | 🔴 Critical |
| 1.5 | **Exhand: IPC, Server & Command Layer** | **3–4 tuần** | **🔴 Critical** |
| 2 | Input/Output Modules | 3–4 tuần | 🔴 Critical |
| 3 | Processing Pipeline | 4–6 tuần | 🟡 High |
| 4 | GPU Acceleration | 3–4 tuần | 🟡 High |
| 5 | Protocol Engines | 4–5 tuần | 🟡 High |
| 6 | .NET Wrapper & API | 3–4 tuần | 🟡 High |
| 7 | Plugin System | 2–3 tuần | 🟢 Medium |
| 8 | Testing, Documentation & Packaging | 3–4 tuần | 🟢 Medium |

---

## Phase 1: Project Setup & Core Architecture

### Mục tiêu
Thiết lập nền tảng dự án, hệ thống build, và core engine.

### Deliverables

#### 1.1 Repository & Build System
- [ ] Khởi tạo Git repository với `.gitignore`, `.clang-format`, `.clang-tidy`
- [ ] Root `CMakeLists.txt` với C++20 standard
- [ ] CMake modules: `CompilerSettings`, `Dependencies`, `Platform`, `Version`
- [ ] `EnvironmentConfig.cmake` — load `.env.demo` / `.env.production`
- [ ] vcpkg manifest (`vcpkg.json`) với các dependencies cơ bản
- [ ] Build scripts: `build.ps1` (Windows), `build.sh` (Linux)
- [ ] CI/CD pipeline skeleton (GitHub Actions / Azure DevOps)

#### 1.2 Core Engine (OpenMedia.Core)
- [ ] `IMediaObject` — base interface cho tất cả media objects
- [ ] `MediaFrame` — unified frame container (video + audio + metadata)
- [ ] `MediaPipeline` — pipeline builder và executor
- [ ] `FrameQueue` — thread-safe, lock-free frame buffer
- [ ] `ClockSync` — clock synchronization cho live workflows
- [ ] `Engine` — factory class tạo và quản lý các media objects
- [ ] `MemoryPool` — pre-allocated memory pool cho zero-copy
- [ ] `Logger` — structured logging (sử dụng spdlog)
- [ ] `ErrorCodes` — unified error code system
- [ ] `Config` — configuration management (JSON-based)

#### 1.3 Environment Configuration
- [ ] `.env.demo` — với logging verbose, debug symbols, watermark
- [ ] `.env.production` — với logging minimal, optimized build, license check
- [ ] `.env.shared` — shared config cho cả 2 environments
- [ ] `EnvironmentConfig.cmake` tự động load đúng environment

### Milestone
✅ Có thể build project, chạy unit test cơ bản, pipeline engine khởi tạo thành công.

---

## Phase 1.5: Exhand — IPC, Server & Command Layer 🔴 **NEW**

### Mục tiêu
Xây dựng lớp Client/Server process separation theo kiến trúc Exhand: IPC transport, OpenMediaServer.exe, Command Dispatcher, Worker Pool, Pipeline Graph, PluginHost, và Shared Memory frame sharing.

### Deliverables

#### 1.5.1 OpenMediaServer.exe
- [ ] Server process entry point (console application / Windows service)
- [ ] Server lifecycle management (start, stop, restart, status)
- [ ] Server configuration (port, max clients, log level)
- [ ] Health monitoring và watchdog
- [ ] Graceful shutdown với cleanup

#### 1.5.2 IPC Transport Layer (OpenMedia.IPC)
- [ ] `IPCTransport` — transport abstraction interface
- [ ] `NamedPipeTransport` — Named Pipes implementation cho commands/responses
  - [ ] Bidirectional communication
  - [ ] Async I/O (OVERLAPPED)
  - [ ] Multi-client support
  - [ ] Timeout và reconnect handling
- [ ] `SharedMemoryBuffer` — Shared Memory cho frame data (zero-copy)
  - [ ] Ring buffer implementation
  - [ ] Fence/semaphore synchronization
  - [ ] Configurable buffer size
- [ ] `D3D11SharedTexture` — D3D11 shared texture interop cho GPU frames
  - [ ] DXGI shared handle
  - [ ] Cross-process texture sharing
  - [ ] Keyed mutex synchronization
- [ ] `CommandMessage` — Command/Response protocol format (binary serialization)
- [ ] `FrameNotification` — Frame ready notifications (event-driven)
- [ ] `IPCClient` — Client-side IPC wrapper (sử dụng bởi OpenMedia.SDK.dll)
- [ ] `IPCServer` — Server-side IPC listener (sử dụng bởi OpenMediaServer.exe)

#### 1.5.3 Command Dispatcher
- [ ] `CommandDispatcher` — Parse, validate, route và execute commands
- [ ] `CommandHandler` — Handler interface cho các module
- [ ] `CommandRegistry` — Đăng ký handler cho từng loại command
- [ ] `CommandTypes` — Định nghĩa các loại command:
  - [ ] Pipeline commands (create, start, stop, pause, resume, destroy)
  - [ ] Source commands (open, close, seek, get-info)
  - [ ] Mixer commands (add-input, remove-input, set-transition)
  - [ ] Encoder commands (configure, start, stop)
  - [ ] Output commands (add, remove, configure)
  - [ ] Plugin commands (load, unload, list)
  - [ ] System commands (status, metrics, config)

#### 1.5.4 Worker Pool
- [ ] `WorkerPool` — Configurable thread pool
- [ ] `TaskQueue` — Priority-based task queue
- [ ] `WorkerThread` — Worker thread với task stealing
- [ ] Priority scheduling: real-time (decode/encode) > normal (filters) > low (metrics)
- [ ] Task cancellation và timeout

#### 1.5.5 Pipeline Graph Engine
- [ ] Nâng cấp `MediaPipeline` từ linear pipeline lên DAG-based graph
- [ ] Node connection: fan-in (Mixer), fan-out (multi-output)
- [ ] Graph validation (cycle detection, type checking)
- [ ] Dynamic graph modification (add/remove nodes at runtime)
- [ ] Graph serialization (JSON format)

#### 1.5.6 OpenMedia.SDK (Client Library)
- [ ] `SDKEngine` — Client-side engine proxy (lập IPC connection tới server)
- [ ] `SDKPipeline` — Client-side pipeline proxy
- [ ] `SDKSource` — Client-side source proxy
- [ ] Auto server launch: tự động khởi động OpenMediaServer.exe nếu chưa chạy
- [ ] Server discovery và connection management

#### 1.5.7 OpenMedia.PluginHost (tách biệt)
- [ ] `PluginHost` — quản lý plugin lifecycle trong server process
- [ ] `PluginSandbox` — crash isolation cho plugins (có thể crash mà không ảnh hưởng server)
- [ ] Plugin hot-reload trong demo mode

### Milestone
✅ Client SDK kết nối tới OpenMediaServer.exe qua IPC, gửi commands và nhận frames qua Shared Memory.

---

## Phase 2: Input/Output Modules

### Mục tiêu
Implement các nguồn input và output cơ bản.

### Deliverables

#### 2.1 File Sources (OpenMedia.IO)
- [ ] `MediaReader` — generic media file reader (FFmpeg backend)
- [ ] `FileSource` — file-based source (MP4, MOV, MXF, AVI, MKV)
- [ ] Demuxing, seeking, metadata extraction
- [ ] Image & Image Sequence source

#### 2.2 Live Sources
- [ ] `LiveSource` — live stream source (RTMP, RTSP, SRT, RIST, HLS, MPEG-TS, UDP, TCP)
- [ ] `NetworkSource` — network protocol abstraction layer
- [ ] Auto-reconnect, buffering, jitter handling

#### 2.3 Device Sources
- [ ] `DeviceFactory` — unified device source factory
- [ ] Blackmagic DeckLink integration (via DeckLink SDK)
- [ ] AJA integration (via NTV2 SDK)
- [ ] Magewell integration
- [ ] DirectShow / MediaFoundation capture
- [ ] Webcam, Audio Device capture
- [ ] Desktop Capture, Window Capture

#### 2.4 Encoding & Output
- [ ] `IEncoder` / `IDecoder` interfaces
- [ ] H.264, H.265, AV1, MPEG-2, JPEG2000 video encoders
- [ ] AAC, MP3, PCM, Opus audio encoders
- [ ] File output: MP4, MOV, MXF, TS
- [ ] Snapshot output

### Milestone
✅ Có thể đọc file media, capture từ device, encode và ghi ra file output.

---

## Phase 3: Processing Pipeline

### Mục tiêu
Xây dựng hệ thống xử lý video/audio pipeline.

### Deliverables

#### 3.1 Mixer (OpenMedia.Mixer)
- [ ] Multi-layer video mixing (composition)
- [ ] Audio mixing
- [ ] Transition engine (cut, dissolve, wipe, push, slide, etc.)
- [ ] Switcher (live switching giữa các inputs)
- [ ] Chroma key, Luma key
- [ ] Crop, Scale, Rotate, Mirror
- [ ] Color Correction, HDR, 10-bit support
- [ ] LUT application

#### 3.2 Overlay (OpenMedia.Overlay)
- [ ] Text overlay (multi-font, multi-style)
- [ ] Logo overlay (PNG, with alpha)
- [ ] Ticker (scrolling text)
- [ ] Clock overlay
- [ ] Subtitle renderer (CC608, CEA708)
- [ ] SCTE35/SCTE104 marker support
- [ ] HTML Overlay renderer (CEF integration)

#### 3.3 CG Engine (OpenMedia.CG)
- [ ] Character Generator template system
- [ ] Real-time CG rendering
- [ ] Data binding (live data feeds)

#### 3.4 Audio Engine (OpenMedia.Audio)
- [ ] Audio mixer (multi-channel)
- [ ] Delay, TimeShift
- [ ] Resampler, Channel mapper
- [ ] Audio meters: LUFS, VU, RMS
- [ ] Waveform display
- [ ] Vectorscope display

#### 3.5 Playlist (OpenMedia.Playlist)
- [ ] Playlist management (add, remove, reorder, loop)
- [ ] Transition between playlist items
- [ ] Replay engine
- [ ] Slow motion engine
- [ ] Metadata processing per item

### Milestone
✅ Full processing pipeline hoạt động: Source → Decode → Filter → Mix → Overlay → Encode → Output.

---

## Phase 4: GPU Acceleration

### Mục tiêu
Tích hợp GPU pipeline cho hiệu năng cao.

### Deliverables

#### 4.1 GPU Framework (OpenMedia.GPU)
- [ ] `GPUContext` — unified GPU context abstraction
- [ ] `GPUFrame` — GPU-resident frame type
- [ ] Zero-copy transfer: CPU ↔ GPU

#### 4.2 NVIDIA
- [ ] CUDA context integration
- [ ] NVDEC (hardware decode)
- [ ] NVENC (hardware encode)

#### 4.3 Intel
- [ ] Intel QuickSync (Media SDK / oneVPL)
- [ ] DXVA2 hardware acceleration

#### 4.4 Graphics APIs
- [ ] D3D11 texture interop
- [ ] D3D12 texture interop
- [ ] Vulkan compute pipeline
- [ ] OpenCL fallback

### Milestone
✅ Hardware-accelerated encode/decode, GPU-based filters, zero-copy pipeline.

---

## Phase 5: Protocol Engines

### Mục tiêu
Implement các protocol engine chuyên dụng.

### Deliverables

#### 5.1 SRT Engine (OpenMedia.SRT)
- [ ] SRT source (listener + caller modes)
- [ ] SRT output (push mode)
- [ ] Encryption, latency tuning
- [ ] Statistics & metrics

#### 5.2 NDI Engine (OpenMedia.NDI)
- [ ] NDI source discovery & capture
- [ ] NDI output (send)
- [ ] NDI|HX support
- [ ] Metadata exchange

#### 5.3 WebRTC Engine (OpenMedia.WebRTC)
- [ ] WebRTC source (receive)
- [ ] WebRTC output (broadcast)
- [ ] Signaling server integration
- [ ] WHIP/WHEP support

#### 5.4 RTMP Engine (OpenMedia.RTMP)
- [ ] RTMP source (receive/pull)
- [ ] RTMP output (push to YouTube, Facebook, Twitch, etc.)
- [ ] RTMPS (TLS) support

#### 5.5 ST 2110 Engine (OpenMedia.ST2110)
- [ ] SMPTE ST 2110 source (essence streams)
- [ ] ST 2110 output
- [ ] PTP clock sync
- [ ] NMOS integration

#### 5.6 ST 2022 Engine (OpenMedia.ST2022)
- [ ] SMPTE ST 2022 source (MPEG-TS over IP)
- [ ] ST 2022 output (with FEC handling)
- [ ] SMPTE ST 2022-7 Hitless Merge support

#### 5.7 Additional Outputs
- [ ] HLS output (segmented)
- [ ] DASH output
- [ ] CMAF output
- [ ] RIST output
- [ ] Shared Memory output
- [ ] SDI / HDMI output (via DeckLink/AJA)

### Milestone
✅ Tất cả protocol engines hoạt động, có thể stream đa nền tảng.

---

## Phase 6: .NET Wrapper & API

### Mục tiêu
Tạo managed wrappers cho .NET 8/9 applications.

### Deliverables

#### 6.1 Native Bridge
- [ ] C export layer (flat C API cho P/Invoke)
- [ ] C++/CLI bridge layer (alternative)
- [ ] Memory management policy (pin/unpin, ref counting)
- [ ] Error marshalling

#### 6.2 .NET Libraries
- [ ] `OpenMedia.Core.NET` — MediaPlayer, Pipeline, Frame
- [ ] `OpenMedia.IO.NET` — FileSource, LiveSource, DeviceSource
- [ ] `OpenMedia.Mixer.NET` — Mixer, Transitions
- [ ] `OpenMedia.WebRTC.NET` — WebRTC engine
- [ ] `OpenMedia.SRT.NET` — SRT engine
- [ ] `OpenMedia.NDI.NET` — NDI engine
- [ ] `OpenMedia.Playlist.NET` — Playlist
- [ ] `OpenMedia.CG.NET` — CG Engine

#### 6.3 High-Level API
- [ ] Fluent API design
- [ ] Event-based callbacks (OnFrameReady, OnError, OnStateChanged)
- [ ] Async/await support
- [ ] WPF/WinUI integration helpers

### Milestone
✅ .NET apps có thể sử dụng toàn bộ SDK features thông qua managed API.

---

## Phase 7: Plugin System

### Mục tiêu
Hoàn thiện Plugin SDK cho extensibility.

### Deliverables

#### 7.1 Plugin Infrastructure
- [ ] `IPlugin` base interface
- [ ] `PluginManager` — dynamic loading (LoadLibrary / dlopen)
- [ ] `PluginRegistry` — registration & discovery
- [ ] Plugin versioning & compatibility check

#### 7.2 Plugin Interfaces
- [ ] `IVideoFilter` — custom video filters
- [ ] `IAudioFilter` — custom audio filters
- [ ] `IEncoderPlugin` — custom encoders
- [ ] `IDecoderPlugin` — custom decoders
- [ ] `INetworkPlugin` — custom network protocols
- [ ] `IOverlayPlugin` — custom overlays
- [ ] `ITransitionPlugin` — custom transitions
- [ ] `IAIFilterPlugin` — AI-based filters (denoise, upscale, etc.)

#### 7.3 Example Plugins
- [ ] Sample Video Filter (grayscale, blur)
- [ ] Sample Audio Filter (gain, EQ)
- [ ] Sample Overlay (animated lower-third)

### Milestone
✅ Third-party developers có thể viết và load plugins.

---

## Phase 8: Testing, Documentation & Packaging

### Mục tiêu
Đảm bảo chất lượng, tài liệu đầy đủ, và đóng gói phân phối.

### Deliverables

#### 8.1 Testing
- [ ] Unit tests cho tất cả modules (Google Test)
- [ ] Integration tests cho pipeline workflows
- [ ] Performance benchmarks
- [ ] Memory leak detection (valgrind / AddressSanitizer)
- [ ] Fuzz testing cho parsers

#### 8.2 Sample Applications
- [ ] C++ Simple Player
- [ ] C++ Broadcast Pipeline
- [ ] C++ Mixer Demo
- [ ] C++ NDI/SRT Output
- [ ] .NET Simple Player (WPF)
- [ ] .NET Broadcast App
- [ ] .NET Mixer Demo
- [ ] .NET Streaming App

#### 8.3 Documentation
- [ ] API Reference (Doxygen-generated)
- [ ] Architecture Overview
- [ ] Getting Started Guide
- [ ] Plugin Development Guide
- [ ] Migration Guide (from Medialooks feature mapping)
- [ ] Changelog

#### 8.4 Packaging & Distribution
- [ ] NuGet packages cho .NET
- [ ] vcpkg port cho C++
- [ ] MSI installer
- [ ] Docker container (for server-side processing)
- [ ] Demo vs Production packaging with environment configs

### Milestone
✅ SDK ready cho commercial release với đầy đủ tests, docs, samples.

---

## Dependency Order

```
Phase 1 (Core) ──────┬─▶ Phase 1.5 (Exhand: IPC/Server) ─┬────────┬────────────┐
                      │                                  │        │            │
                      └─▶ Phase 2 (I/O) ──────────────┴────────┤            │
                                                                   │            │
                                    Phase 3 (Processing) ─────────┘            │
                                                                                │
                                    Phase 4 (GPU) ───────────────────────┤
                                                                                │
                                    Phase 5 (Protocols) ─────────────────┤
                                                                                │
                                                          Phase 6 (.NET) ────┤
                                                                                │
                                                       Phase 7 (Plugins) ───┤
                                                                                │
                                                                                ▼
                                                                    Phase 8 (Test/Doc)
```

> **Lưu ý:** Phase 1.5 (Exhand) cần hoàn thành trước Phase 2 vì I/O modules chạy trong server process. Phase 4 (GPU) và Phase 5 (Protocols) có thể phát triển song song với Phase 3.
