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

---

## 7. Tiêu chí Đánh giá & Nghiệm thu (Verification & Acceptance Criteria)

1. **Tiêu chí Độ tinh gọn Code**: Mọi tác vụ cơ bản (Play file, Capture webcam, Stream RTMP) không vượt quá 5 dòng lệnh C#.
2. **Tiêu chí Ổn định & Cách ly Process**: Nếu `OpenMediaServer.exe` bị tắt đột ngột, ứng dụng WPF Client không bị crash/SEHException mà kích hoạt sự kiện `OpenMediaRuntime.ServerDisconnected`.
3. **Tiêu chí Hiệu năng Preview trên WPF**: Độ trễ render GPU DXGI Shared Texture < 1 frame (< 16ms ở 60fps), mức tiêu thụ CPU phía Client < 2%.
4. **Tiêu chí Tương thích**: Chạy mượt mà trên môi trường .NET 8 / .NET 9 (x64) trên Windows 10/11.
