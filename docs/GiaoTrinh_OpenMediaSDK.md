# GIÁO TRÌNH LẬP TRÌNH ỨNG DỤNG TRUYỀN THÔNG & PHÁT SÓNG THỜI GIAN THỰC VỚI OPENMEDIASDK
*Hệ thống Hướng dẫn Lập trình SDK C++/C# từ Cơ bản đến Chuyên nghiệp*

---

## MỤC LỤC GIÁO TRÌNH

- **Chương 1: Kiến trúc Tổng quan OpenMediaSDK & Cấu hình Môi trường Phát triển**
- **Chương 2: Cốt lõi Hệ thống & Mô hình Media Pipeline**
- **Chương 3: Thực hành App 1 (Cơ bản) - CLI Streaming Player (C# Console App)**
- **Chương 4: Thực hành App 2 (Trung cấp) - Trình xem Video & Đo Tín hiệu Âm thanh (WPF App)**
- **Chương 5: Thực hành App 3 (Chuyên nghiệp) - Studio Phát sóng Đa luồng (Live Production Studio)**
- **Chương 6: Tối ưu hóa Hiệu năng, Giám sát & Triển khai Thương mại**

---

## CHƯƠNG 1: KIẾN TRÚC TỔNG QUAN OPENMEDIASDK & CẤU HÌNH MÔI TRƯỜNG

### 1.1 Tổng quan về OpenMediaSDK
OpenMediaSDK được xây dựng dựa trên kiến trúc **Client/Server** và mô hình **Graph-based Media Pipeline**, cho phép kết nối linh hoạt các nút xử lý (Nodes) từ nguồn vào (Source), bộ lọc hiệu ứng (Filter), bộ trộn hình/tiếng (Mixer), bộ mã hóa (Encoder) đến đầu ra truyền dẫn (Output).

### 1.2 Kiến trúc Đa tầng (Multi-Layer Architecture)
Hệ thống OpenMediaSDK bao gồm 3 tầng chính:

```
+-------------------------------------------------------------------+
|  1. Target Language Layer (C# / .NET 8/9 / WPF / WinUI 3)        |
+-------------------------------------------------------------------+
                                | P/Invoke & SWIG Proxy Classes
+-------------------------------------------------------------------+
|  2. SWIG Binding Layer (openmedia_wrap.dll & SWIG Directors)      |
+-------------------------------------------------------------------+
                                | C-ABI Native Calls
+-------------------------------------------------------------------+
|  3. Native C++ Core Engine (openmedia_core.dll / OpenMediaServer) |
|     - Codecs (NVENC, QSV, FFmpeg)   - Protocols (SRT, NDI, WebRTC)|
|     - Mixer (ChromaKey, Transitions) - CG (Chromium HTML5 Graphics)|
+-------------------------------------------------------------------+
```

### 1.3 Cấu hình Môi trường Phát triển
Để biên dịch và sử dụng SDK, máy tính cần trang bị:
- **Hệ điều hành**: Windows 10/11 x64.
- **C++ Compiler**: Visual Studio 2022 / 2026 (MSVC v143/v144) hoặc C++20 standard.
- **CMake**: Phiên bản 3.28 trở lên.
- **.NET SDK**: .NET 8.0 hoặc .NET 9.0 SDK.
- **vcpkg**: Trình quản lý thư viện C++.

#### Quy trình Biên dịch Native SDK:
```powershell
# 1. Cài đặt môi trường & vcpkg dependencies
.\tools\scripts\setup_env.ps1 -DownloadSDKs

# 2. Biên dịch bản Production bằng Script
.\tools\scripts\build.ps1 -Environment production
```
Sau khi biên dịch thành công, các tập tin thư viện thu được gồm:
- `openmedia_core.dll`: Core Engine C++.
- `openmedia_wrap.dll`: C++ Interop Wrapper do SWIG tạo ra.
- `OpenMedia.Core.NET.dll`: Thư viện Managed C# Assembly tại `wrappers/OpenMedia.Core.NET/`.

---

## CHƯƠNG 2: CỐT LÕI HỆ THỐNG & MÔ HÌNH MEDIA PIPELINE

### 2.1 Vòng đời Ứng dụng (`Engine` & `MediaPipeline`)
- **`Engine`**: Đối tượng Singleton hoặc Factory tối cao, quản lý tài nguyên phần cứng (GPU, Memory Pool, Card Capture).
- **`MediaPipeline`**: Đối tượng xây dựng và điều khiển luồng media theo mô hình trạng thái (State Machine: `Uninitialized` ➔ `Built` ➔ `Running` ➔ `Paused` ➔ `Stopped`).

### 2.2 Các loại Node trong Pipeline (Pipeline Nodes)
1. **Source Node (`IMediaObject`)**: Thu nhận dữ liệu media từ File (`FileSource`), luồng mạng (`SRTSource`, `NDISource`, `WebRTCInput`) hoặc Card phần cứng (`DeckLinkInput`).
2. **Filter Node**: Xử lý hiệu ứng hình ảnh (ChromaKey, LUT, Scale, Crop) hoặc âm thanh (Resample, Audio Delay, Channel Map).
3. **Mixer Node**: Trộn nhiều luồng Video (`VideoMixer`) hoặc Audio (`AudioMixer`).
4. **Encoder Node**: Nén dữ liệu hình ảnh/âm thanh (`NVENCEncoder`, `QSVEncoder`, `FFmpegAACEncoder`).
5. **Output Node**: Xuất dữ liệu ra luồng mạng (`SRTOutput`, `RTMPOutput`, `WebRTCOutput`), File (`FileOutput`), Card SDI (`DeckLinkOutput`) hoặc IPC (`IPCOutput`).

### 2.3 Quản lý Bộ nhớ Zero-Copy & Lipsync
- **`FrameQueue` & `MemoryPool`**: Sử dụng hàng đợi không khóa (lock-free) và Memory Pool được cấp phát sẵn nhằm tránh việc `malloc`/`free` liên tục ở tần số 60 FPS, đảm bảo **Zero-Copy** truyền qua con trỏ bộ nhớ.
- **`AVSyncClock`**: Đồng bộ thời gian thực dựa trên nhãn thời gian Presentation Timestamp (PTS) giữa luồng hình và luồng tiếng, triệt tiêu hiện tượng lệch tiếng (Lip-sync issue).

### 2.4 Cơ chế SWIG Interop & Quản lý Con trỏ
Mỗi đối tượng C# giữ một con trỏ thô (`IntPtr`) trỏ tới đối tượng C++ Native tương ứng.
- **Cờ `swigCMemOwn`**: Quyết định xem phía C# khi hủy object có gọi `delete` đối tượng C++ hay không.
- **`NativeBridge`**: Lớp chứa các khai báo `[DllImport]` C-ABI để thực hiện P/Invoke trực tiếp.

---

## CHƯƠNG 3: THỰC HÀNH APP 1 (CƠ BẢN) - CLI STREAMING PLAYER

### 3.1 Mục tiêu Dự án
Xây dựng một ứng dụng Console C# đọc tập tin video `input.mp4`, mã hóa và phát luồng trực tuyến chuẩn **SRT (Secure Reliable Transport)** ra địa chỉ IP chỉ định.

### 3.2 Khởi tạo Dự án C#
1. Mở Terminal / Visual Studio, tạo dự án Console App:
   ```powershell
   dotnet new console -n CLIStreamingApp -f net8.0
   ```
2. Thêm tham chiếu đến `OpenMedia.Core.NET.csproj`.
3. Đảm bảo cấu hình Target Platform trong `.csproj` là `x64`:
   ```xml
   <PropertyGroup>
     <OutputType>Exe</OutputType>
     <TargetFramework>net8.0</TargetFramework>
     <PlatformTarget>x64</PlatformTarget>
   </PropertyGroup>
   ```

### 3.3 Mã nguồn C# Hoàn chỉnh (`Program.cs`)
```csharp
using System;
using OpenMedia.Core.NET;

namespace CLIStreamingApp
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=============================================");
            Console.WriteLine("  OpenMediaSDK - CLI Streaming Player (SRT)  ");
            Console.WriteLine("=============================================");

            string inputPath = args.Length > 0 ? args[0] : "sample.mp4";
            string srtUrl = args.Length > 1 ? args[1] : "srt://127.0.0.1:9000?mode=caller";

            Console.WriteLine($"[1] Khởi tạo Engine...");
            NativeBridge.ome_engine_init("{\"log_level\": \"info\"}");

            Console.WriteLine($"[2] Tạo Media Pipeline...");
            IntPtr pipeline = NativeBridge.ome_pipeline_create();

            Console.WriteLine($"[3] Khởi tạo File Source: {inputPath}");
            IntPtr fileSource = NativeBridge.ome_source_create_file(inputPath);

            Console.WriteLine($"[4] Khởi tạo SRT Output: {srtUrl}");
            IntPtr srtOutput = NativeBridge.om_create_srt_output(srtUrl);

            Console.WriteLine($"[5] Kết nối các Node vào Pipeline...");
            NativeBridge.ome_pipeline_add_node(pipeline, fileSource);
            NativeBridge.ome_pipeline_add_node(pipeline, srtOutput);

            Console.WriteLine($"[6] Bắt đầu phát luồng SRT!");
            NativeBridge.ome_pipeline_start(pipeline);

            Console.WriteLine("\n--> Luồng đang phát. Nhấn ENTER để dừng chương trình...");
            Console.ReadLine();

            Console.WriteLine($"[7] Dừng luồng và giải phóng tài nguyên...");
            NativeBridge.ome_pipeline_stop(pipeline);
            NativeBridge.ome_source_destroy(fileSource);
            NativeBridge.ome_output_destroy(srtOutput);
            NativeBridge.ome_pipeline_destroy(pipeline);
            NativeBridge.ome_engine_shutdown();

            Console.WriteLine("Đã thoát ứng dụng an toàn.");
        }
    }
}
```

### 3.4 Kiểm tra Kết quả
1. Chạy VLC Player trên máy nhận. Mở `Media > Open Network Stream...` và nhập: `srt://127.0.0.1:9000?mode=listener`.
2. Chạy ứng dụng C#: `dotnet run -- sample.mp4 srt://127.0.0.1:9000?mode=caller`.
3. Kiểm tra tín hiệu phát mượt mà trên VLC với độ trễ dưới 200ms.

---

## CHƯƠNG 4: THỰC HÀNH APP 2 (TRUNG CẤP) - TRÌNH XEM VIDEO & ĐO TÍN HIỆU ÂM THANH (WPF APP)

### 4.1 Mục tiêu Dự án
Xây dựng ứng dụng giao diện đồ họa WPF bao gồm:
1. Màn hình Xem trước Video (Preview) tăng tốc phần cứng GPU DirectX 11 (`D3D11VideoPlayer`).
2. Thanh đo cường độ âm thanh thời gian thực (LUFS / Peak Meter) dùng `openmedia_audio`.
3. Nhận Callback từ Native C++ thông qua cơ chế SWIG Director.

### 4.2 Cấu trúc Giao diện WPF (`MainWindow.xaml`)
```xml
<Window x:Class="WpfAudioVideoApp.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="OpenMediaSDK - WPF Video &amp; Audio Meter" Height="600" Width="900"
        Background="#1E1E1E" Foreground="White">
    <Grid Margin="10">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*"/>
            <ColumnDefinition Width="180"/>
        </Grid.ColumnDefinitions>

        <!-- Khung Render Video DirectX 11 -->
        <Border Grid.Column="0" Background="Black" CornerRadius="8" Margin="0,0,10,0">
            <Image x:Name="VideoPreviewImage" Stretch="Uniform"/>
        </Border>

        <!-- Bảng điều khiển & Đo Âm thanh -->
        <StackPanel Grid.Column="1" VerticalAlignment="Top">
            <TextBlock Text="AUDIO METER" FontWeight="Bold" Foreground="#00E5FF" Margin="0,0,0,10"/>
            
            <TextBlock Text="Left Channel (dB)" FontSize="11"/>
            <ProgressBar x:Name="PbLeftChannel" Height="18" Minimum="-60" Maximum="0" Value="-60" Foreground="#00E676" Margin="0,5,0,15"/>

            <TextBlock Text="Right Channel (dB)" FontSize="11"/>
            <ProgressBar x:Name="PbRightChannel" Height="18" Minimum="-60" Maximum="0" Value="-60" Foreground="#00E676" Margin="0,5,0,20"/>

            <Button x:Name="BtnStart" Content="BẮT ĐẦU phát" Height="40" Background="#2979FF" Foreground="White" Click="BtnStart_Click" Margin="0,0,0,10"/>
            <Button x:Name="BtnStop" Content="DỪNG" Height="35" Background="#D50000" Foreground="White" Click="BtnStop_Click"/>
        </StackPanel>
    </Grid>
</Window>
```

### 4.3 Code-Behind WPF (`MainWindow.xaml.cs`)
```csharp
using System;
using System.Windows;
using System.Windows.Media;
using OpenMedia.Core.NET;
using OpenMedia.SDK;

namespace WpfAudioVideoApp
{
    public partial class MainWindow : Window
    {
        private IntPtr _pipeline = IntPtr.Zero;
        private IntPtr _source = IntPtr.Zero;

        public MainWindow()
        {
            InitializeComponent();
            NativeBridge.ome_engine_init("{\"mode\": \"gui\"}");
        }

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            _pipeline = NativeBridge.ome_pipeline_create();
            _source = NativeBridge.ome_source_create_file("sample.mp4");

            NativeBridge.ome_pipeline_add_node(_pipeline, _source);
            NativeBridge.ome_pipeline_start(_pipeline);

            // Giả lập nhận Callback đo Audio thời gian thực (Director Event)
            CompositionTarget.Rendering += UpdateAudioMeters;
            BtnStart.IsEnabled = false;
        }

        private void UpdateAudioMeters(object sender, EventArgs e)
        {
            // Đọc giá trị Meter từ C++ Core
            float leftDb = (float)(new Random().NextDouble() * 40 - 40);  // Mô phỏng tín hiệu dB
            float rightDb = (float)(new Random().NextDouble() * 40 - 40);

            PbLeftChannel.Value = leftDb;
            PbRightChannel.Value = rightDb;
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            if (_pipeline != IntPtr.Zero)
            {
                CompositionTarget.Rendering -= UpdateAudioMeters;
                NativeBridge.ome_pipeline_stop(_pipeline);
                NativeBridge.ome_source_destroy(_source);
                NativeBridge.ome_pipeline_destroy(_pipeline);
                _pipeline = IntPtr.Zero;
            }
            BtnStart.IsEnabled = true;
        }

        protected override void OnClosed(EventArgs e)
        {
            BtnStop_Click(null, null);
            NativeBridge.ome_engine_shutdown();
            base.OnClosed(e);
        }
    }
}
```

---

## CHƯƠNG 5: THỰC HÀNH APP 3 (CHUYÊN NGHIỆP) - STUDIO PHÁT SÓNG ĐA LUỒNG (LIVE PRODUCTION STUDIO)

### 5.1 Kiến trúc Studio Truyền hình Đa kênh
Ứng dụng Studio chuyên nghiệp quản lý cùng lúc 4 nguồn Video vào (NDI, SRT, WebRTC, File Playback), trộn hình với hiệu ứng **ChromaKey (Tách phông xanh)**, chèn đồ họa động **CEF HTML5 CG** và xuất 4 luồng ra đồng thời (SRT Internet, NDI LAN, OTT HLS và Card Blackmagic DeckLink SDI).

```
[ NDI Cam ] ----\
[ SRT Stream ] ---\   +-------------------------------------------------------+
[ WebRTC Input ] -->  | openmedia_mixer (ChromaKey + Transitions + Multi-view) |
[ File Playlist ] -/   +-------------------------------------------------------+
                                          |
                                          v
                       +-----------------------------------+
                       | openmedia_cg (CEF HTML5 Graphics) |
                       +-----------------------------------+
                                          |
                                          v
       +------------------------------------------------------------------+
       |   Multi-Output: SRT Output + NDI Output + HLS + DeckLink SDI    |
       +------------------------------------------------------------------+
```

### 5.2 Xây dựng Studio Engine C# (`StudioEngine.cs`)
```csharp
using System;
using System.Threading.Tasks;
using OpenMedia.SDK;
using OpenMedia.Core.NET;

namespace LiveProductionStudio
{
    public class StudioEngine
    {
        private IntPtr _pipeline;
        private IntPtr _mixerNode;
        private IntPtr _cgNode;

        public void InitializeStudio()
        {
            // 1. Khởi tạo Engine ở chế độ Studio High Performance
            NativeBridge.ome_engine_init("{\"threads\": 8, \"gpu_acceleration\": true}");
            _pipeline = NativeBridge.ome_pipeline_create();

            // 2. Khởi tạo Bộ trộn Video đa kênh (openmedia_mixer)
            _mixerNode = NativeBridge.ome_mixer_create();
            
            // 3. Khởi tạo Engine Đồ họa HTML5 Chromium (openmedia_cg)
            _cgNode = NativeBridge.ome_cg_create("https://graphics.mystudio.com/lower_third.html");

            // 4. Kết nối Mixer và CG vào Pipeline
            NativeBridge.ome_pipeline_add_node(_pipeline, _mixerNode);
            NativeBridge.ome_pipeline_add_node(_pipeline, _cgNode);
        }

        public void AddNDICameraInput(string ndiName)
        {
            IntPtr ndiSource = NativeBridge.om_create_ndi_source(ndiName);
            NativeBridge.ome_pipeline_add_node(_pipeline, ndiSource);
        }

        public void EnableChromaKey(int inputLayerId, float keyColorR, float keyColorG, float keyColorB)
        {
            // Bật tách phông xanh lá cây cho lớp MC
            NativeBridge.ome_mixer_set_chromakey(_mixerNode, inputLayerId, keyColorR, keyColorG, keyColorB, 0.15f);
        }

        public void AddBroadcastOutputs(string srtDestination, string decklinkDevice)
        {
            // Xuất luồng SRT ra Internet
            IntPtr srtOut = NativeBridge.om_create_srt_output(srtDestination);
            NativeBridge.ome_pipeline_add_node(_pipeline, srtOut);

            // Xuất ra Card Blackmagic DeckLink SDI cho đài truyền hình
            IntPtr decklinkOut = NativeBridge.om_create_decklink_output(decklinkDevice);
            NativeBridge.ome_pipeline_add_node(_pipeline, decklinkOut);
        }

        public void StartBroadcasting()
        {
            NativeBridge.ome_pipeline_start(_pipeline);
        }

        public void StopBroadcasting()
        {
            NativeBridge.ome_pipeline_stop(_pipeline);
            NativeBridge.ome_pipeline_destroy(_pipeline);
            NativeBridge.ome_engine_shutdown();
        }
    }
}
```

---

## CHƯƠNG 6: TỐI ƯU HÓA HIỆU NĂNG, GIÁM SÁT & TRIỂN KHAI THƯƠNG MẠI

### 6.1 Giám sát Hiệu năng Thời gian thực (`openmedia_monitoring`)
Trong ứng dụng sản xuất phát sóng, việc theo dõi chỉ số hạ tầng là bắt buộc. Sử dụng `MediaStatsManager` để lấy chỉ số:
- **Bitrate**: Dung lượng băng thông phát.
- **FPS**: Tốc độ khung hình thực tế (Đảm bảo luôn = 59.94 hoặc 60.0 FPS).
- **Dropped Frames**: Số khung hình bị rớt (Nếu > 0 cần cảnh báo mạng/CPU quá tải).

### 6.2 Mô hình Client-Server IPC Cô lập Crash
Đối với các ứng dụng thương mại lớn, nên tách tiến trình UI C# và Tiến trình xử lý Media C++ thành 2 tiến trình độc lập qua `OpenMediaServer.exe`:
- Tiến trình C# kết nối qua `SDKEngine.Instance.InitializeAsync()`.
- Nếu module C++ gặp sự cố phần cứng, `OpenMediaServer.exe` có thể tự động khởi động lại (Auto-restart) mà **không làm tắt ứng dụng C# của người dùng**.

### 6.3 Checklist Triển khai Ứng dụng Thương mại (Deployment Checklist)
- [x] Đã cấu hình Build C# ở chế độ `Release` và Target `x64`.
- [x] Đã đóng gói đầy đủ các tập tin `.dll` C++ Native (`openmedia_core.dll`, `openmedia_wrap.dll`, `ffmpeg*.dll`, `cef*.dll`).
- [x] Đã kiểm tra tương thích card đồ họa GPU (NVIDIA Driver / Intel Graphics Driver).
- [x] Đã xử lý giải phóng bộ nhớ `Dispose()` cho toàn bộ con trỏ unmanaged khi thoát ứng dụng.

---
*Tài liệu Giáo trình do Antigravity AI Engine biên soạn theo chuẩn Kiến trúc OpenMediaSDK 2026.*
