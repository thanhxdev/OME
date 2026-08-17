# Kế Hoạch Xây Dựng Ứng Dụng Trình Phát Video C#/.NET (Client-Server) - `samples/dotnet/OME_play`

Tài liệu này mô tả chi tiết phương án thiết kế và triển khai ứng dụng trình phát video C#/.NET theo kiến trúc Client-Server (Mục 3 trong [Bài 02-HD_OME_play.md](file:///c:/Users/ASUS%20NUC/Desktop/Code/OME/HDThucHanh/Bài%2002-HD_OME_play.md)), đồng bộ tính năng với bản C++ Native ([samples/cpp/OME_play](file:///c:/Users/ASUS%20NUC/Desktop/Code/OME/samples/cpp/OME_play)).

---

## 1. Mục Tiêu & Yêu Cầu

1. **Kiến Trúc Client-Server (Out-of-Process):**
   - Tự động quản lý vòng đời `OpenMediaServer.exe` (kiểm tra tiến trình, khởi chạy từ `build-demo/bin/Debug/OpenMediaServer.exe` nếu chưa chạy, tắt khi đóng app).
   - Giao tiếp IPC thông qua Named Pipe `OpenMediaSDK` (`IPCClient` / `SDKEngine`).
   - Gửi lệnh `CreatePipeline`, `OpenSource` (file MP4/MKV...), `StartPipeline`, `StopPipeline`.

2. **Kết Xuất Đồ Họa Không Sao Chép (Zero-Copy DXGI Shared Texture):**
   - Gọi `RequestSharedTextureAsync()` nhận 2 `NtHandle0` và `NtHandle1` (Double Buffering).
   - Render bằng Direct3D 11 (thư viện `Vortice.Direct3D11` & `Vortice.DXGI`) qua điều khiển WPF `HwndHost` (`D3D11VideoPlayer`).
   - Hỗ trợ đồng bộ hóa bằng `IDXGIKeyedMutex` (Key 1: Server hoàn tất frame / Client đọc, Key 0: Client giải phóng / Server ghi).
   - Tích hợp 2 chế độ hiển thị tương tự bản C++: **AspectRatioFit** (tự động căn lề đen Letterbox/Pillarbox giữ nguyên tỉ lệ gốc của video) và **Stretch** (kéo giãn đầy khung hình).

3. **Giao Diện Người Dùng WPF Hiện Đại:**
   - Hộp chọn tệp tin video (`Browse...` OpenFileDialog) hoặc nhập đường dẫn trực tiếp.
   - Nút điều khiển: **Connect & Play**, **Stop**, **Toggle Scale Mode** (Fit / Stretch).
   - Bảng hiển thị thông số: Trạng thái Server (Running / Stopped), Độ phân giải Video (1920x1080), Chế độ Scale, FPS.
   - Khung nhật ký sự kiện (Real-time Log Output).

4. **Tổ Chức Mã Nguồn:**
   - Dự án mới: `samples/dotnet/OME_play/OME_play.csproj` (.NET 10 Windows Desktop WPF).
   - Cập nhật mục 3 trong [Bài 02-HD_OME_play.md](file:///c:/Users/ASUS%20NUC/Desktop/Code/OME/HDThucHanh/Bài%2002-HD_OME_play.md) với hướng dẫn biên dịch và chạy mẫu hoàn chỉnh.

---

## 2. Chi Tiết File Thay Đổi / Tạo Mới

### [Component] C# Video Player Sample (`samples/dotnet/OME_play`)

#### [NEW] [OME_play.csproj](file:///c:/Users/ASUS%20NUC/Desktop/Code/OME/samples/dotnet/OME_play/OME_play.csproj)
- Cấu hình dự án WPF target `net10.0-windows`, kiến trúc `x64`.
- Tham chiếu `wrappers/OpenMedia.Core.NET/OpenMedia.Core.NET.csproj`.
- NuGet packages: `Vortice.Direct3D11`, `Vortice.DXGI`, `Vortice.Mathematics`.

#### [NEW] [App.xaml](file:///c:/Users/ASUS%20NUC/Desktop/Code/OME/samples/dotnet/OME_play/App.xaml) & [App.xaml.cs](file:///c:/Users/ASUS%20NUC/Desktop/Code/OME/samples/dotnet/OME_play/App.xaml.cs)
- Cấu hình ứng dụng WPF cơ bản.

#### [NEW] [D3D11VideoPlayer.cs](file:///c:/Users/ASUS%20NUC/Desktop/Code/OME/samples/dotnet/OME_play/D3D11VideoPlayer.cs)
- Lớp kết xuất đồ họa kế thừa `HwndHost`.
- Khởi tạo D3D11 Device, SwapChain, Viewport.
- Quản lý Double Buffering Shared Textures & Keyed Mutexes từ NT Handles nhận từ Server.
- Xử lý Viewport tính toán AspectRatioFit và Stretch khi kích thước cửa sổ thay đổi.

#### [NEW] [MainWindow.xaml](file:///c:/Users/ASUS%20NUC/Desktop/Code/OME/samples/dotnet/OME_play/MainWindow.xaml)
- Giao diện người dùng chia làm 2 phần: Khung Video Player lớn và Bảng điều khiển (File selector, Play/Stop buttons, Status badges, Scale mode switch, Log output).

#### [NEW] [MainWindow.xaml.cs](file:///c:/Users/ASUS%20NUC/Desktop/Code/OME/samples/dotnet/OME_play/MainWindow.xaml.cs)
- Logic điều khiển IPC, kết nối `OpenMediaServer.exe`, tạo `SDKPipeline`, nạp `SDKSource`, lấy NT handles và đưa vào `D3D11VideoPlayer`.

### [Component] Tài Liệu Hướng Dẫn (`HDThucHanh`)

#### [MODIFY] [Bài 02-HD_OME_play.md](file:///c:/Users/ASUS%20NUC/Desktop/Code/OME/HDThucHanh/Bài%2002-HD_OME_play.md)
- Cập nhật mục 3 với chi tiết code mẫu hoàn chỉnh và đường dẫn tới thư mục `samples/dotnet/OME_play`.

---

## 3. Kế Hoạch Kiểm Thử & Xác Minh (Verification Plan)

### Automated / Build Verification
- Chạy `dotnet build samples/dotnet/OME_play/OME_play.csproj -c Debug` để đảm bảo mã nguồn biên dịch thành công 100% không có lỗi hoặc cảnh báo.

### Functional Verification
- Kiểm tra kết nối IPC với `OpenMediaServer.exe`.
- Kiểm tra tính năng Play/Stop và hiển thị video trên SwapChain.
- Kiểm tra phím tắt / nút chuyển đổi chế độ AspectRatioFit và Stretch.
