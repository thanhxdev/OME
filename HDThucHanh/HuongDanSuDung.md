# Hướng dẫn sử dụng OpenMediaSDK

Chào bạn, để có thể bắt đầu sử dụng dự án **OpenMediaSDK**, bạn cần thực hiện theo các bước thiết lập môi trường, tải các thư viện phụ thuộc và tiến hành biên dịch (build) dự án. Dựa trên tài liệu gốc của dự án, đây là hướng dẫn chi tiết dành cho bạn:

## 1. Chuẩn bị môi trường & Công cụ (Yêu cầu hệ thống)
Trước khi bắt đầu, hãy đảm bảo máy tính của bạn đã cài đặt các công cụ sau:
* **CMake:** Phiên bản 3.28 trở lên.
* **Visual Studio 18 2026 (MSVC):** Phiên bản 18.0 trở lên.
* **.NET SDK:** Phiên bản 8.0 hoặc 9.0 (dành cho phần giao diện/wrappers).
* **Windows SDK:** 10.0.22000+.
* **Git:** Phiên bản 2.40+.
* **vcpkg:** (Công cụ quản lý thư viện C/C++ của Microsoft, sẽ được tự động thiết lập ở bước sau).

Về mặt phần cứng, bạn nên có máy tính từ 4 nhân (khuyến nghị 8+ nhân), RAM 8GB (khuyến nghị 32GB) và GPU hỗ trợ DirectX 11 (khuyên dùng NVIDIA RTX hoặc Intel ARC) để tận dụng tốt khả năng tăng tốc phần cứng của Media Engine.

## 2. Thiết lập thư viện và Dependencies
Dự án đã có sẵn các script tự động hóa. Mở Terminal (PowerShell) và trỏ tới thư mục chứa dự án `OpenMediaSDK`, sau đó chạy lệnh cài đặt môi trường sau để tải vcpkg và các SDK cần thiết:

```powershell
.\tools\scripts\setup_env.ps1 -DownloadSDKs
```

## 3. Biên dịch dự án (Build)
Bạn có thể chọn build dự án theo 2 môi trường (Demo hoặc Production):

### Cách 1: Sử dụng script có sẵn (Khuyên dùng)
* **Để Build bản Demo & Chạy Test:**
  ```powershell
  .\tools\scripts\build.ps1 -Environment demo -BuildTests -RunTests
  ```
* **Để Build bản Production:**
  ```powershell
  .\tools\scripts\build.ps1 -Environment production
  ```

### Cách 2: Build thủ công bằng CMake
Nếu bạn muốn tự kiểm soát quá trình build, có thể dùng các lệnh CMake sau:
```powershell
# Tạo cấu hình build
cmake -B build-demo -DOME_ENV_TAG=demo -DCMAKE_BUILD_TYPE=Debug -G "Visual Studio 18 2026" -A x64

# Tiến hành biên dịch
cmake --build build-demo --config Debug --parallel

# Chạy Test (Tuỳ chọn)
ctest --test-dir build-demo -C Debug --output-on-failure
```

## 4. Bắt đầu sử dụng SDK
Sau khi build thành công, bạn sẽ nhận được các thành phần chính theo kiến trúc **Client/Server** của OpenMediaSDK:
* `OpenMediaServer.exe`: Là Engine xử lý Media ở dưới nền (Server).
* `OpenMedia.SDK.dll`: Thư viện API công khai để ứng dụng của bạn gọi tới.
* Nếu bạn là nhà phát triển UI (C#/.NET, WPF, WinUI), bạn sẽ tích hợp/tham chiếu thư viện `.dll` này vào ứng dụng của mình để giao tiếp với Server.
* Bạn có thể xem mã nguồn ứng dụng mẫu trong thư mục `samples/` hoặc xem các plugins ở thư mục `plugins/` để tham khảo cách tích hợp.

### Hướng dẫn tích hợp thư viện (.dll) vào dự án UI (Visual Studio)

Việc thêm một thư viện `.dll` (Dynamic Link Library) vào dự án là một thao tác rất cơ bản và thường xuyên được sử dụng để giao tiếp với Server hoặc dùng lại mã nguồn.

#### Bước 1: Thêm tham chiếu (Reference) vào dự án

1. Mở dự án WPF hoặc WinUI của bạn trong **Visual Studio**.
2. Nhìn sang cửa sổ **Solution Explorer** (thường nằm ở bên phải màn hình).
3. Tìm đến tên dự án của bạn, mở rộng nó ra và tìm mục **Dependencies** (đối với .NET Core/.NET 5+) hoặc **References** (đối với .NET Framework cũ).
4. Nhấp **chuột phải** vào `Dependencies` (hoặc `References`) và chọn **Add Project Reference...** (hoặc **Add Reference...**).
5. Một cửa sổ mới có tên "Reference Manager" sẽ hiện ra. Ở cột bên trái, bạn hãy chọn thẻ **Browse**.
6. Nhấp vào nút **Browse...** ở góc dưới bên phải cửa sổ.
7. Duyệt đến thư mục chứa file `.dll` mà bạn muốn thêm vào, chọn file đó và nhấn **Add**.
8. Đảm bảo rằng file `.dll` bạn vừa thêm đã được đánh dấu tick (✅) trong danh sách, sau đó nhấn **OK**.

#### Bước 2: Sử dụng thư viện trong Code (C#)

Sau khi đã thêm `.dll` thành công, bạn cần khai báo để sử dụng các tính năng bên trong nó. Ở trên cùng của file C# nơi bạn code (ví dụ `MainWindow.xaml.cs`), thêm từ khóa `using` kèm theo Namespace của thư viện:

```csharp
using System;
using System.Windows;
// Thêm namespace của thư viện .dll vào đây
using TenThuVienCuaBan; 

namespace MyApp
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            
            // Bây giờ bạn có thể tạo đối tượng và gọi các hàm từ thư viện
            // Ví dụ:
            // ServerClient client = new ServerClient();
            // client.Connect();
        }
    }
}
```

### Hướng dẫn tạo dự án UI mới và tích hợp thư viện

Nếu bạn chưa có sẵn dự án, dưới đây là cách tạo một dự án UI mới (ví dụ với WPF) và thêm thư viện:

#### Bước 1: Tạo dự án mới trong Visual Studio
1. Mở **Visual Studio 18 2026**.
2. Chọn **Create a new project** (Tạo dự án mới) ở màn hình khởi động.
3. Trong hộp tìm kiếm, nhập `WPF` (hoặc `WinUI` nếu bạn muốn dùng WinUI 3).
4. Chọn template **WPF Application** (C#) và nhấn **Next**.
5. Đặt tên cho dự án, chọn vị trí lưu và nhấn **Next**.
6. Chọn phiên bản **Framework** (khuyên dùng .NET 8.0 trở lên theo yêu cầu của SDK) và nhấn **Create**.

#### Bước 2: Quản lý file `.dll` (Khuyên dùng)
Để dễ dàng quản lý và chia sẻ code sau này, bạn nên tạo một thư mục tên là `Libs` hoặc `Dependencies` ngay bên trong thư mục dự án vừa tạo, sau đó copy file `OpenMedia.SDK.dll` vào thư mục đó thay vì trỏ reference trực tiếp đến nơi build.

#### Bước 3: Thêm tham chiếu và gọi Code
1. Thực hiện lại thao tác **Add Project Reference... > Browse** (như đã hướng dẫn ở phần trước) và trỏ tới file `.dll` nằm trong thư mục `Libs` của bạn.
2. Mở giao diện `MainWindow.xaml`, kéo thả một nút bấm (Button) vào màn hình.
3. Nhấp đúp vào nút bấm đó để Visual Studio tự tạo hàm sự kiện Click trong file `.cs`.
4. Trong hàm sự kiện đó, bạn `using` namespace của thư viện và viết thử một đoạn code gọi SDK để kiểm tra giao tiếp.

💡 **Lưu ý thêm:**
Để hiểu sâu hơn về kiến trúc cũng như cách thiết kế API, bạn hãy đọc thêm các tài liệu thiết kế có trong thư mục `docs/` của dự án (ví dụ: `docs/08_api_design.md` hoặc `docs/01_architecture_overview.md`).

