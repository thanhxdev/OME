# Changelog

All notable changes to the OpenMedia SDK will be documented in this file.

## [1.0.0] - 2026-09-04

### Added
- **Core Engine**: Lock-free frame queues, media pipeline builder, pre-allocated memory pool.
- **I/O Modules**: FFmpeg demuxer, RTSP/RTMP/HLS live sources, Blackmagic/AJA/Magewell device capture.
- **Processing**: Multi-layer video mixer with transitions (Cut, Dissolve, Wipe) and chroma key.
- **Audio**: Multi-input audio mixer, LUFS metering, resampler.
- **GPU Acceleration**: NVIDIA NVENC/NVDEC, Intel QuickSync, D3D11/Vulkan/OpenCL interop and zero-copy pipeline.
- **Protocols**: Production SRT caller/listener, NDI|HX, RTMP push, ST 2110/2022 framework.
- **.NET API**: Complete C# wrappers (.NET 10) with Async/Await, Fluent API, and WinUI 3 / WPF interop (`OpenMedia.Core.NET`, `OpenMedia.Platform`, `OpenMedia.NDI.NET`).
- **Platform & Samples**: `SRT_ENCODE`, `SRT_DECODE` (multi-stream 4-channel sync, NTP alignment, audio monitoring), `OMEPlatform_Play`, `OMEPlatform_AVDelay`.
- **Plugin System**: C++ and C# dynamic plugin loading (Filters, Codecs, Outputs).

### Changed
- Replaced legacy polling architecture with Exhand IPC & Command Layer.
- Upgraded C++ standard to C++23 and .NET framework to .NET 10.
- Standardized CMake configuration across all submodules to CMake 3.28+.
- Centralized .NET versioning (`1.0.0.0`) via root `Directory.Build.props`.
- Disabled experimental stub modules (`OME_ENABLE_WEBRTC`) by default in production builds.

### Fixed
- Memory leaks in FrameQueue during high load.
- D3D11 Shared Texture synchronization issues across processes.
- Solution project references: added `SRT_DECODE` and platform samples to `OpenMedia.NET.slnx`.
