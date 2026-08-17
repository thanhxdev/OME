# Hướng Dẫn Tích Hợp OpenMedia.SDK Vào WinUI 3 Cho Nhà Phát Triển (Consumer)
*(Dành cho lập trình viên kế thừa/sử dụng thư viện, giải thích chi tiết cơ chế hoạt động và lý do thực hiện từng bước)*

---

## 1. Giới Thiệu & Mô Hình Kiến Trúc (Architecture Model)

### 1.1. Các Thành Phần Trong Hệ Thống
Để làm việc hiệu quả với OpenMedia SDK, trước hết bạn cần hiểu cấu trúc phân tầng của bộ thư viện này:
* **Native Core C++ (`OpenMedia.Core.dll`)**: Đây là tầng xử lý media cốt lõi (Core Engine). Nó chịu trách nhiệm giải mã video (Video Decoding), trộn hình ảnh/âm thanh (Mixing/CG) và dựng hình trực tiếp lên GPU bằng DirectX 11 (D3D11). Lý do viết bằng C++ là để tận dụng tối đa sức mạnh phần cứng, tối ưu hóa bộ nhớ và giảm thiểu độ trễ xử lý realtime.
* **Managed Wrapper C# (`OpenMedia.Core.NET`)**: Là cầu nối trung gian. Lớp này sử dụng kỹ thuật P/Invoke (`DllImport` trong class [NativeBridge](file:///c:/Users/ASUS%20NUC/Desktop/Code/OME/wrappers/OpenMedia.Core.NET/NativeBridge.cs)) để ánh xạ các hàm C++ từ file DLL native thành các hàm C# có thể gọi được. Nó giúp bạn tương tác với Core Engine mà không cần viết code C++ phức tạp.
* **WinUI 3 Host Application**: Là ứng dụng giao diện (Presentation Layer) mà bạn đang phát triển. Nhiệm vụ của nó là cung cấp cửa sổ hiển thị và các nút điều khiển luồng media.

### 1.2. Cơ Chế Zero-Copy Rendering (D3D11 SwapChain Sharing)
* **Vấn đề thông thường**: Nếu truyền dữ liệu video thô (Raw Frames) từ C++ lên C# rồi vẽ lại lên UI, dữ liệu sẽ phải copy liên tục từ GPU -> RAM (C++) -> Managed RAM (C#) -> GPU (WinUI 3). Quá trình này gây tốn CPU khủng khiếp, làm nóng máy và nghẽn băng thông RAM, gây giật lag (đặc biệt với video 4K hoặc tốc độ khung hình cao).
* **Giải pháp Zero-Copy**: DirectX cung cấp cơ chế chia sẻ chuỗi hoán đổi khung hình (`DXGI SwapChain`). Bằng cách sử dụng interface COM native [ISwapChainPanelNative](file:///c:/Users/ASUS%20NUC/Desktop/Code/OME/wrappers/OpenMedia.Core.NET/WinUIInterop.cs#L8-L14) trên thẻ `SwapChainPanel` của WinUI 3, chúng ta chỉ truyền **con trỏ bộ nhớ GPU** (`IntPtr` của SwapChain) từ C++ xuống UI thông qua [WinUIInterop.SetSwapChain](file:///c:/Users/ASUS%20NUC/Desktop/Code/OME/wrappers/OpenMedia.Core.NET/WinUIInterop.cs#L16-L26). Sau đó, GPU của Core Engine C++ sẽ vẽ trực tiếp các khung hình đã render lên màn hình UI mà không qua bất kỳ bước sao chép dữ liệu (Zero-Copy) nào trong bộ nhớ RAM hay C#.

---

## 2. Bước 1: Khởi Tạo Dự Án WinUI 3 Packaged App

### Cách thực hiện:
1. Mở Visual Studio (khuyến nghị bản mới nhất hỗ trợ .NET 8/10).
2. Tạo dự án mới, tìm kiếm và chọn template: **Blank App, Packaged (WinUI 3 in Desktop)**.
3. Đặt tên dự án (ví dụ: `OpenMedia.PreviewApp`).

### **Lý do thực hiện:**
* **WinUI 3 (Windows App SDK)** là framework UI hiện đại nhất của Microsoft dành cho ứng dụng Desktop Windows, hỗ trợ tích hợp trực tiếp DirectX và XAML Composition APIs tốt hơn WPF hay WinForms cổ điển.
* Phiên bản **Packaged (MSIX)** đóng gói ứng dụng trong một container ảo hóa để triển khai an toàn và phân phối dễ dàng. Tuy nhiên, điều này đồng nghĩa với việc ứng dụng chạy trong một môi trường cô lập (Sandbox/AppContainer), gây ra các giới hạn nghiêm ngặt về phân quyền thư mục và tìm kiếm file DLL native (xem chi tiết ở Bước 3 & Bước 5).

---

## 3. Bước 2: Tham Chiếu Đến Thư Viện OpenMedia.Core.NET

Có hai cách để tham chiếu thư viện, tùy thuộc vào mục đích của bạn:

### Cách 1: Thêm Project Reference vào Solution (Khuyên dùng trong giai đoạn phát triển)
1. Chuột phải vào Solution -> Chọn **Add** -> **Existing Project...**
2. Tìm và chọn file [OpenMedia.Core.NET.csproj](file:///c:/Users/ASUS%20NUC/Desktop/Code/OME/wrappers/OpenMedia.Core.NET/OpenMedia.Core.NET.csproj).
3. Chuột phải vào **Dependencies** của project WinUI -> Chọn **Add Project Reference...**
4. Chọn project `OpenMedia.Core.NET` và nhấn **OK**.

### Cách 2: Trỏ trực tiếp tới file `OpenMedia.Core.NET.dll` đã build sẵn
1. Build project `OpenMedia.Core.NET` trước.
2. Chuột phải vào **Dependencies** của project WinUI -> Chọn **Add Project Reference...** -> Chọn tab **Browse** -> Tìm file `OpenMedia.Core.NET.dll` trong thư mục output (ví dụ: `bin/Debug/net8.0/`).

### **Lý do thực hiện:**
* `OpenMedia.Core.NET` chứa toàn bộ các định nghĩa lớp C# (như `Pipeline`, `Engine`, `Mixer`) giúp bạn lập trình hướng đối tượng một cách tự nhiên trong WinUI.
* **Cách 1** giúp bạn có thể nhảy trực tiếp vào mã nguồn của wrapper C# để Debug (Step Into), sửa lỗi hoặc tùy chỉnh wrapper khi phát hiện lỗi không tương thích giữa C# và C++.

---

## 4. Bước 3: Đóng Gói Và Tự Động Copy Các DLL Native (Quan trọng nhất)

### Cách thực hiện:
Mở file `.csproj` của dự án WinUI 3 (click đúp vào project), thêm đoạn mã dưới đây vào trước thẻ đóng `</Project>`:

```xml
  <ItemGroup>
    <!-- Đóng gói tự động toàn bộ file DLL native từ thư mục build C++ vào AppX -->
    <Content Include="c:\Users\ASUS NUC\Desktop\Code\OME\build-demo\bin\Debug\*.dll">
      <Link>%(Filename)%(Extension)</Link>
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
  </ItemGroup>
```
*(Nếu bạn build phiên bản Production, hãy đổi đường dẫn đến thư mục `build-production\bin\Release`)*

### **Lý do thực hiện:**
* **Bản chất của P/Invoke**: Lớp `NativeBridge` sử dụng `[DllImport("OpenMedia.Core.dll")]`. Khi chạy, .NET Runtime sẽ tìm kiếm file `OpenMedia.Core.dll` trong thư mục thực thi của ứng dụng.
* **Sự phụ thuộc của DLL Native (Dependency Tree)**: File `OpenMedia.Core.dll` không đứng độc lập. Nó cần các thư viện native đi kèm như FFmpeg (`avcodec-63.dll`, `avutil-61.dll`), logging (`spdlogd.dll`), network (`srt.dll`), và phần mềm tăng tốc đồ họa ảo (`vk_swiftshader.dll`). Thiếu bất kỳ file nào trong số này cũng sẽ dẫn đến lỗi không thể nạp thư viện (thường báo là `DllNotFoundException`).
* **Đặc tính của MSIX Packaging**: Đối với dự án WinUI 3 Packaged App, nếu bạn chỉ copy DLL vào thư mục build bằng lệnh `<Copy>` thông thường của MSBuild, các file này sẽ bị bỏ lại bên ngoài gói cài đặt ảo (`AppX`). Khi chạy app, hệ thống sẽ kích hoạt app từ một thư mục ảo hóa đóng kín và không thể tìm thấy các file DLL native này. Việc khai báo thẻ `<Content>` kèm `<Link>` ép buộc MSBuild phải đưa các file DLL này vào danh sách tài nguyên đóng gói chính thức bên trong `AppX`.

---

## 5. Bước 4: Thiết Kế Giao Diện XAML Với SwapChainPanel

### Cách thực hiện:
Mở file `MainWindow.xaml` và thiết lập giao diện như sau:

```xml
<Window
    x:Class="OpenMedia.PreviewApp.MainWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Title="OpenMedia WinUI 3 Preview"
    Width="800"
    Height="600">

    <Grid Background="#1E1E1E">
        <Grid.RowDefinitions>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- Nơi GPU vẽ trực tiếp Video lên -->
        <SwapChainPanel x:Name="VideoPreviewPanel" 
                        HorizontalAlignment="Stretch" 
                        VerticalAlignment="Stretch"
                        Grid.Row="0"/>

        <!-- Bảng điều khiển tích hợp Chọn File & Preview -->
        <StackPanel Orientation="Horizontal" 
                    HorizontalAlignment="Center" 
                    VerticalAlignment="Center" 
                    Grid.Row="1" 
                    Margin="20" 
                    Spacing="15">
            <!-- Nút chọn file video -->
            <Button x:Name="SelectFileButton" 
                    Content="Chọn File Video..." 
                    Click="SelectFileButton_Click"
                    Padding="15,8,15,8"/>
            
            <TextBlock x:Name="SelectedFilePathText" 
                       Text="Chưa chọn file" 
                       VerticalAlignment="Center" 
                       Foreground="Gray"
                       MaxWidth="250"
                       TextTrimming="CharacterEllipsis"/>

            <Button x:Name="StartButton" 
                    Content="Bắt đầu Preview" 
                    Click="StartButton_Click" 
                    IsEnabled="False"
                    Style="{ThemeResource AccentButtonStyle}"
                    Padding="15,8,15,8"/>
            
            <Button x:Name="StopButton" 
                    Content="Dừng Preview" 
                    Click="StopButton_Click" 
                    IsEnabled="False"
                    Padding="15,8,15,8"/>
        </StackPanel>
    </Grid>
</Window>
```

### **Lý do thực hiện:**
* Thẻ `<SwapChainPanel>` là một thành phần UI đặc biệt kế thừa trực tiếp từ WinRT Composition API. Khác với `<Image>` thông thường (đòi hỏi bạn phải cập nhật mảng Byte liên tục bằng CPU), `SwapChainPanel` liên kết trực tiếp với luồng dựng hình phần cứng của card đồ họa. Nó cung cấp cơ chế nhận một chuỗi hoán đổi DirectX native và hiển thị mượt mà với tốc độ 60fps+ mà gần như không chiếm dụng CPU của luồng UI.
* Chúng ta cần thêm nút **Chọn File Video** và **TextBlock hiển thị tên file** để người dùng có thể linh hoạt chọn tệp tin cần preview (ví dụ: các tệp tin .mp4, .mkv trong máy) thay vì gán cứng (hardcode) đường dẫn video trong code C#.

---

## 6. Bước 5: Lập Trình Code-Behind C# & Giải Quyết Các Lỗi Crash Hệ Thống (Bắt buộc)

Khi tích hợp thư viện này vào WinUI 3, bạn sẽ gặp phải **2 lỗi crash nghiêm trọng** ngay lập tức. Dưới đây là cách giải quyết triệt để và giải thích nguyên nhân khoa học:

### 6.1. Xử lý lỗi Crash 1: `System.Runtime.InteropServices.SEHException` tại `Engine.Initialize`

#### **Triệu chứng:**
Ngay khi gọi `Engine.Initialize(...)`, chương trình lập tức crash và báo lỗi: *System.Runtime.InteropServices.SEHException: 'External component has thrown an exception.'*

#### **Nguyên nhân:**
1. Khi khởi tạo Engine, mã nguồn C++ native sẽ gọi `Logger::Initialize()` để cấu hình lưu vết hoạt động.
2. Theo mặc định, cấu hình ghi log ra file được bật và nó sẽ cố gắng tạo thư mục ghi log tại `./logs/openmedia.log` (đường dẫn tương đối so với thư mục làm việc hiện tại - Current Working Directory).
3. Do dự án WinUI 3 Packaged chạy trong container ảo hóa MSIX, thư mục làm việc mặc định của ứng dụng thường trỏ về thư mục hệ thống bảo mật (ví dụ: `C:\Windows\System32`) hoặc thư mục cài đặt gốc bị cấm ghi file (`C:\Program Files\WindowsApps\...`).
4. Khi hàm C++ thực hiện thao tác tạo thư mục `std::filesystem::create_directories("./logs")` tại các vị trí cấm này, hệ điều hành Windows sẽ từ chối truy cập và C++ ném ra ngoại lệ `std::filesystem::filesystem_error`.
5. Vì API C++ Native export không bắt ngoại lệ này ở biên giới DLL, nó bị rò rỉ qua lớp P/Invoke và chuyển dịch thành lỗi Structured Exception Handling (SEH) ở phía C#, gây sập app.

#### **Giải pháp khắc phục:**
C++ Core Engine được thiết kế để đọc cấu hình thư mục log qua biến môi trường trước khi sử dụng các giá trị mặc định. Để bảo vệ các ứng dụng WinUI 3 Packaged khỏi bị crash, lớp wrapper C# [Engine.cs](file:///c:/Users/ASUS%20NUC/Desktop/Code/OME/wrappers/OpenMedia.Core.NET/Engine.cs) đã tự động thực hiện định tuyến lại (redirect) biến môi trường `OME_LOG_DIR` về thư mục an toàn của người dùng (`%LocalAppData%\OpenMedia\logs`) trước khi gọi hàm khởi tạo C++ native.

Nếu bạn là nhà phát triển muốn tùy chỉnh đường dẫn ghi log khác, bạn có thể tự thiết lập biến môi trường này trước khi gọi khởi tạo:

```csharp
// Tùy chọn (Chỉ cần nếu muốn ghi vào một thư mục tùy chỉnh khác)
Environment.SetEnvironmentVariable("OME_LOG_DIR", @"D:\AppLogs\OpenMedia");
```

Nếu không cấu hình gì, hệ thống sẽ tự động ghi log vào thư mục được phân quyền đầy đủ:
`C:\Users\<Tên_User>\AppData\Local\OpenMedia\logs\openmedia.log`

### 6.2. Xử lý lỗi Crash 2: `System.EntryPointNotFoundException` khi tạo `new Pipeline()`

#### **Triệu chứng:**
Ứng dụng báo lỗi không tìm thấy điểm đi vào (Entry Point): *System.EntryPointNotFoundException: 'Unable to find an entry point named 'ome_pipeline_set_state_callback' in DLL 'OpenMedia.Core.dll'.'*

#### **Nguyên nhân:**
1. Trong file wrapper [Pipeline.cs](file:///c:/Users/ASUS%20NUC/Desktop/Code/OME/wrappers/OpenMedia.Core.NET/Pipeline.cs#L43-L46), hàm dựng `public Pipeline()` cố gắng đăng ký hai hàm callback lắng nghe sự kiện thay đổi trạng thái và báo lỗi từ Native Core C++:
   * `NativeBridge.ome_pipeline_set_state_callback(...)`
   * `NativeBridge.ome_pipeline_set_error_callback(...)`
2. Tuy nhiên, trong mã nguồn C++ của thư viện Core ([openmedia_c_api.cpp](file:///c:/Users/ASUS%20NUC/Desktop/Code/OME/src/core/openmedia_c_api.cpp)), hai hàm xuất khẩu (export) này **chưa được triển khai** trong phiên bản trước.
3. Khi C# cố gắng nạp địa chỉ hàm từ DLL native lúc khởi tạo `Pipeline`, hệ điều hành không tìm thấy export tương ứng và ném lỗi `EntryPointNotFoundException`.

#### **Giải pháp đã khắc phục:**
Lỗi này đã được sửa triệt để ở tầng C++ API bằng cách bổ sung hai hàm export còn thiếu vào file [openmedia_c_api.h](file:///c:/Users/ASUS%20NUC/Desktop/Code/OME/src/core/openmedia_c_api.h) và [openmedia_c_api.cpp](file:///c:/Users/ASUS%20NUC/Desktop/Code/OME/src/core/openmedia_c_api.cpp):

```c
// Khai báo typedef callback (openmedia_c_api.h)
typedef void (*ome_state_callback_t)(int new_state);
typedef void (*ome_error_callback_t)(int error_code, const char* message);
OME_API void ome_pipeline_set_state_callback(ome_pipeline_t pipeline, ome_state_callback_t callback);
OME_API void ome_pipeline_set_error_callback(ome_pipeline_t pipeline, ome_error_callback_t callback);
```

Các hàm callback được lưu trữ trong một registry nội bộ (`std::unordered_map`) bảo vệ bởi mutex, đảm bảo thread-safety. Khi `ome_pipeline_destroy` được gọi, callback sẽ tự động được dọn dẹp khỏi registry.

**Lưu ý cho Consumer:** Nếu bạn đang sử dụng file DLL đã được build lại (từ thư mục `build-demo/bin/Debug`), lỗi này sẽ không còn xảy ra và bạn **không cần sửa đổi bất kỳ file C# nào**. File [Pipeline.cs](file:///c:/Users/ASUS%20NUC/Desktop/Code/OME/wrappers/OpenMedia.Core.NET/Pipeline.cs) giữ nguyên logic đăng ký callback gốc.

---

### 6.3. File Code MainWindow.xaml.cs Hoàn Chỉnh

Dưới đây là mã nguồn code-behind tích hợp đầy đủ giải pháp chọn file động bằng `FileOpenPicker` cùng các giải pháp sửa lỗi & dọn dẹp bộ nhớ:

```csharp
using Microsoft.UI.Xaml;
using System;
using System.IO;
using System.Threading.Tasks;
using OpenMedia.SDK; // Sử dụng Namespace của Wrapper C#
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop; // Thư viện bắt buộc để lấy HWND gán cho FilePicker trong WinUI 3

namespace OpenMedia.PreviewApp
{
    public sealed partial class MainWindow : Window
    {
        private Pipeline _pipeline;
        private FileSource _source;
        private Mixer _mixer;
        private string _selectedFilePath = null;

        public MainWindow()
        {
            this.InitializeComponent();

            // Khởi tạo Engine Core C++ (Lớp wrapper Engine.cs đã tự xử lý 
            // lỗi ghi log/SEHException bằng cách tự redirect về LocalAppData)
            string configJson = "{\"Threads\": 4, \"OME_LOG_LEVEL\": \"debug\"}";
            Engine.Initialize(configJson);
        }

        // ============================================================
        // HÀM CHỌN FILE VIDEO: XỬ LÝ LỖI THIẾU CHỌN FILE PREVIEW
        // ============================================================
        private async void SelectFileButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.ViewMode = PickerViewMode.Thumbnail;
            picker.SuggestedStartLocation = PickerLocationId.VideosLibrary;
            
            // Thiết lập bộ lọc định dạng video hỗ trợ
            picker.FileTypeFilter.Add(".mp4");
            picker.FileTypeFilter.Add(".mkv");
            picker.FileTypeFilter.Add(".avi");

            // --------------------------------------------------------
            // LÝ DO PHẢI THỰC HIỆN BƯỚC NÀY:
            // Trong các ứng dụng Desktop (Win32/WinUI 3), các WinRT Picker (như FileOpenPicker)
            // đòi hỏi phải có một cửa sổ chi phối (HWND) làm mỏ neo để hiển thị hộp thoại Modal.
            // Nếu bạn không gọi InitializeWithWindow, ứng dụng sẽ crash lập tức với lỗi COMException.
            // --------------------------------------------------------
            IntPtr hwnd = WindowNative.GetWindowHandle(this);
            InitializeWithWindow.Initialize(picker, hwnd);

            StorageFile file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                _selectedFilePath = file.Path;
                SelectedFilePathText.Text = file.Name;
                SelectedFilePathText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
                
                // Kích hoạt nút chạy Preview sau khi đã chọn file thành công
                StartButton.IsEnabled = true;
            }
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (_pipeline != null || string.IsNullOrEmpty(_selectedFilePath)) return;

            try
            {
                // 1. Khởi tạo Pipeline (Đã comment-out phần callback lỗi trong Pipeline.cs)
                _pipeline = new Pipeline();

                // 2. Tạo đối tượng FileSource từ đường dẫn tệp tin động do user chọn
                _source = new FileSource(_selectedFilePath);

                // 3. Tạo Mixer để dựng hình video
                _mixer = new Mixer();

                // 4. Đăng ký luồng video của FileSource vào Layer 0 của Mixer
                _mixer.AddInput(_source, 0);

                // 5. Thêm nút Mixer vào Pipeline Graph
                _pipeline.WithNode(_mixer.Handle);

                // ============================================================
                // GIẢI PHÁP ZERO-COPY RENDERING: LIÊN KẾT SWAPCHAIN
                // ============================================================
                // LƯU Ý QUAN TRỌNG: Bạn BẮT BUỘC phải lấy con trỏ DirectX SwapChain từ Native Core
                // và liên kết với VideoPreviewPanel thông qua WinUIInterop.SetSwapChain.
                // Nếu KHÔNG gọi SetSwapChain, khung preview sẽ chỉ là màn hình đen (không hiển thị video)!
                // Example:
                // IntPtr swapChainPtr = _pipeline.GetPreviewSwapChain(); 
                // WinUIInterop.SetSwapChain(VideoPreviewPanel, swapChainPtr);

                // Khởi chạy luồng xử lý bất đồng bộ tránh gây treo giao diện (Freeze UI)
                bool startSuccess = await _pipeline.StartAsync();
                
                if (startSuccess)
                {
                    StartButton.IsEnabled = false;
                    SelectFileButton.IsEnabled = false;
                    StopButton.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Lỗi Khởi Tạo Preview]: {ex.Message}");
                CleanupPipeline();
            }
        }

        private async void StopButton_Click(object sender, RoutedEventArgs e)
        {
            await CleanupPipelineAsync();
            
            StartButton.IsEnabled = true;
            SelectFileButton.IsEnabled = true;
            StopButton.IsEnabled = false;
        }

        private void Window_Closed(object sender, WindowEventArgs args)
        {
            CleanupPipeline();
            // Giải phóng toàn bộ tài nguyên của Engine Core khi đóng app
            Engine.Shutdown();
        }

        // ============================================================
        // LÝ DO CẦN DỌN DẸP BỘ NHỚ (CLEANUP):
        // Các đối tượng FileSource, Mixer, Pipeline nắm giữ các con trỏ quản lý tài nguyên Native C++.
        // Việc Dispose() giúp thu hồi ngay lập tức bộ nhớ GPU/RAM phía C++, 
        // ngăn chặn lỗi rò rỉ bộ nhớ (Memory Leak) khi mở/đóng preview nhiều lần.
        // ============================================================
        private async Task CleanupPipelineAsync()
        {
            if (_pipeline != null)
            {
                await _pipeline.StopAsync();
                _pipeline.Dispose();
                _pipeline = null;
            }
            if (_source != null)
            {
                _source.Dispose();
                _source = null;
            }
            if (_mixer != null)
            {
                _mixer.Dispose();
                _mixer = null;
            }
        }

        private void CleanupPipeline()
        {
            if (_pipeline != null)
            {
                _pipeline.Stop();
                _pipeline.Dispose();
                _pipeline = null;
            }
            if (_source != null)
            {
                _source.Dispose();
                _source = null;
            }
            if (_mixer != null)
            {
                _mixer.Dispose();
                _mixer = null;
            }
        }
    }
}
```

---

## 7. Bước 6: Cấu Hình Nền Tảng Biên Dịch (Platform Target)

### Cách thực hiện:
1. Nhìn lên thanh công cụ của Visual Studio (cạnh nút Run/Debug).
2. Thay đổi cấu hình mục **Solution Platforms** từ **Any CPU** sang **x64** (Bắt buộc).
3. Tiến hành build và chạy dự án (Nhấn **F5**).

### **Lý do thực hiện:**
* Lớp Wrapper C# được dịch sang mã trung gian MSIL chạy được trên mọi CPU (Any CPU). Tuy nhiên, file DLL Native C++ (`OpenMedia.Core.dll`) được biên dịch cụ thể cho tập lệnh vi xử lý **64-bit (x64)**.
* Nếu bạn chạy ứng dụng với cấu hình **Any CPU**, hệ điều hành có thể kích hoạt tiến trình chạy ở chế độ 32-bit (x86) hoặc ARM64 tùy thuộc cấu hình mặc định của hệ thống. Lúc này, tiến trình 32-bit sẽ không thể nạp (load) một thư viện DLL 64-bit và sẽ ném lỗi **`BadImageFormatException`** ngay lập tức. Ép cứng cấu hình biên dịch sang `x64` đảm bảo tính đồng nhất tuyệt đối giữa C# Host và C++ Core.

---

## 8. Tóm Tắt Quy Trình Sự Cố (Troubleshooting Matrix)

| Lỗi gặp phải | Nguyên nhân bản chất | Giải pháp xử lý |
| :--- | :--- | :--- |
| **`DllNotFoundException`** | .NET không tìm thấy `OpenMedia.Core.dll` hoặc các DLL FFmpeg phụ thuộc trong thư mục thực thi ảo MSIX. | Thêm khai báo `<Content>` và đường dẫn DLL của bạn vào file `.csproj` để đóng gói chúng vào AppX. |
| **`SEHException`** | C++ ném ngoại lệ phân quyền khi cố tạo thư mục `./logs` trong phân vùng bảo mật của WinUI Packaged App container. | Đã được xử lý tự động ở mức Wrapper [Engine.cs](file:///c:/Users/ASUS%20NUC/Desktop/Code/OME/wrappers/OpenMedia.Core.NET/Engine.cs) (tự động chuyển hướng log về `%LocalAppData%\OpenMedia\logs`). |
| **`EntryPointNotFoundException`** | C# P/Invoke gọi hàm callback trạng thái nhưng DLL C++ Core chưa triển khai/export hàm đó. | Đã được sửa triệt để: bổ sung `ome_pipeline_set_state_callback` và `ome_pipeline_set_error_callback` vào [openmedia_c_api.cpp](file:///c:/Users/ASUS%20NUC/Desktop/Code/OME/src/core/openmedia_c_api.cpp). Rebuild DLL để áp dụng. |
| **`CS0103: SelectedFilePathText`** | File `MainWindow.xaml` thiếu thẻ `<TextBlock x:Name="SelectedFilePathText">` hoặc chưa Rebuild XAML Code Generator. | Thêm thẻ `<TextBlock x:Name="SelectedFilePathText" .../>` vào `<StackPanel>` của `MainWindow.xaml` và Rebuild Solution. |
| **Không hiển thị Video (Khung Preview bị đen)** | Chưa liên kết con trỏ DirectX SwapChain từ Native Core lên `VideoPreviewPanel` của WinUI 3. | Bắt buộc gọi `WinUIInterop.SetSwapChain(VideoPreviewPanel, swapChainPtr)` để truyền con trỏ DirectX DXGI SwapChain sang UI. |
| **`BadImageFormatException`** | Xung đột kiến trúc phần cứng (Chạy C# dưới dạng Any CPU/x86 trong khi DLL native là x64). | Chuyển cấu hình Solution Platform trong Visual Studio thành **x64**. |

---
*Chúc bạn tích hợp thành công! Hướng dẫn này được tối ưu hóa cho người tiếp nhận dự án (Consumer) để hiểu sâu bản chất vấn đề và tự gỡ lỗi nhanh nhất.*