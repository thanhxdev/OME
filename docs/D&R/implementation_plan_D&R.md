# Kế hoạch Thực hiện: OpenMedia.Platform SDK (MPlatform-Equivalent Layer)

**Tài liệu:** Kế hoạch Thiết kế & Triển khai (D&R)  
**Phiên bản:** 2.1 (Đã phê duyệt 4 Quyết định Kiến trúc)  
**Mục tiêu:** Đơn giản hóa OpenMedia SDK đạt trải nghiệm phát triển tối giản và hiệu quả như Medialooks MPlatform SDK.

---

## 1. Các Quyết định Kỹ thuật đã phê duyệt (Ratified Decisions)

| # | Hạng mục | Quyết định phê duyệt | Quy chuẩn kỹ thuật |
|---|---|---|---|
| **D1** | **Naming & Namespace** | **`OpenMedia.Platform`** | Tầng High-Level API chính thức được đặt tên là `OpenMedia.Platform` (tương đương Medialooks *MPlatform*). |
| **D2** | **Server Distribution** | **Độc lập (Independent)** | `OpenMediaServer.exe` được cài đặt/phân phối riêng, không embed vào NuGet package. `OpenMediaRuntime` áp dụng cơ chế tự động dò tìm (Discovery Path: `OPENMEDIA_SERVER_PATH` env var, Registry, App Config, hoặc Standard Install Directory). |
| **D3** | **Preview Framework** | **WPF (Phase 1) → WinUI 3 (Phase 2)** | Tập trung phát triển & tối ưu bộ Preview Control cho **WPF** (`D3DImage` / D3D11 Native HwndHost) ở Phase 1; mở rộng sang WinUI 3 (`SwapChainPanel`) ở Phase 2. |
| **D4** | **API Architecture** | **Dual-Tier Model** | **Tier 1 (High-Level - `OpenMedia.Platform`)**: Dành cho developer phổ thông (3–5 dòng code).\n**Tier 2 (Low-Level - `OpenMedia.SDK` / `OpenMedia.Core`)**: Dành cho power users, plugin developers cần can thiệp trực tiếp DAG Graph, Frame buffers, IPC raw. |

---

## 2. Phân tích So sánh & Đối chiếu (OpenMedia vs Medialooks)

### 2.1 Trải nghiệm Lập trình viên (Developer Experience)

| Tiêu chí | Medialooks MPlatform | OpenMedia Hiện tại | OpenMedia.Platform (Sau cải tiến) |
|---|---|---|---|
| **Số dòng code Play File + Preview** | 3–5 dòng | 30+ dòng | **3 dòng** |
| **Quản lý Server Process** | Tự động (Internal) | Lộ ra ngoài, thủ công | **Tự động qua `OpenMediaRuntime`** |
| **IPC / Shared Texture** | Ẩn hoàn toàn | Phải tự request NT Handles | **Ẩn 100% trong `AttachPreview()`** |
| **Preview Rendering** | 1 dòng (`PreviewWindowSet`) | Viết renderer D3D11 thủ công | **1 dòng (`player.AttachPreview(control)`)** |
| **Lập trình bất đồng bộ** | Không hỗ trợ (Blocking COM) | Có hỗ trợ một phần | **Hỗ trợ toàn diện `async/await` chuẩn C#** |
| **Phụ thuộc COM / Windows** | Nặng nề COM, Win32 | Không phụ thuộc COM | **Không COM, kiến trúc hiện đại .NET 8/9** |

### 2.2 Bảng ánh xạ đối tượng (Object Mapping)

| Nghiệp vụ Broadcast / Media | Medialooks MPlatform | OpenMedia Low-Level (`OpenMedia.SDK`) | OpenMedia High-Level (`OpenMedia.Platform`) |
|---|---|---|---|
| **Phát file / URL stream** | `MFileClass` | `FileSource` + `Pipeline` + `IPC` | **`MediaPlayer`** |
| **Phát danh sách phát** | `MPlaylistClass` | `Playlist` + `Pipeline` | **`MediaPlaylist`** |
| **Bộ trộn hình / âm thanh** | `MMixerClass` | `Mixer` + `AudioMixer` + Nodes | **`VideoMixer`** |
| **Đầu ra phát sóng (RTMP, SRT, NDI)** | `MWriterClass` | `RTMPOutput` / `SRTEngine` / `NDIEngine` | **`StreamOutput`** |
| **Thu thiết bị (Camera, DeckLink)** | `MLiveClass` | `DeviceSource` | **`DeviceCapture`** |
| **Hiển thị Preview lên UI** | `PreviewWindowSet(hwnd)` | `D3D11SwapChainPanelRenderer` | **`AttachPreview(wpfView)` / `<om:VideoView/>`** |

---

## 3. Kiến trúc Phân tầng (Layered Architecture)

```
┌────────────────────────────────────────────────────────────────────────┐
│                   APPLICATIONS (WPF / WinUI / Console)                 │
├────────────────────────────────────────────────────────────────────────┤
│  🟢 HIGH-LEVEL API: namespace OpenMedia.Platform (TIER 1)              │
│     ├── OpenMediaRuntime       (Discovery & Lifecycle Controller)      │
│     ├── MediaPlayer            (Smart Player: File, Stream, Camera)    │
│     ├── VideoMixer             (Multi-layer Vision Mixer & Switcher)   │
│     ├── MediaPlaylist          (Seamless Playout & Transitions)        │
│     ├── StreamOutput           (RTMP, SRT, NDI, WebRTC, File Egress)   │
│     ├── DeviceCapture          (DirectShow, DeckLink, Screen Capture)  │
│     └── Controls.Wpf           (OpenMediaVideoView - WPF Control)      │
├────────────────────────────────────────────────────────────────────────┤
│  🔵 LOW-LEVEL API: namespace OpenMedia.SDK / Core (TIER 2)             │
│     ├── Pipeline               (DAG Graph & Custom Topology)           │
│     ├── Sources & Sinks        (FileSource, LiveSource, CallbackOutput)│
│     ├── IPCClient              (Named Pipes & Frame Stream Protocol)   │
│     └── NativeBridge           (P/Invoke C-API Exports)                │
├────────────────────────────────────────────────────────────────────────┤
│  ⚙️ MEDIA ENGINE PROCESS (INDEPENDENT)                                 │
│     ├── OpenMediaServer.exe    (Chạy độc lập trên máy chủ / client)    │
│     ├── Shared Memory Buffers  (Zero-copy CPU frames)                  │
│     └── D3D11 Shared Textures  (Zero-copy GPU DXGI NT Handles)         │
└────────────────────────────────────────────────────────────────────────┘
```

---

## 4. Đặc tả API tầng High-Level (`OpenMedia.Platform`)

### 4.1 `OpenMediaRuntime` — Quản lý Server & Tự động dò tìm (Discovery)

```csharp
namespace OpenMedia.Platform
{
    public static class OpenMediaRuntime
    {
        /// <summary>
        /// Khởi tạo và tự động kết nối tới OpenMediaServer.exe theo các quy tắc Discovery:
        /// 1. Biến môi trường OPENMEDIA_SERVER_PATH
        /// 2. App.config / appsettings.json
        /// 3. Registry: HKLM\Software\OpenMedia\ServerPath
        /// 4. Đường dẫn mặc định: %ProgramFiles%\OpenMedia\bin\OpenMediaServer.exe
        /// </summary>
        public static Task<bool> InitializeAsync(RuntimeOptions? options = null);
        
        /// <summary>
        /// Đóng kết nối và giải phóng tài nguyên.
        /// </summary>
        public static void Shutdown();

        public static bool IsConnected { get; }
        public static Version EngineVersion { get; }
    }
}
```

### 4.2 `MediaPlayer` — Trải nghiệm 3 dòng code

```csharp
namespace OpenMedia.Platform
{
    public class MediaPlayer : IDisposable
    {
        public MediaPlayer();
        public MediaPlayer(string sourceUri);

        // Core Actions
        public Task OpenAsync(string sourceUri);
        public Task PlayAsync();
        public Task PauseAsync();
        public Task StopAsync();
        public Task SeekAsync(TimeSpan position);

        // UI Preview Binding (WPF Control hoặc Handle)
        public void AttachPreview(object previewControl);
        public void DetachPreview();

        // Properties
        public TimeSpan Position { get; set; }
        public TimeSpan Duration { get; }
        public double Volume { get; set; }
        public PlaybackState State { get; }
        public MediaInfo Information { get; }

        // Events
        public event EventHandler<PlaybackState> StateChanged;
        public event EventHandler<TimeSpan> PositionChanged;
        public event EventHandler<MediaErrorEventArgs> ErrorOccurred;
        public event EventHandler EndOfMedia;
    }
}
```

### 4.3 `VideoMixer` & `StreamOutput` — Broadcast Switching & Streaming

```csharp
namespace OpenMedia.Platform
{
    public class VideoMixer : IDisposable
    {
        public VideoMixer(int width = 1920, int height = 1080, double frameRate = 29.97);

        public int AddSource(string uri);
        public int AddSource(MediaPlayer player);
        public int AddDevice(string deviceName);

        public void AttachPreview(object previewControl);
        public void AddOutput(StreamOutput output);

        public Task SwitchToAsync(int layerIndex, TransitionType transition = TransitionType.Cut, TimeSpan? duration = null);
        public Task StartAsync();
        public Task StopAsync();
    }

    public class StreamOutput : IDisposable
    {
        public static StreamOutput RTMP(string url, StreamQuality quality = StreamQuality.High1080p);
        public static StreamOutput SRT(string host, int port, SRTMode mode = SRTMode.Caller);
        public static StreamOutput NDI(string streamName);
        public static StreamOutput File(string targetPath, RecordFormat format = RecordFormat.MP4);
        public static StreamOutput WebRTC(string signalingServerUri);
    }
}
```

---

## 5. Lộ trình Triển khai Chi tiết (Implementation Roadmap)

### 📌 Phase A: Nền tảng & WPF Preview (Ước tính: 2-3 tuần) 🔴

> [!IMPORTANT]
> Xây dựng bộ khung `OpenMedia.Platform` và giải quyết dứt điểm cơ chế Preview trên **WPF**.

| Task ID | Hạng mục công việc | Mô tả kỹ thuật | Deliverables |
|---|---|---|---|
| **A1** | **Cấu trúc Project `OpenMedia.Platform`** | Tạo .NET 8 Class Library project `OpenMedia.Platform.csproj` tham chiếu `OpenMedia.Core.NET`. | `wrappers/OpenMedia.Platform/` |
| **A2** | **`OpenMediaRuntime` & Discovery** | Triển khai logic dò tìm `OpenMediaServer.exe` (Env var, Registry, Default Path), kiểm tra IPC heartbeat, tự động kết nối. | `OpenMediaRuntime.cs` |
| **A3** | **WPF Preview Engine (`D3DImage` / `HwndHost`)** | Xây dựng Renderer D3D11 chuyên dụng cho WPF tiếp nhận DXGI Shared Texture từ Server process. | `WpfD3D11Renderer.cs`, `VideoView.xaml.cs` |
| **A4** | **Kiểm thử Preview WPF** | Tạo project mẫu WPF `samples/dotnet/WpfQuickPlayer` phát video mp4 trong 3 dòng code. | `WpfQuickPlayer.csproj` |

---

### 📌 Phase B: Lớp Đối tượng Nghiệp vụ High-Level (Ước tính: 3-4 tuần) 🔴

| Task ID | Hạng mục công việc | Mô tả kỹ thuật | Deliverables |
|---|---|---|---|
| **B1** | **`MediaPlayer`** | Đóng gói toàn bộ vòng đời phát File / Network Stream / Seek / Volume / State Events. | `MediaPlayer.cs` |
| **B2** | **`VideoMixer`** | Bộ trộn đa kênh, quản lý Layer Z-index, chuyển cảnh Cut/Dissolve, hỗ trợ Output đa đích. | `VideoMixer.cs` |
| **B3** | **`StreamOutput`** | Bộ cấu hình Output 1 dòng cho RTMP, SRT, NDI, MP4 File Recording. | `StreamOutput.cs` |
| **B4** | **`DeviceCapture`** | Liệt kê và mở webcam, card DeckLink, desktop capture trực tiếp ra `MediaPlayer`. | `DeviceCapture.cs` |

---

### 📌 Phase C: Mở rộng Playlist & Hỗ trợ WinUI 3 (Ước tính: 2-3 tuần) 🟡

| Task ID | Hạng mục công việc | Mô tả kỹ thuật | Deliverables |
|---|---|---|---|
| **C1** | **`MediaPlaylist`** | Playlist tự động chuyển bài liền mạch (gapless transition), loop, shuffle, lịch phát. | `MediaPlaylist.cs` |
| **C2** | **WinUI 3 Preview (`SwapChainPanel`)** | Tích hợp thêm hỗ trợ WinUI 3 thông qua `D3D11SwapChainPanelRenderer` vào `AttachPreview()`. | `WinUIVideoView.cs` |
| **C3** | **Overlay Fluent API** | Cung cấp DSL đơn giản: `player.Overlay.AddText("Title").AtTopRight();`. | `OverlayExtensions.cs` |

---

### 📌 Phase D: Mẫu Ứng dụng & Hoàn thiện Tài liệu (Ước tính: 1-2 tuần) 🟢

| Task ID | Hạng mục công việc | Mô tả kỹ thuật | Deliverables |
|---|---|---|---|
| **D1** | **Bộ Samples hoàn chỉnh** | 5 Sample ứng dụng WPF và Console (QuickPlayer, VisionSwitcher, LiveStreamer, NdiBridge, PlaylistPlayout). | `samples/dotnet/` |
| **D2** | **Quick Start Guide & Docs** | Hướng dẫn "Từ số 0 đến Play Video trong 60 giây", XML IntelliSense Documentation đầy đủ. | `docs/quickstart_platform.md` |

---

## 6. Kịch bản Minh họa Sử dụng (End-User Code Examples)

### Ví dụ 1: Phát Video trên WPF (Chỉ 3 dòng lệnh)

```csharp
// MainWindow.xaml.cs (WPF)
using OpenMedia.Platform;

public partial class MainWindow : Window
{
    private MediaPlayer _player;

    public async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _player = new MediaPlayer("C:\\Videos\\sample.mp4");
        _player.AttachPreview(WpfVideoControl); // WpfVideoControl là control trên XAML
        await _player.PlayAsync();
    }
}
```

### Ví dụ 2: Trộn 2 Camera và Phát trực tiếp YouTube (RTMP)

```csharp
using OpenMedia.Platform;

// Khởi tạo Mixer 1080p
var mixer = new VideoMixer(1920, 1080);
mixer.AddSource("camera1.mp4");
mixer.AddSource("camera2.mp4");

// Gắn Preview ra màn hình và Đẩy luồng RTMP
mixer.AttachPreview(wpfPreviewPanel);
mixer.AddOutput(StreamOutput.RTMP("rtmp://a.rtmp.youtube.com/live2/YOUR_STREAM_KEY"));

await mixer.StartAsync();

// Chuyển sang Camera 2 với hiệu ứng Dissolve trong 1 giây
await mixer.SwitchToAsync(1, TransitionType.Dissolve, TimeSpan.FromSeconds(1));
```

### Ví dụ 3: Thu Camera và Hiển thị Preview

```csharp
using OpenMedia.Platform;

// Liệt kê thiết bị và mở webcam
var devices = await DeviceCapture.EnumerateDevicesAsync();
var cam = await DeviceCapture.OpenAsync(devices[0].Name);
cam.AttachPreview(wpfVideoView);
await cam.PlayAsync();
```

### Ví dụ 4: Playlist Phát liên tục (Gapless)

```csharp
using OpenMedia.Platform;

var playlist = new MediaPlaylist();
playlist.Add("video1.mp4");
playlist.Add("video2.mp4");
playlist.Add("video3.mp4");
playlist.LoopMode = LoopMode.All;
playlist.AttachPreview(wpfVideoView);
await playlist.PlayAsync();

// Chuyển bài
await playlist.NextAsync();
```

### Ví dụ 5: NDI Bridge — Nhận NDI và Xuất RTMP

```csharp
using OpenMedia.Platform;

// Nhận NDI stream và chuyển tiếp qua RTMP
var mixer = new VideoMixer(1920, 1080);
mixer.AddSource("ndi://My NDI Source");
mixer.AddOutput(StreamOutput.RTMP("rtmp://a.rtmp.youtube.com/live2/KEY"));
mixer.AddOutput(StreamOutput.File("recording.mp4"));
await mixer.StartAsync();
```

### Ví dụ 6: Desktop Capture + SRT Output

```csharp
using OpenMedia.Platform;

// Capture desktop và stream qua SRT
var desktop = await DeviceCapture.CaptureDesktopAsync(0);
desktop.AttachPreview(wpfVideoView);

var mixer = new VideoMixer(1920, 1080);
mixer.AddSource(desktop);
mixer.AddOutput(StreamOutput.SRT("192.168.1.100", 9000, SRTMode.Caller));
await mixer.StartAsync();
```

---

## 7. Tiêu chí Đánh giá & Nghiệm thu (Verification & Acceptance Criteria)

1. **Tiêu chí Độ tinh gọn Code**: Mọi tác vụ cơ bản (Play file, Capture webcam, Stream RTMP) không vượt quá 5 dòng lệnh C#.
2. **Tiêu chí Ổn định & Cách ly Process**: Nếu `OpenMediaServer.exe` bị tắt đột ngột, ứng dụng WPF Client không bị crash/SEHException mà kích hoạt sự kiện `OpenMediaRuntime.ServerDisconnected`.
3. **Tiêu chí Hiệu năng Preview trên WPF**: Độ trễ render GPU DXGI Shared Texture < 1 frame (<16ms ở 60fps), mức tiêu thụ CPU phía Client < 2%.
4. **Tiêu chí Tương thích**: Chạy mượt mà trên môi trường .NET 8 / .NET 9 (x64) trên Windows 10/11.
5. **Tiêu chí Security**: Named Pipe ACL chỉ cho phép current user SID truy cập.
6. **Tiêu chí Diagnostics**: Trace logs đầy đủ cho mọi IPC command (có thể bật/tắt qua `RuntimeOptions`).
7. **Tiêu chí Dispose**: Mọi `IDisposable` objects giải phóng đúng server resources, không memory leak.
8. **Tiêu chí IntelliSense**: XML Documentation `<summary>`, `<param>`, `<returns>`, `<example>` đầy đủ cho tất cả public API.

---

## 8. Đánh giá Rủi ro & Chiến lược Giảm thiểu (Risk Assessment)

| # | Rủi ro | Xác suất | Tác động | Chiến lược Giảm thiểu |
|---|---|---|---|---|
| **R1** | DXGI Shared Texture không hoạt động trên một số GPU | Trung bình | Cao | Triển khai fallback `HwndHost` path (CPU copy); kiểm thử trên Intel/NVIDIA/AMD |
| **R2** | IPC Named Pipe performance bottleneck | Thấp | Cao | Benchmark IPC latency sớm ở Phase A; có thể chuyển sang Memory-Mapped File nếu cần |
| **R3** | WPF `D3DImage` flicker / tearing | Trung bình | Trung bình | Sử dụng `D3DImage.Lock()/Unlock()` đúng lifecycle; fallback `HwndHost` nếu không khắc phục được |
| **R4** | Server process orphaned (không bị kill khi client crash) | Trung bình | Thấp | Server tự tắt nếu không nhận heartbeat trong `HeartbeatTimeout` (mặc định 30s) |
| **R5** | Breaking changes trong OpenMedia.Core.NET API | Thấp | Cao | Platform layer dùng internal adapter pattern; không expose Core types trực tiếp |
| **R6** | WinUI 3 SwapChainPanel API thay đổi | Thấp | Trung bình | Phase C bắt đầu sau khi WinAppSDK stable; interface `IVideoView` cách ly |
| **R7** | DeckLink SDK version incompatibility | Trung bình | Trung bình | Dùng runtime COM loading, không hard-link DeckLink DLL |

---

## 9. Yêu cầu Tài nguyên (Resource Requirements)

### 9.1 Nhân lực

| Vai trò | Số người | Phase | Ghi chú |
|---|---|---|---|
| **C# Platform Developer** | 1–2 | A–D | Core implementation, IPC integration |
| **Graphics/D3D11 Specialist** | 1 | A, C | WPF/WinUI renderer, shared texture handling |
| **QA Engineer** | 1 | B–D | Test plan, integration tests, performance profiling |
| **Technical Writer** | 0.5 | D | Quick Start Guide, API docs review |

### 9.2 Môi trường Phát triển

| Yêu cầu | Tối thiểu | Khuyến nghị |
|---|---|---|
| **OS** | Windows 10 21H2 (x64) | Windows 11 23H2 (x64) |
| **IDE** | Visual Studio 2022 17.8+ | Visual Studio 2022 17.10+ |
| **SDK** | .NET 8 SDK | .NET 9 SDK (preview) |
| **GPU** | DirectX 11 compatible | NVIDIA GTX 1060+ / AMD RX 580+ |
| **RAM** | 8 GB | 16 GB |
| **Thiết bị test** | Webcam USB | Webcam + DeckLink Mini Recorder |

### 9.3 Ước tính Thời gian Tổng thể

| Phase | Thời gian Ước tính | Dependencies |
|---|---|---|
| **A**: Nền tảng & WPF Preview | 2–3 tuần | Không |
| **B**: Đối tượng Nghiệp vụ | 3–4 tuần | Phase A hoàn tất |
| **C**: Playlist & WinUI 3 | 2–3 tuần | Phase B cơ bản hoàn tất |
| **D**: Samples & Docs | 1–2 tuần | Phase B–C hoàn tất |
| **Tổng** | **8–12 tuần** | — |

---

## 10. Định nghĩa Hoàn thành theo Phase (Definition of Done)

### Phase A — DoD

- [ ] Project `OpenMedia.Platform` build thành công trên .NET 8/9
- [ ] `OpenMediaRuntime.InitializeAsync()` kết nối được đến server
- [ ] `ServerDiscovery` hoạt động đúng 4 bước priority chain
- [ ] `WpfD3D11Renderer` hiển thị test pattern từ shared texture
- [ ] Sample `WpfQuickPlayer` phát video MP4 trong ≤ 5 dòng code
- [ ] Latency preview < 16ms (đo bằng profiler)
- [ ] Unit tests cho `ServerDiscovery` pass

### Phase B — DoD

- [ ] `MediaPlayer` state machine hoạt động đúng (Idle → Playing → Paused → Stopped)
- [ ] `VideoMixer` trộn 2 video files với transition Cut/Dissolve
- [ ] `StreamOutput.RTMP()` streaming thành công đến test server
- [ ] `DeviceCapture.EnumerateDevicesAsync()` liệt kê đúng thiết bị
- [ ] Tất cả objects implement `IDisposable` đúng pattern
- [ ] Integration tests cho MediaPlayer end-to-end pass

### Phase C — DoD

- [ ] `MediaPlaylist` gapless transition giữa 3+ items
- [ ] WinUI 3 preview hiển thị video qua `SwapChainPanel`
- [ ] Overlay Fluent API hiển thị text trên preview
- [ ] `IVideoView` interface chung cho WPF và WinUI

### Phase D — DoD

- [ ] Tất cả 5 sample apps build và chạy thành công
- [ ] Quick Start Guide hoàn chỉnh, có screenshots
- [ ] XML docs hiển thị đúng trong Visual Studio IntelliSense
- [ ] README.md cập nhật section về `OpenMedia.Platform`

---

## 11. Chiến lược Rollback & Khôi phục (Rollback Strategy)

### 11.1 Nguyên tắc

- **Mỗi Phase = 1 feature branch** riêng biệt (theo project convention)
- **Merge chỉ khi DoD hoàn tất** và đã review
- **Không break Tier 2 API**: `OpenMedia.Platform` chỉ thêm mới, không sửa `OpenMedia.Core.NET`

### 11.2 Rollback Plan

| Tình huống | Hành động |
|---|---|
| Phase A renderer fail | Revert branch `feature/platform-phase-a`, giữ project structure |
| Phase B API design thay đổi | Sửa trên branch, không affect Phase A đã merge |
| Phase C WinUI 3 incompatible | Skip WinUI, giữ WPF-only (interface `IVideoView` vẫn sẵn sàng) |
| Server IPC protocol change | Adapter pattern trong `Internal/IPCCommandBuilder.cs` cách ly thay đổi |

### 11.3 Branching Strategy

```
main
  └── feature/platform-phase-a     ← Phase A (Foundation + WPF)
        └── feature/platform-phase-b   ← Phase B (Business Objects)
              └── feature/platform-phase-c ← Phase C (Playlist + WinUI)
                    └── feature/platform-phase-d ← Phase D (Samples + Docs)
```

> [!NOTE]
> Mỗi branch được tạo từ branch phase trước (sequential), nhưng Phase C có thể chạy song song một phần với Phase B nếu Phase B cơ bản đã ổn định.

