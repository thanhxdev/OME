# OpenMedia SDK - Library Reference Guide

Tài liệu này cung cấp cái nhìn tổng quan về toàn bộ các thư viện và module thuộc **OpenMedia SDK Version 2.1.0**. Cẩm nang này đóng vai trò như một bản tham chiếu nhanh (Quick Reference) kèm theo các đoạn code ví dụ để tích hợp SDK vào các ứng dụng C++23 và .NET 10 (WPF / WinUI 3).

---

## 1. Quick Reference

| Thư viện | Mục đích chính | Namespace (C++) | Ghi chú / Dependency |
|---|---|---|---|
| **OpenMedia.Core** | Xương sống kiến trúc (Engine, Pipeline, Frame, Queue, Clock) | `openmedia::core` | Bắt buộc cho mọi project |
| **OpenMedia.GPU** | Xử lý tăng tốc phần cứng (CUDA, D3D11, QSV, ScaleFilter) | `openmedia::gpu` | NVIDIA/Intel SDK, D3D11 |
| **OpenMedia.IO** | Đọc/Ghi file, Hardware SDI/HDMI (DeckLink, AJA, Magewell) | `openmedia::io` | FFmpeg, DeckLink, NTV2, MWCapture |
| **OpenMedia.Codecs** | Mã hóa/Giải mã video & audio (Native NVENC, QuickSync, AV1) | `openmedia::codecs` | oneVPL, NVENC, FFmpeg |
| **OpenMedia.Mixer** | Trộn video, Chuyển cảnh, ChromaKey, LumaKey, GPU Scale | `openmedia::mixer` | Phụ thuộc Core |
| **OpenMedia.Audio** | Xử lý âm thanh (Mixer, Resampler, LUFS EBU R128 Meter) | `openmedia::audio` | Phụ thuộc Core |
| **OpenMedia.Rendering** | Hiển thị Preview D3D11 SwapChain / WinUI 3 SwapChainPanel | `openmedia::rendering` | D3D11, XAudio2 |
| **OpenMedia.Overlay** | Phủ chữ, logo, Subtitle (SRT/WebVTT), HTML5/CEF | `openmedia::overlay` | FreeType, CEF (HTML) |
| **OpenMedia.CG** | Render đồ họa động bằng Web/HTML5 | `openmedia::cg` | CEF |
| **OpenMedia.SRT** | SRT Bonding đa mạng (Fiber + 4G/5G), Failover không rớt hình | `openmedia::srt` | libsrt |
| **OpenMedia.RIST** | RIST Simple & Main Profile với link bonding | `openmedia::rist` | librist |
| **OpenMedia.NDI** | Nhận/Phát luồng Video qua mạng LAN (NDI 5) | `openmedia::ndi` | NDI SDK |
| **OpenMedia.WebRTC** | Truyền tải thời gian thực lên Browser (WHIP / WHEP) | `openmedia::webrtc` | libwebrtc |
| **OpenMedia.RTMP** | Stream lên YouTube/Facebook/Twitch qua RTMP/RTMPS | `openmedia::rtmp` | Phụ thuộc IO/FFmpeg |
| **OpenMedia.ST2110** | Broadcast IP uncompressed (2110-20/30), PTP Clock, NMOS IS-04/05 | `openmedia::st2110` | IEEE 1588 PTP, NMOS |
| **OpenMedia.ST2022** | SMPTE ST 2022-7 Hitless Merge & ST 2022-1 2D-FEC matrix | `openmedia::st2022` | Phụ thuộc Core |
| **OpenMedia.Outputs.OTT** | HLS (.m3u8), DASH (.mpd), Low-Latency CMAF chunks | `openmedia::outputs::ott` | Phụ thuộc Core/IO |
| **OpenMedia.Plugins** | Nạp dynamic library với SEH crash isolation sandbox | `openmedia::plugins` | Cross-platform |

---

## 2. Core Framework & GPU

### 2.1 OpenMedia.Core
Là nền tảng của toàn bộ hệ sinh thái, quản lý vòng đời ứng dụng và dữ liệu media.
- **Engine**: Factory object tạo và cấu hình các component.
- **MediaPipeline**: Xây dựng đồ thị (Graph) kết nối từ Source -> Filter -> Output.
- **MediaFrame**: Vùng chứa dữ liệu video (raw/texture/packet), audio và metadata nanosecond.
- **FrameQueue / ClockSync**: Quản lý hàng đợi không khóa và đồng bộ thời gian thực chuẩn PTP/wall-clock.

### 2.2 OpenMedia.GPU
Cung cấp ngữ cảnh (Context) để chạy các tác vụ liên quan đến phần cứng:
- `CUDAContext`: Tăng tốc tính toán trên NVIDIA GPU.
- `D3D11Context`: Chia sẻ Texture zero-copy giữa Engine và WPF/WinUI 3.
- `ScaleFilter`: Phóng to / thu nhỏ 4K sang 1080p trên GPU hardware shaders.

---

## 3. Broadcast IP & High Availability (ST 2110, ST 2022-7, PTP, NMOS)

### 3.1 SMPTE ST 2022-7 Seamless Protection Switching (Hitless Merge)
Tự động gộp 2 luồng RTP từ 2 cổng mạng độc lập (Path A và Path B) với bộ đệm bù lệch trễ (Differential Delay Skew Compensation từ 10ms đến 500ms). Loại bỏ 100% rớt hình khi một trong hai mạng bị gián đoạn.

```cpp
#include <openmedia/st2022/HitlessMerge.h>

using namespace openmedia::st2022;

HitlessMergeConfig config;
config.enabled = true;
config.differentialDelayMs = 100;

HitlessMerge merger(config);
merger.PushPacket(NetworkPath::PathA, seqNum, packetSpan);
auto cleanPacket = merger.PopPacket();
```

### 3.2 SMPTE ST 2022-1 2D-FEC Matrix
Mã hóa ma trận hàng (Row) và cột (Column) XOR để phát hiện và khôi phục các gói tin MPEG-TS/RTP bị mất trên đường truyền IP công cộng.

### 3.3 SMPTE ST 2110 Suite & PTP Grandmaster
- **ST 2110-20**: Đóng gói video uncompressed theo chuẩn RFC 4175 pgroup.
- **ST 2110-30**: Đóng gói âm thanh đa kênh PCM 24-bit theo RFC 3190.
- **PTPClock**: Đồng bộ xung nhịp IEEE 1588-2008 / SMPTE ST 2059 với nanosecond timestamping trên `MediaFrame`.
- **NMOSEngine**: Tự động đăng ký AMWA NMOS IS-04 Node/Device/Sender/Receiver và hỗ trợ điều khiển định tuyến qua NMOS IS-05 Connection Management.

---

## 4. WAN Bonding, WebRTC & RIST

### 4.1 SRT Multi-Interface Bonding & Hot Failover
Hỗ trợ gắn kết đồng thời đường truyền cáp quang (Primary NIC) và modem 4G/5G (Cellular Backup). Khi tỷ lệ packet loss hoặc RTT trên đường chính vượt ngưỡng, hệ thống tự động chuyển vùng lưu lượng sang đường phụ mà không ngắt kết nối stream.

```csharp
var config = new SRTStreamConfig
{
    Host = "ingest.broadcast.net",
    Port = 9000,
    BondingEnabled = true,
    PrimaryInterfaceIp = "192.168.1.100", // Cáp quang
    BackupInterfaceIp = "10.0.0.50",       // 4G/5G USB Modem
    CellularLossThresholdPercent = 4.0
};
```

### 4.2 WebRTC Engine & RIST Bonding
- **WebRTC**: Native pipeline hỗ trợ gửi/nhận sub-second qua WHIP/WHEP.
- **RIST**: RIST Simple Profile và Main Profile với hỗ trợ bonding multi-link.

---

## 5. OTT Distribution (CMAF, HLS, DASH)

- **CMAFOutput**: Tạo các chunk CMAF siêu ngắn (`moof` + `mdat`) phục vụ Apple Low-Latency HLS (LL-HLS) và Low-Latency DASH.
- **HLSOutput**: Tự động sinh playlist `.m3u8` theo chuẩn RFC 8216 với cửa sổ trượt (Live Sliding Window) hoặc chế độ Event.
- **DASHOutput**: Xuất tệp manifest `.mpd` đa luồng thích ứng (ABR).

---

## 6. Hardware Capture & SDI/HDMI

- **DeckLinkOutput**: Phát tín hiệu SDI/HDMI phần cứng trực tiếp ra card Blackmagic DeckLink (`IDeckLinkOutput::ScheduleVideoFrame`).
- **AJASource**: Bắt hình trực tiếp từ card AJA Kona/Corvid qua DMA NTV2.
- **MagewellSource**: Bắt hình độ trễ thấp từ Magewell Pro Capture qua MWCapture API.

---

## 7. UI Controls & Plugin Isolation

### 7.1 WinUI 3 Video View (`WinUIVideoView`)
Control hiển thị video hiệu năng cao dành cho WinUI 3 / Windows App SDK:
- Sử dụng Direct3D 11 `ID3D11Device1.OpenSharedResource1` để mở trực tiếp shared texture handle từ Engine.
- Tạo composition `IDXGISwapChain1` và gán vào `ISwapChainPanelNative` của XAML SwapChainPanel.

### 7.2 Plugin Manager với SEH Crash Isolation
Bộ nạp plugin động (.dll trên Windows, .so trên Linux) được bảo vệ bởi Structured Exception Handling (SEH). Bất kỳ lỗi truy cập bộ nhớ (Access Violation) hoặc exception bên trong plugin bên thứ ba đều được cách ly hoàn toàn, không làm gián đoạn OpenMedia Server.

---
*Tài liệu được cập nhật cho phiên bản OpenMedia SDK v2.1.0 Commercial Release.*
