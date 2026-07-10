# OpenMedia SDK — Build System

**Version:** 1.0  
**Date:** July 2026

---

## 1. Tổng quan

| Component | Tool |
|-----------|------|
| C++ Build System | CMake 3.28+ |
| C++ Compiler | MSVC 17.8+ (Windows) / Clang 17+ (Linux) |
| C++ Standard | C++20 |
| .NET Build | dotnet CLI / MSBuild |
| .NET Version | .NET 8 LTS / .NET 9 |
| Package Manager (C++) | vcpkg (manifest mode) |
| Package Manager (.NET) | NuGet |

---

## 2. Root CMakeLists.txt

```cmake
# ============================================================
# OpenMedia SDK — Root CMakeLists.txt
# ============================================================
cmake_minimum_required(VERSION 3.28)

# --- vcpkg Integration ---
if(DEFINED ENV{VCPKG_ROOT})
    set(CMAKE_TOOLCHAIN_FILE "$ENV{VCPKG_ROOT}/scripts/buildsystems/vcpkg.cmake"
        CACHE STRING "Vcpkg toolchain file")
else()
    set(CMAKE_TOOLCHAIN_FILE "${CMAKE_SOURCE_DIR}/third_party/vcpkg/scripts/buildsystems/vcpkg.cmake"
        CACHE STRING "Vcpkg toolchain file")
endif()

project(OpenMediaSDK
    VERSION 1.0.0
    DESCRIPTION "High-Performance Media Engine SDK"
    LANGUAGES CXX C
)

# --- C++20 Standard ---
set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# --- Global Settings ---
set(CMAKE_EXPORT_COMPILE_COMMANDS ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)
set(CMAKE_WINDOWS_EXPORT_ALL_SYMBOLS OFF)

# --- Output Directories ---
set(CMAKE_RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin")
set(CMAKE_LIBRARY_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/lib")
set(CMAKE_ARCHIVE_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/lib")

# --- Include CMake Modules ---
list(APPEND CMAKE_MODULE_PATH "${CMAKE_SOURCE_DIR}/cmake")
include(CompilerSettings)
include(Dependencies)
include(Platform)
include(Version)
include(EnvironmentConfig)

# --- Options ---
option(OME_BUILD_TESTS      "Build unit tests"         ON)
option(OME_BUILD_BENCHMARKS "Build benchmarks"         OFF)
option(OME_BUILD_SAMPLES    "Build sample apps"        ON)
option(OME_BUILD_PLUGINS    "Build built-in plugins"   ON)
option(OME_BUILD_DOCS       "Build documentation"      OFF)

option(OME_ENABLE_GPU       "Enable GPU acceleration"  ON)
option(OME_ENABLE_NDI       "Enable NDI support"       ON)
option(OME_ENABLE_SRT       "Enable SRT support"       ON)
option(OME_ENABLE_WEBRTC    "Enable WebRTC support"    ON)
option(OME_ENABLE_ST2110    "Enable ST2110 support"    OFF)
option(OME_ENABLE_CEF       "Enable CEF HTML overlay"  ON)

# --- Core Modules (always built) ---
add_subdirectory(src/core)
add_subdirectory(src/io)
add_subdirectory(src/codecs)
add_subdirectory(src/rendering)
add_subdirectory(src/mixer)
add_subdirectory(src/audio)
add_subdirectory(src/overlay)
add_subdirectory(src/cg)
add_subdirectory(src/playlist)
add_subdirectory(src/monitoring)
add_subdirectory(src/plugin_sdk)

# --- Optional Modules ---
if(OME_ENABLE_GPU)
    add_subdirectory(src/gpu)
endif()

# --- Protocol Engines ---
add_subdirectory(src/protocols/rtmp)
if(OME_ENABLE_SRT)
    add_subdirectory(src/protocols/srt)
endif()
if(OME_ENABLE_NDI)
    add_subdirectory(src/protocols/ndi)
endif()
if(OME_ENABLE_WEBRTC)
    add_subdirectory(src/protocols/webrtc)
endif()
if(OME_ENABLE_ST2110)
    add_subdirectory(src/protocols/st2110)
endif()

# --- Tests ---
if(OME_BUILD_TESTS)
    enable_testing()
    add_subdirectory(tests)
endif()

# --- Samples ---
if(OME_BUILD_SAMPLES)
    add_subdirectory(samples/cpp)
endif()

# --- Plugins ---
if(OME_BUILD_PLUGINS)
    add_subdirectory(plugins)
endif()

# --- Install Rules ---
include(GNUInstallDirs)
install(DIRECTORY src/core/include/ DESTINATION ${CMAKE_INSTALL_INCLUDEDIR})
install(DIRECTORY src/io/include/ DESTINATION ${CMAKE_INSTALL_INCLUDEDIR})
# ... (other modules)

# --- Summary ---
message(STATUS "")
message(STATUS "╔══════════════════════════════════════════════════╗")
message(STATUS "║         OpenMedia SDK Build Configuration        ║")
message(STATUS "╠══════════════════════════════════════════════════╣")
message(STATUS "║  Version:      ${PROJECT_VERSION}")
message(STATUS "║  Environment:  ${OME_ENV_TAG}")
message(STATUS "║  Build Type:   ${CMAKE_BUILD_TYPE}")
message(STATUS "║  Compiler:     ${CMAKE_CXX_COMPILER_ID} ${CMAKE_CXX_COMPILER_VERSION}")
message(STATUS "║  C++ Standard: ${CMAKE_CXX_STANDARD}")
message(STATUS "║  GPU:          ${OME_ENABLE_GPU}")
message(STATUS "║  NDI:          ${OME_ENABLE_NDI}")
message(STATUS "║  SRT:          ${OME_ENABLE_SRT}")
message(STATUS "║  WebRTC:       ${OME_ENABLE_WEBRTC}")
message(STATUS "║  ST2110:       ${OME_ENABLE_ST2110}")
message(STATUS "║  CEF:          ${OME_ENABLE_CEF}")
message(STATUS "║  Tests:        ${OME_BUILD_TESTS}")
message(STATUS "║  Samples:      ${OME_BUILD_SAMPLES}")
message(STATUS "╚══════════════════════════════════════════════════╝")
message(STATUS "")
```

---

## 3. CMake Modules

### 3.1 CompilerSettings.cmake

```cmake
# Compiler flags per-platform
if(MSVC)
    add_compile_options(
        /W4           # Warning level 4
        /WX           # Warnings as errors (optional)
        /permissive-  # Standards conformance
        /MP           # Multi-processor compilation
        /Zc:__cplusplus  # Correct __cplusplus macro
        /utf-8        # Source and execution charset
    )
    # Release optimizations
    if(CMAKE_BUILD_TYPE STREQUAL "Release")
        add_compile_options(/O2 /GL /Oi /Gy)
        add_link_options(/LTCG /OPT:REF /OPT:ICF)
    endif()
elseif(CMAKE_CXX_COMPILER_ID MATCHES "Clang|GNU")
    add_compile_options(
        -Wall -Wextra -Wpedantic
        -Wno-unused-parameter
        -fPIC
    )
    if(CMAKE_BUILD_TYPE STREQUAL "Release")
        add_compile_options(-O2 -march=native)
    endif()
endif()
```

### 3.2 Version.cmake

```cmake
# Extract version from git tags
find_package(Git)
if(GIT_FOUND)
    execute_process(
        COMMAND ${GIT_EXECUTABLE} describe --tags --always --dirty
        WORKING_DIRECTORY ${CMAKE_SOURCE_DIR}
        OUTPUT_VARIABLE GIT_VERSION
        OUTPUT_STRIP_TRAILING_WHITESPACE
        ERROR_QUIET
    )
    execute_process(
        COMMAND ${GIT_EXECUTABLE} rev-parse --short HEAD
        WORKING_DIRECTORY ${CMAKE_SOURCE_DIR}
        OUTPUT_VARIABLE GIT_COMMIT_HASH
        OUTPUT_STRIP_TRAILING_WHITESPACE
        ERROR_QUIET
    )
    message(STATUS "Git version: ${GIT_VERSION}")
    message(STATUS "Git commit:  ${GIT_COMMIT_HASH}")
endif()
```

### 3.3 Platform.cmake

```cmake
# Platform-specific settings
if(WIN32)
    add_compile_definitions(
        WIN32_LEAN_AND_MEAN
        NOMINMAX
        _CRT_SECURE_NO_WARNINGS
        UNICODE
        _UNICODE
    )
elseif(UNIX AND NOT APPLE)
    # Linux specifics
    find_package(Threads REQUIRED)
    find_package(PkgConfig REQUIRED)
endif()
```

---

## 4. Module CMakeLists.txt (Ví dụ: Core)

```cmake
# src/core/CMakeLists.txt
add_library(OpenMedia.Core SHARED
    src/MediaPipeline.cpp
    src/FrameQueue.cpp
    src/ClockSync.cpp
    src/Engine.cpp
    src/MemoryPool.cpp
    src/Logger.cpp
)

target_include_directories(OpenMedia.Core
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/src
        ${CMAKE_BINARY_DIR}/generated  # for OpenMediaConfig.h
)

target_link_libraries(OpenMedia.Core
    PUBLIC
        spdlog::spdlog
        nlohmann_json::nlohmann_json
        fmt::fmt
    PRIVATE
        concurrentqueue
)

target_compile_definitions(OpenMedia.Core
    PRIVATE
        OME_CORE_EXPORTS
)

set_target_properties(OpenMedia.Core PROPERTIES
    VERSION ${PROJECT_VERSION}
    SOVERSION ${PROJECT_VERSION_MAJOR}
    OUTPUT_NAME "OpenMedia.Core"
)
```

---

## 5. .NET Solution Build

### 5.1 .NET Solution Structure

```xml
<!-- wrappers/OpenMedia.NET.sln -->
<!-- Standard .NET solution containing all wrapper projects -->
```

### 5.2 Core .NET Project

```xml
<!-- wrappers/OpenMedia.Core.NET/OpenMedia.Core.NET.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net8.0;net9.0</TargetFrameworks>
    <RootNamespace>OpenMedia.Core</RootNamespace>
    <AssemblyName>OpenMedia.Core</AssemblyName>
    <Version>1.0.0</Version>
    <Authors>OpenMedia</Authors>
    <Description>OpenMedia SDK - Core .NET Wrapper</Description>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>

  <PropertyGroup Condition="'$(Configuration)'=='Debug'">
    <DefineConstants>OME_DEMO</DefineConstants>
  </PropertyGroup>
  <PropertyGroup Condition="'$(Configuration)'=='Release'">
    <DefineConstants>OME_PRODUCTION</DefineConstants>
  </PropertyGroup>

  <!-- Native library reference -->
  <ItemGroup>
    <None Include="..\..\dist\$(Configuration)\bin\OpenMedia.Core.dll"
          Pack="true"
          PackagePath="runtimes\win-x64\native" />
  </ItemGroup>
</Project>
```

---

## 6. Build Scripts

### 6.1 Windows Build Script (`tools/scripts/build.ps1`)

```powershell
param(
    [ValidateSet("demo", "production")]
    [string]$Environment = "demo",

    [ValidateSet("Debug", "Release", "RelWithDebInfo")]
    [string]$BuildType = "",

    [switch]$Clean,
    [switch]$BuildTests,
    [switch]$RunTests,
    [switch]$BuildDotNet,
    [switch]$Package
)

$ErrorActionPreference = "Stop"

# Auto-set build type from environment
if (-not $BuildType) {
    $BuildType = if ($Environment -eq "production") { "Release" } else { "Debug" }
}

$BuildDir = "build-$Environment"
$DistDir = "dist/$Environment"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  OpenMedia SDK Build" -ForegroundColor Cyan
Write-Host "  Environment: $Environment" -ForegroundColor Yellow
Write-Host "  Build Type:  $BuildType" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Cyan

# Clean
if ($Clean -and (Test-Path $BuildDir)) {
    Remove-Item -Recurse -Force $BuildDir
}

# CMake Configure
cmake -B $BuildDir `
    -DOME_ENV_TAG=$Environment `
    -DCMAKE_BUILD_TYPE=$BuildType `
    -DOME_BUILD_TESTS:BOOL=$($BuildTests -or $RunTests) `
    -G "Visual Studio 17 2022" -A x64

# CMake Build
cmake --build $BuildDir --config $BuildType --parallel

# Run Tests
if ($RunTests) {
    ctest --test-dir $BuildDir -C $BuildType --output-on-failure
}

# Build .NET
if ($BuildDotNet) {
    $DotNetConfig = if ($Environment -eq "production") { "Release" } else { "Debug" }
    dotnet build wrappers/OpenMedia.NET.sln -c $DotNetConfig
}

# Package
if ($Package) {
    cmake --install $BuildDir --prefix $DistDir
    Write-Host "Package created at: $DistDir" -ForegroundColor Green
}

Write-Host "Build completed successfully!" -ForegroundColor Green
```

---

## 7. CI/CD Pipeline

### GitHub Actions (`tools/ci/github-actions.yml`)

```yaml
name: OpenMedia SDK CI

on:
  push:
    branches: [main, develop]
  pull_request:
    branches: [main]

jobs:
  build-windows:
    runs-on: windows-latest
    strategy:
      matrix:
        environment: [demo, production]

    steps:
      - uses: actions/checkout@v4
        with:
          submodules: recursive

      - name: Setup vcpkg
        uses: lukka/run-vcpkg@v11
        with:
          vcpkgDirectory: '${{ github.workspace }}/third_party/vcpkg'

      - name: Configure CMake
        run: >
          cmake -B build
          -DOME_ENV_TAG=${{ matrix.environment }}
          -DCMAKE_BUILD_TYPE=${{ matrix.environment == 'production' && 'Release' || 'Debug' }}
          -DOME_BUILD_TESTS=ON
          -G "Visual Studio 17 2022" -A x64

      - name: Build
        run: cmake --build build --config ${{ matrix.environment == 'production' && 'Release' || 'Debug' }} --parallel

      - name: Test
        run: ctest --test-dir build -C ${{ matrix.environment == 'production' && 'Release' || 'Debug' }} --output-on-failure

      - name: Build .NET
        run: dotnet build wrappers/OpenMedia.NET.sln -c ${{ matrix.environment == 'production' && 'Release' || 'Debug' }}

      - name: Upload Artifacts
        uses: actions/upload-artifact@v4
        with:
          name: openmedia-${{ matrix.environment }}
          path: build/bin/
```
