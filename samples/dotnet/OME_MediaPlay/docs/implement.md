# PROMPT YÊU CẦU PHÁT TRIỂN ỨNG DỤNG: YOUTUBE-STYLE MEDIA PLAYER (CLIENT/SERVER IPC ARCHITECTURE)

### 📌 MỤC TIÊU DỰ ÁN
Hãy xây dựng một ứng dụng Trình phát Video (Video Player) chạy trên Windows (C# .NET 10 WPF) có các tính năng tương tác thời gian thực chuẩn YouTube (chọn lại độ phân giải, tắt/mở tiếng, tăng/giảm volume, chọn FPS, thanh trượt tua Seekbar) dựa trên **OpenMediaSDK**. 

Ứng dụng bắt buộc tuân theo **Mô hình Kiến trúc Client/Server IPC (Inter-Process Communication)** để cô lập hoàn toàn tiến trình giao diện người dùng (UI) và tiến trình xử lý tín hiệu C++ Native nền.

---

### 🏛️ KIẾN TRÚC HỆ THỐNG (CLIENT/SERVER IPC ARCHITECTURE)

1. **Server Subsystem (`OpenMediaServer.exe` - C++ Native Engine)**:
   - Đóng vai trò làm Server xử lý truyền thông ngầm (Daemon/Background Process).
   - Khởi tạo `MediaPipeline`, tải phần cứng GPU (NVDEC/QSV), quản lý `FileSource`, `ScaleFilter`, `AudioGain`, và `AVSyncClock`.
   - Lắng nghe và tiếp nhận các gói tin điều khiển nhị phân từ Client thông qua **Named Pipes IPC Server** (`openmedia_ipc`).
   - Chia sẻ khung hình Video trực tiếp lên bộ nhớ GPU (Direct3D 11 Shared Texture / Shared Memory) để Client render không tốn chi phí Copy (Zero-Copy).

2. **Client Subsystem (C# WPF)**:
   - Đóng vai trò làm Client hiển thị Giao diện Người dùng (UI Client).
   - Kết nối tới Server qua thư viện C# [`SDKEngine`](file:///c:/Users/ASUS%20NUC/Desktop/Code/OME/wrappers/OpenMedia.Core.NET/SDK/SDKEngine.cs) và [`IPCClient`](file:///c:/Users/ASUS%20NUC/Desktop/Code/OME/wrappers/OpenMedia.Core.NET/IPCClient.cs).
   - Tự động bật `OpenMediaServer.exe` nếu Server chưa chạy.
   - Gửi các lệnh điều khiển nhị phân (`IPC Command Packets`) khi người dùng tương tác trên UI và nhận luồng dữ liệu Render.
   - **Tự bù lỗi (Fault Isolation)**: Nếu `OpenMediaServer.exe` gặp sự cố crash, Client C# không bị văng app mà tự động hiển thị thông báo Reconnecting và khởi động lại Server.

---

### 📊 PHÂN TÍCH THƯ VIỆN OPENMEDIASDK ĐƯỢC SỬ DỤNG

| Chức năng Kiểu YouTube | Module Thư viện C++ Native | Thành phần / Class chính | Vai trò Kỹ thuật |
| :--- | :--- | :--- | :--- |
| **1. Quản lý Pipeline & Play/Pause** | `openmedia_core` | `Engine`, `MediaPipeline` | Khởi tạo Server, điều khiển trạng thái phát luồng (`Start`, `Pause`, `Stop`). |
| **2. Đọc file & Tua Seekbar** | `openmedia_io` & `openmedia_core` | `FileSource`, `MediaPipeline::Seek()` | Đọc tập tin MP4/MKV và thực hiện nhảy mốc thời gian (Seek ms) khi kéo thanh trượt. |
| **3. Chọn lại Quality (1080p/720p/480p/360p)** | `openmedia_mixer` | `ScaleFilter` | GPU Resize độ phân giải tức thời khi đang chạy video. |
| **4. Chọn lại FPS (60 / 30 / 24 FPS)** | `openmedia_core` | `AVSyncClock`, `PacingEngine` | Thay đổi nhịp đồng bộ khung hình (Frame Interval Pacing Timer). |
| **5. Mute / Unmute & Volume (0-100%)** | `openmedia_audio` | `AudioGain`, `AudioMixer` | Điều chỉnh âm lượng từ `0.0f` đến `1.0f` (hoặc `0.0f` khi Mute). |
| **6. Giải mã Phần cứng GPU** | `openmedia_codecs` | `FFmpegH264Decoder`, `NVDECDecoder` | Giải mã video 4K/1080p bằng GPU NVIDIA/Intel QSV. |
| **7. Giao tiếp IPC Client/Server** | `openmedia_ipc` | `IPCServer`, `IPCClient`, `MessageBuilder` | Truyền lệnh điều khiển qua Named Pipe (`\\.\pipe\OpenMediaSDK`). |
| **8. Render hình ảnh UI C#** | `openmedia_rendering` & `gpu` | `D3D11VideoPlayer` | Vẽ Texture trực tiếp lên C# WPF `Image` / D3DImage Control. |
| **9. Assembly Wrapper C#** | `OpenMedia.Core.NET` | [`SDKEngine`](file:///c:/Users/ASUS%20NUC/Desktop/Code/OME/wrappers/OpenMedia.Core.NET/SDK/SDKEngine.cs), [`SDKPipeline`](file:///c:/Users/ASUS%20NUC/Desktop/Code/OME/wrappers/OpenMedia.Core.NET/SDK/SDKPipeline.cs) | Thư viện C# giao tiếp IPC dạng Async (`InitializeAsync()`). |

---

### ⚙️ YÊU CẦU TÍNH NĂNG CHI TIẾT (FUNCTIONAL REQUIREMENTS)

#### 1. Thanh trượt Phát File (Seekbar Slider & Timeline):
- Hiển thị thời gian hiện tại / tổng thời lượng (ví dụ: `01:45 / 05:30`).
- Khi kéo thả thanh trượt `SeekSlider`, Client gửi lệnh `IPC_CMD_SEEK` mang thông số `position_ms` tới Server.
- Server gọi `pipeline->Seek(position_ms)` và lập tức render khung hình tại vị trí mới.

#### 2. Chọn Độ phân giải Động (Resolution / Quality Selector):
- Cung cấp Dropdown ComboBox trên UI: `1080p (FHD)`, `720p (HD)`, `480p (SD)`, `360p`.
- Khi chọn, Client gửi `IPC_CMD_SET_RESOLUTION` với `width` và `height`.
- Server cập nhật `ScaleFilter` trực tiếp trên GPU mà **không cần dừng hay khởi tạo lại Pipeline**.

#### 3. Chọn Tốc độ Khung hình (FPS Selector):
- Cung cấp Dropdown ComboBox: `60 FPS`, `30 FPS`, `24 FPS`.
- Client gửi `IPC_CMD_SET_FPS` chứa giá trị `target_fps`.
- Server cập nhật `AVSyncClock` để điều chỉnh tần số phát khung hình.

#### 4. Tắt/Mở tiếng (Mute) & Thanh trượt Âm lượng (Volume Slider):
- Nút bấm `BtnMute` (Mute/Unmute) và thanh trượt `VolumeSlider` (0% đến 100%).
- Client gửi `IPC_CMD_SET_VOLUME` với giá trị float `0.0f` đến `1.0f`.
- Server áp dụng giá trị Gain lên `AudioGain` node.

---

### 💻 HƯỚNG DẪN MÃ NGUỒN C# CLIENT KẾT NỐI IPC

Hãy sử dụng lớp C# giao tiếp IPC dưới đây làm chuẩn mẫu triển khai:

```csharp
using System;
using System.Threading.Tasks;
using OpenMedia.SDK;

namespace OME_MediaPlay
{
    public class OME_MediaPlayController
    {
        private IPCClient _ipcClient;
        private uint _activePipelineId = 0;

        public bool IsConnected => _ipcClient != null && _ipcClient.IsConnected;

        /// <summary>
        /// Khởi tạo kết nối IPC với OpenMediaServer.exe
        /// </summary>
        public async Task<bool> ConnectServerAsync()
        {
            // SDKEngine tự động tìm và chạy OpenMediaServer.exe nếu chưa bật
            bool success = await SDKEngine.Instance.InitializeAsync(pipeName: "OpenMediaSDK", serverPath: "OpenMediaServer.exe");
            if (success)
            {
                _ipcClient = SDKEngine.Instance.IPC;
            }
            return success;
        }

        /// <summary>
        /// Gửi lệnh nạp File Video và Dựng Pipeline trên Server
        /// </summary>
        public async Task<bool> LoadVideoAsync(string videoPath)
        {
            var msg = new MessageBuilder();
            msg.WriteString(videoPath);
            msg.WriteU32(1920); // Width mặc định 1080p
            msg.WriteU32(1080); // Height
            msg.WriteF64(60.0); // FPS mặc định 60

            byte[] response = await _ipcClient.SendAndReceiveAsync((CommandType)0x10, msg.ToArray()); // CMD_CREATE_PIPELINE
            if (response != null && response.Length >= 4)
            {
                var reader = new MessageReader(response);
                _activePipelineId = reader.ReadU32();
                return _activePipelineId > 0;
            }
            return false;
        }

        public async Task PlayAsync() => await SendPipelineControlCmdAsync(0x01);  // CMD_PLAY
        public async Task PauseAsync() => await SendPipelineControlCmdAsync(0x02); // CMD_PAUSE

        /// <summary>
        /// Gửi lệnh Tua Video (Seekbar)
        /// </summary>
        public async Task SeekToSecondsAsync(double seconds)
        {
            var msg = new MessageBuilder();
            msg.WriteU32(_activePipelineId);
            msg.WriteU64((ulong)(seconds * 1000)); // position_ms

            await _ipcClient.SendAndReceiveAsync((CommandType)0x15, msg.ToArray()); // CMD_SEEK
        }

        /// <summary>
        /// Gửi lệnh Đổi Độ phân giải thời gian thực (1080p, 720p, 480p...)
        /// </summary>
        public async Task ChangeResolutionAsync(int width, int height)
        {
            var msg = new MessageBuilder();
            msg.WriteU32(_activePipelineId);
            msg.WriteU32((uint)width);
            msg.WriteU32((uint)height);

            await _ipcClient.SendAndReceiveAsync((CommandType)0x20, msg.ToArray()); // CMD_SET_RESOLUTION
        }

        /// <summary>
        /// Gửi lệnh Đổi FPS thời gian thực (60, 30, 24 FPS)
        /// </summary>
        public async Task ChangeFPSAsync(float fps)
        {
            var msg = new MessageBuilder();
            msg.WriteU32(_activePipelineId);
            msg.WriteF64(fps);

            await _ipcClient.SendAndReceiveAsync((CommandType)0x21, msg.ToArray()); // CMD_SET_FPS
        }

        /// <summary>
        /// Gửi lệnh Điều chỉnh Volume / Mute
        /// </summary>
        public async Task SetVolumeAsync(float volumeGain)
        {
            var msg = new MessageBuilder();
            msg.WriteU32(_activePipelineId);
            msg.WriteF64(volumeGain); // 0.0f (Mute) -> 1.0f (100%)

            await _ipcClient.SendAndReceiveAsync((CommandType)0x30, msg.ToArray()); // CMD_SET_VOLUME
        }

        private async Task SendPipelineControlCmdAsync(ushort cmdType)
        {
            var msg = new MessageBuilder();
            msg.WriteU32(_activePipelineId);
            await _ipcClient.SendAndReceiveAsync((CommandType)cmdType, msg.ToArray());
        }

        public async Task DisconnectAsync()
        {
            await SDKEngine.Instance.ShutdownAsync();
        }
    }
}