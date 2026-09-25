# Changelog

All notable changes to the OpenMedia SDK will be documented in this file.

## [2.0.0] - 2026-09-24

### Added
- **SMPTE ST 2022-7 Hitless Merging**: Differential Delay Ring Buffer (10ms - 500ms delay skew compensation) and packet-by-packet RTP deduplication with zero frame drop.
- **SMPTE ST 2022-1/2 FEC**: 2D-FEC matrix (Row/Column XOR) for forward error correction and packet loss recovery on IP networks.
- **SMPTE ST 2110-20/30**: Essence pipeline with RFC 4175 uncompressed video encapsulation and RFC 3190 24-bit Linear PCM audio packetization.
- **Precision PTP Clock**: IEEE 1588-2008 / SMPTE ST 2059 synchronization engine with nanosecond timestamp tagging on `MediaFrame`.
- **AMWA NMOS IS-04 & IS-05**: Node registration/heartbeat discovery and connection management routing.
- **SRT Multi-Interface Bonding**: Multi-path socket binding supporting simultaneous Fiber Optic and 4G/5G Cellular Modem paths with automated failover and link quality telemetry.
- **Native WebRTC Engine**: C++ WebRTC integration via `PeerConnectionFactory`, audio/video tracks, and WHIP/WHEP low-latency (<100ms) browser streaming.
- **VSF TR-06 RIST**: Simple Profile (ARQ loss recovery) and Main Profile (DTLS encryption and bonding).
- **OTT Delivery Outputs**: Production HLS segmenter (Live sliding window & Event mode `.m3u8`), MPEG-DASH ABR manifest generator (`.mpd`), and Chunked Low-Latency CMAF.
- **Native Hardware Capture Drivers**: Blackmagic DeckLink SDI/HDMI input/output with zero-copy YUV/BGRA conversion, AJA NTV2 DMA capture, and Magewell MWCapture SDK engine.
- **GPU Acceleration**: D3D11 Video Processor & CUDA bilinear scaler, HLSL/CUDA real-time Luma and Chroma keying.
- **Subtitle Renderer**: SRT and WebVTT subtitle parser with PTS-synchronized text overlay.
- **Intel QuickSync Video**: oneVPL / MediaSDK hardware encoder integration (`MFXVideoENCODE`).
- **WinUI 3 Video View**: Full DirectX 11 `SwapChainPanel` preview control via DXGI shared texture handle.
- **Plugin Fault Isolation**: Dynamic library loader with SEH crash containment.
- **Automated Verification**: Comprehensive unit & integration test suites for hitless merging, 2D-FEC, SRT bonding, and 4K GPU scaling.

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
