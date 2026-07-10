# OpenMedia SDK — Coding Standards

**Version:** 1.0  
**Date:** July 2026

---

## 1. Ngôn ngữ & Standard

| Thành phần | Standard | Compiler |
|-----------|----------|----------|
| C++ Core | C++20 | MSVC 17.8+ / Clang 17+ |
| .NET Wrapper | C# 12 (.NET 8/9) | Roslyn |
| Build Scripts | PowerShell 7+ / Bash | Cross-platform |
| CMake | 3.28+ | — |

---

## 2. C++ Coding Style

### 2.1 Naming Conventions

| Element | Style | Example |
|---------|-------|---------|
| Namespace | `snake_case` | `openmedia::core` |
| Class / Struct | `PascalCase` | `MediaPipeline`, `FrameQueue` |
| Interface | `IPascalCase` | `IMediaObject`, `IPlugin` |
| Method | `PascalCase` | `CreateFileSource()`, `GetWidth()` |
| Variable (local) | `camelCase` | `frameCount`, `sourceUrl` |
| Member variable | `m_camelCase` | `m_pipeline`, `m_frameQueue` |
| Static member | `s_camelCase` | `s_instance` |
| Global constant | `kPascalCase` | `kMaxFrameQueueSize` |
| Enum class | `PascalCase` | `PixelFormat::NV12` |
| Macro | `OME_UPPER_CASE` | `OME_CORE_EXPORTS` |
| Template param | `PascalCase` | `template<typename T>` |
| File names | `PascalCase` | `MediaPipeline.h`, `FrameQueue.cpp` |

### 2.2 Code Style

```cpp
// Header guard: #pragma once (preferred over include guards)
#pragma once

// Includes: grouped and sorted
// 1. Corresponding header
// 2. Project headers
// 3. Third-party headers
// 4. System/STL headers

#include "MediaFrame.h"

#include <openmedia/core/Types.h>

#include <spdlog/spdlog.h>

#include <memory>
#include <string>
#include <vector>

namespace openmedia::core {

// Enum class with explicit underlying type
enum class PixelFormat : uint32_t {
    Unknown = 0,
    NV12,
    BGRA,
    YUV420P,
    YUV422P,
    RGB24,
};

// Class with RAII, no raw owning pointers
class MediaPipeline {
public:
    // Rule of five: explicitly default or delete
    MediaPipeline();
    ~MediaPipeline();
    MediaPipeline(const MediaPipeline&) = delete;
    MediaPipeline& operator=(const MediaPipeline&) = delete;
    MediaPipeline(MediaPipeline&&) noexcept;
    MediaPipeline& operator=(MediaPipeline&&) noexcept;

    // Public API — clear, documented
    /// @brief Add a source to the pipeline
    /// @param source The media source to add (ownership transferred)
    /// @return Reference to this pipeline for chaining
    MediaPipeline& SetSource(std::unique_ptr<IMediaObject> source);

    /// @brief Build and validate the pipeline
    /// @return Result with success or error details
    [[nodiscard]] Result<void> Build();

    /// @brief Start the pipeline
    [[nodiscard]] bool Start();

private:
    // Implementation (Pimpl pattern for ABI stability)
    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

} // namespace openmedia::core
```

### 2.3 Best Practices

- ✅ Sử dụng `[[nodiscard]]` cho functions mà return value không nên bị ignore
- ✅ Sử dụng `constexpr` / `consteval` khi có thể
- ✅ Sử dụng `std::string_view` thay vì `const std::string&` cho read-only strings
- ✅ Sử dụng `std::span` cho array views
- ✅ Sử dụng structured bindings: `auto [key, value] = map.find(k);`
- ✅ Sử dụng `enum class` thay vì `enum`
- ✅ Sử dụng `nullptr` thay vì `NULL` hoặc `0`
- ✅ Prefer `std::unique_ptr` over `std::shared_ptr` khi có thể
- ✅ Sử dụng Pimpl pattern cho public headers (ABI stability)
- ❌ Không dùng raw owning pointers (`new` / `delete`)
- ❌ Không dùng C-style casts — dùng `static_cast`, `dynamic_cast`, `reinterpret_cast`
- ❌ Không dùng `using namespace std;` trong headers
- ❌ Không dùng exceptions trong hot path (use Result type)

---

## 3. .NET / C# Coding Style

### 3.1 Naming Conventions

| Element | Style | Example |
|---------|-------|---------|
| Namespace | `PascalCase` | `OpenMedia.Core` |
| Class | `PascalCase` | `MediaPlayer` |
| Interface | `IPascalCase` | `IMediaObject` |
| Method | `PascalCase` | `CreateSource()` |
| Property | `PascalCase` | `Width`, `IsPlaying` |
| Field (private) | `_camelCase` | `_pipeline`, `_frameQueue` |
| Parameter | `camelCase` | `sourceUrl`, `frameCount` |
| Event | `PascalCase` | `OnError`, `OnFrameReady` |
| Const | `PascalCase` | `MaxRetries` |

### 3.2 C# Style

```csharp
namespace OpenMedia.Core;

/// <summary>
/// High-level media player for file and stream playback.
/// </summary>
public sealed class MediaPlayer : IDisposable
{
    private readonly IntPtr _nativeHandle;
    private bool _disposed;

    public MediaPlayer()
    {
        _nativeHandle = NativeBridge.CreateMediaPlayer();
    }

    /// <summary>
    /// Opens a media file or stream URL.
    /// </summary>
    /// <param name="url">File path or stream URL</param>
    /// <exception cref="MediaException">Thrown when file cannot be opened</exception>
    public void Open(string url)
    {
        ArgumentException.ThrowIfNullOrEmpty(url);
        var result = NativeBridge.MediaPlayer_Open(_nativeHandle, url);
        if (!result.IsSuccess)
            throw new MediaException(result.Error);
    }

    // Events
    public event EventHandler<MediaErrorEventArgs>? OnError;
    public event EventHandler<FrameEventArgs>? OnFrameReady;

    // IDisposable
    public void Dispose()
    {
        if (!_disposed)
        {
            NativeBridge.DestroyMediaPlayer(_nativeHandle);
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }

    ~MediaPlayer() => Dispose();
}
```

---

## 4. Documentation Standards

### 4.1 C++ (Doxygen)

```cpp
/// @brief Creates a new file source for reading media files.
///
/// Supports MP4, MOV, MXF, AVI, MKV and other formats via FFmpeg backend.
/// The file is opened and headers are parsed during creation.
///
/// @param path Path to the media file (UTF-8 encoded)
/// @param options Optional configuration for the reader
/// @return Unique pointer to the created FileSource, or nullptr on failure
///
/// @code
/// auto source = engine->CreateFileSource("video.mp4");
/// if (source) {
///     auto info = source->GetMediaInfo();
///     // ...
/// }
/// @endcode
///
/// @see LiveSource, DeviceSource
/// @since 1.0.0
std::unique_ptr<FileSource> CreateFileSource(
    std::string_view path,
    const FileSourceOptions& options = {}
);
```

### 4.2 C# (XML Doc)

```csharp
/// <summary>
/// Creates a new file source for reading media files.
/// </summary>
/// <param name="path">Path to the media file.</param>
/// <returns>A new <see cref="FileSource"/> instance.</returns>
/// <exception cref="FileNotFoundException">Thrown when the file does not exist.</exception>
/// <example>
/// <code>
/// var source = engine.CreateFileSource("video.mp4");
/// var info = source.GetMediaInfo();
/// </code>
/// </example>
public FileSource CreateFileSource(string path);
```

---

## 5. Git Conventions

### 5.1 Branch Strategy

```
main              ← Stable releases
├── develop       ← Integration branch
│   ├── feature/core-pipeline
│   ├── feature/srt-engine
│   ├── feature/ndi-support
│   ├── bugfix/frame-queue-deadlock
│   └── refactor/gpu-context
├── release/1.0
└── hotfix/1.0.1
```

### 5.2 Commit Messages

```
<type>(<scope>): <subject>

<body>

<footer>
```

**Types:** `feat`, `fix`, `refactor`, `test`, `docs`, `build`, `ci`, `perf`, `chore`

**Examples:**
```
feat(core): add lock-free frame queue implementation

Implemented moodycamel::ConcurrentQueue-based frame queue
for zero-contention producer-consumer pattern.

Performance: ~200M ops/sec on 12-core Ryzen.
Closes #42

fix(codecs): resolve H.265 encoder memory leak on flush

The encoder was not releasing reference frames during
the flush operation, causing ~50MB leak per session.

Fixes #108
```

---

## 6. .clang-format

```yaml
# .clang-format
Language: Cpp
BasedOnStyle: Google
IndentWidth: 4
TabWidth: 4
UseTab: Never
ColumnLimit: 120
BreakBeforeBraces: Attach
AllowShortFunctionsOnASingleLine: Inline
AllowShortIfStatementsOnASingleLine: Never
NamespaceIndentation: None
AccessModifierOffset: -4
PointerAlignment: Left
SortIncludes: CaseInsensitive
IncludeBlocks: Preserve
```

---

## 7. .clang-tidy

```yaml
# .clang-tidy
Checks: >
  -*,
  bugprone-*,
  clang-analyzer-*,
  cppcoreguidelines-*,
  modernize-*,
  performance-*,
  readability-*,
  -modernize-use-trailing-return-type,
  -readability-magic-numbers,
  -cppcoreguidelines-avoid-magic-numbers

WarningsAsErrors: ''
HeaderFilterRegex: 'src/.*'
```

---

## 8. Code Review Checklist

- [ ] Follows naming conventions
- [ ] Includes unit tests for new functionality
- [ ] No memory leaks (RAII, smart pointers)
- [ ] Thread-safe where required
- [ ] Error handling (Result type, no unhandled exceptions)
- [ ] Documentation (Doxygen/XML comments)
- [ ] No hardcoded values (use Config/Constants)
- [ ] Builds in both demo and production environments
- [ ] No compiler warnings
- [ ] Performance considerations documented
