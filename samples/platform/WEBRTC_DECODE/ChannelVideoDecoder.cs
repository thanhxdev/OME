using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace WEBRTC_DECODE
{
    /// <summary>
    /// Handles real-time raw H.264 / H.265 Annex B NAL decoding to raw BGRA video frames using ultra-low-latency FFmpeg.
    /// </summary>
    public sealed class ChannelVideoDecoder : IDisposable
    {
        private readonly int _channelIndex;
        private readonly int _width;
        private readonly int _height;
        private readonly int _frameSizeBytes;
        private readonly string _codecFormat; // "h264" or "hevc"

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

        public ChannelVideoDecoder(int channelIndex, int width = 1920, int height = 1080, string codecFormat = "h264")
        {
            _channelIndex = channelIndex;
            _width = width;
            _height = height;
            _frameSizeBytes = width * height * 4; // BGRA32 (4 bytes per pixel)
            _codecFormat = (codecFormat.Contains("265") || codecFormat.Contains("hevc")) ? "hevc" : "h264";
        }

        public bool Start()
        {
            if (_isRunning) return true;

            try
            {
                // Ultra-low latency FFmpeg raw bitstream decoder:
                // - probesize 64k & analyzeduration 200k for zero startup delay
                // - fflags nobuffer+flush_packets & flags low_delay eliminates internal queues
                // - -f h264 / -f hevc accepts raw Annex B NAL stream from pipe:0
                // - outputs rawvideo BGRA directly matching WPF WriteableBitmap Bgra32 format
                string args = $"-hide_banner -loglevel error -probesize 64k -analyzeduration 200k -fflags nobuffer+flush_packets -flags low_delay -f {_codecFormat} -i pipe:0 -an -sn -dn -f rawvideo -pix_fmt bgra -s {_width}x{_height} pipe:1";

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

                // Start stderr drain loop so FFmpeg never hangs
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var reader = proc.StandardError;
                        while (!token.IsCancellationRequested && !proc.HasExited)
                        {
                            string? line = await reader.ReadLineAsync().ConfigureAwait(false);
                            if (line == null) break;
                            Log("[FFMPEG-VID]", $"Cam {_channelIndex + 1}: {line}");
                        }
                    }
                    catch { }
                }, token);

                // Start stdout BGRA frame read loop
                _ = Task.Run(async () =>
                {
                    byte[] frameBuffer = new byte[_frameSizeBytes];
                    int totalRead = 0;

                    try
                    {
                        while (!token.IsCancellationRequested && !proc.HasExited)
                        {
                            int bytesRead = await stdout.ReadAsync(frameBuffer.AsMemory(totalRead, _frameSizeBytes - totalRead), token).ConfigureAwait(false);
                            if (bytesRead <= 0) break;

                            totalRead += bytesRead;
                            if (totalRead >= _frameSizeBytes)
                            {
                                byte[] frameCopy = new byte[_frameSizeBytes];
                                Buffer.BlockCopy(frameBuffer, 0, frameCopy, 0, _frameSizeBytes);
                                totalRead = 0;

                                FrameDecoded?.Invoke(_channelIndex, frameCopy, _width, _height);
                            }
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        Log("[WARN]", $"Lỗi đọc khung hình Cam {_channelIndex + 1}: {ex.Message}");
                    }
                }, token);

                Log("[VIDEO]", $"✅ FFmpeg Video Decoder ({_codecFormat.ToUpper()} -> BGRA32 {_width}x{_height}) cho Cam {_channelIndex + 1} đã hoạt động.");
                return true;
            }
            catch (Exception ex)
            {
                Log("[ERROR]", $"Ngoại lệ khi khởi chạy Video Decoder cho Cam {_channelIndex + 1}: {ex.Message}");
                _isRunning = false;
                return false;
            }
        }

        public void FeedData(byte[] data, int length)
        {
            if (!_isRunning || _stdin == null || _disposed || length <= 0) return;

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

            try { _cts?.Cancel(); } catch { }

            try
            {
                _stdin?.Close();
                _stdout?.Close();
            }
            catch { }

            try
            {
                if (_process != null && !_process.HasExited)
                {
                    _process.Kill();
                    _process.WaitForExit(500);
                }
            }
            catch { }
            finally
            {
                _process?.Dispose();
                _process = null;
                _stdin = null;
                _stdout = null;
                _cts?.Dispose();
                _cts = null;
            }
        }

        private void Log(string tag, string message)
        {
            LogEmitted?.Invoke(tag, message);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
