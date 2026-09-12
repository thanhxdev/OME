using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SRT_DECODE
{
    /// <summary>
    /// Handles real-time MPEG-TS decoding to raw BGRA video frames using a lightweight FFmpeg subprocess.
    /// </summary>
    public sealed class ChannelVideoDecoder : IDisposable
    {
        private readonly int _channelIndex;
        private readonly int _width;
        private readonly int _height;
        private readonly int _frameSizeBytes;

        private Process? _process;
        private Stream? _stdin;
        private Stream? _stdout;
        private CancellationTokenSource? _cts;
        private bool _isRunning;
        private bool _disposed;

        private int _detectedWidth = 1920;
        private int _detectedHeight = 1080;
        private double _detectedFps = 59.94;
        private int _decodedFrameCounter;
        private long _lastFpsTick = Environment.TickCount64;
        private double _measuredFps = 0.0;

        public int ChannelIndex => _channelIndex;
        public int Width => _width;
        public int Height => _height;
        public bool IsRunning => _isRunning;

        public int DetectedWidth => _detectedWidth;
        public int DetectedHeight => _detectedHeight;
        public double DetectedFps => _detectedFps;
        public double MeasuredFps => _measuredFps;

        public double GetCurrentFps()
        {
            long elapsed = Environment.TickCount64 - _lastFpsTick;
            if (elapsed > 2000 && _decodedFrameCounter == 0)
            {
                _measuredFps = 0.0;
            }
            return _measuredFps > 0.1 ? _measuredFps : (_detectedFps > 0.1 ? _detectedFps : 0.0);
        }

        /// <summary>
        /// Fired when a new decoded BGRA video frame is ready.
        /// Arguments: (channelIndex, frameBytes, width, height)
        /// </summary>
        public event Action<int, byte[], int, int>? FrameDecoded;
        public event Action<string, string>? LogEmitted;

        public ChannelVideoDecoder(int channelIndex, int width = 1920, int height = 1080)
        {
            _channelIndex = channelIndex;
            _width = width;
            _height = height;
            _frameSizeBytes = width * height * 4; // BGRA32 (4 bytes per pixel)
        }

        public bool Start()
        {
            if (_isRunning) return true;

            try
            {
                // Ultra-low latency FFmpeg decoder parameters:
                // - probesize & analyzeduration kept minimal to start rendering first frames immediately
                // - nobuffer and low_delay flags eliminate internal buffering
                // - rawvideo bgra matches WPF WriteableBitmap Bgra32 pixel format directly
                // - loglevel info outputs stream header metadata (resolution & fps) to stderr for auto-detection
                string args = $"-hide_banner -loglevel info -probesize 128k -analyzeduration 250k -fflags nobuffer+flush_packets -flags low_delay -f mpegts -i pipe:0 -an -sn -dn -f rawvideo -pix_fmt bgra -s {_width}x{_height} pipe:1";

                var psi = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = args,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                _process = Process.Start(psi);
                if (_process == null)
                {
                    Log("[ERROR]", $"Không thể khởi chạy FFmpeg Video Decoder cho Cam {_channelIndex + 1}");
                    return false;
                }

                _stdin = _process.StandardInput.BaseStream;
                _stdout = _process.StandardOutput.BaseStream;
                _cts = new CancellationTokenSource();
                _isRunning = true;

                var token = _cts.Token;
                var proc = _process;
                var stdout = _stdout;

                // Start stderr drain loop so FFmpeg never hangs on full stderr buffer
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var reader = proc.StandardError;
                        while (!token.IsCancellationRequested && !proc.HasExited)
                        {
                            string? line = await reader.ReadLineAsync().ConfigureAwait(false);
                            if (line == null) break;

                            // Detect input stream resolution & nominal FPS from FFmpeg banner
                            if (line.Contains("Video:"))
                            {
                                var resMatch = System.Text.RegularExpressions.Regex.Match(line, @"\b(\d{3,4})x(\d{3,4})\b");
                                if (resMatch.Success && int.TryParse(resMatch.Groups[1].Value, out int w) && int.TryParse(resMatch.Groups[2].Value, out int h))
                                {
                                    if (w >= 320 && h >= 240)
                                    {
                                        _detectedWidth = w;
                                        _detectedHeight = h;
                                    }
                                }
                                var fpsMatch = System.Text.RegularExpressions.Regex.Match(line, @"([0-9.]+)\s+fps");
                                if (fpsMatch.Success && double.TryParse(fpsMatch.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsedFps))
                                {
                                    if (parsedFps > 1.0)
                                    {
                                        _detectedFps = parsedFps;
                                    }
                                }
                            }

                            // Bỏ qua các dòng log chứa thông số và tốc độ truyền frames (frame=, fps=, speed=, bitrate=, time=, stream mapping)
                            if (line.Contains("frame=") || line.Contains("fps=") || line.Contains("speed=") || 
                                line.Contains("bitrate=") || line.Contains("time=") || line.Contains("Stream #") ||
                                line.Contains("size=") || line.Contains("q="))
                            {
                                continue;
                            }

                            // Chỉ ghi log các lỗi hoặc cảnh báo thực sự quan trọng
                            if (line.Contains("Error", StringComparison.OrdinalIgnoreCase) || 
                                line.Contains("Fatal", StringComparison.OrdinalIgnoreCase))
                            {
                                Log("[FFMPEG]", $"Cam {_channelIndex + 1}: {line}");
                            }
                        }
                    }
                    catch { }
                }, token);

                // Start stdout frame reader loop
                _ = Task.Run(async () =>
                {
                    byte[] frameBuffer = new byte[_frameSizeBytes];
                    try
                    {
                        while (!token.IsCancellationRequested && _isRunning && !proc.HasExited)
                        {
                            int totalRead = 0;
                            while (totalRead < _frameSizeBytes)
                            {
                                int read = await stdout.ReadAsync(frameBuffer.AsMemory(totalRead, _frameSizeBytes - totalRead), token).ConfigureAwait(false);
                                if (read <= 0) break;
                                totalRead += read;
                            }

                            if (totalRead == _frameSizeBytes)
                            {
                                _decodedFrameCounter++;
                                long now = Environment.TickCount64;
                                long elapsed = now - _lastFpsTick;
                                if (elapsed >= 1000)
                                {
                                    _measuredFps = Math.Round((_decodedFrameCounter * 1000.0) / elapsed, 2);
                                    _decodedFrameCounter = 0;
                                    _lastFpsTick = now;
                                }

                                // Create a copy of the frame bytes for UI thread consumption
                                byte[] frameCopy = new byte[_frameSizeBytes];
                                Buffer.BlockCopy(frameBuffer, 0, frameCopy, 0, _frameSizeBytes);

                                FrameDecoded?.Invoke(_channelIndex, frameCopy, _width, _height);
                            }
                            else if (totalRead == 0)
                            {
                                await Task.Delay(5, token).ConfigureAwait(false);
                            }
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        Log("[WARN]", $"Lỗi đọc khung hình Decoder Cam {_channelIndex + 1}: {ex.Message}");
                    }
                }, token);

                Log("[DECODER]", $"✅ Khởi động FFmpeg Decoder Engine thành công cho Cam {_channelIndex + 1} ({_width}x{_height} BGRA32)");
                return true;
            }
            catch (Exception ex)
            {
                Log("[ERROR]", $"Lỗi khởi động Decoder Cam {_channelIndex + 1}: {ex.Message}");
                _isRunning = false;
                return false;
            }
        }

        public async Task FeedDataAsync(byte[] data, int length)
        {
            if (!_isRunning || _stdin == null || data == null || length <= 0) return;

            try
            {
                await _stdin.WriteAsync(data.AsMemory(0, length)).ConfigureAwait(false);
                await _stdin.FlushAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log("[WARN]", $"Lỗi ghi dữ liệu vào Decoder Cam {_channelIndex + 1}: {ex.Message}");
            }
        }

        public void FeedData(byte[] data, int length)
        {
            if (!_isRunning || _stdin == null || data == null || length <= 0) return;

            try
            {
                _stdin.Write(data, 0, length);
                _stdin.Flush();
            }
            catch { }
        }

        public void Stop()
        {
            if (!_isRunning) return;
            _isRunning = false;
            _measuredFps = 0.0;
            _decodedFrameCounter = 0;

            try
            {
                _cts?.Cancel();

                // Kill process tree first so child threads and pipe readers exit immediately
                if (_process != null && !_process.HasExited)
                {
                    try
                    {
                        _process.Kill(entireProcessTree: true);
                        _process.WaitForExit(150);
                    }
                    catch { }
                    try { _process.Dispose(); } catch { }
                    _process = null;
                }

                try { _stdin?.Close(); } catch { }
                _stdin = null;

                try { _stdout?.Close(); } catch { }
                _stdout = null;

                try { _cts?.Dispose(); } catch { }
                _cts = null;

                Log("[DECODER]", $"Đã dừng FFmpeg Decoder cho Cam {_channelIndex + 1}");
            }
            catch (Exception ex)
            {
                Log("[WARN]", $"Lỗi khi đóng Decoder Cam {_channelIndex + 1}: {ex.Message}");
            }
        }

        private void Log(string tag, string message)
        {
            LogEmitted?.Invoke(tag, message);
            Trace.WriteLine($"[ChannelVideoDecoder]{tag} {message}");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
