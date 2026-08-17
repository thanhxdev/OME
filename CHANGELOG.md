# Changelog

All notable changes to the OpenMedia SDK will be documented in this file.

## [1.0.0] - 2026-07-30

### Added
- **Core Engine**: Lock-free frame queues, media pipeline builder, pre-allocated memory pool.
- **I/O Modules**: FFmpeg demuxer, RTSP/RTMP/HLS live sources, Blackmagic/AJA/Magewell device capture.
- **Processing**: Multi-layer video mixer with transitions (Cut, Dissolve, Wipe) and chroma key.
- **Audio**: Multi-input audio mixer, LUFS metering, resampler.
- **GPU Acceleration**: NVIDIA NVENC/NVDEC, Intel QuickSync, D3D11/Vulkan/OpenCL interop and zero-copy pipeline.
- **Protocols**: SRT, NDI|HX, WebRTC, RTMP push, ST 2110, ST 2022 (FEC).
- **.NET API**: Complete C# wrappers with Async/Await, Fluent API, and WinUI 3 / WPF interop.
- **Plugin System**: C++ and C# dynamic plugin loading (Filters, Codecs, Outputs).

### Changed
- Replaced legacy polling architecture with Exhand IPC & Command Layer.
- Upgraded C++ standard to C++23.

### Fixed
- Memory leaks in FrameQueue during high load.
- D3D11 Shared Texture synchronization issues across processes.
