# OpenMedia SDK - Library Reference Guide

Tài liệu này cung cấp cái nhìn tổng quan về toàn bộ các thư viện và module thuộc **OpenMedia SDK**. Cẩm nang này đóng vai trò như một bản tham chiếu nhanh (Quick Reference) kèm theo các đoạn code ví dụ để tích hợp SDK vào các ứng dụng C++ và C#.

---

## 1. Quick Reference

| Thư viện | Mục đích chính | Namespace (C++) | Ghi chú / Dependency |
|---|---|---|---|
| **OpenMedia.Core** | Xương sống kiến trúc (Engine, Pipeline, Frame) | `openmedia::core` | Bắt buộc cho mọi project |
| **OpenMedia.GPU** | Xử lý tăng tốc phần cứng (CUDA, D3D11, QSV) | `openmedia::gpu` | NVIDIA/Intel SDK, D3D11 |
| **OpenMedia.IO** | Đọc/Ghi file, Capture thiết bị (Camera, Desktop) | `openmedia::io` | FFmpeg, DeckLink, WASAPI |
| **OpenMedia.Codecs** | Mã hóa/Giải mã video & audio (H264, AAC, AV1) | `openmedia::codecs` | FFmpeg, NVENC, QSV |
| **OpenMedia.Mixer** | Trộn nhiều nguồn, chuyển cảnh, keying, LUT | `openmedia::mixer` | Phụ thuộc Core |
| **OpenMedia.Audio** | Xử lý âm thanh (Mixer, Resampler, Meter) | `openmedia::audio` | Phụ thuộc Core |
| **OpenMedia.Rendering** | Hiển thị Preview ra cửa sổ màn hình | `openmedia::rendering` | D3D11, XAudio2 |
| **OpenMedia.Overlay** | Phủ chữ, logo, cuộn chữ (Ticker), Subtitles | `openmedia::overlay` | FreeType, CEF (HTML) |
| **OpenMedia.CG** | Render đồ họa động bằng Web/HTML5 | `openmedia::cg` | CEF |
| **OpenMedia.SRT** | Truyền dẫn độ trễ thấp qua giao thức SRT | `openmedia::srt` | libsrt |
| **OpenMedia.NDI** | Nhận/Phát luồng Video qua mạng LAN | `openmedia::ndi` | NDI SDK |
| **OpenMedia.WebRTC** | Truyền tải thời gian thực lên Browser (WHIP) | `openmedia::webrtc` | libwebrtc |
| **OpenMedia.RTMP** | Stream lên YouTube/Facebook/Twitch | `openmedia::rtmp` | Phụ thuộc IO/FFmpeg |
| **OpenMedia.ST2110** | Broadcast IP (Video/Audio/Ancillary uncompressed) | `openmedia::st2110` | PTP, NMOS |
| **OpenMedia.ST2022** | Truyền TS qua UDP có FEC | `openmedia::st2022` | Phụ thuộc IO |

---

## 2. Core Framework & GPU

### 2.1 OpenMedia.Core
Là nền tảng của toàn bộ hệ thống, quản lý vòng đời ứng dụng và dữ liệu.
- **Engine**: Factory object để tạo các component.
- **MediaPipeline**: Xây dựng đồ thị (Graph) kết nối từ Source -> Filter -> Output.
- **MediaFrame**: Vùng chứa dữ liệu video (raw/texture), audio và metadata.
- **FrameQueue / ClockSync**: Quản lý hàng đợi không khóa và đồng bộ thời gian thực.

**Demo: Khởi tạo Engine & Pipeline**
```cpp
#include <openmedia/core/Engine.h>
#include <openmedia/core/MediaPipeline.h>

using namespace openmedia::core;

auto engine = Engine::Create();
auto pipeline = engine->CreatePipeline();

// Kết nối các module vào pipeline...
// pipeline->SetSource(source);
// pipeline->AddOutput(output);

pipeline->Build();
pipeline->Start();
```

### 2.2 OpenMedia.GPU
Cung cấp ngữ cảnh (Context) để chạy các tác vụ liên quan đến phần cứng (ví dụ: Zero-copy hardware decode).
- Hỗ trợ: `CUDAContext`, `D3D11Context`, `D3D12Context`, `VulkanContext`.

---

## 3. Media Processing (Xử lý Truyền thông)

### 3.1 OpenMedia.IO
Quản lý luồng dữ liệu vào/ra cơ bản.
- **FileSource**: Mở tệp tin video/audio cục bộ qua FFmpeg.
- **LiveSource**: Bắt luồng RTSP, HLS, MPEG-TS.
- **DeviceSource**: Thu thập tín hiệu từ Capture Card (DeckLink, AJA) hoặc Webcam (DirectShow, MediaFoundation), WASAPI, và DesktopCapture.

**Demo: Đọc file và Capture Camera**
```cpp
#include <openmedia/io/FileSource.h>
#include <openmedia/io/DeviceFactory.h>

using namespace openmedia::io;

auto fileSource = std::make_shared<FileSource>();
fileSource->Open("video.mp4");

auto cameraSource = DeviceFactory::CreateDeviceSource("DShow_Cam_01");
```

### 3.2 OpenMedia.Codecs
Cung cấp các bộ mã hóa/giải mã phần mềm và phần cứng.
- **Decoders**: `H264Decoder`, `H265Decoder`.
- **Encoders**: `H264Encoder` (libx264, nvenc, qsv), `AACEncoder`, `OpusEncoder`.

**Demo: Thiết lập Encoder**
```cpp
#include <openmedia/codecs/CodecFactory.h>

auto encoder = CodecFactory::CreateVideoEncoder("libx264");
// encoder->SetBitrate(5000000);
```

### 3.3 OpenMedia.Mixer
Trái tim của hệ thống live production.
- **Mixer**: Bộ trộn video nhiều lớp (Multi-layer) hỗ trợ Z-order.
- **Switcher / Transition**: Chuyển đổi giữa 2 nguồn với hiệu ứng Cut, Wipe, Dissolve.
- **ChromaKey / LumaKey**: Tách nền xanh.
- **Filters**: Phân lớp màu (Color Correction), xoay, crop.

**Demo: Trộn 2 lớp video**
```cpp
#include <openmedia/mixer/Mixer.h>

auto mixer = std::make_shared<openmedia::mixer::Mixer>();
mixer->SetOutputFormat(1920, 1080, 60.0);

int layer0 = mixer->AddInput(); // Background
int layer1 = mixer->AddInput(); // PiP
```

### 3.4 OpenMedia.Audio
- **AudioMixer**: Trộn đa kênh (Multi-channel).
- **AudioMeter**: Đo lường chuẩn phát sóng (LUFS, RMS, VU).
- **Resampler**: Thay đổi tần số lấy mẫu (Sample rate conversion).

---

## 4. Đồ họa & Hiển thị

### 4.1 OpenMedia.Rendering
- **D3D11Renderer**: Hiển thị chuỗi hình ảnh ra một cửa sổ UI (HWND).

### 4.2 OpenMedia.Overlay
- Cung cấp: `TextOverlay`, `LogoOverlay`, `TickerOverlay`, `ClockOverlay`.
- `SCTE35Processor`: Phát hiện và xử lý điểm chèn quảng cáo.

**Demo: Thêm Logo Overlay**
```cpp
#include <openmedia/overlay/OverlayEngine.h>
#include <openmedia/overlay/LogoOverlay.h>

auto overlayEngine = std::make_shared<OverlayEngine>();
auto logo = std::make_shared<LogoOverlay>();
logo->LoadImage("watermark.png");
overlayEngine->AddOverlay(logo);
```

### 4.3 OpenMedia.CG
Sử dụng Chromium Embedded Framework (CEF) để render các đồ họa HTML/CSS tĩnh hoặc động.
- `CGTemplate`, `CGEngine`: Nạp trang web, giao tiếp dữ liệu biến (data binding) qua JavaScript và biến thành luồng Video Alpha (RGBA).

---

## 5. Truyền dẫn Protocols (Giao thức mạng)

OpenMedia SDK hỗ trợ đầy đủ các chuẩn phát sóng IP hiện đại nhất:
- **OpenMedia.SRT**: `SRTSource` (Listener/Caller) & `SRTOutput` (Caller) hỗ trợ mã hóa AES.
- **OpenMedia.NDI**: Tích hợp NewTek NDI SDK để phát hiện và gửi tín hiệu qua mạng LAN.
- **OpenMedia.WebRTC**: Giao thức độ trễ siêu thấp (Sub-second), hỗ trợ WHIP (ingest) và WHEP (egress).
- **OpenMedia.RTMP**: Push tín hiệu livestream truyền thống (YouTube, Facebook).
- **OpenMedia.ST2110 / ST2022**: Tiêu chuẩn Broadcast uncompressed/compressed qua mạng IP với NMOS và SMPTE 2022-7 Hitless Merge.

---

## 6. Mở rộng & Sinh thái (Ecosystem)

### 6.1 OpenMedia.PluginSDK
SDK hỗ trợ việc tạo các Plugin dạng DLL (.dll, .so) có thể load động trong runtime mà không cần compile lại Engine.
- Interface có sẵn: `IVideoFilter`, `IAudioFilter`, `IEncoderPlugin`, `IDecoderPlugin`.
- Hỗ trợ viết Plugin bằng cả **C++** và **C# (.NET)**.

### 6.2 C# / .NET Wrappers
OpenMedia SDK cung cấp các thư viện `OpenMedia.Core.NET`, `OpenMedia.Mixer.NET`, v.v. thông qua P/Invoke, giúp nhà phát triển C# (WPF, WinForms, WinUI) xây dựng ứng dụng với mã lệnh quen thuộc (Managed Code).

**Demo: Khởi tạo Pipeline bằng C# (Managed Code)**
```csharp
using OpenMedia.Core;
using OpenMedia.IO;
using OpenMedia.Mixer;

// Khởi tạo Engine
using var engine = new Engine();
using var pipeline = engine.CreatePipeline();

// Cấu hình Source
var source = new FileSource();
source.Open("video.mp4");

// Cấu hình Mixer
var mixer = new Mixer();
mixer.SetOutputFormat(1920, 1080, 60.0);
mixer.AddInput();

// Chạy pipeline
pipeline.SetSource(source);
pipeline.AddFilter(mixer);
pipeline.Build();
pipeline.Start();
```

---
*Tài liệu được sinh tự động thông qua quá trình phát triển OpenMedia SDK v2.0.*
