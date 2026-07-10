# OpenMedia SDK — Master Task List

**Version:** 1.0  
**Date:** July 2026  
**Tổng tasks:** 312  
**Cập nhật lần cuối:** 2026-07-10

---

> **Ký hiệu:**
> - `[ ]` — Chưa bắt đầu
> - `[/]` — Đang thực hiện
> - `[x]` — Hoàn thành
> - 🔴 Critical | 🟡 High | 🟢 Medium | ⚪ Low

---

## Phase 1: Project Setup & Core Architecture 🔴

**Ước tính:** 2–3 tuần | **Tiên quyết:** Không

### 1.1 Repository & Toolchain Setup
- [x] 1.1.1 Khởi tạo Git repository
- [x] 1.1.2 Tạo `.gitignore` (C++, .NET, build artifacts, IDE files, .env.*)
- [x] 1.1.3 Tạo `.clang-format` (Google-based, indent 4, column limit 120)
- [x] 1.1.4 Tạo `.clang-tidy` (bugprone, modernize, performance, readability)
- [x] 1.1.5 Tạo `.editorconfig`
- [x] 1.1.6 Tạo `LICENSE` file
- [x] 1.1.7 Tạo `README.md` — project overview, build instructions, requirements

### 1.2 Build System (CMake)
- [x] 1.2.1 Root `CMakeLists.txt` — C++20, vcpkg integration, module includes
- [x] 1.2.2 `cmake/CompilerSettings.cmake` — MSVC/Clang flags, warnings, sanitizers
- [x] 1.2.3 `cmake/Dependencies.cmake` — find_package, third_party paths
- [x] 1.2.4 `cmake/Platform.cmake` — WIN32/UNIX defines, platform libs
- [x] 1.2.5 `cmake/Version.cmake` — Git tag version extraction
- [x] 1.2.6 `cmake/EnvironmentConfig.cmake` — load `.env.demo` / `.env.production`
- [x] 1.2.7 `cmake/OpenMediaConfig.h.in` — generated config header template
- [x] 1.2.8 `cmake/Toolchain/Windows-MSVC.cmake`
- [ ] 1.2.9 Verify full CMake configure + build cycle (empty project)

### 1.3 Environment Configuration
- [x] 1.3.1 Tạo `.env.shared` — project info, media defaults, pipeline defaults
- [x] 1.3.2 Tạo `.env.demo` — debug logging, watermark, no license, sanitizers
- [x] 1.3.3 Tạo `.env.production` — release build, license check, telemetry, LTO
- [ ] 1.3.4 Verify CMake loads correct env file based on `-DOME_ENV_TAG`
- [ ] 1.3.5 Generated `OpenMediaConfig.h` reflects correct env values

### 1.4 Dependency Setup
- [x] 1.4.1 Tạo `vcpkg.json` manifest — ffmpeg, spdlog, nlohmann-json, gtest, fmt, concurrentqueue
- [ ] 1.4.2 Clone vcpkg vào `third_party/vcpkg/`
- [ ] 1.4.3 Bootstrap vcpkg, verify `vcpkg install --triplet x64-windows`
- [x] 1.4.4 Tạo `third_party/README.md` — license notes, download instructions
- [ ] 1.4.5 Download & setup FFmpeg pre-built → `third_party/ffmpeg/`
- [ ] 1.4.6 Verify `find_package(FFmpeg)` works in CMake

### 1.5 Directory Structure Scaffold
- [x] 1.5.1 Tạo toàn bộ thư mục `src/` theo cấu trúc (core, io, codecs, rendering, mixer, audio, overlay, cg, playlist, gpu, protocols/*, monitoring, plugin_sdk)
- [x] 1.5.2 Tạo thư mục `wrappers/` cho .NET projects
- [x] 1.5.3 Tạo thư mục `plugins/examples/`, `plugins/builtin/`
- [x] 1.5.4 Tạo thư mục `samples/cpp/`, `samples/dotnet/`
- [x] 1.5.5 Tạo thư mục `tests/unit/`, `tests/integration/`, `tests/benchmark/`
- [x] 1.5.6 Tạo thư mục `tools/scripts/`, `tools/ci/`
- [x] 1.5.7 Tạo thư mục `dist/demo/`, `dist/production/`

### 1.6 Core Engine — OpenMedia.Core
- [x] 1.6.1 `Types.h` — PixelFormat, SampleFormat, ColorSpace, TransferFunction enums
- [x] 1.6.2 `ErrorCodes.h` — ErrorCode enum, Error struct, Result<T> template
- [x] 1.6.3 `Logger.h/.cpp` — spdlog wrapper, structured logging, log levels
- [x] 1.6.4 `Config.h/.cpp` — JSON-based config loader, env var reader
- [x] 1.6.5 `MemoryPool.h/.cpp` — pre-allocated memory pool, slab allocator
- [x] 1.6.6 `MediaMetadata.h` — metadata key-value store
- [x] 1.6.7 `MediaFrame.h/.cpp` — unified video+audio+metadata frame container
  - [x] Video data planes, line sizes
  - [x] Audio data per channel
  - [x] GPU texture handle
  - [x] Allocate / Clone / ConvertTo
- [x] 1.6.8 `IMediaObject.h` — pure virtual interface (Connect, Start, Stop, PullFrame, PushFrame)
- [x] 1.6.9 `FrameQueue.h/.cpp` — lock-free concurrent queue (moodycamel::ConcurrentQueue wrapper)
  - [x] Push/Pop with timeout
  - [x] Backpressure (IsFull/IsEmpty)
  - [x] Statistics (drops, latency)
- [x] 1.6.10 `ClockSync.h/.cpp` — media clock, PTS tracking, sync to wall clock
- [x] 1.6.11 `MediaPipeline.h/.cpp` — pipeline builder pattern
  - [x] SetSource, AddFilter, SetMixer, SetEncoder, AddOutput
  - [x] Build (validate graph), Start, Stop, Pause, Resume
  - [x] State machine (Idle → Building → Ready → Running → Paused → Stopped)
  - [x] Error callbacks, state change callbacks
- [x] 1.6.12 `Engine.h/.cpp` — main factory class
  - [x] Create() factory method
  - [x] CreatePipeline(), CreateFileSource(), CreateMixer(), etc.
  - [ ] EnumerateDevices()
  - [x] Run/Stop lifecycle
- [x] 1.6.13 `src/core/CMakeLists.txt` — library target, include dirs, link deps
- [x] 1.6.14 Unit tests: test_types, test_error_codes, test_logger, test_config
- [x] 1.6.15 Unit tests: test_memory_pool, test_media_frame, test_frame_queue
- [x] 1.6.16 Unit tests: test_clock_sync, test_media_pipeline, test_engine

### 1.7 Build & CI Scripts
- [x] 1.7.1 `tools/scripts/build.ps1` — parameterized build (env, config, tests, dotnet)
- [x] 1.7.2 `tools/scripts/setup_env.ps1` — environment setup, SDK downloads
- [x] 1.7.3 `tools/scripts/generate_version.py` — version header generator
- [x] 1.7.4 `tools/ci/github-actions.yml` — CI pipeline skeleton (build + test matrix)
- [ ] 1.7.5 Verify: `build.ps1 -Environment demo` succeeds end-to-end
- [ ] 1.7.6 Verify: `build.ps1 -Environment production` succeeds end-to-end

### ✅ Phase 1 Milestone
> Build project thành công cho cả demo/production, core engine khởi tạo, unit tests pass.

---

## Phase 2: Input/Output Modules 🔴

**Ước tính:** 3–4 tuần | **Tiên quyết:** Phase 1

### 2.1 File Sources — OpenMedia.IO
- [ ] 2.1.1 FFmpeg wrapper utilities (AVFormatContext helpers, error mapping)
- [ ] 2.1.2 `MediaReader.h/.cpp` — generic demuxer (open, read packet, seek, get stream info)
- [ ] 2.1.3 `FileSource.h/.cpp` — implements IMediaObject, wraps MediaReader
  - [ ] Support: MP4, MOV, MXF, AVI, MKV
  - [ ] Seeking (time-based, frame-based)
  - [ ] Metadata extraction (duration, codecs, resolution, bitrate)
  - [ ] EOF handling, loop mode
- [ ] 2.1.4 Image source (PNG, JPEG, BMP)
- [ ] 2.1.5 Image Sequence source (frame_%04d.png pattern)
- [ ] 2.1.6 `src/io/CMakeLists.txt` — link FFmpeg libs
- [ ] 2.1.7 Unit tests: test_media_reader (open, read frames, seek)
- [ ] 2.1.8 Unit tests: test_file_source (lifecycle, metadata, seeking)

### 2.2 Live Sources
- [ ] 2.2.1 `NetworkSource.h/.cpp` — protocol abstraction (URL parsing, protocol detection)
- [ ] 2.2.2 `LiveSource.h/.cpp` — live stream source via FFmpeg
  - [ ] RTMP input
  - [ ] RTSP input
  - [ ] HLS input
  - [ ] MPEG-TS over UDP/TCP input
- [ ] 2.2.3 Auto-reconnect logic (configurable attempts, delay)
- [ ] 2.2.4 Jitter buffer / buffering management
- [ ] 2.2.5 Unit tests: test_live_source (mock streams, reconnect)

### 2.3 Device Sources
- [ ] 2.3.1 `DeviceInfo.h` — device descriptor (name, type, capabilities, index)
- [ ] 2.3.2 `DeviceFactory.h/.cpp` — unified factory, enumerate all device types
- [ ] 2.3.3 `DeviceSource.h/.cpp` — base device capture class
- [ ] 2.3.4 Blackmagic DeckLink integration
  - [ ] Download & setup DeckLink SDK → `third_party/decklink_sdk/`
  - [ ] `DeckLinkSource.h/.cpp` — IDeckLinkInput capture, format negotiation
  - [ ] SDI input modes (1080i, 1080p, 2160p)
- [ ] 2.3.5 AJA NTV2 integration
  - [ ] Clone AJA NTV2 SDK → `third_party/aja_sdk/`
  - [ ] `AJASource.h/.cpp` — NTV2 capture
- [ ] 2.3.6 Magewell integration
  - [ ] Download Magewell SDK → `third_party/magewell_sdk/`
  - [ ] `MagewellSource.h/.cpp`
- [ ] 2.3.7 DirectShow capture wrapper
- [ ] 2.3.8 MediaFoundation capture wrapper
- [ ] 2.3.9 Webcam source (via DirectShow/MF)
- [ ] 2.3.10 Audio Device source (WASAPI)
- [ ] 2.3.11 `DesktopCapture.h/.cpp` — DXGI Desktop Duplication API
- [ ] 2.3.12 `WindowCapture.h/.cpp` — specific window capture
- [ ] 2.3.13 Unit tests: test_device_factory, test_desktop_capture

### 2.4 Codecs — OpenMedia.Codecs
- [ ] 2.4.1 `IDecoder.h` — decoder interface
- [ ] 2.4.2 `IEncoder.h` — encoder interface (Open, EncodeFrame, Flush, Close)
- [ ] 2.4.3 `CodecFactory.h/.cpp` — create encoder/decoder by codec name
- [ ] 2.4.4 `H264Decoder.h/.cpp` — FFmpeg avcodec wrapper
- [ ] 2.4.5 `H264Encoder.h/.cpp` — FFmpeg x264/x265 wrapper
  - [ ] Presets: ultrafast → veryslow
  - [ ] Profile/level configuration
  - [ ] Rate control: CBR, VBR, CQ
- [ ] 2.4.6 `H265Decoder.h/.cpp`
- [ ] 2.4.7 `H265Encoder.h/.cpp`
- [ ] 2.4.8 `AV1Encoder.h/.cpp` — libaom wrapper
- [ ] 2.4.9 `AACEncoder.h/.cpp` — fdk-aac wrapper
- [ ] 2.4.10 `OpusEncoder.h/.cpp` — libopus wrapper
- [ ] 2.4.11 `src/codecs/CMakeLists.txt`
- [ ] 2.4.12 Unit tests: test_h264_encoder, test_h264_decoder
- [ ] 2.4.13 Unit tests: test_h265_encoder, test_aac_encoder, test_codec_factory

### 2.5 File Output
- [ ] 2.5.1 `FileOutput.h/.cpp` — mux encoded frames to file container
  - [ ] MP4 output (moov atom placement)
  - [ ] MOV output
  - [ ] MXF output
  - [ ] MPEG-TS output
- [ ] 2.5.2 `SnapshotOutput.h/.cpp` — capture single frame to image file (PNG/JPEG)
- [ ] 2.5.3 Unit tests: test_file_output

### 2.6 Integration Tests
- [ ] 2.6.1 Test: FileSource → H264Decoder → H264Encoder → FileOutput (transcode pipeline)
- [ ] 2.6.2 Test: FileSource → SnapshotOutput (thumbnail extraction)
- [ ] 2.6.3 Test: Multiple format inputs (MP4, MKV, MOV)
- [ ] 2.6.4 Performance baseline: decode + encode throughput 1080p

### ✅ Phase 2 Milestone
> Đọc file media, capture từ device, encode và ghi ra file output thành công.

---

## Phase 3: Processing Pipeline 🟡

**Ước tính:** 4–6 tuần | **Tiên quyết:** Phase 2

### 3.1 Rendering — OpenMedia.Rendering
- [ ] 3.1.1 `Renderer.h/.cpp` — base renderer interface
- [ ] 3.1.2 `D3D11Renderer.h/.cpp` — Direct3D 11 video rendering
- [ ] 3.1.3 `Preview.h/.cpp` — preview window (HWND-based rendering)
- [ ] 3.1.4 `VulkanRenderer.h/.cpp` — Vulkan rendering backend (optional)
- [ ] 3.1.5 `src/rendering/CMakeLists.txt`
- [ ] 3.1.6 Unit tests: test_renderer

### 3.2 Mixer — OpenMedia.Mixer
- [ ] 3.2.1 `MixerLayer.h/.cpp` — single layer (position, size, opacity, visibility)
- [ ] 3.2.2 `Mixer.h/.cpp` — multi-layer video composition engine
  - [ ] AddInput/RemoveInput per layer
  - [ ] Layer z-order management
  - [ ] Output resolution & framerate config
  - [ ] Background color/image
- [ ] 3.2.3 `Transition.h/.cpp` — transition engine
  - [ ] Cut, Dissolve (crossfade)
  - [ ] Wipe (left, right, up, down, diagonal)
  - [ ] Push, Slide
  - [ ] Custom transition duration
- [ ] 3.2.4 `Switcher.h/.cpp` — live switching (Take, Auto, preview/program)
- [ ] 3.2.5 `ChromaKey.h/.cpp` — chroma key (green screen, blue screen, custom color)
- [ ] 3.2.6 Luma key
- [ ] 3.2.7 Video filters: Crop, Scale, Rotate, Mirror
- [ ] 3.2.8 Color Correction — brightness, contrast, saturation, hue
- [ ] 3.2.9 HDR support — PQ (HDR10), HLG transfer functions
- [ ] 3.2.10 10-bit pixel format pipeline
- [ ] 3.2.11 LUT application (3D LUT, .cube files)
- [ ] 3.2.12 Audio mixing within mixer (per-layer audio gain, mute)
- [ ] 3.2.13 `src/mixer/CMakeLists.txt`
- [ ] 3.2.14 Unit tests: test_mixer, test_mixer_layer, test_transition, test_chroma_key

### 3.3 Overlay — OpenMedia.Overlay
- [ ] 3.3.1 `OverlayEngine.h/.cpp` — overlay manager (add, remove, reorder overlays)
- [ ] 3.3.2 `TextOverlay.h/.cpp` — multi-font, multi-style text rendering (DirectWrite/FreeType)
- [ ] 3.3.3 `LogoOverlay.h/.cpp` — PNG logo with alpha channel, position, scale
- [ ] 3.3.4 `TickerOverlay.h/.cpp` — scrolling text (left, right, up, down)
- [ ] 3.3.5 `ClockOverlay.h/.cpp` — real-time clock display (configurable format)
- [ ] 3.3.6 `SubtitleRenderer.h/.cpp` — CC608, CEA708 subtitle rendering
- [ ] 3.3.7 SCTE35/SCTE104 marker detection & insertion
- [ ] 3.3.8 `HTMLRenderer.h/.cpp` — CEF integration for HTML overlay
  - [ ] Download & setup CEF → `third_party/cef/`
  - [ ] Off-screen rendering to texture
  - [ ] JavaScript bidirectional communication
- [ ] 3.3.9 `src/overlay/CMakeLists.txt`
- [ ] 3.3.10 Unit tests: test_text_overlay, test_logo_overlay, test_ticker_overlay

### 3.4 CG Engine — OpenMedia.CG
- [ ] 3.4.1 `CGTemplate.h/.cpp` — CG template format (JSON-based definition)
- [ ] 3.4.2 `CGEngine.h/.cpp` — template loading, field binding, render to frame
- [ ] 3.4.3 `CGRenderer.h/.cpp` — real-time rendering with animation support
- [ ] 3.4.4 Data binding — live data feeds (file, API, manual)
- [ ] 3.4.5 `src/cg/CMakeLists.txt`
- [ ] 3.4.6 Unit tests: test_cg_engine, test_cg_template

### 3.5 Audio Engine — OpenMedia.Audio
- [ ] 3.5.1 `AudioEngine.h/.cpp` — audio processing pipeline coordinator
- [ ] 3.5.2 `AudioMixer.h/.cpp` — multi-input audio mixer
  - [ ] Per-channel gain, pan, mute, solo
  - [ ] Multi-channel support (mono, stereo, 5.1, 7.1)
- [ ] 3.5.3 `Resampler.h/.cpp` — sample rate conversion (libswresample wrapper)
- [ ] 3.5.4 `ChannelMapper.h/.cpp` — channel layout remapping
- [ ] 3.5.5 Delay effect (configurable ms)
- [ ] 3.5.6 TimeShift (audio buffer for delayed playback)
- [ ] 3.5.7 `AudioMeter.h/.cpp` — real-time audio metering
  - [ ] LUFS (EBU R128)
  - [ ] VU meter
  - [ ] RMS level
  - [ ] Peak level
- [ ] 3.5.8 `Waveform.h/.cpp` — waveform display data generator
- [ ] 3.5.9 `Vectorscope.h/.cpp` — stereo vectorscope data generator
- [ ] 3.5.10 `src/audio/CMakeLists.txt`
- [ ] 3.5.11 Unit tests: test_audio_mixer, test_resampler, test_audio_meter

### 3.6 Playlist — OpenMedia.Playlist
- [ ] 3.6.1 `PlaylistItem.h/.cpp` — single item (source, in/out points, transition)
- [ ] 3.6.2 `Playlist.h/.cpp` — playlist management
  - [ ] Add, remove, insert, reorder items
  - [ ] Loop mode, shuffle
  - [ ] Auto-advance with transition
  - [ ] Current/next item tracking
- [ ] 3.6.3 `ReplayEngine.h/.cpp` — instant replay from ring buffer
- [ ] 3.6.4 `SlowMotion.h/.cpp` — variable speed playback (0.1x → 4x)
- [ ] 3.6.5 Per-item metadata processing
- [ ] 3.6.6 `src/playlist/CMakeLists.txt`
- [ ] 3.6.7 Unit tests: test_playlist, test_replay_engine, test_slow_motion

### 3.7 Integration Tests
- [ ] 3.7.1 Test: FileSource → Mixer(2 layers) → H264Encoder → FileOutput
- [ ] 3.7.2 Test: FileSource → OverlayEngine(logo + text) → Encoder → FileOutput
- [ ] 3.7.3 Test: Playlist(3 items + transitions) → Encoder → FileOutput
- [ ] 3.7.4 Test: AudioMixer(3 inputs) → AudioMeter verification
- [ ] 3.7.5 Full pipeline: Source → Decode → Filter → Mix → Overlay → Encode → Output

### ✅ Phase 3 Milestone
> Full processing pipeline hoạt động end-to-end.

---

## Phase 4: GPU Acceleration 🟡

**Ước tính:** 3–4 tuần | **Tiên quyết:** Phase 2 (có thể song song Phase 3)

### 4.1 GPU Framework
- [ ] 4.1.1 `GPUContext.h/.cpp` — unified GPU context abstraction (create, destroy, device query)
- [ ] 4.1.2 `GPUFrame.h/.cpp` — GPU-resident frame (texture handle, format, dimensions)
- [ ] 4.1.3 CPU → GPU upload (zero-copy when possible, staging buffer fallback)
- [ ] 4.1.4 GPU → CPU download (readback)
- [ ] 4.1.5 GPU → GPU transfer (texture-to-texture copy)
- [ ] 4.1.6 `src/gpu/CMakeLists.txt`

### 4.2 NVIDIA
- [ ] 4.2.1 Download NVIDIA Video Codec SDK → `third_party/nvidia_codec_sdk/`
- [ ] 4.2.2 `CUDAContext.h/.cpp` — CUDA device management, context creation
- [ ] 4.2.3 NVDEC integration — hardware H.264/H.265 decode
- [ ] 4.2.4 NVENC integration — hardware H.264/H.265/AV1 encode
- [ ] 4.2.5 CUDA-based pixel format conversion (NV12 ↔ BGRA, etc.)
- [ ] 4.2.6 Unit tests: test_cuda_context, test_nvenc, test_nvdec

### 4.3 Intel
- [ ] 4.3.1 Download Intel oneVPL → `third_party/intel_onevpl/`
- [ ] 4.3.2 Intel QuickSync decode integration
- [ ] 4.3.3 Intel QuickSync encode integration
- [ ] 4.3.4 DXVA2 hardware acceleration fallback
- [ ] 4.3.5 Unit tests: test_quicksync

### 4.4 Graphics API Interop
- [ ] 4.4.1 `D3D11Context.h/.cpp` — D3D11 device, texture creation, interop
- [ ] 4.4.2 `D3D12Context.h/.cpp` — D3D12 device, fence sync
- [ ] 4.4.3 `VulkanContext.h/.cpp` — Vulkan compute pipeline for filters
- [ ] 4.4.4 `OpenCLContext.h/.cpp` — OpenCL fallback
- [ ] 4.4.5 GPU filter pipeline — apply video filters on GPU
- [ ] 4.4.6 Unit tests: test_d3d11_context, test_gpu_frame, test_gpu_transfer

### 4.5 Benchmarks
- [ ] 4.5.1 Benchmark: CPU vs GPU encode 1080p (fps comparison)
- [ ] 4.5.2 Benchmark: CPU vs GPU decode 1080p
- [ ] 4.5.3 Benchmark: CPU↔GPU transfer latency
- [ ] 4.5.4 Benchmark: GPU filter pipeline throughput

### ✅ Phase 4 Milestone
> Hardware-accelerated encode/decode, GPU-based filters, zero-copy pipeline.

---

## Phase 5: Protocol Engines 🟡

**Ước tính:** 4–5 tuần | **Tiên quyết:** Phase 2 (có thể song song Phase 3/4)

### 5.1 SRT Engine — OpenMedia.SRT
- [ ] 5.1.1 `SRTEngine.h/.cpp` — SRT session management
- [ ] 5.1.2 `SRTSource.h/.cpp` — SRT listener mode, SRT caller mode
- [ ] 5.1.3 `SRTOutput.h/.cpp` — SRT push output
- [ ] 5.1.4 Encryption (AES-128, AES-256), passphrase
- [ ] 5.1.5 Latency tuning, bandwidth management
- [ ] 5.1.6 Statistics & metrics (RTT, loss, bitrate, retransmit)
- [ ] 5.1.7 `src/protocols/srt/CMakeLists.txt` — link libsrt
- [ ] 5.1.8 Unit tests: test_srt_engine

### 5.2 NDI Engine — OpenMedia.NDI
- [ ] 5.2.1 Download NDI SDK → `third_party/ndi_sdk/` (require license agreement)
- [ ] 5.2.2 `NDIEngine.h/.cpp` — NDI runtime initialization
- [ ] 5.2.3 `NDISource.h/.cpp` — NDI source discovery, receive frames
- [ ] 5.2.4 `NDIOutput.h/.cpp` — NDI send (output)
- [ ] 5.2.5 NDI|HX support (compressed NDI)
- [ ] 5.2.6 Metadata exchange (XML metadata, tally)
- [ ] 5.2.7 `src/protocols/ndi/CMakeLists.txt` — link NDI SDK
- [ ] 5.2.8 Unit tests: test_ndi_engine

### 5.3 WebRTC Engine — OpenMedia.WebRTC
- [ ] 5.3.1 WebRTC library integration (libwebrtc or mediasoup)
- [ ] 5.3.2 `WebRTCEngine.h/.cpp` — peer connection management
- [ ] 5.3.3 `WebRTCSource.h/.cpp` — receive WebRTC stream
- [ ] 5.3.4 `WebRTCOutput.h/.cpp` — broadcast via WebRTC
- [ ] 5.3.5 Signaling server integration (WebSocket-based)
- [ ] 5.3.6 WHIP/WHEP support (standards-based ingest/egress)
- [ ] 5.3.7 `src/protocols/webrtc/CMakeLists.txt`
- [ ] 5.3.8 Unit tests: test_webrtc_engine

### 5.4 RTMP Engine — OpenMedia.RTMP
- [ ] 5.4.1 `RTMPEngine.h/.cpp` — RTMP session management
- [ ] 5.4.2 `RTMPSource.h/.cpp` — RTMP receive/pull
- [ ] 5.4.3 `RTMPOutput.h/.cpp` — RTMP push (YouTube, Facebook, Twitch, custom)
- [ ] 5.4.4 RTMPS (TLS) support
- [ ] 5.4.5 `src/protocols/rtmp/CMakeLists.txt`
- [ ] 5.4.6 Unit tests: test_rtmp_engine

### 5.5 ST 2110 Engine — OpenMedia.ST2110
- [ ] 5.5.1 `ST2110Engine.h/.cpp` — SMPTE ST 2110 session management
- [ ] 5.5.2 `ST2110Source.h/.cpp` — essence stream receive (video, audio, ancillary)
- [ ] 5.5.3 `ST2110Output.h/.cpp` — essence stream output
- [ ] 5.5.4 PTP clock synchronization
- [ ] 5.5.5 NMOS IS-04/IS-05 integration (discovery, connection management)
- [ ] 5.5.6 `src/protocols/st2110/CMakeLists.txt`
- [ ] 5.5.7 Unit tests: test_st2110_engine

### 5.6 Additional Output Formats
- [ ] 5.6.1 HLS output — segmented TS/fMP4, playlist generation (.m3u8)
- [ ] 5.6.2 DASH output — segmented, MPD generation
- [ ] 5.6.3 CMAF output — low-latency CMAF segments
- [ ] 5.6.4 RIST output — librist integration
- [ ] 5.6.5 Shared Memory output — inter-process frame sharing
- [ ] 5.6.6 SDI output (via DeckLink) — DeckLinkOutput wrapper
- [ ] 5.6.7 HDMI output (via DeckLink/Magewell)

### 5.7 Monitoring — OpenMedia.Monitoring
- [ ] 5.7.1 `Metrics.h/.cpp` — pipeline metrics collection (fps, bitrate, drops, latency)
- [ ] 5.7.2 `Waveform.h/.cpp` — video waveform scope data
- [ ] 5.7.3 `Vectorscope.h/.cpp` — video vectorscope data
- [ ] 5.7.4 `HealthCheck.h/.cpp` — system health (CPU, GPU, memory, temperature)
- [ ] 5.7.5 `src/monitoring/CMakeLists.txt`
- [ ] 5.7.6 Unit tests: test_metrics, test_waveform, test_vectorscope

### 5.8 Integration Tests
- [ ] 5.8.1 Test: FileSource → SRTOutput (SRT streaming)
- [ ] 5.8.2 Test: SRTSource → FileOutput (SRT receive + record)
- [ ] 5.8.3 Test: NDISource → NDIOutput (NDI passthrough)
- [ ] 5.8.4 Test: FileSource → RTMPOutput
- [ ] 5.8.5 Test: Multi-output (same source → SRT + RTMP + File simultaneously)

### ✅ Phase 5 Milestone
> Tất cả protocol engines hoạt động, stream đa nền tảng.

---

## Phase 6: .NET Wrapper & API 🟡

**Ước tính:** 3–4 tuần | **Tiên quyết:** Phase 3, Phase 5

### 6.1 Native Bridge Layer
- [ ] 6.1.1 C export layer — flat C API cho P/Invoke
  - [ ] `openmedia_c_api.h` — all exported functions
  - [ ] Engine lifecycle (create, destroy, run, stop)
  - [ ] Pipeline management
  - [ ] Source/Encoder/Output creation
  - [ ] Frame access
- [ ] 6.1.2 Error marshalling (ErrorCode → managed exception mapping)
- [ ] 6.1.3 Memory management policy — pin/unpin, ref counting for callbacks
- [ ] 6.1.4 String marshalling (UTF-8 ↔ .NET string)
- [ ] 6.1.5 Callback marshalling (native → managed delegate invocation)

### 6.2 .NET Solution Setup
- [ ] 6.2.1 Create `wrappers/OpenMedia.NET.sln`
- [ ] 6.2.2 `OpenMedia.Core.NET.csproj` — target net8.0;net9.0
- [ ] 6.2.3 `NativeBridge.cs` — P/Invoke declarations for all C exports
- [ ] 6.2.4 Native DLL loading strategy (runtime/platform detection)

### 6.3 .NET Wrapper Libraries
- [ ] 6.3.1 `OpenMedia.Core.NET` — MediaPlayer, MediaPipeline, MediaFrame, Engine, Config
- [ ] 6.3.2 `OpenMedia.IO.NET` — FileSource, LiveSource, DeviceSource
- [ ] 6.3.3 `OpenMedia.Mixer.NET` — Mixer, MixerLayer, Transitions
- [ ] 6.3.4 `OpenMedia.WebRTC.NET` — WebRTCEngine
- [ ] 6.3.5 `OpenMedia.SRT.NET` — SRTEngine, SRTSource, SRTOutput
- [ ] 6.3.6 `OpenMedia.NDI.NET` — NDIEngine, NDISource, NDIOutput
- [ ] 6.3.7 `OpenMedia.Playlist.NET` — Playlist, PlaylistItem
- [ ] 6.3.8 `OpenMedia.CG.NET` — CGEngine, CGTemplate

### 6.4 High-Level .NET API
- [ ] 6.4.1 Fluent API — chainable pipeline builder
- [ ] 6.4.2 Event-based callbacks — OnFrameReady, OnError, OnStateChanged, OnMetadata
- [ ] 6.4.3 Async/await support — StartAsync, StopAsync, OpenAsync
- [ ] 6.4.4 IDisposable pattern — deterministic native resource cleanup
- [ ] 6.4.5 WPF integration helper — D3DImage interop for video preview
- [ ] 6.4.6 WinUI integration helper — SwapChainPanel interop

### 6.5 .NET Tests
- [ ] 6.5.1 xUnit test project setup
- [ ] 6.5.2 Unit tests: Engine lifecycle, Pipeline construction
- [ ] 6.5.3 Unit tests: FileSource open/play, Encoder config
- [ ] 6.5.4 Integration tests: Full pipeline through .NET API
- [ ] 6.5.5 Verify: `dotnet build OpenMedia.NET.sln` succeeds
- [ ] 6.5.6 Verify: `dotnet test` all tests pass

### ✅ Phase 6 Milestone
> .NET apps sử dụng toàn bộ SDK features thông qua managed API.

---

## Phase 7: Plugin System 🟢

**Ước tính:** 2–3 tuần | **Tiên quyết:** Phase 3

### 7.1 Plugin Infrastructure
- [ ] 7.1.1 `IPlugin.h` — base interface, PluginInfo, capability flags, entry macros
- [ ] 7.1.2 `PluginManager.h/.cpp` — LoadLibrary/dlopen, entry point resolution
  - [ ] LoadPluginsFromDirectory
  - [ ] LoadPlugin (single DLL)
  - [ ] UnloadPlugin, UnloadAll
  - [ ] Hot-reload support (demo mode)
- [ ] 7.1.3 `PluginRegistry.h/.cpp` — registration, query by capability, versioning
- [ ] 7.1.4 Plugin API version compatibility check
- [ ] 7.1.5 Plugin sandbox (crash isolation) — optional
- [ ] 7.1.6 `src/plugin_sdk/CMakeLists.txt`
- [ ] 7.1.7 Unit tests: test_plugin_manager, test_plugin_registry

### 7.2 Plugin Interfaces
- [ ] 7.2.1 `IVideoFilter.h` — Setup, ProcessFrame, ProcessFrameGPU, GetOutputParams
- [ ] 7.2.2 `IAudioFilter.h` — Setup, ProcessSamples, GetOutputParams
- [ ] 7.2.3 `IEncoderPlugin.h` — Open, EncodeFrame, Flush, Close
- [ ] 7.2.4 `IDecoderPlugin.h` — Open, DecodePacket, Close
- [ ] 7.2.5 `INetworkPlugin.h` — Connect, Send, Receive, Statistics
- [ ] 7.2.6 `IOverlayPlugin.h` — Render overlay to frame
- [ ] 7.2.7 `ITransitionPlugin.h` — Render transition between frames
- [ ] 7.2.8 `IAIFilterPlugin.h` — LoadModel, ProcessFrame, inference device selection

### 7.3 Example Plugins
- [ ] 7.3.1 GrayscaleFilter plugin (video filter example)
  - [ ] Plugin project, CMakeLists.txt, plugin.json
  - [ ] GrayscaleFilter.h/.cpp
  - [ ] Build → verify load by PluginManager
- [ ] 7.3.2 GainFilter plugin (audio filter example)
- [ ] 7.3.3 LowerThirdOverlay plugin (overlay example)
- [ ] 7.3.4 Plugin Developer Guide draft

### 7.4 Built-in Plugins
- [ ] 7.4.1 ColorCorrectionFilter — brightness, contrast, saturation, hue
- [ ] 7.4.2 LUTFilter — 3D LUT application (.cube file)
- [ ] 7.4.3 NoiseReductionFilter — temporal/spatial denoise

### 7.5 Integration Tests
- [ ] 7.5.1 Test: PluginManager load/unload cycle
- [ ] 7.5.2 Test: Pipeline with plugin video filter
- [ ] 7.5.3 Test: Hot-reload plugin in demo mode

### ✅ Phase 7 Milestone
> Third-party developers có thể viết và load plugins.

---

## Phase 8: Testing, Documentation & Packaging 🟢

**Ước tính:** 3–4 tuần | **Tiên quyết:** Phase 6, Phase 7

### 8.1 Comprehensive Testing
- [ ] 8.1.1 Unit test coverage audit — target ≥ 80% line coverage for core modules
- [ ] 8.1.2 Integration test suite — all major pipeline workflows
- [ ] 8.1.3 Performance benchmarks — encoding, decoding, pipeline latency
- [ ] 8.1.4 Memory leak detection — AddressSanitizer full test run
- [ ] 8.1.5 Fuzz testing — demuxer parsers, protocol handlers
- [ ] 8.1.6 Stress testing — long-running pipelines (24h+), many concurrent pipelines
- [ ] 8.1.7 Platform validation — Windows 10, Windows 11, Windows Server 2022

### 8.2 Sample Applications (C++)
- [ ] 8.2.1 `simple_player` — file playback with preview window
- [ ] 8.2.2 `broadcast_pipeline` — multi-source mixer → multi-output
- [ ] 8.2.3 `mixer_demo` — interactive mixer with transitions
- [ ] 8.2.4 `ndi_srt_output` — NDI receive → SRT output bridge
- [ ] 8.2.5 `plugin_loader` — demonstrate plugin loading and usage

### 8.3 Sample Applications (.NET)
- [ ] 8.3.1 `SimplePlayer` (WPF) — file playback with WPF UI
- [ ] 8.3.2 `BroadcastApp` (WPF) — multi-source broadcast application
- [ ] 8.3.3 `MixerDemo` (WPF) — mixer with UI controls
- [ ] 8.3.4 `StreamingApp` (Console) — command-line streaming tool

### 8.4 Documentation
- [ ] 8.4.1 Doxygen configuration — generate C++ API reference
- [ ] 8.4.2 Run Doxygen → verify HTML output
- [ ] 8.4.3 Architecture Overview document (finalize from `01_architecture_overview.md`)
- [ ] 8.4.4 Getting Started Guide — setup, build, first app, first pipeline
- [ ] 8.4.5 Plugin Development Guide — interface spec, build plugin, register, examples
- [ ] 8.4.6 Migration Guide — Medialooks feature mapping, equivalent APIs
- [ ] 8.4.7 .NET API reference — XML doc → DocFX
- [ ] 8.4.8 Changelog v1.0.0

### 8.5 Packaging & Distribution
- [ ] 8.5.1 NuGet packages — per .NET library (OpenMedia.Core, OpenMedia.IO, etc.)
- [ ] 8.5.2 vcpkg port — for C++ consumers
- [ ] 8.5.3 MSI installer — SDK installation for Windows
- [ ] 8.5.4 Docker container — headless media processing (Linux server)
- [ ] 8.5.5 Demo package — with watermark, all features, no license
- [ ] 8.5.6 Production package — license-gated, optimized, stripped
- [ ] 8.5.7 `tools/scripts/package.ps1` — automated packaging script
- [ ] 8.5.8 Release checklist — verify all deliverables

### 8.6 SDK Library Reference Guide
> **Mục tiêu:** Sau khi phát triển xong dự án, viết file tóm tắt toàn bộ thư viện SDK gồm tên, tính năng, nhiệm vụ và demo/cách sử dụng.

- [ ] 8.6.1 Tạo file `docs/sdk_library_reference.md` — tổng quan toàn bộ thư viện
- [ ] 8.6.2 **OpenMedia.Core** — mô tả tính năng (Engine, Pipeline, Frame, Queue, Clock), vai trò nền tảng, code demo khởi tạo engine + pipeline
- [ ] 8.6.3 **OpenMedia.IO** — mô tả tính năng (FileSource, LiveSource, DeviceSource), formats hỗ trợ, code demo đọc file / capture device
- [ ] 8.6.4 **OpenMedia.Codecs** — mô tả tính năng (H264/H265/AV1/AAC/Opus encoder/decoder), code demo transcode file
- [ ] 8.6.5 **OpenMedia.Rendering** — mô tả tính năng (D3D11 renderer, Preview window), code demo preview video
- [ ] 8.6.6 **OpenMedia.Mixer** — mô tả tính năng (multi-layer, transition, switcher, chroma key, LUT), code demo mixer 2-layer + transition
- [ ] 8.6.7 **OpenMedia.Audio** — mô tả tính năng (mixer, meter, resampler, waveform, vectorscope), code demo audio mixing + metering
- [ ] 8.6.8 **OpenMedia.Overlay** — mô tả tính năng (text, logo, ticker, clock, subtitle, HTML), code demo thêm overlay vào pipeline
- [ ] 8.6.9 **OpenMedia.CG** — mô tả tính năng (template system, data binding, animation), code demo CG template rendering
- [ ] 8.6.10 **OpenMedia.Playlist** — mô tả tính năng (playlist management, replay, slow-motion), code demo playlist auto-advance
- [ ] 8.6.11 **OpenMedia.GPU** — mô tả tính năng (CUDA, NVENC/NVDEC, QuickSync, D3D11/12, Vulkan), code demo GPU-accelerated encode
- [ ] 8.6.12 **OpenMedia.SRT** — mô tả tính năng (listener/caller, encryption, stats), code demo SRT streaming
- [ ] 8.6.13 **OpenMedia.NDI** — mô tả tính năng (discovery, receive, send, metadata), code demo NDI output
- [ ] 8.6.14 **OpenMedia.WebRTC** — mô tả tính năng (receive, broadcast, WHIP/WHEP), code demo WebRTC broadcast
- [ ] 8.6.15 **OpenMedia.RTMP** — mô tả tính năng (input, push output, RTMPS), code demo push to YouTube/Twitch
- [ ] 8.6.16 **OpenMedia.ST2110** — mô tả tính năng (essence streams, PTP, NMOS), code demo ST 2110 receive
- [ ] 8.6.17 **OpenMedia.Monitoring** — mô tả tính năng (metrics, waveform, vectorscope, health), code demo pipeline monitoring
- [ ] 8.6.18 **OpenMedia.PluginSDK** — mô tả tính năng (plugin interfaces, dynamic loading), code demo viết custom video filter plugin
- [ ] 8.6.19 **.NET Wrappers** — tóm tắt toàn bộ .NET libraries (Core, IO, Mixer, SRT, NDI, WebRTC, Playlist, CG), code demo C# pipeline
- [ ] 8.6.20 Bảng tổng hợp Quick Reference — tên thư viện, mục đích 1 dòng, dependency, namespace
- [ ] 8.6.21 Review và finalize `sdk_library_reference.md`

### ✅ Phase 8 Milestone
> SDK ready cho commercial release: tests pass, docs complete, samples work, packages built, SDK reference guide hoàn chỉnh.

---

## Summary

| Phase | Tasks | Status |
|-------|-------|--------|
| Phase 1 — Core Setup | 52 | `[/]` ~85% done |
| Phase 2 — I/O Modules | 48 | `[ ]` |
| Phase 3 — Processing | 52 | `[ ]` |
| Phase 4 — GPU | 22 | `[ ]` |
| Phase 5 — Protocols | 39 | `[ ]` |
| Phase 6 — .NET | 27 | `[ ]` |
| Phase 7 — Plugins | 23 | `[ ]` |
| Phase 8 — Test/Doc/Pkg/Ref | 49 | `[ ]` |
| **TOTAL** | **312** | **~14% complete** |
