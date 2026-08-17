# Hướng Dẫn Sửa Lỗi Khung Hình Màu Đen Trên WinUI 3 Preview

Khi làm việc với **OpenMedia SDK**, nếu bạn chạy ứng dụng WinUI 3 Preview theo mô hình In-Process (chạy pipeline trực tiếp trong app), bạn sẽ thấy **khung hình màu đen và video không chạy**. Tài liệu này giải thích chi tiết nguyên nhân và cung cấp giải pháp khắc phục triệt để.

---

## 1. Nguyên Nhân Gây Lỗi

Lỗi khung hình màu đen xảy ra do hai nguyên nhân chính:

1. **C API nội bộ đang là Mock/Stub:**
   Thư viện lõi native `OpenMedia.Core.dll` được biên dịch dưới dạng Shared Library (DLL) ở đáy của cây phụ thuộc. Do quy định chống phụ thuộc vòng (circular dependency) trong C++, `OpenMedia.Core` không thể liên kết trực tiếp với các module xử lý logic tĩnh như `OpenMedia.IO` (đọc file), `OpenMedia.Mixer` (trộn hình) hay `OpenMedia.Codecs`. Do đó, các export C API trong `openmedia_c_api.cpp` (như `ome_source_create_file`, `ome_pipeline_add_node`) hiện tại là các **mock stub** và không thực hiện xử lý media thực tế.

2. **Cách tiếp cận In-Process chưa hoàn thiện:**
   Các dòng code liên kết DirectX SwapChain trong phần code-behind của hướng dẫn gốc đang bị comment lại và sử dụng phương thức giả định `_pipeline.GetPreviewSwapChain()`, phương thức này không có thực tế trong Wrapper class C#.

---

## 2. Giải Pháp: Sử Dụng Kiến Trúc Out-of-Process (Exhand Architecture)

Kiến trúc chuẩn của **OpenMedia SDK** hoạt động theo mô hình **Client-Server (Exhand Architecture)**:
* **Server (`OpenMediaServer.exe`)**: Thực hiện các tác vụ nặng như giải mã video (FFmpeg), xử lý hình ảnh trên GPU và ghi dữ liệu ra một GPU texture dùng chung (**DXGI Shared Texture**).
* **Client (WinUI 3 App)**: Kết nối với Server thông qua Named Pipes để gửi lệnh điều khiển (Play/Stop/Seek) và nhận về các **NT Handles** của Shared Texture, sau đó render trực tiếp lên `SwapChainPanel` của WinUI 3 thông qua DirectX 11 mà không tốn tài nguyên sao chép RAM (Zero-Copy).

---

## 3. Các Bước Cấu Hình Để Play Video Trên WinUI 3 Preview

Để chuyển đổi ứng dụng WinUI 3 của bạn sang kiến trúc Client-Server và hiển thị video thành công, hãy thực hiện theo các bước sau:

### Bước 1: Cài đặt NuGet Packages cho DirectX
Để thao tác với DirectX 11 trong C#, bạn cần cài đặt thư viện wrapper DirectX. Khuyên dùng **Vortice.Direct3D11** và **Vortice.DXGI** (đây là các thư viện hiện đại hỗ trợ tốt .NET 8/9/10):

Mở **Package Manager Console** trong Visual Studio và chạy lệnh:
```bash
Install-Package Vortice.Direct3D11
Install-Package Vortice.DXGI
```

---

### Bước 2: Tạo Lớp Helper Render Cho SwapChainPanel
Tạo một file class mới tên là `D3D11SwapChainPanelRenderer.cs` trong dự án WinUI 3 của bạn để quản lý vòng đời D3D11 Device, Composition SwapChain, và thực hiện copy frame từ Shared Texture của Server:

```csharp
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace OpenMedia.PreviewApp
{
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("63aad0b8-7c24-40ff-85a8-640d944cc325")]
    public interface ISwapChainPanelNative
    {
        [PreserveSig]
        int SetSwapChain(IntPtr swapChain);
    }

    public class D3D11SwapChainPanelRenderer : IDisposable
    {
        private ID3D11Device1? _d3dDevice;
        private ID3D11DeviceContext? _d3dContext;
        private IDXGISwapChain1? _swapChain;
        
        // Double buffering shared textures từ server
        private ID3D11Texture2D[]? _sharedTextures;
        private IDXGIKeyedMutex[]? _keyedMutexes;
        private int _currentBufferIndex = 0;
        private bool _isInitialized = false;

        public void Initialize(object swapChainPanel, int width, int height)
        {
            if (_isInitialized) return;

            try
            {
                // 1. Khởi tạo D3D11 Device & Context
                D3D11.D3D11CreateDevice(
                    null,
                    Vortice.Direct3D.DriverType.Hardware,
                    DeviceCreationFlags.BgraSupport,
                    new[] { Vortice.Direct3D.FeatureLevel.Level_11_1, Vortice.Direct3D.FeatureLevel.Level_11_0 },
                    out ID3D11Device device,
                    out ID3D11DeviceContext context).CheckError();

                _d3dDevice = device.QueryInterface<ID3D11Device1>();
                _d3dContext = context;

                // 2. Tạo DXGI SwapChain dành riêng cho Composition (WinUI 3 SwapChainPanel)
                using var dxgiDevice = _d3dDevice.QueryInterface<IDXGIDevice2>();
                using var dxgiAdapter = dxgiDevice.GetAdapter();
                using var dxgiFactory = dxgiAdapter.GetParent<IDXGIFactory2>();

                var swapChainDesc = new SwapChainDescription1
                {
                    Width = (uint)width,
                    Height = (uint)height,
                    Format = Format.B8G8R8A8_UNorm,
                    BufferCount = 2,
                    BufferUsage = Usage.RenderTargetOutput,
                    SampleDescription = new SampleDescription(1, 0),
                    Scaling = Scaling.Stretch,
                    SwapEffect = SwapEffect.FlipDiscard,
                    AlphaMode = AlphaMode.Ignore,
                };

                _swapChain = dxgiFactory.CreateSwapChainForComposition(_d3dDevice, swapChainDesc);

                // 3. Liên kết SwapChain với WinUI SwapChainPanel trực tiếp qua COM Native Interface
                SetPanelSwapChain(swapChainPanel, _swapChain.NativePointer);

                _isInitialized = true;
                Debug.WriteLine($"[Renderer]: Khởi tạo SwapChain thành công ({width}x{height})");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Renderer Error Initialize]: {ex.Message}");
                throw;
            }
        }

        private static void SetPanelSwapChain(object swapChainPanel, IntPtr swapChainPtr)
        {
            IntPtr pUnknown = IntPtr.Zero;
            try
            {
                pUnknown = Marshal.GetIUnknownForObject(swapChainPanel);
                if (pUnknown != IntPtr.Zero)
                {
                    Guid iid = typeof(ISwapChainPanelNative).GUID;
                    if (Marshal.QueryInterface(pUnknown, ref iid, out IntPtr pNative) == 0 && pNative != IntPtr.Zero)
                    {
                        try
                        {
                            var panelNative = (ISwapChainPanelNative)Marshal.GetObjectForIUnknown(pNative);
                            int hr = panelNative.SetSwapChain(swapChainPtr);
                            if (hr < 0)
                            {
                                Marshal.ThrowExceptionForHR(hr);
                            }
                            return;
                        }
                        finally
                        {
                            Marshal.Release(pNative);
                        }
                    }
                }
            }
            finally
            {
                if (pUnknown != IntPtr.Zero)
                {
                    Marshal.Release(pUnknown);
                }
            }

            throw new InvalidOperationException("Không thể lấy interface ISwapChainPanelNative từ SwapChainPanel.");
        }

        public void SetSharedHandles(ulong handle0, ulong handle1)
        {
            if (!_isInitialized || _d3dDevice == null) return;

            try
            {
                CleanupSharedResources();

                _sharedTextures = new ID3D11Texture2D[2];
                _keyedMutexes = new IDXGIKeyedMutex[2];

                // Mở Shared Texture từ NT Handles được Server cung cấp
                _sharedTextures[0] = _d3dDevice.OpenSharedResource1<ID3D11Texture2D>((IntPtr)handle0);
                _keyedMutexes[0] = _sharedTextures[0].QueryInterface<IDXGIKeyedMutex>();

                _sharedTextures[1] = _d3dDevice.OpenSharedResource1<ID3D11Texture2D>((IntPtr)handle1);
                _keyedMutexes[1] = _sharedTextures[1].QueryInterface<IDXGIKeyedMutex>();

                // Đăng ký loop vẽ frame trên mỗi chu kỳ render của UI
                CompositionTarget.Rendering += OnRendering;
                Debug.WriteLine("[Renderer]: Đã liên kết 2 GPU Shared Textures thành công!");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Renderer Error SetSharedHandles]: {ex.Message}");
                throw;
            }
        }

        private void OnRendering(object? sender, object e)
        {
            if (!_isInitialized || _swapChain == null || _sharedTextures == null || _keyedMutexes == null || _d3dContext == null) return;

            for (int i = 0; i < 2; i++)
            {
                int bufferIdx = (_currentBufferIndex + i) % 2;
                var mutex = _keyedMutexes[bufferIdx];
                if (mutex == null) continue;

                try
                {
                    // Chờ tối đa 5ms để lấy khóa đọc frame từ GPU Server (thành công nếu không ném ngoại lệ)
                    mutex.AcquireSync(1, 5);

                    var sharedTex = _sharedTextures[bufferIdx];
                    using var backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
                    if (backBuffer != null && sharedTex != null)
                    {
                        _d3dContext.CopyResource(backBuffer, sharedTex);
                    }

                    mutex.ReleaseSync(0);
                    _swapChain.Present(0, PresentFlags.None);

                    _currentBufferIndex = (bufferIdx + 1) % 2;
                    break;
                }
                catch
                {
                    // Tiếp tục thử buffer kế tiếp nếu timeout
                }
            }
        }

        private void CleanupSharedResources()
        {
            CompositionTarget.Rendering -= OnRendering;

            if (_sharedTextures != null)
            {
                foreach (var tex in _sharedTextures) tex?.Dispose();
                _sharedTextures = null;
            }
            if (_keyedMutexes != null)
            {
                foreach (var mutex in _keyedMutexes) mutex?.Dispose();
                _keyedMutexes = null;
            }
        }

        public void Dispose()
        {
            CleanupSharedResources();
            _swapChain?.Dispose();
            _d3dContext?.Dispose();
            _d3dDevice?.Dispose();
            _isInitialized = false;
        }
    }
}
```

---

### Bước 3: Cấu Hình Code-Behind Của MainWindow.xaml.cs
Cập nhật file `MainWindow.xaml.cs` để khởi tạo kết nối IPC tới server, gửi lệnh phát video và nạp Texture Handles vào Renderer:

```csharp
using Microsoft.UI.Xaml;
using System;
using System.Diagnostics;
using System.IO;
using OpenMedia.SDK;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace OpenMedia.PreviewApp
{
    public sealed partial class MainWindow : Window
    {
        private IPCClient? _ipcClient = null;
        private D3D11SwapChainPanelRenderer _renderer;
        private string? _selectedFilePath = null;
        private Process? _serverProcess = null;

        public MainWindow()
        {
            this.InitializeComponent();
            _renderer = new D3D11SwapChainPanelRenderer();
            
            // Tự động khởi chạy OpenMediaServer nếu chưa chạy (Có thể chạy thủ công ngoài Console)
            StartServerProcess();
        }

        private void StartServerProcess()
        {
            var processes = Process.GetProcessesByName("OpenMediaServer");
            if (processes.Length == 0)
            {
                try
                {
                    // Trỏ tới thư mục chứa file build của bạn
                    string serverPath = @"c:\Users\ASUS NUC\Desktop\Code\OME\build-demo\bin\Debug\OpenMediaServer.exe";
                    if (File.Exists(serverPath))
                    {
                        _serverProcess = Process.Start(new ProcessStartInfo
                        {
                            FileName = serverPath,
                            WorkingDirectory = Path.GetDirectoryName(serverPath), // Bắt buộc để Server load được các DLL phụ thuộc (FFmpeg, spdlog...)
                            UseShellExecute = false,
                            CreateNoWindow = true
                        });
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Error launching server]: {ex.Message}");
                }
            }
        }

        private async void SelectFileButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.ViewMode = PickerViewMode.Thumbnail;
            picker.SuggestedStartLocation = PickerLocationId.VideosLibrary;
            picker.FileTypeFilter.Add(".mp4");
            picker.FileTypeFilter.Add(".mkv");
            picker.FileTypeFilter.Add(".avi");

            IntPtr hwnd = WindowNative.GetWindowHandle(this);
            InitializeWithWindow.Initialize(picker, hwnd);

            StorageFile file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                _selectedFilePath = file.Path;
                SelectedFilePathText.Text = file.Name;
                SelectedFilePathText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
                StartButton.IsEnabled = true;
            }
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedFilePath)) return;

            try
            {
                // 1. Kết nối tới IPC Server (mặc định channel: OpenMediaSDK)
                _ipcClient = new IPCClient();
                bool connected = await _ipcClient.ConnectAsync("OpenMediaSDK", 5000);
                if (!connected)
                {
                    Debug.WriteLine("[Error]: Không thể kết nối tới OpenMediaServer.");
                    return;
                }

                // 2. Yêu cầu Server chia sẻ GPU Texture Handles của luồng phát
                var payload = await _ipcClient.RequestSharedTextureAsync();
                if (payload != null)
                {
                    // Khởi tạo renderer cục bộ khớp độ phân giải video
                    _renderer.Initialize(VideoPreviewPanel, (int)payload.Value.Width, (int)payload.Value.Height);
                    
                    // Gán NT Handles của Shared Texture cho Renderer để vẽ lên SwapChainPanel
                    _renderer.SetSharedHandles(payload.Value.NtHandle0, payload.Value.NtHandle1);

                    StartButton.IsEnabled = false;
                    SelectFileButton.IsEnabled = false;
                    StopButton.IsEnabled = true;
                }
                else
                {
                    Debug.WriteLine("[Error]: Không lấy được Texture Payload từ Server.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Error starting preview]: {ex.Message}");
                CleanupConnection();
            }
        }

        private async void StopButton_Click(object sender, RoutedEventArgs e)
        {
            if (_ipcClient != null)
            {
                await _ipcClient.SendAndReceiveAsync(CommandType.StopPipeline);
            }
            
            CleanupConnection();

            StartButton.IsEnabled = true;
            SelectFileButton.IsEnabled = true;
            StopButton.IsEnabled = false;
        }

        private void CleanupConnection()
        {
            _renderer.Dispose();
            _ipcClient?.Dispose();
            _ipcClient = null;
        }

        private void Window_Closed(object sender, WindowEventArgs args)
        {
            CleanupConnection();
            
            // Tắt server process nếu app tự khởi động nó
            if (_serverProcess != null && !_serverProcess.HasExited)
            {
                try { _serverProcess.Kill(); } catch { }
                _serverProcess.Dispose();
            }
        }
    }
}
```

---

## 4. Chạy Ứng Dụng
1. Đảm bảo bạn đã build toàn bộ project C++ và có file `OpenMediaServer.exe` trong thư mục output (ví dụ: `build-demo/bin/Debug/`).
2. Build và Run ứng dụng WinUI 3 Preview của bạn ở cấu hình **x64**.
3. Chọn một file video bất kỳ (`.mp4`, `.mkv`) và nhấn **Bắt đầu Preview**. 
4. Video sẽ được giải mã trực tiếp phía Server và hiển thị mượt mà trên `SwapChainPanel` của ứng dụng WinUI 3 của bạn!
