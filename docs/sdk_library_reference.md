# Cẩm Nang Tra Cứu Thư Viện OpenMedia SDK (v2.0.0)
# OpenMedia SDK Library Reference Guide

Tài liệu này là bản đặc tả tra cứu toàn diện (Comprehensive Technical Reference) về toàn bộ các thư viện, phân hệ (modules), lớp đối tượng (classes), và các giao diện lập trình (APIs) hiện có trong hệ sinh thái **OpenMedia SDK Version 2.0.0**.

Tài liệu được phân loại theo từng nhóm chức năng tác vụ chuyên biệt, liệt kê chi tiết từng đơn vị thành phần, nhiệm vụ kỹ thuật, tệp tiêu đề (headers/namespaces), và kèm theo code mẫu thực tế cho cả **C++23** và **.NET 10 (C#)**.

---

## 📑 Bảng Tra Cứu Nhanh Các Phân Hệ (Quick Reference Table)

| STT | Tên Thư Viện / Module | Nhóm Chức Năng | Namespace C++ | Namespace .NET | Nhiệm Vụ & Công Dụng Chính |
|:---:|---|---|---|---|---|
| **1** | **OpenMedia.Core** | Core Architecture | `openmedia::core` | `OpenMedia.Core` | Quản lý vòng đời Engine, MediaPipeline, MediaFrame, hàng đợi lock-free, ClockSync, MemoryPool. |
| **2** | **OpenMedia.IPC & Server** | Inter-Process Comm | `openmedia::ipc` | `OpenMedia.IPC` | Kiến trúc Exhand: OpenMediaServer.exe, NamedPipe, SharedMemoryBuffer, D3D11SharedTexture. |
| **3** | **OpenMedia.CommandDispatcher** | Command & Task | `openmedia::dispatcher` | `OpenMedia.SDK` | Định tuyến lệnh nhị phân, hàng đợi ưu tiên TaskQueue, WorkerPool đa luồng task stealing. |
| **4** | **OpenMedia.IO** | Media Input / Output | `openmedia::io` | `OpenMedia.IO` | Đọc file MP4/MOV/MXF/MKV, LiveSource (RTSP/UDP), Desktop/Window capture, Hardware Capture (AJA, Magewell). |
| **5** | **OpenMedia.Outputs.DeckLink** | Hardware Playout | `openmedia::outputs::decklink` | `OpenMedia.Platform` | Phát tín hiệu SDI/HDMI ra màn hình ngoài và router phần cứng qua Blackmagic DeckLink SDK. |
| **6** | **OpenMedia.Codecs** | Codec & Compression | `openmedia::codecs` | `OpenMedia.Codecs` | Bộ mã hóa/giải mã H.264, H.265 (HEVC), AV1, AAC, Opus. Hỗ trợ phần mềm và phần cứng. |
| **7** | **OpenMedia.GPU** | Hardware Acceleration | `openmedia::gpu` | `OpenMedia.GPU` | Tăng tốc GPU qua CUDA, D3D11/12, Native NVENC (10-bit HDR), Intel QuickSync (oneVPL). |
| **8** | **OpenMedia.Mixer** | Video Composition | `openmedia::mixer` | `OpenMedia.Mixer` | Trộn video đa lớp (Multi-layer), Switcher Cut/Auto, Wipe/Dissolve transition, ChromaKey, LumaKey, GPU Scale. |
| **9** | **OpenMedia.Audio** | Audio Processing | `openmedia::audio` | `OpenMedia.Audio` | Trộn âm thanh đa kênh (AudioMixer), Resampler (libswresample), đo âm lượng chuẩn LUFS EBU R128, XAudio2 player. |
| **10** | **OpenMedia.Overlay** | Graphics & Subtitles | `openmedia::overlay` | `OpenMedia.Overlay` | Phủ chữ (TextOverlay), Logo PNG alpha, Ticker chạy chữ, Đồng hồ thời gian thực, Subtitle SRT/WebVTT. |
| **11** | **OpenMedia.CG** | HTML5 Motion Graphics | `openmedia::cg` | `OpenMedia.CG` | Render đồ họa động HTML5/CSS/JS thời gian thực qua CEF (Chromium Embedded Framework). |
| **12** | **OpenMedia.ST2022** | Broadcast IP HA | `openmedia::st2022` | `OpenMedia.Platform` | SMPTE ST 2022-7 Seamless Protection (Hitless Merge) bù lệch trễ và ST 2022-1 2D-FEC Matrix XOR. |
| **13** | **OpenMedia.ST2110** | Uncompressed IP | `openmedia::st2110` | `OpenMedia.Platform` | Chuẩn truyền dẫn uncompressed ST 2110-20 (video), ST 2110-30 (audio), PTP Grandmaster (IEEE 1588), NMOS IS-04/05. |
| **14** | **OpenMedia.SRT** | WAN Bonding & Low-Lat | `openmedia::srt` | `OpenMedia.Platform` | SRT Caller/Listener, mã hóa AES, gán card mạng cục bộ (localip), Bonding đa mạng Fiber + 4G/5G, Hot Failover. |
| **15** | **OpenMedia.RIST** | WAN Video Bonding | `openmedia::rist` | `OpenMedia.Platform` | Giao thức RIST Simple & Main Profile với hỗ trợ multi-link bonding và chống rớt gói. |
| **16** | **OpenMedia.WebRTC** | Ultra Low-Latency Web | `openmedia::webrtc` | `OpenMedia.WebRTC` | Truyền hình thời gian thực độ trễ dưới 1 giây (sub-second), chuẩn WHIP ingest và WHEP egress. |
| **17** | **OpenMedia.RTMP** | Internet Streaming | `openmedia::rtmp` | `OpenMedia.Platform` | Phát luồng RTMP/RTMPS (TLS) trực tiếp lên YouTube Live, Facebook Live, Twitch, CDN. |
| **18** | **OpenMedia.Outputs.OTT** | OTT Packaging | `openmedia::outputs::ott` | `OpenMedia.Platform` | Đóng gói HLS (playlist .m3u8), MPEG-DASH (manifest .mpd), và chunked CMAF cho Apple LL-HLS. |
| **19** | **OpenMedia.Monitoring** | Diagnostics & Metrics | `openmedia::monitoring` | `OpenMedia.Platform` | Giám sát FPS, Bitrate, RTT, Packet Loss, sinh dữ liệu Waveform Scope, Vectorscope, HealthCheck phần cứng. |
| **20** | **OpenMedia.PluginSDK** | Extensibility & Sandbox | `openmedia::plugins` | `OpenMedia.Plugins` | Nạp plugin C++/C# động (.dll/.so), cách ly lỗi sập (Crash Isolation) bằng SEH guard. |
| **21** | **OpenMedia.Platform** | .NET 10 High-Level API | `OpenMedia.Platform` | `OpenMedia.Platform` | API cấp cao cho C#: MediaPlayer, MatrixRouter, SRTStreamSession, PlayoutWorker, WinUIVideoView, WPF View. |

---

## 1. Nhóm Phân Hệ Nền Tảng & Kiến Trúc Lõi (Core & IPC Architecture)

### 1.1 `OpenMedia.Core`
Xương sống kiến trúc của SDK, quản lý toàn bộ vòng đời đối tượng media và điều phối dữ liệu bộ nhớ.

- **`Engine`** (`openmedia/core/Engine.h`): Factory chính khởi tạo hệ thống, tạo pipeline, quản trị phiên làm việc media.
- **`MediaPipeline`** (`openmedia/core/MediaPipeline.h`): Xây dựng đồ thị (pipeline graph) tuần tự hoặc phân nhánh từ Source ➔ Filters ➔ Mixer ➔ Encoders ➔ Outputs.
- **`MediaFrame`** (`openmedia/core/MediaFrame.h`): Vùng chứa dữ liệu hợp nhất (Unified Container) cho video raw (BGRA, NV12, YUV420P), GPU Texture Handle, Audio PCM/Float, Packet NALU mã hóa, kèm PTS/DTS nanosecond.
- **`FrameQueue`** (`openmedia/core/FrameQueue.h`): Hàng đợi không khóa (lock-free concurrent queue) tốc độ cao, hỗ trợ kiểm soát áp lực ngược (backpressure).
- **`ClockSync`** (`openmedia/core/ClockSync.h`): Đồng bộ xung nhịp PTS với đồng hồ hệ thống (Wall-Clock) hoặc nguồn xung nhịp PTP/NTP.
- **`MemoryPool`** (`openmedia/core/MemoryPool.h`): Bộ cấp phát bộ nhớ phân mảnh (slab allocator) cấp phát trước để đạt zero-copy và triệt tiêu Garbage Collection.

```cpp
#include <openmedia/core/Engine.h>
#include <openmedia/core/MediaPipeline.h>

auto engine = openmedia::core::Engine::Create();
auto pipeline = engine->CreatePipeline();
// Thêm source, filter, encoder, output vào pipeline...
pipeline->Build();
pipeline->Start();
```

### 1.2 `OpenMedia.IPC` & `OpenMediaServer.exe`
Kiến trúc phân tách tiến trình **Exhand Architecture**, đưa toàn bộ tác vụ decode/encode/mixing nặng vào một tiến trình độc lập (`OpenMediaServer.exe`) để bảo vệ ứng dụng UI không bao giờ bị đứng hình hay sập.

- **`IPCTransport` / `NamedPipeTransport`**: Kênh truyền thông liên tiến trình qua Windows Named Pipes.
- **`SharedMemoryBuffer`**: Chia sẻ frame nhị phân dung lượng lớn qua vùng nhớ ánh xạ (Memory-Mapped Files).
- **`D3D11SharedTexture`**: Trao đổi handle texture DirectX 11 giữa tiến trình Server và ứng dụng UI WPF/WinUI 3 để render 60/120 FPS không tiêu tốn CPU.

### 1.3 `OpenMedia.CommandDispatcher` & `WorkerPool`
Hệ thống hàng đợi và phân phối tác vụ bất đồng bộ.

- **`CommandDispatcher`**: Định tuyến các lệnh nhị phân (JSON-free binary command protocol) tới đúng mô-đun chức năng.
- **`WorkerPool`**: Thread pool hiệu năng cao, phân bổ công việc theo độ ưu tiên và cơ chế task stealing (đánh cắp tác vụ).

---

## 2. Nhóm Thu Tín Hiệu & Phát Tín Hiệu Phần Cứng (Media I/O & SDI/HDMI)

### 2.1 `OpenMedia.IO`
Cung cấp các nguồn tín hiệu đầu vào từ tệp tin, mạng máy tính và thiết bị ngoại vi.

- **`FileSource`** (`openmedia/io/FileSource.h`): Đọc và demux tệp media (MP4, MOV, MXF, AVI, MKV, ProRes) qua FFmpeg demuxer tốc độ cao, hỗ trợ seek chính xác theo từng frame và loop vô hạn.
- **`LiveSource`** (`openmedia/io/LiveSource.h`): Thu nhận luồng trực tiếp RTSP, HLS, UDP/TCP multicast. Tự động phục hồi kết nối (auto-reconnect) khi mất mạng.
- **`DeviceFactory`** (`openmedia/io/DeviceFactory.h`): Liệt kê và tạo nguồn từ Webcam USB (DirectShow, MediaFoundation), Micro WASAPI, Thiết bị Capture SDI/HDMI.
- **`DesktopCapture` / `WindowCapture`**: Chụp màn hình Desktop với DirectX DXGI Desktop Duplication API và chụp từng cửa sổ ứng dụng cụ thể.
- **`AJASource`** (`openmedia/io/AJASource.h`): Bắt tín hiệu SDI 3G/6G/12G và 4K từ các dòng card AJA Kona, Corvid qua NTV2 DMA.
- **`MagewellSource`** (`openmedia/io/MagewellSource.h`): Bắt tín hiệu HDMI/SDI từ các dòng card Magewell Pro Capture qua MWCapture API.
- **`FileOutput`** (`openmedia/io/FileOutput.h`): Ghi luồng trực tiếp ra file MP4 (chuẩn hóa vị trí moov atom để phát trực tuyến ngay), MOV, MXF broadcast, MPEG-TS.
- **`SnapshotOutput`**: Chụp lại một khung hình đang chạy ra ảnh tĩnh PNG/JPEG độ phân giải đầy đủ.

### 2.2 `OpenMedia.Outputs.DeckLink`
Phân hệ phát hình phần cứng chuyên dụng cho môi trường truyền hình.

- **`DeckLinkOutput`** (`openmedia/outputs/decklink/DeckLinkOutput.h`): Phát trực tiếp khung hình ra màn hình Monitor tham chiếu hoặc Router truyền hình qua cổng SDI/HDMI của card Blackmagic Design (DeckLink Mini Monitor, Studio 4K, Quad 2, 8K Pro) sử dụng giao diện `IDeckLinkOutput::ScheduleVideoFrame`.

```cpp
#include <openmedia/outputs/decklink/DeckLinkOutput.h>

openmedia::outputs::decklink::DeckLinkOutput sdiOut;
sdiOut.Start(0, 1920, 1080, 59.94); // Phát ra Card DeckLink Device 0 ở chuẩn 1080p59.94
sdiOut.PushFrame(mediaFrame);
```

---

## 3. Nhóm Mã Hóa, Nén & Tăng Tốc Phần Cứng (Codecs & GPU Acceleration)

### 3.1 `OpenMedia.Codecs`
Hệ thống giải mã và mã hóa toàn diện:

- **`CodecFactory`** (`openmedia/codecs/CodecFactory.h`): Tự động phát hiện GPU và lựa chọn bộ mã hóa tối ưu nhất (AutoSelectBestEncoder).
- **Phần mềm (Software)**:
  - Video: `FFmpegH264Encoder` (libx264), `FFmpegH265Encoder` (libx265), `FFmpegAV1Encoder` (libaom).
  - Audio: `FFmpegAACEncoder` (fdk-aac), `FFmpegOpusEncoder` (libopus).
- **Phần cứng (Hardware Acceleration)**:
  - `NVENCEncoder` / `H264Encoder_NV`: Giao tiếp trực tiếp với NVIDIA Video Codec SDK (NVENC C-API) giảm 90% tải CPU, hỗ trợ mã hóa HEVC 10-bit HDR (P010LE).
  - `H264Encoder_QSV` / `FFmpegQSVEncoder`: Giao tiếp với Intel QuickSync (oneVPL / MediaSDK) trên đồ họa tích hợp Intel HD/Iris/Arc.

### 3.2 `OpenMedia.GPU`
Cung cấp ngữ cảnh tài nguyên phần cứng đồng nhất:
- **`D3D11Context` / `D3D12Context`**: Khởi tạo Direct3D Device, quản lý swapchain và kết cấu (textures).
- **`CUDAContext`**: Quản lý CUDA Device, phân bổ bộ nhớ GPU và chuyển đổi không gian màu (NV12 sang BGRA) trên GPU.
- **`VulkanContext` / `OpenCLContext`**: Môi trường tính toán mở dự phòng cho các GPU AMD Radeon.

---

## 4. Nhóm Xử Lý Video, Hòa Trộn & Chuyển Cảnh (Video Mixing & Shaders)

### 4.1 `OpenMedia.Mixer`
Bộ bàn trộn hình (Vision Mixer / Switcher) ảo thời gian thực đạt chuẩn thương mại.

- **`Mixer`** (`openmedia/mixer/Mixer.h`): Trộn không giới hạn số lượng lớp video (Layers), hỗ trợ sắp xếp thứ tự Z-order, định vị vị trí (X, Y), tỷ lệ (Scale), độ trong suốt (Opacity).
- **`MixerLayer`**: Đại diện cho từng lớp nguồn tín hiệu đưa vào bàn trộn.
- **`Switcher`** (`openmedia/mixer/Switcher.h`): Điều khiển chuyển đổi giữa hai nguồn Program (PGM) và Preview (PVW) bằng lệnh `Take` hoặc `Auto`.
- **`Transition`** (`openmedia/mixer/Transition.h`): Tạo hiệu ứng chuyển cảnh: Cut, Dissolve (Crossfade), Wipe (Left/Right/Up/Down/Diagonal), Slide, Push với thời lượng tùy biến tính bằng mili-giây.
- **`ChromaKey`** (`openmedia/mixer/ChromaKey.h`): Tách phông xanh (Green Screen, Blue Screen) chuyên nghiệp với thuật toán khử ám màu (spill suppression).
- **`LumaKey`** (`openmedia/mixer/LumaKey.h`): Tách phông dựa trên độ sáng, hỗ trợ tính toán trực tiếp trên GPU Pixel Shader.
- **`ScaleFilter`** (`openmedia/mixer/ScaleFilter.h`): Thay đổi kích thước khung hình (ví dụ 4K UHD 3840x2160 xuống 1080p hoặc 720p) tối ưu hóa bằng GPU hardware bilinear interpolation, giữ nguyên tỷ lệ khung hình (Letterbox/Pillarbox).

```cpp
#include <openmedia/mixer/Mixer.h>
#include <openmedia/mixer/Transition.h>

auto mixer = std::make_shared<openmedia::mixer::Mixer>();
mixer->SetOutputFormat(1920, 1080, 59.94);

int cam1 = mixer->AddInput(); // Background Layer 0
int cam2 = mixer->AddInput(); // PiP Layer 1
mixer->SetLayerBounds(cam2, 1280, 720, 600, 338); // Picture-in-Picture góc dưới
mixer->SetLayerOpacity(cam2, 0.9f);
```

---

## 5. Nhóm Xử Lý Âm Thanh & Đo Lường (Audio Engine & Loudness Metering)

### 5.1 `OpenMedia.Audio`
Xử lý luồng âm thanh đa kênh với độ trễ cực thấp.

- **`AudioMixer`** (`openmedia/audio/AudioMixer.h`): Trộn nhiều luồng âm thanh từ các nguồn khác nhau, điều chỉnh Gain (-60dB đến +12dB), Pan (Left/Right), Mute, Solo cho từng kênh. Hỗ trợ layout Mono, Stereo, 5.1 Surround, 7.1.
- **`Resampler`** (`openmedia/audio/Resampler.h`): Chuyển đổi tần số lấy mẫu (Sample Rate Conversion ví dụ 44.1kHz lên 48kHz Broadcast) và đổi định dạng mẫu (S16LE, S32LE, Float32).
- **`ChannelMapper`**: Hoán đổi, sao chép hoặc trích xuất các kênh âm thanh (ví dụ: tách Channel 1 và Channel 2 từ SDI Embedded Audio).
- **`AudioMeter`** (`openmedia/audio/AudioMeter.h`): Đo lường âm thanh chuẩn phát thanh truyền hình:
  - **LUFS**: Chuẩn EBU R128 và ITU-R BS.1770-4 (Integrated, Short-term, Momentary loudness).
  - **Peak & True Peak (dBFS)**: Phát hiện ngưỡng clip âm thanh.
  - **VU Meter & RMS**: Đo cường độ âm trung bình.
- **`AudioPlayer`** (`openmedia/audio/AudioPlayer.h`): Phát trực tiếp âm thanh ra loa kiểm âm hoặc tai nghe qua Microsoft XAudio2.

---

## 6. Nhóm Đồ Họa, Phụ Đề & HTML5 CG (Overlays & Character Generator)

### 6.1 `OpenMedia.Overlay`
Chèn các thành phần đồ họa tĩnh và động lên trên luồng video:

- **`OverlayEngine`** (`openmedia/overlay/OverlayEngine.h`): Bộ quản lý tập trung toàn bộ các lớp overlay.
- **`TextOverlay`** (`openmedia/overlay/TextOverlay.h`): Hiển thị chữ với phông chữ tùy biến, kích thước, viền chữ (outline), đổ bóng (shadow) qua FreeType / DirectWrite.
- **`LogoOverlay`** (`openmedia/overlay/LogoOverlay.h`): Chèn logo đài truyền hình dạng PNG với kênh Alpha trong suốt, hỗ trợ co giãn và định vị tọa độ.
- **`TickerOverlay`** (`openmedia/overlay/TickerOverlay.h`): Thanh chữ chạy tin tức cuộn ngang (crawl) hoặc cuộn dọc mượt mà ở 60 FPS.
- **`ClockOverlay`** (`openmedia/overlay/ClockOverlay.h`): Hiển thị đồng hồ thời gian thực (Timecode, Giờ:Phút:Giây, Ngày/Tháng).
- **`SubtitleRenderer`** (`openmedia/overlay/SubtitleRenderer.h`): Nạp và hiển thị phụ đề chuẩn broadcast từ tệp `.srt` hoặc WebVTT `.vtt`.
- **`SCTE35Processor`**: Phát hiện và chèn tín hiệu đánh dấu điểm chèn quảng cáo tự động SCTE-35 / SCTE-104.

### 6.2 `OpenMedia.CG`
Character Generator thế hệ mới dựa trên nền tảng Web:
- **`CGEngine` / `CGTemplate`** (`openmedia/cg/CGEngine.h`): Nạp trang web đồ họa HTML5/CSS3/JavaScript (thanh tên nhân vật Lower-Third, bảng tỷ số thể thao, bảng thời tiết) và render off-screen ra texture BGRA/Alpha bằng Chromium (CEF), cho phép cập nhật dữ liệu trực tiếp bằng JavaScript API.

---

## 7. Nhóm Giao Thức Mạng IP Truyền Hình Chuyên Dụng (SMPTE ST 2110 & ST 2022)

### 7.1 `OpenMedia.ST2022` (High Availability IP)
Dành cho truyền dẫn đóng gói MPEG-TS qua mạng IP với độ tin cậy tuyệt đối:

- **`HitlessMerge`** (`openmedia/st2022/HitlessMerge.h`): Chuẩn **SMPTE ST 2022-7 Seamless Protection Switching**. Nhận đồng thời 2 luồng IP từ 2 cổng mạng độc lập (Path A và Path B), sử dụng bộ đệm trượt Differential Delay Skew Compensation (10ms - 500ms) để tự động khử trùng lặp và ghép thành 1 luồng nguyên vẹn, đảm bảo 0% packet loss ngay cả khi một switch mạng bị tắt nguồn.
- **`ST2022Output`** (`openmedia/st2022/ST2022Output.h`): Bộ phát MPEG-TS over IP tích hợp thuật toán mã hóa ma trận **SMPTE ST 2022-1 2D-FEC** (hàng L x cột D XOR packets).
- **`ST2022Source`** (`openmedia/st2022/ST2022Source.h`): Bộ thu MPEG-TS over IP với bộ giải mã lặp 2D-FEC khôi phục hoàn toàn các gói tin bị mất trên đường truyền.

### 7.2 `OpenMedia.ST2110` (Uncompressed Broadcast IP)
Tiêu chuẩn phòng điều khiển truyền hình tương lai qua cáp quang 25G/100G IP:

- **`ST2110Output` / `ST2110Source`**:
  - **ST 2110-20**: Truyền dẫn Video thô uncompressed đóng gói theo RFC 4175 pgroup.
  - **ST 2110-30**: Truyền dẫn Âm thanh đa kênh PCM 24-bit chất lượng cao theo RFC 3190.
- **`PTPClock`** (`openmedia/st2110/PTPClock.h`): Đồng bộ hóa đồng hồ PTP Grandmaster Clock theo chuẩn **IEEE 1588-2008** và **SMPTE ST 2059-1/2**, gắn timestamp độ chính xác nanosecond lên từng MediaFrame.
- **`NMOSEngine`** (`openmedia/st2110/NMOSEngine.h`): Tự động phát hiện và đăng ký thiết bị theo chuẩn **AMWA NMOS IS-04** (Node & Device Registry) và tiếp nhận lệnh điều khiển định tuyến mạng từ phần mềm đạo diễn qua **AMWA NMOS IS-05** (Connection Management).

---

## 8. Nhóm Giao Thức Internet, WAN Bonding & Độ Trễ Thấp (SRT, RIST, WebRTC, RTMP)

### 8.1 `OpenMedia.SRT`
Chuẩn truyền dẫn độ trễ thấp bảo mật cao qua mạng Internet công cộng:

- **`SRTEngine`**: Quản lý phiên làm việc libsrt.
- **`SRTSource` / `SRTOutput`**: Chế độ Caller, Listener, Rendezvous với mã hóa AES-128/256 bit.
- **Multi-Interface Bonding & Failover**: Cho phép gắn socket vào card mạng cụ thể (`localip`), hỗ trợ kết nối kép đồng thời qua Cáp quang (Ethernet) và USB 4G/5G Dongle (Cellular). Tự động hoán đổi luồng nóng khi mất sóng mà không gây dừng luồng.

### 8.2 `OpenMedia.RIST`
- **`RISTEngine` / `RISTSource` / `RISTOutput`**: Hỗ trợ chuẩn mở Reliable Internet Stream Transport (RIST Simple & Main Profile) với tính năng liên kết đa đường truyền (Multi-link bonding).

### 8.3 `OpenMedia.WebRTC`
- **`WebRTCEngine` / `WebRTCOutput`**: Phát sóng trực tiếp lên trình duyệt Web với độ trễ dưới 500ms, hỗ trợ chuẩn **WHIP** (WebRTC HTTP Ingestion Protocol) và **WHEP** (WebRTC HTTP Egress Protocol).

### 8.4 `OpenMedia.RTMP`
- **`RTMPEngine` / `RTMPOutput` / `RTMPSource`**: Phát luồng RTMP/RTMPS mã hóa TLS tới YouTube, Facebook, Twitch hoặc nhận luồng từ phần mềm OBS Studio / vMix.

---

## 9. Nhóm Đóng Gói Phân Phối OTT (CMAF, HLS, DASH)

### 9.1 `OpenMedia.Outputs.OTT`
Dành cho các hệ thống đài truyền hình phát trực tuyến lên Internet (OTT / CDN):

- **`CMAFOutput`** (`openmedia/outputs/ott/CMAFOutput.h`): Đóng gói Common Media Application Format với các chunk ngắn (`moof` + `mdat`) phục vụ công nghệ **Apple Low-Latency HLS (LL-HLS)** và **DASH-IF Low Latency**.
- **`HLSOutput`** (`openmedia/outputs/ott/HLSOutput.h`): Cắt video thành các phân đoạn `.ts` hoặc `.m4s`, tự động cập nhật tệp danh sách phát `.m3u8` theo chuẩn RFC 8216 với chế độ cửa sổ trượt (Live Sliding Window) hoặc chế độ sự kiện (Event).
- **`DASHOutput`** (`openmedia/outputs/ott/DASHOutput.h`): Tự động sinh tệp tin mô tả manifest XML `.mpd` đa mức bitrate thích ứng (Adaptive Bitrate Streaming - ABR).

---

## 10. Nhóm Giám Sát, Đo Đạc & Đo Đoán Lỗi (Monitoring & Diagnostics)

### 10.1 `OpenMedia.Monitoring`
Cung cấp dữ liệu đo đạc thời gian thực phục vụ màn hình kỹ thuật viên:

- **`Metrics`** (`openmedia/monitoring/Metrics.h`): Thu thập thống kê chi tiết: Tốc độ khung hình (FPS), Bitrate (kbps), Độ trễ RTT, Số frame bị rớt (dropped frames), Dung lượng hàng đợi.
- **`Waveform`** (`openmedia/monitoring/Waveform.h`): Tính toán dữ liệu biểu đồ video waveform scope (Luma / RGB Parade) để cân chỉnh màu sắc.
- **`Vectorscope`** (`openmedia/monitoring/Vectorscope.h`): Sinh dữ liệu biểu đồ vectorscope kiểm tra góc pha màu và độ bão hòa (Hue & Saturation).
- **`HealthCheck`**: Giám sát mức tải CPU, GPU Engine Load, VRAM, và nhiệt độ phần cứng.

---

## 11. Nhóm Hệ Thống Plugin Mở Rộng & Sandbox (Plugin SDK)

### 11.1 `OpenMedia.PluginSDK` & `OpenMedia.Plugins`
Cho phép bên thứ ba mở rộng tính năng của Engine bằng cách tạo các file thư viện động (.dll trên Windows, .so trên Linux):

- **`PluginManager`** (`openmedia/plugins/PluginManager.h`): Tự động nạp, quản lý vòng đời và giải phóng plugin.
- **`SEH Guard & Sandbox`**: Sử dụng Structured Exception Handling (SEH) bao bọc toàn bộ lời gọi hàm sang Plugin. Nếu một plugin bên thứ ba gặp lỗi bộ nhớ (Access Violation hoặc Segmentation Fault), Engine sẽ cô lập hoàn toàn lỗi này và vô hiệu hóa plugin đó mà không làm crash tiến trình phát sóng.
- **Các Interfaces Plugin**:
  - `IVideoFilter`: Tạo bộ lọc xử lý hình ảnh (CPU hoặc GPU).
  - `IAudioFilter`: Tạo bộ lọc xử lý âm thanh (VST, EQ, Compressor).
  - `IEncoderPlugin` / `IDecoderPlugin`: Bổ sung codec chuyên dụng.

---

## 12. Phân Hệ Cấp Cao Dành Cho .NET 10 & WinUI 3 (Managed Platform API)

### 12.1 `OpenMedia.Platform`
Thư viện quản lý cấp cao (Tier 1 High-Level Managed API) được thiết kế theo phong cách hiện đại của .NET 10, chỉ mất từ 3 đến 5 dòng code để thực hiện các tác vụ phát sóng phức tạp.

- **`MediaPlayer`**: Trình phát đa phương tiện toàn diện, hỗ trợ play/pause/seek, nạp file hoặc luồng mạng, gán trực tiếp vào giao diện UI.
- **`VideoMixer`**: Đối tượng hòa trộn video trong C#, hỗ trợ chuyển cảnh, tách phông nền, Picture-in-Picture.
- **`MatrixRouter`**: Bàn điều khiển ma trận tín hiệu (Routing Switcher), chuyển mạch tức thì (Zero-glitch Switching) giữa các nguồn camera đầu vào và các đích đầu ra.
- **`SRTStreamSession`**: Quản lý phiên phát luồng SRT chuyên dụng, tích hợp tính năng tự động chuyển mạng dự phòng 4G/5G (Cellular Failover) và thống kê telemetry.
- **`ColorGradingEngine`**: Động cơ chỉnh màu trực tiếp: Brightness, Contrast, Saturation, Gamma, 3D LUT `.cube`.
- **`MultiStreamReceiverEngine`**: Bộ thu và giải mã song song nhiều luồng mạng SRT/NDI cùng lúc.
- **`SdiNativeOutputWorker`**: Worker chạy ngầm phụ trách đẩy khung hình liên tục ra card SDI Blackmagic.

### 12.2 Các Điều Khiển Giao Diện Người Dùng (UI Controls)

- **`WinUIVideoView`** (`OpenMedia.Platform.Controls.WinUI.WinUIVideoView`):
  - Dành riêng cho các ứng dụng hiện đại viết bằng **WinUI 3 / Windows App SDK**.
  - Tích hợp trực tiếp với XAML `SwapChainPanel` qua COM Interface `ISwapChainPanelNative`.
  - Mở Direct3D 11 Texture Handle chia sẻ từ Server bằng `ID3D11Device1.OpenSharedResource1`, đạt tốc độ hiển thị tối đa với **Zero-copy** và độ trễ bằng 0.

- **`OpenMediaVideoView`** (`OpenMedia.Platform.Controls.Wpf.OpenMediaVideoView`):
  - Dành cho các ứng dụng máy trạm truyền thống viết bằng **WPF (.NET 10)**.
  - Sử dụng `WpfD3D11Renderer` ánh xạ dữ liệu trực tiếp vào `WriteableBitmap` hoặc `D3DImage`, cho phép hiển thị hình ảnh 60 FPS mượt mà.

---

## 13. Hướng Dẫn Code Mẫu Mẫu Điển Hình (Practical Code Examples)

### Ví dụ 1: Phát Luồng Trực Tuyến SRT Kèm Dự Phòng 4G/5G Bằng C# (.NET 10)
```csharp
using OpenMedia.Platform;
using OpenMedia.Platform.Models;

// 1. Thiết lập cấu hình SRT với tính năng Bonding đa card mạng
var config = new SRTStreamConfig
{
    Host = "ingest.broadcast.vn",
    Port = 9000,
    Mode = SRTMode.Caller,
    LatencyMs = 120,
    BondingEnabled = true,
    PrimaryInterfaceIp = "192.168.1.100", // Cáp quang văn phòng
    BackupInterfaceIp = "10.0.0.50",       // Modem USB 4G/5G
    CellularLossThresholdPercent = 3.5,   // Tự động chuyển mạng khi rớt > 3.5% gói
    VideoCodec = "H.264",
    BitrateKbps = 8000
};

// 2. Khởi tạo phiên truyền dẫn
using var srtSession = new SRTStreamSession(config);
await srtSession.StartAsync();

// 3. Đăng ký nhận thông số telemetry thời gian thực
srtSession.TelemetryUpdated += (sender, stats) =>
{
    Console.WriteLine($"[RTT: {stats.RttMs}ms] [Loss: {stats.PacketLossPercent}%] [Bitrate: {stats.CurrentBitrateKbps} kbps]");
};
```

### Ví dụ 2: Ghép 2 Luồng Mạng ST 2022-7 Bằng C++23 (Hitless Merge Zero Drop)
```cpp
#include <openmedia/st2022/HitlessMerge.h>
#include <iostream>

using namespace openmedia::st2022;

int main() {
    HitlessMergeConfig config;
    config.enabled = true;
    config.differentialDelayMs = 150; // Khử lệch trễ tối đa 150ms giữa 2 đường mạng
    
    HitlessMerge merger(config);
    
    // Nhận gói tin từ Cổng mạng A và Cổng mạng B
    // merger.PushPacket(NetworkPath::PathA, seqNumA, payloadA);
    // merger.PushPacket(NetworkPath::PathB, seqNumB, payloadB);
    
    // Rút gói tin đã được ghép nguyên vẹn và sắp xếp đúng thứ tự
    while (auto cleanPacket = merger.PopPacket()) {
        // Chuyển gói tin sạch tới bộ giải mã video...
    }
    
    auto stats = merger.GetStats();
    std::cout << "Recovered from Path B: " << stats.recoveredFromPathB 
              << ", Duplicates dropped: " << stats.duplicatesDropped << "\n";
    return 0;
}
```

### Ví dụ 3: Gắn Khung Hình Lên Giao Diện WinUI 3 Bằng C#
```csharp
using OpenMedia.Platform;
using OpenMedia.Platform.Controls.WinUI;

// Trong file Code-Behind MainWindow.xaml.cs của WinUI 3:
public sealed partial class MainWindow : Microsoft.UI.Xaml.Window
{
    private WinUIVideoView _videoView;
    private MediaPlayer _player;

    public MainWindow()
    {
        this.InitializeComponent();
        
        // Khởi tạo control WinUI preview
        _videoView = new WinUIVideoView();
        _videoView.SetSwapChainPanel(MyXamlSwapChainPanel);

        // Khởi tạo player và gắn vào View
        _player = new MediaPlayer();
        _player.AttachPreview(_videoView);
        _player.Open("d:\\videos\\broadcast_show_4k.mp4");
        _player.Play();
    }
}
```

---

*Tài liệu được biên soạn và cập nhật chính thức cho phiên bản OpenMedia SDK v2.0.0 Commercial Release.*
