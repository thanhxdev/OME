# OpenMedia SDK — Environment Configuration

**Version:** 1.0  
**Date:** July 2026

---

## 1. Tổng quan

Dự án sử dụng **2 biến môi trường tag** (environment tags):

| Tag | Mục đích | Mô tả |
|-----|----------|-------|
| **`demo`** | Development & Demo | Cho phát triển, testing, demo cho khách hàng. Bao gồm watermark, verbose logging, debug tools |
| **`production`** | Production Release | Bản phát hành chính thức. Tối ưu hiệu năng, license check, minimal logging |

---

## 2. File Environment

### 2.1 `.env.shared` — Cấu hình chung

```ini
# ============================================================
# OpenMedia SDK — Shared Environment Configuration
# Áp dụng cho TẤT CẢ environments (demo + production)
# ============================================================

# --- Project Info ---
OME_PROJECT_NAME=OpenMediaSDK
OME_COMPANY_NAME=OpenMedia
OME_COPYRIGHT=Copyright (c) 2026 OpenMedia. All rights reserved.

# --- Version ---
OME_VERSION_MAJOR=1
OME_VERSION_MINOR=0
OME_VERSION_PATCH=0
OME_VERSION_STRING=1.0.0

# --- Paths ---
OME_PLUGIN_DIR=./plugins
OME_CONFIG_DIR=./config
OME_LOG_DIR=./logs
OME_CACHE_DIR=./cache
OME_TEMP_DIR=./temp

# --- Media Defaults ---
OME_DEFAULT_VIDEO_WIDTH=1920
OME_DEFAULT_VIDEO_HEIGHT=1080
OME_DEFAULT_FRAMERATE=29.97
OME_DEFAULT_AUDIO_SAMPLERATE=48000
OME_DEFAULT_AUDIO_CHANNELS=2
OME_DEFAULT_PIXEL_FORMAT=NV12
OME_DEFAULT_AUDIO_FORMAT=FLOAT32

# --- Pipeline ---
OME_MAX_PIPELINE_DEPTH=32
OME_FRAME_QUEUE_SIZE=8
OME_MAX_CONCURRENT_PIPELINES=16

# --- Threading ---
OME_THREAD_POOL_SIZE=auto
OME_IO_THREAD_COUNT=4
OME_ENCODE_THREAD_COUNT=auto

# --- Network ---
OME_DEFAULT_SRT_LATENCY=120
OME_DEFAULT_RTMP_CHUNK_SIZE=4096
OME_DEFAULT_WEBRTC_ICE_SERVERS=stun:stun.l.google.com:19302
OME_NETWORK_TIMEOUT_MS=5000
OME_NETWORK_RECONNECT_ATTEMPTS=5
OME_NETWORK_RECONNECT_DELAY_MS=2000

# --- GPU ---
OME_GPU_ENABLED=true
OME_GPU_PREFER=auto
# auto | cuda | quicksync | d3d11 | vulkan | opencl

# --- Plugin System ---
OME_PLUGIN_AUTOLOAD=true
OME_PLUGIN_SANDBOX=true
OME_PLUGIN_MAX_LOAD_TIME_MS=5000
```

### 2.2 `.env.demo` — Cấu hình Demo

```ini
# ============================================================
# OpenMedia SDK — DEMO Environment Configuration
# Tag: demo
# Dùng cho: Development, Testing, Customer Demo
# ============================================================

OME_ENV_TAG=demo
OME_BUILD_TYPE=Debug

# --- Logging ---
OME_LOG_LEVEL=debug
# trace | debug | info | warn | error | critical | off
OME_LOG_TO_CONSOLE=true
OME_LOG_TO_FILE=true
OME_LOG_FILE_MAX_SIZE_MB=50
OME_LOG_FILE_MAX_COUNT=10
OME_LOG_FORMAT=[%Y-%m-%d %H:%M:%S.%e] [%l] [%t] [%n] %v
OME_LOG_PERFORMANCE_METRICS=true

# --- Debug Tools ---
OME_DEBUG_OVERLAY=true
OME_DEBUG_FRAME_COUNTER=true
OME_DEBUG_PIPELINE_STATS=true
OME_DEBUG_MEMORY_TRACKING=true
OME_DEBUG_GPU_MARKERS=true

# --- Watermark ---
OME_WATERMARK_ENABLED=true
OME_WATERMARK_TEXT=OpenMedia SDK - DEMO
OME_WATERMARK_POSITION=bottom-right
OME_WATERMARK_OPACITY=0.5
OME_WATERMARK_FONT_SIZE=16

# --- License ---
OME_LICENSE_CHECK=false
OME_LICENSE_FILE=
OME_FEATURE_LIMIT_NONE=true

# --- Build ---
OME_COMPILER_OPTIMIZATIONS=O0
OME_DEBUG_SYMBOLS=true
OME_SANITIZERS=address,undefined
OME_PROFILING=true

# --- Output Limits ---
OME_MAX_OUTPUT_RESOLUTION=3840x2160
OME_MAX_OUTPUT_FRAMERATE=60
OME_MAX_ENCODE_BITRATE_MBPS=50
OME_MAX_SIMULTANEOUS_OUTPUTS=4
OME_DEMO_TIME_LIMIT_MINUTES=0
# 0 = no time limit in demo

# --- Feature Flags ---
OME_FEATURE_GPU_ACCELERATION=true
OME_FEATURE_NDI=true
OME_FEATURE_SRT=true
OME_FEATURE_WEBRTC=true
OME_FEATURE_ST2110=true
OME_FEATURE_HTML_OVERLAY=true
OME_FEATURE_AI_FILTERS=true
OME_FEATURE_REPLAY=true
OME_FEATURE_PLUGINS=true

# --- Telemetry ---
OME_TELEMETRY_ENABLED=false
OME_TELEMETRY_ENDPOINT=
OME_CRASH_REPORTING=true
OME_CRASH_REPORT_DIR=./crash_dumps

# --- Hot Reload ---
OME_HOT_RELOAD_PLUGINS=true
OME_HOT_RELOAD_CONFIG=true
OME_HOT_RELOAD_SHADERS=true
```

### 2.3 `.env.production` — Cấu hình Production

```ini
# ============================================================
# OpenMedia SDK — PRODUCTION Environment Configuration
# Tag: production
# Dùng cho: Commercial Release, Deployment
# ============================================================

OME_ENV_TAG=production
OME_BUILD_TYPE=Release

# --- Logging ---
OME_LOG_LEVEL=warn
OME_LOG_TO_CONSOLE=false
OME_LOG_TO_FILE=true
OME_LOG_FILE_MAX_SIZE_MB=100
OME_LOG_FILE_MAX_COUNT=5
OME_LOG_FORMAT=[%Y-%m-%d %H:%M:%S.%e] [%l] %v
OME_LOG_PERFORMANCE_METRICS=false

# --- Debug Tools ---
OME_DEBUG_OVERLAY=false
OME_DEBUG_FRAME_COUNTER=false
OME_DEBUG_PIPELINE_STATS=false
OME_DEBUG_MEMORY_TRACKING=false
OME_DEBUG_GPU_MARKERS=false

# --- Watermark ---
OME_WATERMARK_ENABLED=false
OME_WATERMARK_TEXT=
OME_WATERMARK_POSITION=
OME_WATERMARK_OPACITY=0
OME_WATERMARK_FONT_SIZE=0

# --- License ---
OME_LICENSE_CHECK=true
OME_LICENSE_FILE=./license/openmedia.lic
OME_FEATURE_LIMIT_NONE=false
OME_LICENSE_SERVER_URL=https://license.openmedia.io/api/v1
OME_LICENSE_GRACE_PERIOD_HOURS=72

# --- Build ---
OME_COMPILER_OPTIMIZATIONS=O2
OME_DEBUG_SYMBOLS=false
OME_SANITIZERS=
OME_PROFILING=false
OME_LTO=true
OME_STRIP_SYMBOLS=true

# --- Output Limits (theo license tier) ---
OME_MAX_OUTPUT_RESOLUTION=7680x4320
OME_MAX_OUTPUT_FRAMERATE=120
OME_MAX_ENCODE_BITRATE_MBPS=200
OME_MAX_SIMULTANEOUS_OUTPUTS=32
OME_DEMO_TIME_LIMIT_MINUTES=0

# --- Feature Flags (theo license tier) ---
OME_FEATURE_GPU_ACCELERATION=true
OME_FEATURE_NDI=license
OME_FEATURE_SRT=license
OME_FEATURE_WEBRTC=license
OME_FEATURE_ST2110=license
OME_FEATURE_HTML_OVERLAY=license
OME_FEATURE_AI_FILTERS=license
OME_FEATURE_REPLAY=license
OME_FEATURE_PLUGINS=true

# --- Telemetry ---
OME_TELEMETRY_ENABLED=true
OME_TELEMETRY_ENDPOINT=https://telemetry.openmedia.io/api/v1
OME_TELEMETRY_INTERVAL_SECONDS=3600
OME_CRASH_REPORTING=true
OME_CRASH_REPORT_DIR=./crash_dumps
OME_CRASH_REPORT_UPLOAD=true

# --- Hot Reload ---
OME_HOT_RELOAD_PLUGINS=false
OME_HOT_RELOAD_CONFIG=false
OME_HOT_RELOAD_SHADERS=false

# --- Security ---
OME_SECURE_MEMORY=true
OME_API_KEY_VALIDATION=true
OME_ENCRYPTED_CONFIG=true
```

---

## 3. CMake Environment Loading

### `cmake/EnvironmentConfig.cmake`

```cmake
# ============================================================
# EnvironmentConfig.cmake
# Load environment variables based on OME_ENV_TAG
# ============================================================

# Determine environment tag
if(NOT DEFINED OME_ENV_TAG)
    set(OME_ENV_TAG "demo" CACHE STRING "Environment tag: demo or production")
endif()

message(STATUS "╔══════════════════════════════════════════╗")
message(STATUS "║  OpenMedia SDK Environment: ${OME_ENV_TAG}")
message(STATUS "╚══════════════════════════════════════════╝")

# Load shared environment
set(ENV_SHARED_FILE "${CMAKE_SOURCE_DIR}/.env.shared")
set(ENV_TAG_FILE "${CMAKE_SOURCE_DIR}/.env.${OME_ENV_TAG}")

# Parse .env file helper function
function(load_env_file FILE_PATH)
    if(EXISTS "${FILE_PATH}")
        message(STATUS "Loading env file: ${FILE_PATH}")
        file(STRINGS "${FILE_PATH}" ENV_LINES)
        foreach(LINE IN LISTS ENV_LINES)
            # Skip comments and empty lines
            string(REGEX MATCH "^[#]" IS_COMMENT "${LINE}")
            string(STRIP LINE_STRIPPED "${LINE}")
            if(NOT IS_COMMENT AND NOT "${LINE_STRIPPED}" STREQUAL "")
                string(REGEX MATCH "^([^=]+)=(.*)$" _ "${LINE_STRIPPED}")
                if(CMAKE_MATCH_1 AND CMAKE_MATCH_2)
                    string(STRIP KEY "${CMAKE_MATCH_1}")
                    string(STRIP VALUE "${CMAKE_MATCH_2}")
                    set("${KEY}" "${VALUE}" PARENT_SCOPE)
                    message(VERBOSE "  ${KEY} = ${VALUE}")
                endif()
            endif()
        endforeach()
    else()
        message(WARNING "Environment file not found: ${FILE_PATH}")
    endif()
endfunction()

# Load shared first, then tag-specific (overrides shared)
load_env_file("${ENV_SHARED_FILE}")
load_env_file("${ENV_TAG_FILE}")

# Apply build type
if(OME_BUILD_TYPE)
    set(CMAKE_BUILD_TYPE "${OME_BUILD_TYPE}" CACHE STRING "" FORCE)
endif()

# Apply compiler settings based on environment
if(OME_ENV_TAG STREQUAL "production")
    add_compile_definitions(OME_PRODUCTION=1)
    add_compile_definitions(NDEBUG)
    if(OME_LTO)
        set(CMAKE_INTERPROCEDURAL_OPTIMIZATION TRUE)
    endif()
elseif(OME_ENV_TAG STREQUAL "demo")
    add_compile_definitions(OME_DEMO=1)
    add_compile_definitions(OME_DEBUG=1)
    if(OME_SANITIZERS)
        add_compile_options(-fsanitize=${OME_SANITIZERS})
        add_link_options(-fsanitize=${OME_SANITIZERS})
    endif()
endif()

# Generate config header
configure_file(
    "${CMAKE_SOURCE_DIR}/cmake/OpenMediaConfig.h.in"
    "${CMAKE_BINARY_DIR}/generated/OpenMediaConfig.h"
    @ONLY
)
```

---

## 4. C++ Config Header Template

### `cmake/OpenMediaConfig.h.in`

```cpp
#pragma once

// ============================================================
// OpenMedia SDK — Auto-generated Configuration
// Environment: @OME_ENV_TAG@
// Generated at build time by CMake
// ============================================================

// --- Project Info ---
#define OME_PROJECT_NAME     "@OME_PROJECT_NAME@"
#define OME_COMPANY_NAME     "@OME_COMPANY_NAME@"
#define OME_VERSION_MAJOR    @OME_VERSION_MAJOR@
#define OME_VERSION_MINOR    @OME_VERSION_MINOR@
#define OME_VERSION_PATCH    @OME_VERSION_PATCH@
#define OME_VERSION_STRING   "@OME_VERSION_STRING@"

// --- Environment Tag ---
#define OME_ENV_TAG          "@OME_ENV_TAG@"

#if defined(OME_PRODUCTION)
    #define OME_IS_PRODUCTION 1
    #define OME_IS_DEMO       0
#elif defined(OME_DEMO)
    #define OME_IS_PRODUCTION 0
    #define OME_IS_DEMO       1
#endif

// --- Features ---
#define OME_WATERMARK_ENABLED     @OME_WATERMARK_ENABLED@
#define OME_LICENSE_CHECK          @OME_LICENSE_CHECK@
#define OME_DEBUG_OVERLAY          @OME_DEBUG_OVERLAY@
#define OME_TELEMETRY_ENABLED      @OME_TELEMETRY_ENABLED@
```

---

## 5. Runtime Environment Detection (C++)

```cpp
// src/core/include/openmedia/core/Config.h

#pragma once
#include <string>
#include <unordered_map>

namespace openmedia::core {

enum class EnvironmentTag {
    Demo,
    Production
};

class EnvironmentConfig {
public:
    static EnvironmentConfig& Instance();

    // Get current environment tag
    EnvironmentTag GetTag() const;
    std::string GetTagString() const;

    // Check environment
    bool IsDemo() const;
    bool IsProduction() const;

    // Get config value
    std::string Get(const std::string& key, const std::string& defaultValue = "") const;
    int GetInt(const std::string& key, int defaultValue = 0) const;
    bool GetBool(const std::string& key, bool defaultValue = false) const;
    double GetDouble(const std::string& key, double defaultValue = 0.0) const;

    // Feature flags
    bool IsFeatureEnabled(const std::string& featureName) const;

private:
    EnvironmentConfig();
    void LoadFromFile(const std::string& filePath);
    void LoadFromSystem();

    EnvironmentTag m_tag;
    std::unordered_map<std::string, std::string> m_values;
};

} // namespace openmedia::core
```

---

## 6. Runtime Environment Detection (.NET)

```csharp
// wrappers/OpenMedia.Core.NET/EnvironmentConfig.cs

namespace OpenMedia.Core
{
    public enum EnvironmentTag
    {
        Demo,
        Production
    }

    public static class EnvironmentConfig
    {
        public static EnvironmentTag CurrentTag { get; private set; }
        public static bool IsDemo => CurrentTag == EnvironmentTag.Demo;
        public static bool IsProduction => CurrentTag == EnvironmentTag.Production;

        static EnvironmentConfig()
        {
            var tag = Environment.GetEnvironmentVariable("OME_ENV_TAG") ?? "demo";
            CurrentTag = tag.ToLower() switch
            {
                "production" => EnvironmentTag.Production,
                _ => EnvironmentTag.Demo
            };
        }

        public static string Get(string key, string defaultValue = "")
        {
            return Environment.GetEnvironmentVariable(key) ?? defaultValue;
        }

        public static bool IsFeatureEnabled(string feature)
        {
            var value = Get($"OME_FEATURE_{feature.ToUpper()}", "false");
            return value == "true" || value == "license"; // license = check license server
        }
    }
}
```

---

## 7. Build Commands

```powershell
# Build cho Demo
cmake -B build-demo -DOME_ENV_TAG=demo -DCMAKE_BUILD_TYPE=Debug ..
cmake --build build-demo

# Build cho Production
cmake -B build-prod -DOME_ENV_TAG=production -DCMAKE_BUILD_TYPE=Release ..
cmake --build build-prod --config Release

# Hoặc sử dụng build script
.\tools\scripts\build.ps1 -Environment demo
.\tools\scripts\build.ps1 -Environment production
```

---

## 8. So sánh Demo vs Production

| Tính năng | Demo | Production |
|-----------|------|-----------|
| Build Type | Debug | Release |
| Optimization | O0 (no opt) | O2 + LTO |
| Debug Symbols | ✅ Có | ❌ Không |
| Sanitizers | ✅ ASan + UBSan | ❌ Tắt |
| Logging | Debug (verbose) | Warn (minimal) |
| Console Log | ✅ Có | ❌ Không |
| Watermark | ✅ "DEMO" | ❌ Không |
| Debug Overlay | ✅ FPS, Pipeline stats | ❌ Tắt |
| License Check | ❌ Tắt | ✅ Bật |
| Feature Limits | Không giới hạn | Theo license tier |
| Hot Reload | ✅ Plugins, config, shaders | ❌ Tắt |
| Telemetry | ❌ Tắt | ✅ Bật |
| Crash Upload | ❌ Local only | ✅ Upload |
| Memory Tracking | ✅ Bật | ❌ Tắt |
| Security | Basic | Full encryption |
