using System;
using System.IO;
using System.Threading.Tasks;
using OpenMedia.SDK;

namespace OME_App01
{
    class Program
    {
        private static bool _isPlaying = false;
        private static long _currentPositionMs = 0;
        private static long _totalDurationMs = 30000;

        static async Task Main(string[] args)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("========================================================================");
            Console.WriteLine("    OpenMediaSDK - OME_App01: MFP CLI Stream Inspector & Player       ");
            Console.WriteLine("    (Sử dụng SDKEngine/Binary IPC thay vì MFPClient JSON cũ)           ");
            Console.WriteLine("========================================================================");
            Console.ResetColor();

            string filePath = args.Length > 0 ? args[0] : "sample.mp4";
            if (!File.Exists(filePath))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"[!] Không tìm thấy file '{filePath}'. Nhập đường dẫn file media khác (hoặc Enter để tiếp tục với '{filePath}'): ");
                Console.ResetColor();
                string? input = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(input))
                {
                    filePath = input.Trim('"');
                }
            }

            if (!File.Exists(filePath))
            {
                Console.WriteLine("Không tìm thấy file hợp lệ. Đang thoát.");
                return;
            }

            try
            {
                Console.WriteLine("\n[1/4] Khởi tạo Server Process qua SDKEngine...");
                string serverPath = @"c:\Users\ASUS NUC\Desktop\Code\OME\build-demo\bin\Debug\OpenMediaServer.exe";
                bool connected = await SDKEngine.Instance.InitializeAsync("OpenMediaSDK", serverPath);

                if (!connected)
                {
                    Console.WriteLine("[Lỗi] Không thể kết nối tới Server (OpenMediaServer.exe).");
                    return;
                }

                Console.WriteLine("[2/4] Tạo Pipeline và mở Source...");
                var pipeline = await SDKPipeline.CreateAsync("App01_Pipeline", 1920, 1080, 60.0);
                string absolutePath = Path.GetFullPath(filePath);
                var source = await SDKSource.CreateAsync(pipeline, 1, absolutePath, loop: true);

                Console.WriteLine("[3/4] Đang lấy thông tin file media (GetSourceInfo)...");
                string codec = "Unknown";
                int width = 0;
                int height = 0;
                double fps = 0;
                long bitrate = 0;

                try
                {
                    var info = await source.GetInfoAsync();
                    _totalDurationMs = (long)info.DurationMs;
                    codec = $"{info.VideoCodec} / {info.AudioCodec}";
                    width = (int)info.Width;
                    height = (int)info.Height;
                    fps = info.FrameRate;
                    bitrate = info.BitrateKbps;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Cảnh báo] Lỗi khi lấy thông tin media: {ex.Message}");
                }

                string resStr = $"{width} x {height} ({GetAspectName(width, height)})";
                string fpsStr = $"{fps:F2} FPS";
                string bitStr = $"{bitrate} Kbps";

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\n+----------------------------------------------------------------------+");
                Console.WriteLine("|                  BẢNG THÔNG SỐ KỸ THUẬT TỆP MEDIA                    |");
                Console.WriteLine("+----------------------------------------------------------------------+");
                Console.WriteLine($"|  Tên tập tin    : {Path.GetFileName(filePath),-50} |");
                Console.WriteLine($"|  Độ phân giải   : {resStr,-50} |");
                Console.WriteLine($"|  Mã hóa (Codec) : {codec,-50} |");
                Console.WriteLine($"|  Tốc độ khung   : {fpsStr,-50} |");
                Console.WriteLine($"|  Băng thông     : {bitStr,-50} |");
                Console.WriteLine($"|  Thời lượng     : {FormatTime(_totalDurationMs),-50} |");
                Console.WriteLine("+----------------------------------------------------------------------+\n");
                Console.ResetColor();

                Console.WriteLine("[4/4] BẮT ĐẦU ĐIỀU KHIỂN LUỒNG PHÁT.");
                PrintHelp();

                _isPlaying = true;
                await pipeline.StartAsync();

                bool keepRunning = true;
                while (keepRunning)
                {
                    if (Console.KeyAvailable)
                    {
                        var keyInfo = Console.ReadKey(intercept: true);
                        switch (keyInfo.Key)
                        {
                            case ConsoleKey.Spacebar:
                                _isPlaying = !_isPlaying;
                                if (_isPlaying) await pipeline.StartAsync();
                                else await pipeline.StopAsync(); // using StopAsync as Pause is not implemented in SDKPipeline yet
                                Console.ForegroundColor = _isPlaying ? ConsoleColor.Green : ConsoleColor.Yellow;
                                Console.WriteLine($"\r[Trạng thái]: {(_isPlaying ? "► PLAYING" : "❚❚ PAUSED ")}                          ");
                                Console.ResetColor();
                                break;

                            case ConsoleKey.S:
                            case ConsoleKey.Q:
                            case ConsoleKey.Escape:
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine("\r[Dừng phát] Đang dừng pipeline và thoát...");
                                Console.ResetColor();
                                await pipeline.StopAsync();
                                await SDKEngine.Instance.ShutdownAsync();
                                keepRunning = false;
                                break;
                        }
                    }

                    if (_isPlaying && _currentPositionMs < _totalDurationMs)
                    {
                        _currentPositionMs += 200;
                    }
                    await Task.Delay(200);
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[Lỗi Ứng dụng] {ex.Message}");
                Console.ResetColor();
            }

            Console.WriteLine("\nĐã thoát ứng dụng OME_App01 an toàn.");
        }

        private static void PrintHelp()
        {
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("------------------------------------------------------------------------");
            Console.WriteLine(" PHÍM ĐIỀU KHIỂN:");
            Console.WriteLine("   [Space]       : Bật / Tạm dừng (Play / Pause)");
            Console.WriteLine("   [S] / [Q] /Esc: Dừng phát & Thoát chương trình");
            Console.WriteLine("------------------------------------------------------------------------");
            Console.ResetColor();
        }

        private static string FormatTime(long timeMs)
        {
            TimeSpan ts = TimeSpan.FromMilliseconds(timeMs);
            return $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds / 100:D1}";
        }

        private static string GetAspectName(int w, int h)
        {
            if (w == 1920 && h == 1080) return "16:9 FHD";
            if (w == 3840 && h == 2160) return "16:9 4K UHD";
            if (w == 1280 && h == 720) return "16:9 HD";
            return "Custom";
        }
    }
}
