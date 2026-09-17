# OME_PLAYOUT: Master Production & Playout Engine

**OME_PLAYOUT** là hệ thống Master Production, Switcher DVE & Playout Engine thời gian thực chuyên nghiệp thuộc hệ sinh thái OME Broadcast. Ứng dụng kết nối trực tiếp với **Hệ thống IP Video Matrix Router** từ `WEBRTC_DECODE` hoặc `SRT_DECODE` thông qua cơ chế **Zero-Copy Direct3D11 Shared Textures** (VRAM-to-VRAM) với độ trễ 0ms (sub-frame), hỗ trợ đầy đủ bộ công cụ kiểm soát phát sóng truyền hình tiêu chuẩn: Multi-Cam DVE, CCU & 3D LUT, Lip-Sync Delay Buffer, Broadcast CG Overlay, NDI Master Out, và hệ thống Emergency Fail-Safe SMPTE Color Bars.

---

## 🏗️ Kiến Trúc Hệ Thống (Zero-Copy IP Matrix Router)

```
       [ 10x WebRTC / SRT Ingest Cameras ]
                       │
                       ▼
    [ WEBRTC_DECODE / SRT_DECODE Broadcast Router ]
                       │
      ┌────────────────┴────────────────┐
      ▼                                 ▼
[ 11x D3D11 Shared Textures ]     [ 11-Ch Audio Ring Buffer ]
- Global\OME_TEX_PGM_MASTER       - Global\OME_MATRIX_AUDIO_MMF
- Global\OME_TEX_ISO_CAM_01..10   - Global\OME_MATRIX_AUDIO_SYNC_EVENT
      │                                 │
      └────────────────┬────────────────┘
                       │ (Zero-Copy Inter-Process Communication)
                       ▼
             [ OME_PLAYOUT Engine ]
                       │
 ┌─────────────────────┼─────────────────────┐
 ▼                     ▼                     ▼
[ 3D LUT & CCU ]   [ DVE Multi-Box ]     [ Lip-Sync Delay ]
(Lift/Gamma/Gain)  (Single/2-Box/Quad)   (0 - 2000 ms)
 └─────────────────────┬─────────────────────┘
                       │
                       ▼
          [ Broadcast Graphics (CG) ]
        (Logo Bug, Lower-Third, Ticker)
                       │
                       ▼
      [ Emergency Fail-Safe Watchdog ]
      (1.5s Signal Loss -> SMPTE Bars)
                       │
                       ▼
      [ Broadcast Out: NDI / Direct3D11 ]
```

---

## 🚀 Hướng Dẫn Vận Hành Hệ Thống

### 1. Kích hoạt IP Output Matrix từ `WEBRTC_DECODE` hoặc `SRT_DECODE`
1. Mở ứng dụng **WEBRTC_DECODE** hoặc **SRT_DECODE**.
2. Nhấn kết nối các luồng Camera (từ 1 đến 10 camera).
3. Chuyển sang tab **"7. IP Output Matrix"** trên giao diện chính:
   - Nhấn **"▶ Start Matrix Output"** để mở router liên tiến trình:
     - Hệ thống tự động cấp phát 11 Direct3D11 Shared Textures (`Global\OME_TEX_PGM_MASTER` và `Global\OME_TEX_ISO_CAM_01..10`).
     - Mở Memory-Mapped File `Global\OME_MATRIX_AUDIO_MMF` và phát nhịp `Global\OME_MATRIX_AUDIO_SYNC_EVENT`.
   - Có thể bật/tắt luồng NDI mạng LAN cho từng kênh riêng biệt (`OME PGM MASTER`, `OME ISO CAM 01` đến `10`).

---

### 2. Khởi chạy & Kết nối `OME_PLAYOUT`
1. Khởi động ứng dụng **OME_PLAYOUT**.
2. Kiểm tra tín hiệu Ingest Matrix tại Tab **"1. Ingest Matrix"**:
   - Nhấn vào các kênh `PGM Master` hoặc `CAM 01` đến `CAM 10` để chuyển nguồn nền (Background Ingest Source).
   - Đèn trạng thái Router IPC sẽ chuyển sang màu xanh lá: `IPC ROUTER: CONNECTED (0ms Shared VRAM)`.
3. Quan sát 3 màn hình giám sát chuyên dụng (Monitors):
   - **PGM In Monitor**: Tín hiệu Master gốc nhận từ Ingest Matrix.
   - **DVE Preview Monitor**: Tín hiệu composited sau khi ghép bố cục DVE Multi-Cam.
   - **Master Out (On-Air) Monitor**: Tín hiệu tổng cuối cùng bao gồm CCU/LUT, DVE, CG Graphics và Watchdog.

---

### 3. Điều khiển Multi-Cam DVE & Broadcast Graphics (CG)
Chuyển sang Tab **"2. Multi-Cam DVE & CG"**:
- **DVE Compositor Layout**:
  - **Single (Full)**: Hiển thị 1 nguồn toàn màn hình.
  - **2-Box (Split Screen)**: Chia đôi màn hình phỏng vấn, trường quay - hiện trường.
  - **3-Box (1 Large + 2 Small)**: Bố cục trường quay chính kết hợp 2 khách mời.
  - **Quad (4-Cam PiP)**: Chia 4 góc giám sát đa điểm cầu.
- **Tùy chọn hiển thị**: Bật/tắt đường viền khung hình (Tally Border / Accent) và đổ bóng Drop Shadow.
- **Lựa chọn kênh DVE**: Chọn Box A, Box B, Box C, Box D tương ứng với các camera ISO 01..10 hoặc PGM Master.
- **Broadcast Graphics Overlay**:
  - **Channel Logo Bug**: Bật/tắt logo kênh tại góc trên bên phải màn hình.
  - **Lower-Third Nameplate**: Bảng tên nhân vật / sự kiện với hiệu ứng dải màu gradient và font chữ truyền hình.
  - **News Crawl (Ticker)**: Dải chữ tin tức chạy ngang chân màn hình cập nhật liên tục mượt mà 60 FPS.

---

### 4. Hiệu chỉnh CCU & Nạp 3D LUT (.cube)
Chuyển sang Tab **"3. CCU & 3D LUT"**:
- **Color Correction Unit (CCU)**:
  - **Lift (Shadows)**: Tinh chỉnh vùng tối (-0.5 đến +0.5).
  - **Gamma (Midtones)**: Cân bằng dải màu trung tính (0.5 đến 2.0).
  - **Gain (Highlights)**: Tăng giảm cường độ sáng vùng highlight (0.0 đến 2.0).
  - **Saturation**: Độ bão hòa màu sắc (0.0 đến 2.0).
  - **Temperature / Tint**: Cân bằng trắng nhiệt độ màu.
- **3D LUT Engine**:
  - Hỗ trợ nạp trực tiếp file `.cube` chuẩn ngành (hỗ trợ size 17x17x17, 33x33x33, 65x65x65).
  - Nhấn nút **"Browse .cube LUT..."** để chọn file LUT điện ảnh / truyền hình.
  - Nhấn **"Reset CCU"** để đưa các thông số về mặc định.

---

### 5. Khử lệch Pha m thanh - Hình ảnh (Lip-Sync Delay)
Chuyển sang Tab **"4. Lip-Sync Delay"**:
- Kéo thanh trượt **Video Delay** và **Audio Delay** từ **0ms đến 2000ms** (bước nhảy 1ms):
  - Ring buffer lưu trữ Direct3D11 Texture2D trên VRAM bộ nhớ đệm đảm bảo không giật lag.
  - Vòng đệm âm thanh RAM buffer duy trì nhịp mẫu chuẩn 48kHz Stereo PCM.
  - Hỗ trợ các nút chọn nhanh: `0 ms (Live)`, `50 ms`, `100 ms`, `250 ms`, `500 ms`.

---

### 6. Cấu hình Phát sóng Broadcast Out, Video Codec & SDI Phần Cứng
Chuyển sang Tab **"5. Broadcast Out & Fail-Safe"**:
- **Cấu hình Master Video Codec & Định dạng phát**:
  - **Độ phân giải (Resolution)**: `1920x1080 (1080p Standard)`, `3840x2160 (4K UHD)`, `1280x720 (720p)`, `1080i59.94 (Interlaced)`.
  - **Chuẩn nén Codec**:
    - `Uncompressed UYVY 4:2:2 (Direct SDI)`: Tiêu chuẩn baseband phát sóng truyền hình qua SDI.
    - `H.264 / AVC (NVENC Hardware)`: Nén thời gian thực độ trễ cực thấp qua GPU NVIDIA.
    - `H.265 / HEVC (NVENC Hardware)`: Nén hiệu năng cao thế hệ mới.
    - `Apple ProRes 422 HQ (Broadcast Master)`: Định dạng master tiêu chuẩn hậu kỳ & phát sóng quốc tế.
    - `Avid DNxHD / DNxHR (10-bit Production)`: Chuẩn studio phát sóng chuyên nghiệp.
  - **Bitrate nén**: `Uncompressed / Auto`, `8 Mbps`, `15 Mbps`, `25 Mbps`, `50 Mbps`, `100 Mbps`.
  - **Tốc độ khung hình (FPS)**: `59.94 fps` (NTSC Broadcast), `50.00 fps` (PAL Broadcast), `60.00 fps`, `29.97 fps`, `25.00 fps`, `24.00 fps`.
- **Cổng phát sóng SDI phần cứng (Hardware SDI Playout)**:
  - Tự động dò quét card SDI phần cứng trong máy trạm (`Blackmagic DeckLink 8K Pro / Studio 4K / Duo 2 / Quad 2`, `AJA Kona 5`, `Magewell SDI`).
  - Nút **"🔄 Scan SDI"** để quét và cập nhật lại danh sách thiết bị khi vừa cắm card hoặc khởi động lại driver.
  - Lựa chọn chế độ phát sóng SDI: `1080p59.94 (Fill + Key)`, `1080p50`, `1080i59.94`, `2160p59.94 12G-SDI`.
  - Nút chuyển trạng thái **"BẬT SDI OUT ⚪ / TẮT SDI OUT 🔴"** kèm đèn báo trạng thái `ON AIR (SDI TX)` và hiển thị telemetry trên thanh Status Bar.
- **Phát sóng NDI Master**:
  - Bật toggle **"NDI Master Out: OME MASTER BROADCAST"** để đẩy luồng ra mạng LAN cho vMix, OBS, Tricaster hoặc bộ giải mã SDI/NDI.
- **Chế độ Fail-Safe Tự động (Bảo vệ sóng)**:
  - Tích hợp mạch giám sát Watchdog chu kỳ kiểm tra 1.5 giây.
  - Nếu mất tín hiệu IP từ Matrix Ingest quá 1.5 giây, hệ thống **tự động cắt sóng sang bảng màu SMPTE Color Bars tiêu chuẩn EBU/SMPTE kèm âm thanh kiểm tra 1kHz Tone (-20 dBFS)**, ngăn chặn hiện tượng màn hình đen hoặc đứng hình trên sóng truyền hình trực tiếp.
  - Có thể kích hoạt thủ công chế độ kiểm tra bằng nút **"🚨 BẬT THỬ NGHIỆM SMPTE COLOR BARS"**.


---

## 📊 Thông Số Kỹ Thuật Định Tuyến (Matrix Channels)

| Kênh Matrix | Tên Shared Texture (VRAM) | Cổng m thanh (MMF) | Định dạng Video | Định dạng m thanh |
| :--- | :--- | :--- | :--- | :--- |
| **Channel 0** | `Global\OME_TEX_PGM_MASTER` | Port 0 | 1920x1080 BGRA 59.94p | 48kHz 16-bit Stereo PCM |
| **Channel 1** | `Global\OME_TEX_ISO_CAM_01` | Port 1 | 1920x1080 BGRA 59.94p | 48kHz 16-bit Stereo PCM |
| **Channel 2** | `Global\OME_TEX_ISO_CAM_02` | Port 2 | 1920x1080 BGRA 59.94p | 48kHz 16-bit Stereo PCM |
| **Channel 3** | `Global\OME_TEX_ISO_CAM_03` | Port 3 | 1920x1080 BGRA 59.94p | 48kHz 16-bit Stereo PCM |
| **Channel 4** | `Global\OME_TEX_ISO_CAM_04` | Port 4 | 1920x1080 BGRA 59.94p | 48kHz 16-bit Stereo PCM |
| **Channel 5** | `Global\OME_TEX_ISO_CAM_05` | Port 5 | 1920x1080 BGRA 59.94p | 48kHz 16-bit Stereo PCM |
| **Channel 6** | `Global\OME_TEX_ISO_CAM_06` | Port 6 | 1920x1080 BGRA 59.94p | 48kHz 16-bit Stereo PCM |
| **Channel 7** | `Global\OME_TEX_ISO_CAM_07` | Port 7 | 1920x1080 BGRA 59.94p | 48kHz 16-bit Stereo PCM |
| **Channel 8** | `Global\OME_TEX_ISO_CAM_08` | Port 8 | 1920x1080 BGRA 59.94p | 48kHz 16-bit Stereo PCM |
| **Channel 9** | `Global\OME_TEX_ISO_CAM_09` | Port 9 | 1920x1080 BGRA 59.94p | 48kHz 16-bit Stereo PCM |
| **Channel 10**| `Global\OME_TEX_ISO_CAM_10` | Port 10| 1920x1080 BGRA 59.94p | 48kHz 16-bit Stereo PCM |

---

## 🛠️ Biên Dịch Dự Án (Build Instructions)

Yêu cầu môi trường: **.NET 10 SDK**, **Windows 10/11 x64**, Card đồ họa hỗ trợ **DirectX 11 (Feature Level 11_0 trở lên)**.

```powershell
# Biên dịch toàn bộ solution
dotnet build wrappers/OpenMedia.NET.slnx -c Release

# Hoặc biên dịch riêng OME_PLAYOUT
dotnet build samples/platform/OME_PLAYOUT/OME_PLAYOUT.csproj -c Release
```
