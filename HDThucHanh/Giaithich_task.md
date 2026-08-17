### 1.1 Repository & Toolchain Setup
- [x] 1.1.1 Khởi tạo Git repository
- [x] 1.1.2 Tạo `.gitignore` (C++, .NET, build artifacts, IDE files, .env.*)
- [x] 1.1.3 Tạo `.clang-format` (Google-based, indent 4, column limit 120)
- [x] 1.1.4 Tạo `.clang-tidy` (bugprone, modernize, performance, readability)
- [x] 1.1.5 Tạo `.editorconfig`
- [x] 1.1.6 Tạo `LICENSE` file
- [x] 1.1.7 Tạo `README.md` — project overview, build instructions, requirements

Giải thích những việc ở trên làm nhiệm vụ gì ?

Dưới đây là giải thích chi tiết về mục đích của từng công việc trong phần thiết lập dự án mà bạn vừa đề cập:

**1.1.1 Khởi tạo Git repository**
*   **Mục đích:** Tạo một kho lưu trữ mã nguồn cục bộ bằng Git (hệ thống quản lý phiên bản). Việc này giúp theo dõi lịch sử các lần thay đổi mã nguồn, cho phép khôi phục lại các phiên bản cũ nếu có lỗi, và là nền tảng bắt buộc để làm việc nhóm (kết hợp với GitHub, GitLab,...).

**1.1.2 Tạo `.gitignore`**
*   **Mục đích:** Chỉ định cho Git biết những tệp hoặc thư mục nào **không nên** được theo dõi và đưa lên kho lưu trữ.
*   **Ví dụ trong ngữ cảnh của bạn:** Bỏ qua các file biên dịch trung gian của C++, file rác sinh ra bởi .NET, các thư mục cấu hình cá nhân của IDE (như `.vs/`, `.idea/`), hoặc các file chứa thông tin nhạy cảm như biến môi trường (`.env.*`). Việc này giúp kho lưu trữ gọn nhẹ và bảo mật.

**1.1.3 Tạo `.clang-format`**
*   **Mục đích:** Định cấu hình công cụ tự động format (định dạng) mã nguồn C/C++. 
*   **Cấu hình cụ thể:** Ở đây dự án sử dụng chuẩn của Google làm gốc nhưng có tùy chỉnh lại: thụt lề (indent) là 4 dấu cách thay vì 2, và độ dài tối đa của một dòng (column limit) là 120 ký tự. File này giúp tất cả các thành viên trong team, dù code bằng IDE nào, đều tuân theo đúng một quy chuẩn trình bày code giống hệt nhau.

**1.1.4 Tạo `.clang-tidy`**
*   **Mục đích:** Định cấu hình công cụ "linter" (kiểm tra tĩnh) cho C++. Khác với `.clang-format` chỉ lo về cách trình bày đẹp/xấu, `.clang-tidy` đọc sâu vào logic code để tìm lỗi.
*   **Các nhóm luật được kích hoạt:** `bugprone` (cảnh báo các đoạn code dễ sinh lỗi), `modernize` (khuyên dùng các cú pháp C++ đời mới thay vì cú pháp cũ), `performance` (cảnh báo code chạy chậm), và `readability` (cảnh báo code khó đọc). Công cụ này đóng vai trò như một người review code tự động.

**1.1.5 Tạo `.editorconfig`**
*   **Mục đích:** Cấu hình môi trường soạn thảo tiêu chuẩn cho nhiều loại IDE/Text Editor khác nhau (như Visual Studio, VS Code, Notepad++). Nó quy định những thứ cơ bản nhất như: dùng khoảng trắng (space) hay phím Tab, kết thúc dòng (line ending) theo chuẩn Windows (CRLF) hay Linux (LF), và đảm bảo có một dòng trống ở cuối mỗi file.

**1.1.6 Tạo `LICENSE` file**
*   **Mục đích:** Khai báo giấy phép bản quyền của dự án. File này cho người khác biết dự án của bạn là mã nguồn mở hay đóng, những ai được phép sử dụng lại đoạn code này, có được phép mang đi bán hay không, và giới hạn trách nhiệm pháp lý của bạn tới đâu.

**1.1.7 Tạo `README.md`**
*   **Mục đích:** Đây là tài liệu "mặt tiền" của dự án. Khi bất kỳ ai (hoặc chính bạn sau vài tháng) mở dự án lên, đây là file đầu tiên họ đọc. Nó cung cấp: dự án này làm cái gì (overview), cần cài phần mềm gì để chạy được (requirements), và các bước gõ lệnh để build/chạy ứng dụng (build instructions).

**Tóm lại:** Toàn bộ phần 1.1 này nhằm mục đích xây dựng một bộ móng vững chắc, chuẩn mực, tự động hóa việc kiểm tra chất lượng code và loại bỏ các xung đột không đáng có trước khi các lập trình viên bắt tay vào viết dòng code đầu tiên.

===========
### 1.2 Build System (CMake)
- [x] 1.2.1 Root `CMakeLists.txt` — C++20, vcpkg integration, module includes
- [x] 1.2.2 `cmake/CompilerSettings.cmake` — MSVC/Clang flags, warnings, sanitizers
- [x] 1.2.3 `cmake/Dependencies.cmake` — find_package, third_party paths
- [x] 1.2.4 `cmake/Platform.cmake` — WIN32/UNIX defines, platform libs
- [x] 1.2.5 `cmake/Version.cmake` — Git tag version extraction
- [x] 1.2.6 `cmake/EnvironmentConfig.cmake` — load `.env.demo` / `.env.production`
- [x] 1.2.7 `cmake/OpenMediaConfig.h.in` — generated config header template
- [x] 1.2.8 `cmake/Toolchain/Windows-MSVC.cmake`
- [x] 1.2.9 Verify full CMake configure + build cycle (empty project)
Giải thích những việc ở trên làm nhiệm vụ gì ?

Edited Giaithich_task.md
Viewed Giaithich_task.md:1-23

Dưới đây là giải thích chi tiết về nhiệm vụ của từng file/công việc trong phần cấu hình hệ thống Build (CMake) mà bạn đưa ra:

**1.2.1 Root `CMakeLists.txt`**
*   **Mục đích:** Đây là file "nhạc trưởng" (file chính) của toàn bộ hệ thống build đặt ở thư mục gốc của dự án.
*   **Nhiệm vụ cụ thể:** Nó khai báo tên dự án, ép buộc trình biên dịch phải sử dụng chuẩn ngôn ngữ **C++20**, cấu hình để tích hợp trình quản lý gói **vcpkg** (giúp tự động tải và cài đặt các thư viện ngoài), và gọi (include) các file cấu hình con ở trong thư mục `cmake/` để giữ cho file chính không bị quá dài và rối rắm.

**1.2.2 `cmake/CompilerSettings.cmake`**
*   **Mục đích:** Quản lý toàn bộ cấu hình liên quan đến trình biên dịch (như MSVC trên Windows, GCC/Clang trên Linux/Mac).
*   **Nhiệm vụ cụ thể:** Thay vì để mặc định, file này sẽ bật các **flags** (cờ) cảnh báo lỗi (warnings) ở mức độ khắt khe nhất (nhằm phát hiện lỗi sớm). Nó cũng cấu hình các công cụ **sanitizers** (như AddressSanitizer) để tự động phát hiện các lỗi rò rỉ bộ nhớ hoặc truy cập bộ nhớ sai quy định khi chạy thử ứng dụng.

**1.2.3 `cmake/Dependencies.cmake`**
*   **Mục đích:** Chuyên trách việc tìm kiếm và liên kết các thư viện của bên thứ ba (Third-party libraries).
*   **Nhiệm vụ cụ thể:** Sử dụng lệnh `find_package` để tìm các thư viện đã được cài (qua vcpkg chẳng hạn), hoặc trỏ đường dẫn tới thư mục chứa code của các thư viện bên ngoài mà dự án sử dụng. Nó giúp tập trung việc quản lý thư viện vào một chỗ.

**1.2.4 `cmake/Platform.cmake`**
*   **Mục đích:** Xử lý các logic đặc thù cho từng hệ điều hành.
*   **Nhiệm vụ cụ thể:** Nếu ứng dụng của bạn chạy đa nền tảng (Cross-platform), file này sẽ kiểm tra xem máy đang build là Windows hay UNIX (Linux/Mac). Từ đó, nó sẽ định nghĩa các cờ (như `WIN32`, `UNIX`) và liên kết (link) các thư viện hệ thống bắt buộc của riêng nền tảng đó.

**1.2.5 `cmake/Version.cmake`**
*   **Mục đích:** Tự động hóa việc quản lý phiên bản phần mềm.
*   **Nhiệm vụ cụ thể:** Thay vì phải sửa bằng tay số phiên bản (ví dụ 1.0.0 thành 1.0.1) trong code C++, file này sẽ chạy lệnh để đọc "tag" gần nhất từ Git. Sau đó, nó truyền số phiên bản này từ Git vào thẳng hệ thống build, giúp mọi bản build đều có thông tin phiên bản chính xác.

**1.2.6 `cmake/EnvironmentConfig.cmake`**
*   **Mục đích:** Quản lý cấu hình theo từng môi trường làm việc.
*   **Nhiệm vụ cụ thể:** Đọc các file biến môi trường như `.env.demo` (dành cho lúc dev/chạy thử) hoặc `.env.production` (dành cho lúc phát hành). Việc này giúp ứng dụng có thể build ra các phiên bản trỏ tới các server khác nhau hoặc mang các thiết lập khác nhau mà không cần sửa code.

**1.2.7 `cmake/OpenMediaConfig.h.in`**
*   **Mục đích:** Là một file mẫu (template) để tạo ra file header `.h` trong C++.
*   **Nhiệm vụ cụ thể:** Khi CMake chạy, nó sẽ lấy các thông tin nó biết (như số phiên bản ở bước 1.2.5, hoặc đường dẫn cài đặt) và điền vào file mẫu `.in` này để sinh ra một file header (ví dụ `OpenMediaConfig.h`). Mã nguồn C++ của bạn sẽ `#include` file header này để đọc được các cấu hình từ CMake.

**1.2.8 `cmake/Toolchain/Windows-MSVC.cmake`**
*   **Mục đích:** Định cấu hình cụm công cụ (toolchain) bắt buộc cho Windows.
*   **Nhiệm vụ cụ thể:** Ép hệ thống build sử dụng đúng trình biên dịch MSVC của Visual Studio, với đúng kiến trúc (như 64-bit) và đúng bộ thư viện chuẩn của Windows thay vì để CMake đoán mò (dễ gây lỗi nếu máy tính cài nhiều trình biên dịch khác nhau).

**1.2.9 Verify full CMake configure + build cycle (empty project)**
*   **Mục đích:** Bước nghiệm thu phần "móng" của hệ thống build.
*   **Nhiệm vụ cụ thể:** Trước khi viết những dòng code logic đầu tiên, bạn sẽ chạy thử CMake trên một file C++ "trắng" (chỉ có hàm `main` rỗng). Nếu bước cấu hình (configure) và biên dịch (build) thành công 100%, chứng tỏ toàn bộ 8 file cấu hình ở trên đã liên kết với nhau hoàn hảo và dự án đã sẵn sàng để phát triển.
===========
