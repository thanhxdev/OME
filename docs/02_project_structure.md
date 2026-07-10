# OpenMedia SDK — Project Structure

**Version:** 2.0  
**Date:** July 2026 — Updated with Exhand Architecture

---

## Cấu trúc thư mục dự án

```
OpenMediaSDK/
│
├── .env.demo                          # Biến môi trường cho Demo
├── .env.production                    # Biến môi trường cho Production
├── .env.shared                        # Biến môi trường chung
├── .gitignore
├── .clang-format
├── .clang-tidy
├── LICENSE
├── README.md
│
├── CMakeLists.txt                     # Root CMake — C++20, multi-platform
├── cmake/
│   ├── OpenMediaConfig.cmake          # Package config
│   ├── CompilerSettings.cmake         # Compiler flags, sanitizers
│   ├── Dependencies.cmake             # Third-party dependency management
│   ├── Platform.cmake                 # Platform-specific settings
│   ├── Version.cmake                  # Version extraction from git tag
│   ├── EnvironmentConfig.cmake        # Demo/Production env loading
│   └── Toolchain/
│       ├── Windows-MSVC.cmake
│       └── Linux-Clang.cmake
│
├── vcpkg.json                         # vcpkg manifest (dependencies)
├── conanfile.py                       # Conan alternative
│
├── docs/                              # Tài liệu dự án
│   ├── 01_architecture_overview.md
│   ├── 02_project_structure.md
│   ├── 03_development_phases.md
│   ├── 04_dependencies.md
│   ├── 05_environment_config.md
│   ├── 06_build_system.md
│   ├── 07_plugin_sdk_spec.md
│   ├── 08_api_design.md
│   ├── 09_testing_plan.md
│   ├── 10_coding_standards.md
│   ├── Index Lib.docx
│   └── OpenMedia_SDK_Prompts_Plan.md
│
├── src/                               # Source code chính
│   ├── core/                          # OpenMedia.Core
│   │   ├── CMakeLists.txt
│   │   ├── include/
│   │   │   └── openmedia/core/
│   │   │       ├── IMediaObject.h
│   │   │       ├── MediaFrame.h
│   │   │       ├── MediaPipeline.h
│   │   │       ├── FrameQueue.h
│   │   │       ├── ClockSync.h
│   │   │       ├── MediaMetadata.h
│   │   │       ├── Engine.h
│   │   │       ├── MemoryPool.h
│   │   │       ├── Logger.h
│   │   │       ├── ErrorCodes.h
│   │   │       ├── Config.h
│   │   │       └── Types.h
│   │   └── src/
│   │       ├── MediaPipeline.cpp
│   │       ├── FrameQueue.cpp
│   │       ├── ClockSync.cpp
│   │       ├── Engine.cpp
│   │       ├── MemoryPool.cpp
│   │       └── Logger.cpp
│   │
│   ├── io/                            # OpenMedia.IO
│   │   ├── CMakeLists.txt
│   │   ├── include/
│   │   │   └── openmedia/io/
│   │   │       ├── FileSource.h
│   │   │       ├── LiveSource.h
│   │   │       ├── MediaReader.h
│   │   │       ├── DeviceSource.h
│   │   │       ├── DeviceFactory.h
│   │   │       ├── DesktopCapture.h
│   │   │       ├── WindowCapture.h
│   │   │       └── NetworkSource.h
│   │   └── src/
│   │       ├── FileSource.cpp
│   │       ├── LiveSource.cpp
│   │       ├── MediaReader.cpp
│   │       ├── DeviceSource.cpp
│   │       ├── DeviceFactory.cpp
│   │       ├── DesktopCapture.cpp
│   │       └── WindowCapture.cpp
│   │
│   ├── codecs/                        # OpenMedia.Codecs
│   │   ├── CMakeLists.txt
│   │   ├── include/
│   │   │   └── openmedia/codecs/
│   │   │       ├── IDecoder.h
│   │   │       ├── IEncoder.h
│   │   │       ├── H264Decoder.h
│   │   │       ├── H264Encoder.h
│   │   │       ├── H265Decoder.h
│   │   │       ├── H265Encoder.h
│   │   │       ├── AV1Encoder.h
│   │   │       ├── AACEncoder.h
│   │   │       ├── OpusEncoder.h
│   │   │       └── CodecFactory.h
│   │   └── src/
│   │       ├── H264Decoder.cpp
│   │       ├── H264Encoder.cpp
│   │       ├── H265Decoder.cpp
│   │       ├── H265Encoder.cpp
│   │       ├── AV1Encoder.cpp
│   │       ├── AACEncoder.cpp
│   │       ├── OpusEncoder.cpp
│   │       └── CodecFactory.cpp
│   │
│   ├── rendering/                     # OpenMedia.Rendering
│   │   ├── CMakeLists.txt
│   │   ├── include/
│   │   │   └── openmedia/rendering/
│   │   │       ├── Renderer.h
│   │   │       ├── Preview.h
│   │   │       ├── D3D11Renderer.h
│   │   │       └── VulkanRenderer.h
│   │   └── src/
│   │       ├── Renderer.cpp
│   │       ├── Preview.cpp
│   │       ├── D3D11Renderer.cpp
│   │       └── VulkanRenderer.cpp
│   │
│   ├── mixer/                         # OpenMedia.Mixer
│   │   ├── CMakeLists.txt
│   │   ├── include/
│   │   │   └── openmedia/mixer/
│   │   │       ├── Mixer.h
│   │   │       ├── MixerLayer.h
│   │   │       ├── Transition.h
│   │   │       ├── Switcher.h
│   │   │       └── ChromaKey.h
│   │   └── src/
│   │       ├── Mixer.cpp
│   │       ├── MixerLayer.cpp
│   │       ├── Transition.cpp
│   │       ├── Switcher.cpp
│   │       └── ChromaKey.cpp
│   │
│   ├── audio/                         # OpenMedia.Audio
│   │   ├── CMakeLists.txt
│   │   ├── include/
│   │   │   └── openmedia/audio/
│   │   │       ├── AudioEngine.h
│   │   │       ├── AudioMixer.h
│   │   │       ├── AudioMeter.h
│   │   │       ├── Resampler.h
│   │   │       └── ChannelMapper.h
│   │   └── src/
│   │       ├── AudioEngine.cpp
│   │       ├── AudioMixer.cpp
│   │       ├── AudioMeter.cpp
│   │       ├── Resampler.cpp
│   │       └── ChannelMapper.cpp
│   │
│   ├── overlay/                       # OpenMedia.Overlay
│   │   ├── CMakeLists.txt
│   │   ├── include/
│   │   │   └── openmedia/overlay/
│   │   │       ├── OverlayEngine.h
│   │   │       ├── TextOverlay.h
│   │   │       ├── LogoOverlay.h
│   │   │       ├── TickerOverlay.h
│   │   │       ├── ClockOverlay.h
│   │   │       ├── SubtitleRenderer.h
│   │   │       └── HTMLRenderer.h
│   │   └── src/
│   │       ├── OverlayEngine.cpp
│   │       ├── TextOverlay.cpp
│   │       ├── LogoOverlay.cpp
│   │       ├── TickerOverlay.cpp
│   │       ├── ClockOverlay.cpp
│   │       ├── SubtitleRenderer.cpp
│   │       └── HTMLRenderer.cpp
│   │
│   ├── cg/                            # OpenMedia.CG
│   │   ├── CMakeLists.txt
│   │   ├── include/
│   │   │   └── openmedia/cg/
│   │   │       ├── CGEngine.h
│   │   │       ├── CGTemplate.h
│   │   │       └── CGRenderer.h
│   │   └── src/
│   │       ├── CGEngine.cpp
│   │       ├── CGTemplate.cpp
│   │       └── CGRenderer.cpp
│   │
│   ├── playlist/                      # OpenMedia.Playlist
│   │   ├── CMakeLists.txt
│   │   ├── include/
│   │   │   └── openmedia/playlist/
│   │   │       ├── Playlist.h
│   │   │       ├── PlaylistItem.h
│   │   │       ├── ReplayEngine.h
│   │   │       └── SlowMotion.h
│   │   └── src/
│   │       ├── Playlist.cpp
│   │       ├── PlaylistItem.cpp
│   │       ├── ReplayEngine.cpp
│   │       └── SlowMotion.cpp
│   │
│   ├── gpu/                           # OpenMedia.GPU
│   │   ├── CMakeLists.txt
│   │   ├── include/
│   │   │   └── openmedia/gpu/
│   │   │       ├── GPUContext.h
│   │   │       ├── GPUFrame.h
│   │   │       ├── CUDAContext.h
│   │   │       ├── D3D11Context.h
│   │   │       ├── D3D12Context.h
│   │   │       ├── VulkanContext.h
│   │   │       └── OpenCLContext.h
│   │   └── src/
│   │       ├── GPUContext.cpp
│   │       ├── GPUFrame.cpp
│   │       ├── CUDAContext.cpp
│   │       ├── D3D11Context.cpp
│   │       ├── D3D12Context.cpp
│   │       ├── VulkanContext.cpp
│   │       └── OpenCLContext.cpp
│   │
│   ├── protocols/                     # Network Protocol Engines
│   │   ├── srt/                       # OpenMedia.SRT
│   │   │   ├── CMakeLists.txt
│   │   │   ├── include/openmedia/srt/
│   │   │   │   ├── SRTEngine.h
│   │   │   │   ├── SRTSource.h
│   │   │   │   └── SRTOutput.h
│   │   │   └── src/
│   │   │       ├── SRTEngine.cpp
│   │   │       ├── SRTSource.cpp
│   │   │       └── SRTOutput.cpp
│   │   │
│   │   ├── ndi/                       # OpenMedia.NDI
│   │   │   ├── CMakeLists.txt
│   │   │   ├── include/openmedia/ndi/
│   │   │   │   ├── NDIEngine.h
│   │   │   │   ├── NDISource.h
│   │   │   │   └── NDIOutput.h
│   │   │   └── src/
│   │   │       ├── NDIEngine.cpp
│   │   │       ├── NDISource.cpp
│   │   │       └── NDIOutput.cpp
│   │   │
│   │   ├── webrtc/                    # OpenMedia.WebRTC
│   │   │   ├── CMakeLists.txt
│   │   │   ├── include/openmedia/webrtc/
│   │   │   │   ├── WebRTCEngine.h
│   │   │   │   ├── WebRTCSource.h
│   │   │   │   └── WebRTCOutput.h
│   │   │   └── src/
│   │   │       ├── WebRTCEngine.cpp
│   │   │       ├── WebRTCSource.cpp
│   │   │       └── WebRTCOutput.cpp
│   │   │
│   │   ├── rtmp/                      # OpenMedia.RTMP
│   │   │   ├── CMakeLists.txt
│   │   │   ├── include/openmedia/rtmp/
│   │   │   │   ├── RTMPEngine.h
│   │   │   │   ├── RTMPSource.h
│   │   │   │   └── RTMPOutput.h
│   │   │   └── src/
│   │   │       ├── RTMPEngine.cpp
│   │   │       ├── RTMPSource.cpp
│   │   │       └── RTMPOutput.cpp
│   │   │
│   │   └── st2110/                    # OpenMedia.ST2110
│   │       ├── CMakeLists.txt
│   │       ├── include/openmedia/st2110/
│   │       │   ├── ST2110Engine.h
│   │       │   ├── ST2110Source.h
│   │       │   └── ST2110Output.h
│   │       └── src/
│   │           ├── ST2110Engine.cpp
│   │           ├── ST2110Source.cpp
│   │           └── ST2110Output.cpp
│   │
│   ├── monitoring/                    # OpenMedia.Monitoring
│   │   ├── CMakeLists.txt
│   │   ├── include/openmedia/monitoring/
│   │   │   ├── Metrics.h
│   │   │   ├── Waveform.h
│   │   │   ├── Vectorscope.h
│   │   │   └── HealthCheck.h
│   │   └── src/
│   │       ├── Metrics.cpp
│   │       ├── Waveform.cpp
│   │       ├── Vectorscope.cpp
│   │       └── HealthCheck.cpp
│   │
│   └── plugin_sdk/                    # OpenMedia.PluginSDK
│       ├── CMakeLists.txt
│       ├── include/openmedia/plugin/
│       │   ├── IPlugin.h
│       │   ├── PluginManager.h
│       │   ├── PluginRegistry.h
│       │   ├── IVideoFilter.h
│       │   ├── IAudioFilter.h
│       │   ├── IEncoderPlugin.h
│       │   ├── IDecoderPlugin.h
│   │       ├── IAIFilterPlugin.h
│   │       └── IAIFilterPlugin.h
│       └── src/
│           ├── PluginManager.cpp
│           └── PluginRegistry.cpp
│
│   ├── server/                        # OpenMediaServer.exe
│   │   ├── CMakeLists.txt
│   │   ├── main.cpp                   # Server entry point
│   │   ├── include/openmedia/server/
│   │   │   ├── ServerApp.h
│   │   │   ├── ServerConfig.h
│   │   │   └── ServerLifecycle.h
│   │   └── src/
│   │       ├── ServerApp.cpp
│   │       ├── ServerConfig.cpp
│   │       └── ServerLifecycle.cpp
│   │
│   ├── ipc/                           # OpenMedia.IPC (Client + Server)
│   │   ├── CMakeLists.txt
│   │   ├── include/openmedia/ipc/
│   │   │   ├── IPCClient.h            # Client-side IPC
│   │   │   ├── IPCServer.h            # Server-side IPC
│   │   │   ├── IPCTransport.h         # Transport abstraction
│   │   │   ├── NamedPipeTransport.h   # Named Pipes implementation
│   │   │   ├── SharedMemoryBuffer.h   # Shared Memory for frames
│   │   │   ├── D3D11SharedTexture.h   # D3D11 shared texture interop
│   │   │   ├── CommandMessage.h        # Command/Response protocol
│   │   │   └── FrameNotification.h    # Frame ready notifications
│   │   └── src/
│   │       ├── IPCClient.cpp
│   │       ├── IPCServer.cpp
│   │       ├── NamedPipeTransport.cpp
│   │       ├── SharedMemoryBuffer.cpp
│   │       ├── D3D11SharedTexture.cpp
│   │       └── CommandMessage.cpp
│   │
│   ├── command_dispatcher/            # OpenMedia.CommandDispatcher
│   │   ├── CMakeLists.txt
│   │   ├── include/openmedia/dispatcher/
│   │   │   ├── CommandDispatcher.h     # Command routing & execution
│   │   │   ├── CommandHandler.h        # Handler interface
│   │   │   ├── CommandRegistry.h       # Handler registration
│   │   │   └── CommandTypes.h          # Command enum & payloads
│   │   └── src/
│   │       ├── CommandDispatcher.cpp
│   │       ├── CommandHandler.cpp
│   │       └── CommandRegistry.cpp
│   │
│   ├── worker_pool/                   # OpenMedia.WorkerPool
│   │   ├── CMakeLists.txt
│   │   ├── include/openmedia/workers/
│   │   │   ├── WorkerPool.h           # Thread pool management
│   │   │   ├── TaskQueue.h            # Priority task queue
│   │   │   ├── WorkerThread.h         # Worker thread implementation
│   │   │   └── TaskTypes.h            # Task priorities & types
│   │   └── src/
│   │       ├── WorkerPool.cpp
│   │       ├── TaskQueue.cpp
│   │       └── WorkerThread.cpp
│   │
│   ├── plugin_host/                   # OpenMedia.PluginHost
│   │   ├── CMakeLists.txt
│   │   ├── include/openmedia/plugin_host/
│   │   │   ├── PluginHost.h           # Plugin host manager
│   │   │   ├── PluginSandbox.h        # Plugin isolation
│   │   │   └── PluginLifecycle.h      # Plugin lifecycle hooks
│   │   └── src/
│   │       ├── PluginHost.cpp
│   │       ├── PluginSandbox.cpp
│   │       └── PluginLifecycle.cpp
│   │
│   └── sdk/                           # OpenMedia.SDK (Client API)
│       ├── CMakeLists.txt
│       ├── include/openmedia/sdk/
│       │   ├── OpenMediaSDK.h         # Main client SDK header
│       │   ├── SDKEngine.h            # Client-side Engine proxy
│       │   ├── SDKPipeline.h          # Client-side Pipeline proxy
│       │   ├── SDKSource.h            # Client-side Source proxy
│       │   └── SDKConfig.h            # SDK configuration
│       └── src/
│           ├── OpenMediaSDK.cpp
│           ├── SDKEngine.cpp
│           ├── SDKPipeline.cpp
│           └── SDKSource.cpp
│
├── wrappers/                          # .NET Wrappers
│   ├── OpenMedia.NET.sln              # .NET Solution
│   ├── OpenMedia.Core.NET/
│   │   ├── OpenMedia.Core.NET.csproj
│   │   ├── NativeBridge.cs
│   │   ├── MediaPlayer.cs
│   │   ├── MediaPipeline.cs
│   │   └── MediaFrame.cs
│   ├── OpenMedia.IO.NET/
│   │   ├── OpenMedia.IO.NET.csproj
│   │   ├── FileSource.cs
│   │   ├── LiveSource.cs
│   │   └── DeviceSource.cs
│   ├── OpenMedia.Mixer.NET/
│   │   ├── OpenMedia.Mixer.NET.csproj
│   │   └── Mixer.cs
│   ├── OpenMedia.WebRTC.NET/
│   │   ├── OpenMedia.WebRTC.NET.csproj
│   │   └── WebRTCEngine.cs
│   ├── OpenMedia.SRT.NET/
│   │   ├── OpenMedia.SRT.NET.csproj
│   │   └── SRTEngine.cs
│   ├── OpenMedia.NDI.NET/
│   │   ├── OpenMedia.NDI.NET.csproj
│   │   └── NDIEngine.cs
│   ├── OpenMedia.Playlist.NET/
│   │   ├── OpenMedia.Playlist.NET.csproj
│   │   └── Playlist.cs
│   └── OpenMedia.CG.NET/
│       ├── OpenMedia.CG.NET.csproj
│       └── CGEngine.cs
│
├── plugins/                           # Built-in Plugins
│   ├── examples/
│   │   ├── SampleVideoFilter/
│   │   ├── SampleAudioFilter/
│   │   └── SampleOverlay/
│   └── builtin/
│       ├── ColorCorrectionFilter/
│       ├── LUTFilter/
│       └── NoiseReductionFilter/
│
├── samples/                           # Sample Applications
│   ├── cpp/
│   │   ├── simple_player/
│   │   ├── broadcast_pipeline/
│   │   ├── mixer_demo/
│   │   ├── ndi_srt_output/
│   │   └── plugin_loader/
│   └── dotnet/
│       ├── SimplePlayer/
│       ├── BroadcastApp/
│       ├── MixerDemo/
│       └── StreamingApp/
│
├── tests/                             # Unit & Integration Tests
│   ├── CMakeLists.txt
│   ├── unit/
│   │   ├── core/
│   │   ├── io/
│   │   ├── codecs/
│   │   ├── mixer/
│   │   ├── audio/
│   │   ├── overlay/
│   │   ├── gpu/
│   │   └── protocols/
│   ├── integration/
│   │   ├── pipeline_tests/
│   │   ├── encoding_tests/
│   │   └── streaming_tests/
│   └── benchmark/
│       ├── encoding_bench/
│       ├── decoding_bench/
│       └── pipeline_bench/
│
├── tools/                             # Build & Dev Tools
│   ├── scripts/
│   │   ├── build.ps1                  # Windows build script
│   │   ├── build.sh                   # Linux build script
│   │   ├── setup_env.ps1              # Environment setup
│   │   ├── package.ps1                # Packaging script
│   │   └── generate_version.py       # Version generator
│   └── ci/
│       ├── azure-pipelines.yml
│       └── github-actions.yml
│
├── third_party/                       # Vendored / bundled third-party
│   ├── README.md                      # License & source notes
│   ├── ffmpeg/                        # Pre-built FFmpeg libs
│   │   ├── include/
│   │   ├── lib/
│   │   │   ├── x64-windows/
│   │   │   └── x64-linux/
│   │   └── LICENSE.md
│   ├── ndi_sdk/                       # NDI SDK
│   │   ├── include/
│   │   ├── lib/
│   │   └── LICENSE.md
│   ├── decklink_sdk/                  # Blackmagic DeckLink SDK
│   │   ├── include/
│   │   └── LICENSE.md
│   ├── aja_sdk/                       # AJA NTV2 SDK
│   │   ├── include/
│   │   ├── lib/
│   │   └── LICENSE.md
│   ├── cef/                           # Chromium Embedded Framework
│   │   ├── include/
│   │   ├── lib/
│   │   └── LICENSE.md
│   ├── srt/                           # libsrt
│   │   └── (built via vcpkg or bundled)
│   ├── spdlog/                        # Logging
│   ├── nlohmann_json/                 # JSON
│   ├── googletest/                    # Google Test
│   └── imgui/                         # Debug UI (optional)
│
└── dist/                              # Build output / distribution
    ├── demo/
    │   ├── bin/
    │   ├── lib/
    │   └── config/
    └── production/
        ├── bin/
        ├── lib/
        └── config/
```

## Ghi chú

- Mỗi module có `CMakeLists.txt` riêng, được include từ root CMake
- Header files sử dụng namespace `openmedia::<module>`
- Third-party libraries được vendored hoặc quản lý qua vcpkg
- Build output tách biệt cho `demo` và `production` environments
- **Exhand Architecture**: `OpenMediaServer.exe` chạy như server process riêng, `OpenMedia.SDK.dll` là client-side API
- **IPC Layer**: Named Pipes cho commands, Shared Memory + D3D11 Shared Textures cho frames
- **Command Dispatcher**: Routing commands từ IPC tới đúng module handler
- **Worker Pool**: Thread pool quản lý task scheduling cho pipeline operations
- **Plugin Host**: Nạp plugin động (NDI, DeckLink, WebRTC, SRT, FFmpeg...) với isolation
