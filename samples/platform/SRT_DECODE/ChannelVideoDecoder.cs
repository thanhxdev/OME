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

        public int ChannelIndex => _channelIndex;
        public int Width => _width;
        public int Height => _height;
        public bool IsRunning => _isRunning;

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
                string args = $"-hide_banner -loglevel error -probesize 64k -analyzeduration 200k -fflags nobuffer+flush_packets -flags low_delay -f mpegts -i pipe:0 -an -sn -dn -f rawvideo -pix_fmt bgra -s {_width}x{_height} pipe:1";

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
                            Log("[FFMPEG]", $"Cam {_channelIndex + 1}: {line}");
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

            try
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = null;

                _stdin?.Close();
                _stdin = null;

                _stdout?.Close();
                _stdout = null;

                if (_process != null && !_process.HasExited)
                {
                    try
                    {
                        _process.Kill();
                        _process.WaitForExit(500);
                    }
                    catch { }
                    _process.Dispose();
                    _process = null;
                }

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
