# Bài 02: Hướng dẫn viết chương trình Play Video từ File với OME SDK

Chào mừng bạn đến với hướng dẫn phát triển ứng dụng play video từ tệp tin sử dụng **OpenMediaSDK (OME SDK)**. Tài liệu này được thiết kế dành riêng cho lập trình viên mới tiếp cận dự án, giúp bạn nắm vững kiến trúc xử lý video của SDK và tự xây dựng được chương trình phát video bằng cả **C++ (Native)** lẫn **C#/.NET (Client-Server)**.

---

## 1. Tổng Quan Kiến Trúc Xử Lý Video (Architecture Overview)

OME SDK cung cấp hai mô hình lập trình chính để phát video tùy thuộc vào ngôn ngữ và yêu cầu ứng dụng của bạn:

```mermaid
graph TD
    subgraph In-Process (C++ Native)
        FileSource[FileSource] -->|Encoded Frames| Decoder[H264 Decoder]
        Decoder -->|Raw GPU Frames| Renderer[D3D11 Renderer]
    end

    subgraph Out-of-Process (Client-Server)
        Server[OpenMediaServer.exe] -->|DXGI Shared Texture / NT Handles| ClientUI[WPF / WinUI 3 Client]
        ClientUI -->|IPC Control Commands| Server
    end
```

### 1.1. Mô Hình Native C++ (In-Process Pipeline)
* **Cách hoạt động:** Mọi module xử lý media chạy chung trong một tiến trình (Process) của ứng dụng.
* **Luồng đi của dữ liệu:** 
  1. `FileSource` đọc dữ liệu nhị phân từ tệp tin (.mp4, .mkv...).
  2. Dữ liệu mã hóa được đẩy sang `FFmpegH264Decoder` để giải mã thành các khung hình thô (Raw Frame).
  3. Ứng dụng chủ động kéo khung hình từ Decoder bằng vòng lặp và truyền cho `D3D11Renderer` để hiển thị trực tiếp lên GPU.

### 1.2. Mô Hình Client-Server (Out-of-Process Pipeline)
* **Tại sao cần dùng?** do các giới hạn phân quyền ứng dụng Windows hiện đại (như WinUI 3 Packaged App) và để tối ưu hóa hiệu năng, OME SDK khuyến nghị mô hình tách rời:
  * **Server (`OpenMediaServer.exe`):** Chạy ngầm chịu trách nhiệm giải mã video và vẽ lên một texture đồ họa GPU dùng chung (**DXGI Shared Texture**).
  * **Client (App của bạn):** Giao tiếp qua Named Pipe (`IPCClient`) gửi lệnh điều khiển (Play/Stop), nhận về các khóa định danh GPU (**NT Handles**), và render trực tiếp lên khung hình UI với cơ chế **Zero-Copy Rendering** (không sao chép dữ liệu trên RAM, giảm tải tối đa cho CPU).

---

## 2. Xây Dựng Trình Phát Video Bằng C++ (Native In-Process)

Nếu bạn đang viết một ứng dụng C++ hoặc các module xử lý trực tiếp trên Core, dưới đây là cách xây dựng luồng phát video.

### 2.1. Code Mẫu Chi Tiết (`main.cpp`)

Dưới đây là mã nguồn C++ Native hoàn chỉnh cho một trình phát video chuẩn xác, xử lý tạo cửa sổ Win32, quản lý luồng tin nhắn hệ thống (`WM_SIZE` để co giãn màn hình), đồng bộ tốc độ khung hình (FPS) và tự động đóng khi phát xong:

```cpp
#include <iostream>
#include <memory>
#include <thread>
#include <chrono>
#include <windows.h>

#include <openmedia/io/FileSource.h>
#include <openmedia/rendering/D3D11Renderer.h>

using namespace openmedia::core;
using namespace openmedia::io;
using namespace openmedia::rendering;

// Con trỏ toàn cục để xử lý co giãn (Resize) cửa sổ trong WndProc
std::shared_ptr<D3D11Renderer> g_renderer;

// Win32 Window Procedure để xử lý các sự kiện cửa sổ
LRESULT CALLBACK WndProc(HWND hwnd, UINT uMsg, WPARAM wParam, LPARAM lParam) {
    switch (uMsg) {
        case WM_SIZE: {
            if (g_renderer) {
                int width = LOWORD(lParam);
                int height = HIWORD(lParam);
                g_renderer->Resize(width, height);
            }
            return 0;
        }
        case WM_DESTROY:
            PostQuitMessage(0);
            return 0;
        default:
            return DefWindowProcA(hwnd, uMsg, wParam, lParam);
    }
}

// Hàm khởi tạo cửa sổ Win32 Desktop hiển thị video
HWND CreatePlayerWindow(int width, int height) {
    const char* CLASS_NAME = "OMEPlayWindowClass";
    
    WNDCLASSA wc = {};
    wc.lpfnWndProc = WndProc;
    wc.hInstance = GetModuleHandle(nullptr);
    wc.lpszClassName = CLASS_NAME;
    wc.hbrBackground = (HBRUSH)GetStockObject(BLACK_BRUSH);
    wc.hCursor = LoadCursor(nullptr, IDC_ARROW);
    
    RegisterClassA(&wc);
    
    HWND hwnd = CreateWindowExA(
        0, CLASS_NAME, "OpenMedia SDK - Native C++ Play Video",
        WS_OVERLAPPEDWINDOW, CW_USEDEFAULT, CW_USEDEFAULT, width, height,
        nullptr, nullptr, GetModuleHandle(nullptr), nullptr
    );
    
    if (hwnd) {
        ShowWindow(hwnd, SW_SHOW);
        UpdateWindow(hwnd);
    }
    
    return hwnd;
}

int main(int argc, char* argv[]) {
    if (argc < 2) {
        std::cerr << "Usage: OME_play <video_file_path>" << std::endl;
        return 1;
    }

    std::string filePath = argv[1];
    std::cout << "OME_play: Playing file: " << filePath << std::endl;

    // Khởi tạo cửa sổ Win32 kích thước mặc định 1280x720
    HWND hwnd = CreatePlayerWindow(1280, 720);
    if (!hwnd) {
        std::cerr << "Failed to create Win32 window" << std::endl;
        return 1;
    }

    // 1. Tạo các thành phần xử lý (Pipeline Components)
    auto fileSource = std::make_shared<FileSource>();
    
    // Mở file video và kiểm tra lỗi ngay lập tức
    auto openRes = fileSource->Open(filePath);
    if (!openRes.has_value()) {
        std::cerr << "Failed to open video file: " << filePath << std::endl;
        std::cerr << "Error: " << openRes.error().message << std::endl;
        if (hwnd) DestroyWindow(hwnd);
        return 1;
    }

    auto renderer = std::make_shared<D3D11Renderer>();
    g_renderer = renderer; // Lưu trữ con trỏ toàn cục phục vụ Resize

    // 2. Khởi tạo các thành phần
    if (!fileSource->Initialize()) {
        std::cerr << "Failed to initialize FileSource" << std::endl;
        if (hwnd) DestroyWindow(hwnd);
        return 1;
    }

    // Khởi tạo D3D11 Renderer và truyền vào HWND của cửa sổ Win32
    if (!renderer->Initialize((void*)hwnd).has_value()) {
        std::cerr << "Failed to initialize Renderer" << std::endl;
        if (hwnd) DestroyWindow(hwnd);
        return 1;
    }

    // 3. Khởi chạy FileSource
    fileSource->Start();

    // Xác định FPS và thời lượng của mỗi khung hình để phát đúng tốc độ (Pacing)
    double frameDuration = 1.0 / 30.0; // Mặc định 30 FPS
    auto streams = fileSource->GetStreams();
    for (const auto& stream : streams) {
        if (stream.type == MediaType::Video && stream.frameRate > 0.0) {
            frameDuration = 1.0 / stream.frameRate;
            std::cout << "Detected video FPS: " << stream.frameRate 
                      << " (" << stream.width << "x" << stream.height << ")" << std::endl;
            break;
        }
    }

    std::cout << "Pipeline running. Close the window to exit..." << std::endl;

    auto startTime = std::chrono::steady_clock::now();
    double videoTime = 0.0;
    MSG msg = {};
    bool playing = true;

    // 4. Vòng lặp tin nhắn Win32 kết hợp kết xuất đồ họa (Message & Render Loop)
    while (playing && fileSource->GetState() == PipelineState::Running) {
        // Xử lý và phân phối toàn bộ tin nhắn Windows (tránh đơ cửa sổ)
        while (PeekMessage(&msg, nullptr, 0, 0, PM_REMOVE)) {
            TranslateMessage(&msg);
            DispatchMessage(&msg);
            if (msg.message == WM_QUIT) {
                playing = false;
                break;
            }
        }
        if (!playing) break;

        // Đồng bộ hóa khung hình theo thời gian thực (Pacing)
        auto now = std::chrono::steady_clock::now();
        double elapsed = std::chrono::duration<double>(now - startTime).count();

        while (videoTime <= elapsed && playing) {
            // Lấy khung hình đã giải mã trực tiếp từ FileSource (đã chuyển đổi sang BGRA)
            auto frameResult = fileSource->PullVideoFrame();
            if (frameResult.has_value() && frameResult.value()) {
                renderer->Render(frameResult.value());
                videoTime += frameDuration;
            } else {
                auto err = frameResult.error();
                if (err.code == openmedia::core::ErrorCode::EndOfStream) {
                    std::cout << "End of video stream reached." << std::endl;
                } else {
                    std::cerr << "Error pulling video frame: " << err.message << std::endl;
                }
                playing = false;
                break;
            }
        }

        std::this_thread::sleep_for(std::chrono::milliseconds(5));
    }

    // 5. Giải phóng tài nguyên
    std::cout << "Stopping pipeline..." << std::endl;
    fileSource->Stop();
    renderer->Shutdown();
    fileSource->Close();

    if (hwnd) {
        DestroyWindow(hwnd);
    }

    std::cout << "Pipeline stopped successfully." << std::endl;
    return 0;
}
```

### 2.2. Các Điểm Cần Lưu Ý Về Giao Diện Và Tỷ Lệ Khung Hình

Để trình phát video hoạt động chính xác và không bị méo/vỡ hình khi co giãn cửa sổ, các cơ chế sau đã được tích hợp:

1. **Khởi tạo Window Handle (`HWND`):** Trình dựng hình DirectX 11 (`D3D11Renderer`) bắt buộc phải nhận một handle cửa sổ hợp lệ để liên kết SwapChain. Việc truyền `nullptr` sẽ gây ra lỗi `InvalidArgument`.
2. **Kéo khung hình trực tiếp (Direct Frame Pulling):** Thay vì sử dụng bộ giải mã rời rạc `FFmpegH264Decoder`, ta kéo trực tiếp qua hàm `PullVideoFrame()` của `FileSource`. FileSource của OME SDK đã tích hợp sẵn luồng giải mã FFmpeg và bộ chuyển đổi thang màu (`swscale`) thành ảnh `BGRA` sẵn sàng hiển thị.
3. **Đồng bộ co giãn cửa sổ (`Resize`):** Khi cửa sổ thay đổi kích thước, hệ thống Windows gửi tin nhắn `WM_SIZE`. Hàm `WndProc` sẽ bắt sự kiện này và gọi `renderer->Resize(width, height)` để cập nhật kích thước vùng đệm đồ họa (SwapChain Backbuffer), ngăn chặn việc hình ảnh bị kéo dãn nhòe.
4. **Bảo toàn tỷ lệ khung hình (Aspect Ratio & Viewport):** Trong lõi D3D11Renderer, Viewport dựng hình được tự động tính toán lại dựa trên kích thước vùng Client của cửa sổ (`GetClientRect`) để tạo dải đen trên/dưới hoặc trái/phải (Letterboxing/Pillarboxing) giúp giữ nguyên tỷ lệ gốc của video:
   ```cpp
   float videoAspect = (float)width / (float)height;
   float clientAspect = clientWidth / clientHeight;
   if (videoAspect > clientAspect) {
       // Video rộng hơn tỉ lệ cửa sổ -> Letterbox
       vpHeight = clientWidth / videoAspect;
       vpY = (clientHeight - vpHeight) / 2.0f;
   } else {
       // Video cao hơn tỉ lệ cửa sổ -> Pillarbox
       vpWidth = clientHeight * videoAspect;
       vpX = (clientWidth - vpWidth) / 2.0f;
   }
   ```
5. **Đồng bộ hóa tốc độ (Pacing):** Video được duy trì phát đúng FPS (ví dụ 60 FPS) dựa trên phép so sánh thời gian hệ thống và thời gian ảo của luồng video (`videoTime`), giúp tránh việc CPU đọc và dựng hình quá nhanh làm trôi video.

---

## 3. Xây Dựng Trình Phát Video Bằng C#/.NET (Client-Server)

Đối với các nhà phát triển ứng dụng giao diện Windows (.NET WPF / WinUI 3), mã nguồn mẫu hoàn chỉnh đã được xây dựng sẵn tại thư mục:
📂 **[samples/dotnet/OME_play](file:///c:/Users/ASUS%20NUC/Desktop/Code/OME/samples/dotnet/OME_play)**

Ứng dụng hoạt động theo mô hình Client-Server tách rời (Out-of-Process), sử dụng Named Pipe IPC để điều khiển và cơ chế **DXGI KeyedMutex Zero-Copy Shared Textures** để kết xuất đồ họa hiệu năng cao.

### 3.1. Ý Tưởng & Quy Trình Hoạt Động (Architecture Flow)

1. **Khởi chạy Server:** Kiểm tra xem `OpenMediaServer.exe` đã chạy hay chưa, nếu chưa thì Client tự động spawn tiến trình Server ngầm.
2. **Khởi tạo IPC & Pipeline:** Kết nối tới Named Pipe `OpenMediaSDK` (`IPCClient`), gửi lệnh khởi tạo Pipeline (`SDKPipeline.CreateAsync`) và mở luồng tệp tin video (`SDKSource.CreateAsync`).
3. **Phát video trên Server:** Khi gọi `pipeline.StartAsync()`, Server bắt đầu giải mã các frame và vẽ tuần tự vào vùng nhớ GPU dùng chung (Shared Textures Pool).
4. **Nhận NT Handles & Zero-Copy Rendering:** Client gọi `_ipcClient.RequestSharedTextureAsync()` để lấy 2 khóa định danh GPU (**NT Handles**). Lớp điều khiển Direct3D 11 (`D3D11VideoPlayer`) mở tài nguyên thông qua `OpenSharedResource1` và đồng bộ khóa `IDXGIKeyedMutex` (Key = 1 để đọc frame mới nhất, Key = 0 để trả quyền cho Server ghi tiếp).
5. **Hỗ trợ 2 chế độ hiển thị (Scale Modes):**
   - **`AspectRatioFit` (Mặc định):** Tự động tính toán Viewport để giữ nguyên tỷ lệ gốc của video, căn giữa và thêm dải đen Letterbox/Pillarbox.
   - **`Stretch`:** Kéo giãn hình ảnh lấp đầy toàn bộ diện tích khung hình.
   - Hỗ trợ phím tắt `[S]` để chuyển đổi tức thì giữa 2 chế độ (tương tự bản C++).

---

### 3.2. Cấu Trúc Dự Án C#/.NET

```
samples/dotnet/OME_play/
├── OME_play.csproj          # File cấu hình .NET 10 WPF x64
├── App.xaml / App.xaml.cs   # Khởi tạo WPF Application
├── D3D11VideoPlayer.cs      # Direct3D 11 HwndHost kết xuất đồ họa Zero-Copy
├── MainWindow.xaml          # Giao diện điều khiển hiện đại (Dark Theme)
└── MainWindow.xaml.cs       # Quản lý IPC Client, Server lifecycle & Playback
```

---

### 3.3. Mã Nguồn Lớp Kết Xuất Đồ Họa (`D3D11VideoPlayer.cs`)

Lớp `D3D11VideoPlayer` kế thừa `HwndHost` của WPF, sử dụng thư viện `Vortice.Direct3D11` và `Vortice.DXGI` để mở NT Handles đồ họa từ Server:

```csharp
using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace OME_play
{
    public enum ScaleMode { AspectRatioFit, Stretch }

    public class D3D11VideoPlayer : HwndHost
    {
        private IntPtr _hwndHost = IntPtr.Zero;
        private ID3D11Device1? _d3dDevice;
        private ID3D11DeviceContext? _d3dContext;
        private IDXGISwapChain1? _swapChain;
        private ID3D11RenderTargetView? _renderTargetView;

        // Double Buffering NT Handles
        private ID3D11Texture2D[]? _sharedTextures;
        private IDXGIKeyedMutex[]? _keyedMutexes;
        private int _currentBufferIndex = 0;
        private uint _videoWidth = 1920;
        private uint _videoHeight = 1080;
        private ScaleMode _scaleMode = ScaleMode.AspectRatioFit;

        public void SetSharedHandles(ulong handle0, ulong handle1, uint width = 1920, uint height = 1080)
        {
            _videoWidth = width;
            _videoHeight = height;
            _sharedTextures = new ID3D11Texture2D[2];
            _keyedMutexes = new IDXGIKeyedMutex[2];

            // Mở Shared Texture được chia sẻ từ Server GPU (hỗ trợ cả KMT handle và NT handle)
            _sharedTextures[0] = OpenSharedTextureSafe(handle0);
            _keyedMutexes[0] = _sharedTextures[0].QueryInterface<IDXGIKeyedMutex>();

            _sharedTextures[1] = OpenSharedTextureSafe(handle1);
            _keyedMutexes[1] = _sharedTextures[1].QueryInterface<IDXGIKeyedMutex>();
        }

        private ID3D11Texture2D OpenSharedTextureSafe(ulong handle)
        {
            try { return _d3dDevice.OpenSharedResource<ID3D11Texture2D>((IntPtr)handle); }
            catch { return _d3dDevice.OpenSharedResource1<ID3D11Texture2D>((IntPtr)handle); }
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            if (_d3dContext == null || _swapChain == null || _keyedMutexes == null) return;

            var mutex = _keyedMutexes[_currentBufferIndex];
            try
            {
                // Acquire lock Key = 1 (Server đã vẽ xong frame và bàn giao cho Client)
                mutex.AcquireSync(1, 0);

                var sharedTex = _sharedTextures[_currentBufferIndex];
                using var backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
                
                // Xóa nền đen để phục vụ Aspect Ratio Fit (Letterboxing / Pillarboxing)
                _d3dContext.ClearRenderTargetView(_renderTargetView, new Color4(0f, 0f, 0f, 1f));

                // Tính toán Viewport theo tỉ lệ khung hình
                Viewport vp = CalculateViewport(_scaleMode);
                _d3dContext.RSSetViewport(vp);
                _d3dContext.CopyResource(backBuffer, sharedTex);

                // Trả khóa Key = 0 để Server tiếp tục vẽ frame kế tiếp
                mutex.ReleaseSync(0);

                _swapChain.Present(0, PresentFlags.None);
                _currentBufferIndex = (_currentBufferIndex + 1) % 2;
            }
            catch { /* Bỏ qua nếu frame chưa sẵn sàng */ }
        }
    }
}
```

---

### 3.4. Hướng Dẫn Biên Dịch Và Chạy Ứng Dụng C#/.NET

Bạn có thể chạy ứng dụng trực tiếp bằng CLI .NET SDK:

```powershell
# 1. Biên dịch toàn bộ dự án C# OME_play
dotnet build "c:\Users\ASUS NUC\Desktop\Code\OME\samples\dotnet\OME_play\OME_play.csproj" -c Debug

# 2. Chạy ứng dụng trình phát video
dotnet run --project "c:\Users\ASUS NUC\Desktop\Code\OME\samples\dotnet\OME_play\OME_play.csproj"
```

Khi giao diện mở ra:
1. Nhấn nút **📁 Browse...** để chọn tệp tin video của bạn (hoặc giữ đường dẫn mặc định).
2. Nhấn **▶ Play Video**: Ứng dụng sẽ tự động kích hoạt `OpenMediaServer.exe`, thiết lập kênh IPC, nhận Shared Textures và trình chiếu video.
3. Nhấn phím **`S`** trên bàn phím hoặc bấm nút **📐 Mode** trên giao diện để chuyển đổi linh hoạt giữa chế độ **AspectRatioFit** và **Stretch**.
4. Nhấn nút **⏹ Stop** hoặc phím **`Space`** để dừng phát.

---

## 4. Danh Sách Các Class & API Quan Trọng Trong OME SDK

| Lớp (Class) | Ngôn ngữ | Mô tả chức năng chính |
| :--- | :--- | :--- |
| `openmedia::io::FileSource` | C++ | Lớp nguồn đọc file video native. Có các phương thức chính: `Open(path)`, `Initialize()`, `Start()`, `Stop()`. |
| `openmedia::codecs::FFmpegH264Decoder` | C++ | Lớp giải mã video chuẩn H.264 dựa trên thư viện FFmpeg. Cung cấp API `PullFrame()`. |
| `openmedia::rendering::D3D11Renderer` | C++ | Lớp hiển thị video lên cửa sổ thông qua DirectX 11. Cung cấp API `Render(frame)`. |
| `OpenMedia.SDK.IPCClient` | C# | Lớp client thực hiện giao tiếp IPC Named Pipe với Server. Cung cấp API `ConnectAsync()`, `SendAndReceiveAsync()`, `RequestSharedTextureAsync()`. |
| `OpenMedia.SDK.Pipeline` | C# | Lớp Wrapper đại diện cho luồng xử lý (Graph). Có các lệnh điều khiển như `Start()`, `Stop()`, `WithNode(nodeHandle)`. |

---

## 5. Các Lỗi Thường Gặp Khi Mới Bắt Đầu (Troubleshooting Matrix)

Khi xây dựng chương trình phát video với OME SDK, bạn rất dễ gặp các lỗi sau. Hãy tham khảo bảng khắc phục nhanh dưới đây:

| Lỗi gặp phải | Nguyên nhân thực tế | Cách giải quyết triệt để |
| :--- | :--- | :--- |
| **`DllNotFoundException`** ở C# | .NET không tìm thấy `OpenMedia.Core.dll` hoặc các file DLL của FFmpeg, spdlog trong thư mục thực thi. | Copy toàn bộ các file DLL trong thư mục `build-demo/bin/Debug` vào thư mục chạy ứng dụng của bạn, hoặc cấu hình thẻ `<Content>` trong file `.csproj` để MSBuild tự động copy khi build. |
| **`SEHException`** tại `Engine.Initialize` | File log mặc định `./logs/openmedia.log` cố ghi vào thư mục hệ thống bị cấm quyền ghi (thường gặp khi chạy WinUI 3 Packaged App). | Đã được xử lý tự động trong wrapper. Nếu tự code, hãy set biến môi trường `OME_LOG_DIR` về đường dẫn an toàn (ví dụ: `%LocalAppData%\OpenMedia\logs`) trước khi khởi tạo Engine. |
| **`EntryPointNotFoundException`** | Sự bất đồng bộ phiên bản giữa DLL C++ Native và Wrapper C# (gọi hàm callback chưa được export trong C++). | Cập nhật và build lại DLL Native C++ mới nhất có chứa định nghĩa `ome_pipeline_set_state_callback` và `ome_pipeline_set_error_callback`. |
| **`BadImageFormatException`** | Xung đột kiến trúc CPU (Ứng dụng C# chạy ở chế độ Any CPU/x86 trong khi DLL native C++ là x64). | Mở cấu hình **Solution Platforms** trong Visual Studio và chuyển bắt buộc sang **x64**. |
| **Video không hiển thị (Màn hình đen)** | Chưa thực hiện gán/vẽ NT Handles đồ họa nhận được từ Server lên swapchain của giao diện Client. | Bắt buộc phải gọi hàm gán NT Handles (như `SetSharedHandles`) và kích hoạt vòng lặp CopyResource bằng DirectX trong ứng dụng Client. |

---
*Chúc bạn xây dựng chương trình play video đầu tiên thành công! Nếu gặp bất kỳ khó khăn nào, hãy kiểm tra file log tại `%LocalAppData%\OpenMedia\logs\openmedia.log` để xem thông tin chi tiết.*
