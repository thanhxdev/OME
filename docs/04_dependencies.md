# OpenMedia SDK — Dependencies & Third-Party Libraries

**Version:** 1.0  
**Date:** July 2026

---

## 1. Quản lý Dependencies

Dự án sử dụng **2 phương thức** quản lý thư viện bên thứ ba:

| Phương thức | Dùng cho | Lưu trữ |
|-------------|----------|---------|
| **vcpkg** (manifest mode) | Thư viện open-source phổ biến | `vcpkg.json` tại root |
| **Vendored** (bundled) | SDK độc quyền, pre-built binaries | `third_party/` trong dự án |

> **Nguyên tắc:** Mọi thư viện cần thiết đều được lưu cùng dự án sản phẩm (trong `third_party/` hoặc qua vcpkg manifest) để đảm bảo build reproducible.

---

## 2. Core Dependencies (vcpkg)

### vcpkg.json manifest

```json
{
  "name": "openmedia-sdk",
  "version": "1.0.0",
  "description": "OpenMedia SDK - High-Performance Media Engine",
  "dependencies": [
    {
      "name": "ffmpeg",
      "features": [
        "avformat",
        "avcodec",
        "avutil",
        "swscale",
        "swresample",
        "avfilter",
        "nvcodec",
        "x264",
        "x265",
        "aom",
        "opus",
        "fdk-aac",
        "srt"
      ],
      "version>=": "6.1"
    },
    {
      "name": "spdlog",
      "version>=": "1.13.0"
    },
    {
      "name": "nlohmann-json",
      "version>=": "3.11.3"
    },
    {
      "name": "gtest",
      "version>=": "1.14.0"
    },
    {
      "name": "benchmark",
      "version>=": "1.8.3"
    },
    {
      "name": "cef",
      "version>=": "120"
    },
    {
      "name": "libsrt",
      "version>=": "1.5.3"
    },
    {
      "name": "librist",
      "version>=": "0.2.10"
    },
    {
      "name": "protobuf",
      "version>=": "25.0"
    },
    {
      "name": "grpc",
      "version>=": "1.60.0"
    },
    {
      "name": "catch2",
      "version>=": "3.5.0"
    },
    {
      "name": "imgui",
      "version>=": "1.90"
    },
    {
      "name": "stb",
      "version>=": "2023.12"
    },
    {
      "name": "fmt",
      "version>=": "10.2.0"
    },
    {
      "name": "toml11",
      "version>=": "3.8.0"
    },
    {
      "name": "concurrentqueue",
      "version>=": "1.0.4"
    }
  ],
  "builtin-baseline": "latest",
  "overrides": []
}
```

---

## 3. Vendored / Bundled Libraries (third_party/)

Các SDK độc quyền hoặc pre-built binaries **không có trong vcpkg** sẽ được lưu trực tiếp trong `third_party/`:

### 3.1 FFmpeg Pre-built

| Thành phần | Mô tả |
|-----------|-------|
| Thư mục | `third_party/ffmpeg/` |
| Version | 6.1+ (GPL or LGPL tùy license model) |
| Bao gồm | libavformat, libavcodec, libavutil, libswscale, libswresample, libavfilter |
| Platforms | `lib/x64-windows/`, `lib/x64-linux/` |
| License | LGPL 2.1+ (hoặc GPL nếu dùng x264/x265) |

### 3.2 NDI SDK

| Thành phần | Mô tả |
|-----------|-------|
| Thư mục | `third_party/ndi_sdk/` |
| Provider | Vizrt (NewTek) |
| Version | NDI 6 SDK |
| Bao gồm | Headers, runtime DLLs, import libraries |
| License | Proprietary (NDI SDK License Agreement) |
| Download | https://ndi.video/for-developers/ndi-sdk/ |

### 3.3 Blackmagic DeckLink SDK

| Thành phần | Mô tả |
|-----------|-------|
| Thư mục | `third_party/decklink_sdk/` |
| Provider | Blackmagic Design |
| Version | DeckLink SDK 13.x |
| Bao gồm | Headers (IDL-based interfaces), COM wrappers |
| License | Blackmagic SDK License |
| Download | https://www.blackmagicdesign.com/developer/product/capture-and-playback |

### 3.4 AJA NTV2 SDK

| Thành phần | Mô tả |
|-----------|-------|
| Thư mục | `third_party/aja_sdk/` |
| Provider | AJA Video Systems |
| Version | NTV2 SDK 17.x |
| Bao gồm | Headers, static/dynamic libraries |
| License | MIT (NTV2 is open-source) |
| Download | https://github.com/aja-video/ntv2 |

### 3.5 Magewell SDK

| Thành phần | Mô tả |
|-----------|-------|
| Thư mục | `third_party/magewell_sdk/` |
| Provider | Magewell |
| Bao gồm | Pro Capture SDK headers & libraries |
| License | Magewell SDK License |

### 3.6 Chromium Embedded Framework (CEF)

| Thành phần | Mô tả |
|-----------|-------|
| Thư mục | `third_party/cef/` |
| Version | CEF 120+ (Chromium-based) |
| Dùng cho | HTML Overlay rendering |
| Bao gồm | Headers, DLLs, resources |
| License | BSD |
| Download | https://cef-builds.spotifycdn.com/index.html |

### 3.7 NVIDIA Video Codec SDK

| Thành phần | Mô tả |
|-----------|-------|
| Thư mục | `third_party/nvidia_codec_sdk/` |
| Bao gồm | NVENC/NVDEC headers |
| License | NVIDIA Codec SDK License |
| Lưu ý | Runtime DLLs đi kèm driver, không cần bundle |

### 3.8 Intel oneVPL / Media SDK

| Thành phần | Mô tả |
|-----------|-------|
| Thư mục | `third_party/intel_onevpl/` |
| Bao gồm | oneVPL headers & dispatcher |
| License | MIT |
| Download | https://github.com/intel/libvpl |

---

## 4. Bảng tổng hợp License

| Library | License | Loại | Ghi chú |
|---------|---------|------|---------|
| FFmpeg | LGPL 2.1+ / GPL | Open Source | GPL nếu link x264/x265 statically |
| spdlog | MIT | Open Source | |
| nlohmann-json | MIT | Open Source | |
| Google Test | BSD-3 | Open Source | Chỉ dùng trong test |
| libsrt | MPL-2.0 | Open Source | |
| librist | BSD-2 | Open Source | |
| NDI SDK | Proprietary | Commercial | Cần license agreement |
| DeckLink SDK | Proprietary | Free | Free to use, ko redistribute |
| AJA NTV2 | MIT | Open Source | |
| Magewell SDK | Proprietary | Free | Free for Magewell hardware |
| CEF | BSD | Open Source | |
| NVIDIA Codec SDK | Proprietary | Free | Free, headers only |
| Intel oneVPL | MIT | Open Source | |
| fmt | MIT | Open Source | |
| protobuf | BSD-3 | Open Source | |
| gRPC | Apache-2.0 | Open Source | |
| imgui | MIT | Open Source | Optional, debug UI |
| concurrentqueue | BSD | Open Source | Lock-free queue |

---

## 5. Dependency Installation

### 5.1 Cài đặt vcpkg

```powershell
# Clone vcpkg vào third_party
git clone https://github.com/microsoft/vcpkg.git third_party/vcpkg
.\third_party\vcpkg\bootstrap-vcpkg.bat

# Cài dependencies từ manifest
.\third_party\vcpkg\vcpkg install --triplet x64-windows
```

### 5.2 Cài đặt Vendored SDKs

```powershell
# Script tự động tải và setup SDKs
.\tools\scripts\setup_env.ps1 -DownloadSDKs

# Hoặc manual:
# 1. Download NDI SDK → extract to third_party/ndi_sdk/
# 2. Download DeckLink SDK → extract to third_party/decklink_sdk/
# 3. Download CEF → extract to third_party/cef/
# ... (xem README.md trong third_party/)
```

### 5.3 CMake Integration

```cmake
# Dependencies.cmake
list(APPEND CMAKE_PREFIX_PATH "${CMAKE_SOURCE_DIR}/third_party")

# vcpkg toolchain
set(CMAKE_TOOLCHAIN_FILE "${CMAKE_SOURCE_DIR}/third_party/vcpkg/scripts/buildsystems/vcpkg.cmake")

# Find packages
find_package(FFmpeg REQUIRED COMPONENTS avformat avcodec avutil swscale swresample)
find_package(spdlog REQUIRED)
find_package(nlohmann_json REQUIRED)
find_package(GTest REQUIRED)
find_package(SRT REQUIRED)
```

---

## 6. Ghi chú quan trọng

> [!IMPORTANT]
> - Tất cả dependencies đều được lưu cùng dự án (vcpkg manifest hoặc vendored trong `third_party/`)
> - Không sử dụng system-installed libraries để đảm bảo build reproducible
> - Các SDK proprietary (NDI, DeckLink) chỉ include headers — runtime DLLs cần cài riêng trên máy đích

> [!WARNING]
> - FFmpeg license: Nếu sử dụng x264/x265 encoder, toàn bộ binary sẽ là GPL. Cần cân nhắc LGPL build nếu commercial distribution.
> - NDI SDK yêu cầu đăng ký developer account tại ndi.video trước khi download.
