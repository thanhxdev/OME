# OpenMedia SDK

**High-Performance Media Engine with Client/Server Process Separation**

[![Build Status](https://img.shields.io/badge/build-passing-brightgreen)]()
[![Version](https://img.shields.io/badge/version-1.0.0-blue)]()
[![License](https://img.shields.io/badge/license-MIT-green)]()
[![C++](https://img.shields.io/badge/C%2B%2B-23-orange)]()
[![.NET](https://img.shields.io/badge/.NET-10-purple)]()

---

## Overview

OpenMedia SDK is a high-performance media engine SDK built with modern C++23 and .NET 10. It features a **Client/Server Process Separation** architecture (Exhand Architecture) that isolates UI from media processing for maximum stability and scalability.

### Key Features

- **Process Isolation** — Client UI and Server engine run in separate processes
- **Zero-Copy IPC** — Shared Memory + D3D11 Shared Textures for frame transfer
- **Pipeline Graph (DAG)** — Flexible Source → Filter → Mixer → Encoder → Output connections
- **GPU Acceleration** — CUDA/NVENC/NVDEC, Intel QuickSync, D3D11/D3D12, Vulkan
- **Protocol Engines** — SRT, NDI, WebRTC, RTMP, ST 2110
- **Plugin System** — Dynamic plugin loading with crash isolation
- **Modern C++23** — Concepts, coroutines, ranges, smart pointers, std::expected, std::print
- **No COM Dependency** — Clean, modern API

### Architecture

```
        UI (.NET App / WPF / WinUI)
                │
        OpenMedia.SDK.dll (Public API)
                │
          IPC Client Layer
                │
═══════════════════════════════
      OpenMediaServer.exe
═══════════════════════════════
                │
        Command Dispatcher
                │
   ┌────────────┼─────────────┐
   │            │             │
  Decoder     Mixer        Encoder
   │            │             │
  Playlist    Overlay       SRT
   │            │             │
  NDI        WebRTC        File
   │            │             │
GPU Manager Audio Mixer  Device Manager
```

---

## Requirements

### Build Requirements

| Component | Minimum Version |
|-----------|----------------|
| **CMake** | 3.28+ |
| **MSVC** | 17.8+ (Visual Studio 2022) |
| **Clang** | 17+ (optional, Linux) |
| **.NET SDK** | 10.0+ |
| **vcpkg** | Latest |
| **Windows SDK** | 10.0.22000+ |
| **Git** | 2.40+ |

### Hardware Requirements

| Component | Minimum | Recommended |
|-----------|---------|-------------|
| **CPU** | 4 cores | 8+ cores |
| **RAM** | 8 GB | 32+ GB |
| **GPU** | DirectX 11 | NVIDIA RTX / Intel ARC |
| **Storage** | 10 GB | 50+ GB (with SDKs) |

---

## Quick Start

### 1. Clone Repository

```bash
git clone https://github.com/openmedia/OpenMediaSDK.git
cd OpenMediaSDK
```

### 2. Setup Dependencies

```powershell
# Bootstrap vcpkg and install dependencies
.\tools\scripts\setup_env.ps1 -DownloadSDKs
```

### 3. Build (Demo)

```powershell
.\tools\scripts\build.ps1 -Environment demo -BuildTests -RunTests
```

### 4. Build (Production)

```powershell
.\tools\scripts\build.ps1 -Environment production
```

### Manual CMake Build

```powershell
# Configure
cmake -B build-demo -DOME_ENV_TAG=demo -DCMAKE_BUILD_TYPE=Debug -G "Visual Studio 18 2026" -A x64

# Build
cmake --build build-demo --config Debug --parallel

# Test
ctest --test-dir build-demo -C Debug --output-on-failure
```

---

## Project Structure

```
OpenMediaSDK/
├── src/                    # C++ source code
│   ├── core/              # OpenMedia.Core — base classes, pipeline, threading
│   ├── io/                # OpenMedia.IO — file/stream/device sources
│   ├── codecs/            # OpenMedia.Codecs — encoders/decoders
│   ├── server/            # OpenMediaServer.exe — server process
│   ├── ipc/               # OpenMedia.IPC — IPC transport layer
│   ├── command_dispatcher/# OpenMedia.CommandDispatcher
│   ├── worker_pool/       # OpenMedia.WorkerPool
│   ├── sdk/               # OpenMedia.SDK — client API
│   ├── mixer/             # OpenMedia.Mixer
│   ├── audio/             # OpenMedia.Audio
│   ├── overlay/           # OpenMedia.Overlay
│   ├── gpu/               # OpenMedia.GPU
│   ├── protocols/         # SRT, NDI, WebRTC, RTMP, ST2110
│   └── plugin_sdk/        # OpenMedia.PluginSDK
├── wrappers/              # .NET managed wrappers
├── plugins/               # Built-in and example plugins
├── samples/               # Sample applications
├── tests/                 # Unit, integration, and benchmark tests
├── tools/                 # Build scripts and CI
├── third_party/           # Vendored dependencies
├── cmake/                 # CMake modules
└── docs/                  # Documentation
```

---

## Documentation

| Document | Description |
|----------|-------------|
| [Architecture Overview](docs/01_architecture_overview.md) | System architecture and design |
| [Project Structure](docs/02_project_structure.md) | Directory layout and modules |
| [Development Phases](docs/03_development_phases.md) | Implementation roadmap |
| [Dependencies](docs/04_dependencies.md) | Third-party libraries |
| [Environment Config](docs/05_environment_config.md) | Demo vs Production setup |
| [Build System](docs/06_build_system.md) | CMake configuration |
| [Plugin SDK](docs/07_plugin_sdk_spec.md) | Plugin development guide |
| [API Design](docs/08_api_design.md) | API reference |
| [Testing Plan](docs/09_testing_plan.md) | Test strategy |
| [Coding Standards](docs/10_coding_standards.md) | Code style guide |

---

## License

This project is licensed under the MIT License — see the [LICENSE](LICENSE) file for details.

Copyright (c) 2026 OpenMedia. All rights reserved.
