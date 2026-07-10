# OpenMedia SDK — Architecture Overview

**Version:** 2.0  
**Date:** July 2026  
**Status:** Draft — Updated with Client/Server Process Separation (Exhand Architecture)

---

## 1. Tầm nhìn dự án

OpenMedia SDK là bộ SDK media engine hiệu năng cao, được phát triển độc lập hoàn toàn bằng C++20 và .NET 8/9. SDK hướng đến thay thế các giải pháp thương mại trong lĩnh vực broadcast và production, với kiến trúc hiện đại, plugin-based, không phụ thuộc COM.

**Triết lý kiến trúc Client/Server:** Hệ thống tách biệt hoàn toàn giữa UI (client process) và media processing (server process), tương tự triết lý của Medialooks nhưng hiện đại hóa bằng C++20/.NET 8, IPC tốc độ cao và plugin động. Mô hình này đảm bảo:
- **Ổn định**: Một thành phần lỗi không làm sập toàn bộ ứng dụng
- **Mở rộng**: Plugin và engine chạy trong process riêng
- **Hiệu năng**: Zero-copy frame sharing qua Shared Memory + D3D11 Shared Textures

## 2. Kiến trúc Client/Server Process Separation

### 2.1 Mô hình tổng quan

```
╔══════════════════════════════════════════════════════════════════╗
║                     CLIENT PROCESS (UI)                          ║
║  ┌──────────────────────────────────────────────────────────┐    ║
║  │            Application Layer                              │    ║
║  │  (.NET 8/9 Apps, WPF/WinUI, Console Tools, REST API)     │    ║
║  ├──────────────────────────────────────────────────────────┤    ║
║  │          .NET Managed Wrapper Layer                        │    ║
║  │  OpenMedia.SDK.dll (Public API)                            │    ║
║  │  P/Invoke / C++/CLI Bridge                                │    ║
║  ├──────────────────────────────────────────────────────────┤    ║
║  │          IPC Client Layer                                  │    ║
║  │  Named Pipes | Shared Memory | D3D11 Shared Textures      │    ║
║  └──────────────────────────────────────────────────────────┘    ║
╚══════════════════════════════════════════════════════════════════╝
                              │
                     IPC Transport Layer
            (Named Pipes + Shared Memory + D3D11 Shared Textures)
                              │
╔══════════════════════════════════════════════════════════════════╗
║                   SERVER PROCESS (Engine)                        ║
║                   OpenMediaServer.exe                             ║
║  ┌──────────────────────────────────────────────────────────┐    ║
║  │          IPC Server Layer                                  │    ║
║  │  Command Receiver | Response Sender | Frame Notifier       │    ║
║  ├──────────────────────────────────────────────────────────┤    ║
║  │          Command Dispatcher                                │    ║
║  │  Parse → Route → Execute → Response                        │    ║
║  ├──────────────────────────────────────────────────────────┤    ║
║  │          Worker Pool                                       │    ║
║  │  Thread Pool | Task Queue | Priority Scheduling            │    ║
║  ├────────────────┬────────────────┬──────────────────────┤    ║
║  │   Decoder      │    Mixer       │     Encoder          │    ║
║  │   Playlist     │    Overlay     │     SRT/NDI/RTMP     │    ║
║  │   NDI/WebRTC   │    Audio Mixer │     File Output      │    ║
║  ├────────────────┴────────────────┴──────────────────────┤    ║
║  │          Pipeline Graph Engine                             │    ║
║  │  Source → Filter → Mixer → Encoder → Output (DAG-based)   │    ║
║  ├──────────────────────────────────────────────────────────┤    ║
║  │          OpenMedia.PluginHost                              │    ║
║  │  Dynamic plugin loading (NDI, DeckLink, WebRTC, SRT, FF)  │    ║
║  ├──────────────────────────────────────────────────────────┤    ║
║  │          Core Services                                     │    ║
║  │  GPU Manager | Audio Mixer | Device Manager                │    ║
║  │  Memory Pool | Clock Sync | Logging | Config               │    ║
║  ├──────────────────────────────────────────────────────────┤    ║
║  │          Hardware Abstraction Layer (HAL)                   │    ║
║  │  GPU (CUDA/NVENC/QuickSync/D3D11/D3D12/Vulkan/OpenCL)     │    ║
║  │  Capture Devices (Blackmagic/AJA/Magewell/DirectShow/MF)  │    ║
║  │  Audio Devices (WASAPI/ASIO)                               │    ║
║  └──────────────────────────────────────────────────────────┘    ║
╚══════════════════════════════════════════════════════════════════╝
```

### 2.2 Kiến trúc phân tầng chi tiết

```
┌──────────────────────────────────────────────────────────────────┐
│                    Application Layer                             │
│  (.NET 8/9 Apps, WPF/WinUI, Console Tools, REST API)            │
├──────────────────────────────────────────────────────────────────┤
│                  .NET Managed Wrapper Layer                       │
│  (P/Invoke / C++/CLI Bridge)                                     │
│  OpenMedia.Core.dll | OpenMedia.IO.dll | OpenMedia.WebRTC.dll    │
│  OpenMedia.SRT.dll  | OpenMedia.NDI.dll | OpenMedia.Playlist.dll │
│  OpenMedia.CG.dll                                                │
├──────────────────────────────────────────────────────────────────┤
│                  IPC / SDK Client Layer                           │
│  (Named Pipes for commands, Shared Memory for frames)            │
│  OpenMedia.SDK.dll → IPC Client → OpenMediaServer.exe            │
├──────────────────────────────────────────────────────────────────┤
│                    C++ API Layer                                  │
│  (Modern C++20 API — No COM)                                     │
│  Engine → CreateSource / CreateMixer / CreateEncoder / ...       │
├──────────────────────────────────────────────────────────────────┤
│                  Command Dispatcher + Worker Pool                 │
│  Command routing | Task scheduling | Pipeline orchestration      │
├──────────────────────────────────────────────────────────────────┤
│                  Plugin Host Layer                                │
│  (Dynamic Loading — dlopen / LoadLibrary)                        │
│  OpenMedia.PluginHost — NDI, DeckLink, WebRTC, SRT, FFmpeg...   │
│  VideoFilter | AudioFilter | Encoder | Decoder | Overlay | AI   │
├──────────────────────────────────────────────────────────────────┤
│                 Pipeline Graph Engine                             │
│  DAG-based pipeline: Source → Filter → Mixer → Encoder → Output │
│  (Đồ thị linh hoạt, không cố định linear)                        │
├──────────────────────────────────────────────────────────────────┤
│                  Core Services Layer                              │
│  Threading | Memory Pool | Clock Sync | Logging | Error Handling │
│  Config Management | Metrics | Telemetry                         │
├──────────────────────────────────────────────────────────────────┤
│                  Hardware Abstraction Layer (HAL)                 │
│  GPU (CUDA/NVENC/QuickSync/D3D11/D3D12/Vulkan/OpenCL)          │
│  Capture Devices (Blackmagic/AJA/Magewell/DirectShow/MF)       │
│  Audio Devices (WASAPI/ASIO)                                     │
└──────────────────────────────────────────────────────────────────┘
```

## 3. Modules chính

| Module | Vai trò | Tương đương Medialooks | Process |
|--------|---------|----------------------|---------|
| **OpenMedia.SDK** | Public SDK API (client-side) | MPlatform/MFormats | Client |
| **OpenMediaServer** | Media processing server process | (internal) | Server |
| **OpenMedia.IPC** | IPC transport (Named Pipes + Shared Memory) | (internal) | Cả 2 |
| **OpenMedia.Core** | Base classes, IMediaObject, Pipeline, Threading | MPlatform/MFormats | Server |
| **OpenMedia.IO** | File/Stream Sources, Network Input | MFReader, MFile, MLive | Server |
| **OpenMedia.Codecs** | Decoders/Encoders (H.264, H.265, AV1, AAC, Opus...) | MFWriter (encoder) | Server |
| **OpenMedia.Rendering** | Video rendering, Preview window | MRenderer, MPreview | Client |
| **OpenMedia.Mixer** | Multi-layer video/audio mixing, Transitions, Switcher | MMixer | Server |
| **OpenMedia.Audio** | Audio engine, Mixer, Meters (LUFS, VU, RMS) | Audio Engine | Server |
| **OpenMedia.Overlay** | Text, Logo, Ticker, Clock, Subtitle | HTML Overlay | Server |
| **OpenMedia.CG** | Character Generator engine | Character Generator | Server |
| **OpenMedia.Playlist** | Playlist management, Replay, Slow-motion | MPlaylist | Server |
| **OpenMedia.GPU** | GPU acceleration, Zero-copy transfers | GPU Pipeline | Server |
| **OpenMedia.WebRTC** | WebRTC protocol engine | WebRTC | Server |
| **OpenMedia.SRT** | SRT protocol engine | SRT | Server |
| **OpenMedia.NDI** | NDI protocol engine | NDI | Server |
| **OpenMedia.RTMP** | RTMP protocol engine | RTMP | Server |
| **OpenMedia.ST2110** | SMPTE ST 2110 engine | ST2110 | Server |
| **OpenMedia.Monitoring** | Metrics, Waveform, Vectorscope, Scopes | (built-in) | Server |
| **OpenMedia.PluginHost** | Plugin loading & isolation (riêng biệt) | Plugin | Server |
| **OpenMedia.PluginSDK** | Plugin interface & dynamic loading | Plugin | Server |
| **OpenMedia.CommandDispatcher** | Command routing, validation, execution | (internal) | Server |
| **OpenMedia.WorkerPool** | Thread pool, task scheduling, priority | (internal) | Server |

## 4. Pipeline Graph Model

Kiến trúc Exhand sử dụng **Pipeline Graph (DAG-based)** thay vì pipeline tuyến tính cố định. Các node kết nối theo dạng đồ thị có hướng không chu trình (DAG), cho phép cấu hình pipeline linh hoạt.

### 4.1 Linear Pipeline (cơ bản)

```
┌─────────┐   ┌─────────┐   ┌────────────┐   ┌──────────────┐
│ Source   │──▶│ Decoder │──▶│ FrameQueue │──▶│ VideoFilter  │
└─────────┘   └─────────┘   └────────────┘   └──────┬───────┘
                                                      │
                                                      ▼
┌─────────┐   ┌─────────┐   ┌────────────┐   ┌──────────────┐
│ Output  │◀──│ Encoder │◀──│  Overlay   │◀──│    Mixer     │
└─────────┘   └─────────┘   └────────────┘   └──────────────┘
```

### 4.2 DAG Pipeline Graph (nâng cao — Exhand)

```
                Source A ──┐
                           ├──▶ Decoder A ──┐
                Source B ──┘                 │
                                             ├──▶ Mixer ──▶ Overlay ──┬──▶ Encoder H264 ──▶ SRT Output
                Source C ──▶ Decoder C ──────┘                        │
                                                                       ├──▶ Encoder H265 ──▶ RTMP Output
                                                                       │
                                                                       └──▶ NDI Output (uncompressed)
```

### 4.3 Đặc điểm Pipeline Graph

- **DAG-based graph** — kết nối Source → Filter → Mixer → Encoder → Output linh hoạt, không cố định
- **Push/Pull hybrid model** — mỗi node tự chọn mode phù hợp
- **Thread-safe frame queue** giữa mỗi node
- **Zero-copy transfer** khi có GPU pipeline
- **Shared Memory + D3D11 Shared Textures** cho inter-process frame sharing
- **Clock synchronization** cho live workflows
- **Backpressure mechanism** tránh buffer overflow
- **Fan-out** — một node output có thể kết nối nhiều downstream nodes
- **Fan-in** — Mixer nhận nhiều input sources

## 4A. IPC & Shared Memory Architecture

### 4A.1 IPC Transport Layer

```
Client (OpenMedia.SDK.dll)          Server (OpenMediaServer.exe)
┌────────────────────┐              ┌────────────────────┐
│  IPC Client        │              │  IPC Server        │
│  ┌──────────────┐  │  Named Pipe  │  ┌──────────────┐  │
│  │ Command Pipe │──┼──────────────┼──│ Command Recv │  │
│  └──────────────┘  │              │  └──────────────┘  │
│  ┌──────────────┐  │  Named Pipe  │  ┌──────────────┐  │
│  │ Response Pipe│◀─┼──────────────┼──│ Response Send│  │
│  └──────────────┘  │              │  └──────────────┘  │
│  ┌──────────────┐  │ Shared Mem   │  ┌──────────────┐  │
│  │ Frame Buffer │──┼──zero-copy───┼──│ Frame Buffer │  │
│  │ (SharedMem)  │  │              │  │ (SharedMem)  │  │
│  └──────────────┘  │              │  └──────────────┘  │
│  ┌──────────────┐  │ D3D11 Share  │  ┌──────────────┐  │
│  │ GPU Textures │──┼──zero-copy───┼──│ GPU Textures │  │
│  │ (DXGI Share) │  │              │  │ (DXGI Share) │  │
│  └──────────────┘  │              │  └──────────────┘  │
└────────────────────┘              └────────────────────┘
```

### 4A.2 Command Dispatcher + Worker Pool

```
IPC Server receives command
         │
         ▼
┌──────────────────────┐
│  Command Dispatcher  │
│  ┌────────────────┐  │
│  │ Parse Command  │──┼──▶ Validate
│  └────────────────┘  │
│  ┌────────────────┐  │
│  │ Route to       │──┼──▶ Find target module
│  │ Worker         │  │
│  └────────────────┘  │
│  ┌────────────────┐  │
│  │ Execute        │──┼──▶ Submit to Worker Pool
│  └────────────────┘  │
└──────────────────────┘
         │
         ▼
┌──────────────────────┐
│    Worker Pool       │
│  Thread 1: Decoder   │
│  Thread 2: Mixer     │
│  Thread 3: Encoder   │
│  Thread N: ...       │
│  Priority Queue      │
│  Task Scheduling     │
└──────────────────────┘
```

## 5. Design Principles

1. **Process Isolation (Exhand)** — Tách UI (client) khỏi engine (server) để tăng ổn định
2. **Independent Architecture** — Thiết kế API và kiến trúc hoàn toàn độc lập
3. **Modern C++20** — Sử dụng concepts, coroutines, ranges, modules
4. **Plugin-First** — Mọi filter/encoder/decoder đều có thể là plugin, nạp qua PluginHost
5. **Zero-Copy IPC** — Shared Memory + D3D11 Shared Textures cho inter-process frame transfer
6. **Pipeline Graph** — DAG-based pipeline thay vì linear, cho phép fan-in/fan-out
7. **Command-Driven** — Command Dispatcher + Worker Pool điều phối toàn bộ pipeline
8. **Thread-Safe** — Lock-free queues, atomic operations
9. **No COM Dependency** — API hiện đại, không phụ thuộc COM
10. **Cross-platform Ready** — Kiến trúc cho phép mở rộng sang Linux trong tương lai

## 6. Công nghệ nền tảng

| Thành phần | Công nghệ |
|-----------|-----------|
| Ngôn ngữ core | C++20 (MSVC / Clang) |
| Build system | CMake 3.28+ |
| Managed wrapper | .NET 8/9 (C#) |
| Bridge | P/Invoke + C++/CLI |
| Media backend | FFmpeg (libavformat, libavcodec, libavutil, libswscale, libswresample) |
| GPU | CUDA Toolkit, Intel Media SDK, D3D11/D3D12 |
| Network | libsrt, librist, libwebrtc, librtmp |
| NDI | NDI SDK (Newtek/Vizrt) |
| Capture | Blackmagic DeckLink SDK, AJA NTV2 SDK, Magewell SDK |
| HTML Overlay | Chromium Embedded Framework (CEF) |
| Testing | Google Test (gtest), Catch2 |
| Documentation | Doxygen, Sphinx |
| Package Manager | vcpkg / Conan |
| CI/CD | GitHub Actions / Azure DevOps |

## 7. Supported Formats

### Input
| Category | Formats |
|----------|---------|
| File | MP4, MOV, MXF, AVI, MKV, Image, Image Sequence |
| Stream | RTMP, RTSP, SRT, RIST, HLS, MPEG-TS, UDP, TCP |
| Protocol | WebRTC, NDI |
| Device | SDI (Blackmagic, AJA), HDMI (Magewell, DeckLink), DirectShow, MediaFoundation, Webcam, Desktop Capture, Window Capture, Audio Device |

### Processing
| Category | Features |
|----------|----------|
| Video | Crop, Scale, Rotate, Mirror, Color Correction, HDR, 10-bit, LUT, Chroma Key, Luma Key, Alpha |
| Audio | Mixer, Delay, TimeShift, Replay, Slow Motion, LUFS, VU, RMS, Waveform, Vectorscope |
| Overlay | Logo, Ticker, Clock, Subtitle, CC608, CEA708, SCTE35, SCTE104 |
| Pipeline | Playlist, Mixer, Transition, Switcher, Frame Sync |

### Encoding
H.264, H.265, AV1, MPEG-2, JPEG2000, AAC, MP3, PCM, Opus

### Output
| Category | Formats |
|----------|---------|
| File | MP4, MOV, MXF, TS |
| Stream | RTMP, SRT, RIST, UDP, RTSP, HLS, DASH, CMAF |
| Protocol | WebRTC, NDI |
| Hardware | SDI, HDMI |
| Other | Snapshot, Shared Memory |
