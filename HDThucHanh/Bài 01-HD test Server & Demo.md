# Bài 01: Hướng dẫn test Server & Demo

Tài liệu này hướng dẫn cách build dự án OpenMedia SDK ở môi trường demo, cách chạy Server, cũng như cách thực thi các bài test và ứng dụng mẫu (demo).

> **💡 Tổng quan nhanh về các loại file thực thi (`.exe`):**
> Hệ thống được thiết kế theo mô hình Client-Server. Dưới đây là các file bạn sẽ dùng để test:
> - **File chạy Server:** `OpenMediaServer.exe` (Chạy ngầm để xử lý media).
> - **File chạy Demo (Client):** `pipeline_demo.exe`, `mixer_demo.exe`, `rtmp_demo.exe`,... (Đóng vai trò là Client kết nối với Server).
> - **File chạy Test (Code/Chức năng):** `test_core.exe`, `test_io.exe`,... (Các Unit/Integration test tự động).
> - *(Lưu ý: Có sẵn một file video `sample.mp4` trong thư mục build dùng làm dữ liệu đầu vào cho các demo).*

## 1. Yêu cầu hệ thống cơ bản
Đảm bảo bạn đã cài đặt đủ các công cụ sau trước khi tiến hành:
- **CMake** (3.28+)
- **Visual Studio 2022** (MSVC 17.8+)
- **.NET 8.0 SDK+**
- **Git** (2.40+)
- **PowerShell**

## 2. Chuẩn bị môi trường & Build dự án

Mở PowerShell (với quyền Administrator nếu cần thiết để cài đặt SDK) và di chuyển tới thư mục gốc của dự án (`c:\Users\ASUS NUC\Desktop\Code\OME`).

### 2.1 Cài đặt các dependencies (chỉ làm lần đầu)
Chạy script sau để tải và cài đặt các thư viện (vcpkg) và SDK cần thiết:
```powershell
.\tools\scripts\setup_env.ps1 -DownloadSDKs
```

### 2.2 Build dự án (Môi trường Demo)
Thực thi lệnh build bằng PowerShell script đã được chuẩn bị sẵn, lệnh này sẽ build dự án và chạy luôn các unit test:
```powershell
.\tools\scripts\build.ps1 -Environment demo -BuildTests -RunTests
```
> **Lưu ý:** Quá trình build sẽ tạo ra thư mục `build-demo` chứa các file thực thi (exe) và thư viện (dll, pdb).

---

## 3. Hướng dẫn chạy OpenMedia Server

Kiến trúc của dự án tách biệt Client và Server. Bạn cần khởi chạy Server để xử lý media trước, sau đó mới chạy các UI/Demo client kết nối tới.

1. Mở Terminal (PowerShell hoặc CMD).
2. Di chuyển vào thư mục chứa file thực thi của bản build Debug:
```powershell
cd build-demo\bin\Debug
```
3. Chạy Server:
```powershell
.\OpenMediaServer.exe
```
> Khi Server đang chạy, bạn có thể để nguyên cửa sổ Terminal này và mở một cửa sổ Terminal mới để chạy các file demo.

---

## 4. Hướng dẫn chạy Demo (Ứng dụng mẫu)

Dự án cung cấp một số ứng dụng mẫu (Demo) nằm chung trong thư mục `build-demo\bin\Debug`.

Sử dụng Terminal mới, di chuyển vào thư mục trên:
```powershell
cd build-demo\bin\Debug
```

Chạy một trong các ứng dụng demo sau (đảm bảo `OpenMediaServer.exe` đang chạy nền):

- **Pipeline Demo:**
  ```powershell
  .\pipeline_demo.exe
  ```
- **Mixer Demo:**
  ```powershell
  .\mixer_demo.exe
  ```
- **RTMP Demo:**
  ```powershell
  .\rtmp_demo.exe
  ```
- **Broadcast Pipeline:**
  ```powershell
  .\broadcast_pipeline.exe
  ```

---

## 5. Hướng dẫn chạy Test

Nếu bạn muốn chạy lại các bài test hệ thống (Unit tests, Integration tests, v.v.) thủ công, bạn có thể dùng công cụ `ctest` của CMake hoặc gọi trực tiếp file test.

### Cách 1: Dùng CTest (Chạy tất cả)
Từ thư mục gốc của dự án, chạy:
```powershell
ctest --test-dir build-demo -C Debug --output-on-failure
```

### Cách 2: Chạy từng bài test cụ thể
Vào thư mục `build-demo\bin\Debug` và chạy trực tiếp file test. Ví dụ:
- Test Core:
  ```powershell
  .\test_core.exe
  ```
- Test Codecs:
  ```powershell
  .\test_codecs.exe
  ```
- Test IO:
  ```powershell
  .\test_io.exe
  ```
