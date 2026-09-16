using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using OpenMedia.Platform.Models;

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
        private Channel<byte[]>? _inputChannel;
        private CancellationTokenSource? _cts;
        private bool _isRunning;
        private bool _disposed;

        private int _detectedWidth = 1920;
        private int _detectedHeight = 1080;
        private double _detectedFps = 0.0;
        private int _decodedFrameCounter;
        private long _lastFpsTick = Environment.TickCount64;
        private double _measuredFps = 0.0;

        private MediaStreamInfo? _expectedFormat;
        private NutFrameDemuxer? _demuxer;

        public int ChannelIndex => _channelIndex;
        public int Width => _width;
        public int Height => _height;
        public bool IsRunning => _isRunning;

        public int DetectedWidth => _detectedWidth;
        public int DetectedHeight => _detectedHeight;
        public double DetectedFps => _detectedFps;
        public double MeasuredFps => _measuredFps;

        public static double SnapToBroadcastRate(double rawFps, double nominalHint = 0.0)
        {
            if (rawFps <= 0.5) return 0.0;

            // Nếu nominalHint khả dụng và rawFps nằm trong dung sai ±6.0 fps, khóa chặt vào nominalHint
            if (nominalHint > 1.0 && Math.Abs(rawFps - nominalHint) <= 6.0)
            {
                return nominalHint;
            }

            // Danh mục tần số khung hình chuẩn truyền hình quốc tế
            double[] broadcastRates = { 23.98, 24.0, 25.0, 29.97, 30.0, 50.0, 59.94, 60.0, 100.0, 119.88, 120.0 };
            foreach (double rate in broadcastRates)
            {
                if (Math.Abs(rawFps - rate) <= 2.5)
                {
                    return rate;
                }
            }

            return Math.Round(rawFps, 2);
        }

        public double GetCurrentFps()
        {
            long elapsed = Environment.TickCount64 - _lastFpsTick;
            if (elapsed > 3500 && _decodedFrameCounter == 0)
            {
                _measuredFps = 0.0;
                return 0.0;
            }

            if (_detectedFps > 0.1)
            {
                return _detectedFps;
            }

            if (_measuredFps > 0.1)
            {
                return SnapToBroadcastRate(_measuredFps);
            }

            return 0.0;
        }

        public void SetExpectedFormat(MediaStreamInfo info)
        {
            if (info == null) return;
            _expectedFormat = info;
            if (info.Width > 0 && info.Height > 0)
            {
                _detectedWidth = info.Width;
                _detectedHeight = info.Height;
            }
            if (info.FrameRateDouble > 0)
            {
                _detectedFps = info.FrameRateDouble;
            }
            _demuxer?.UpdateFormat(_width, _height, info.FrameRateNum, info.FrameRateDen);
        }

        /// <summary>
        /// Bắn ra khi có khung hình BGRA32 mới kèm theo mốc thời gian PTS 90kHz và thời lượng khung hình.
        /// Arguments: (channelIndex, frameBytes, width, height, pts90k, duration90k)
        /// </summary>
        public event Action<int, byte[], int, int, long, long>? FrameDecodedWithPts;

        /// <summary>
        /// Fired when a new decoded BGRA video frame is ready (Backward compatibility).
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
                // - probesize & analyzeduration 1000k ensure reliable detection of H.264 SPS/PPS headers
                // - probe buffer is retained for clean playback start on non-seekable pipe:0
                // - nobuffer and low_delay eliminate decoding latency
                // - forced output resolution {_width}x{_height} bgra matches NutFrameDemuxer buffer size
                string args = $"-hide_banner -loglevel info -probesize 1000k -analyzeduration 1000k -thread_queue_size 1024 -fflags nobuffer -flags low_delay -f mpegts -i pipe:0 -an -sn -dn -f nut -c:v rawvideo -pix_fmt bgra -s {_width}x{_height} pipe:1";

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
                // Unbounded channel: NEVER drop compressed MPEG-TS packets (dropping packets destroys H.264 reference frames)
                _inputChannel = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false
                });
                _cts = new CancellationTokenSource();
                _isRunning = true;

                var token = _cts.Token;
                var proc = _process;
                var stdout = _stdout;
                var stdin = _stdin;
                var inputChannel = _inputChannel;

                // Dedicated decoupled background task to pump TS data into FFmpeg stdin with batch coalescing
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var reader = inputChannel.Reader;
                        byte[] batchBuffer = new byte[32768];
                        int batchLen = 0;

                        while (!token.IsCancellationRequested && await reader.WaitToReadAsync(token).ConfigureAwait(false))
                        {
                            while (reader.TryRead(out var chunk))
                            {
                                if (proc.HasExited || stdin == null) return;

                                if (chunk.Length > batchBuffer.Length - batchLen)
                                {
                                    if (batchLen > 0)
                                    {
                                        await stdin.WriteAsync(batchBuffer.AsMemory(0, batchLen), token).ConfigureAwait(false);
                                        batchLen = 0;
                                    }
                                }

                                if (chunk.Length >= batchBuffer.Length)
                                {
                                    await stdin.WriteAsync(chunk.AsMemory(0, chunk.Length), token).ConfigureAwait(false);
                                }
                                else
                                {
                                    Buffer.BlockCopy(chunk, 0, batchBuffer, batchLen, chunk.Length);
                                    batchLen += chunk.Length;
                                }
                            }

                            if (batchLen > 0)
                            {
                                if (proc.HasExited || stdin == null) return;
                                await stdin.WriteAsync(batchBuffer.AsMemory(0, batchLen), token).ConfigureAwait(false);
                                batchLen = 0;
                            }

                            if (stdin != null)
                            {
                                await stdin.FlushAsync(token).ConfigureAwait(false);
                            }
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch { }
                }, token);

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
                                var fpsMatch = System.Text.RegularExpressions.Regex.Match(line, @"\b(\d+(?:\.\d+)?)\s*(?:fps|tbr)\b");
                                if (fpsMatch.Success && double.TryParse(fpsMatch.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsedFps))
                                {
                                    if (parsedFps > 1.0)
                                    {
                                        _detectedFps = SnapToBroadcastRate(parsedFps);
                                        var rational = BroadcastFrameRates.SnapToRational(_detectedFps);
                                        _demuxer?.UpdateFormat(_width, _height, rational.num, rational.den);
                                        Log("[DECODER]", $"Cam {_channelIndex + 1}: Tự động đồng bộ chuẩn phát hình: {_detectedWidth}x{_detectedHeight} -> hiển thị {_width}x{_height} @ {_detectedFps:F2} FPS ({rational.num}/{rational.den})");
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

                // Start stdout NUT frame demuxer loop
                int fpsNum = _expectedFormat?.FrameRateNum ?? (_detectedFps > 1.0 ? BroadcastFrameRates.SnapToRational(_detectedFps).num : 25);
                int fpsDen = _expectedFormat?.FrameRateDen ?? (_detectedFps > 1.0 ? BroadcastFrameRates.SnapToRational(_detectedFps).den : 1);
                _demuxer = new NutFrameDemuxer(_channelIndex, _width, _height, fpsNum, fpsDen);
                _demuxer.LogEmitted += (tag, msg) => Log(tag, msg);
                _demuxer.FrameDemuxed += (chIdx, frameBytes, w, h, pts, duration) =>
                {
                    _decodedFrameCounter++;
                    long now = Environment.TickCount64;

                    // Nếu chưa có nominal FPS, trích xuất ngay từ duration90k
                    if (_detectedFps < 1.0 && duration > 0)
                    {
                        double fpsFromDuration = 90000.0 / duration;
                        _detectedFps = SnapToBroadcastRate(fpsFromDuration);
                    }

                    if (_measuredFps < 0.1 && _detectedFps > 1.0)
                    {
                        _measuredFps = _detectedFps;
                    }

                    long elapsed = now - _lastFpsTick;
                    if (elapsed >= 1000)
                    {
                        double raw = (_decodedFrameCounter * 1000.0) / elapsed;
                        _measuredFps = SnapToBroadcastRate(raw, _detectedFps);
                        _decodedFrameCounter = 0;
                        _lastFpsTick = now;
                    }

                    // Phát sự kiện chứa mốc thời gian PTS 90kHz và duration
                    FrameDecodedWithPts?.Invoke(chIdx, frameBytes, w, h, pts, duration);
                    // Duy trì sự kiện cũ để tương thích ngược
                    FrameDecoded?.Invoke(chIdx, frameBytes, w, h);
                };

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _demuxer.ReadLoopAsync(stdout, token).ConfigureAwait(false);
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
            FeedData(data, length);
            await Task.CompletedTask;
        }

        public void FeedData(byte[] data, int length)
        {
            if (!_isRunning || _inputChannel == null || data == null || length <= 0) return;

            try
            {
                byte[] chunk = new byte[length];
                Buffer.BlockCopy(data, 0, chunk, 0, length);
                _inputChannel.Writer.TryWrite(chunk);
            }
            catch { }
        }

        public void Flush()
        {
            // Handled asynchronously in decoupled pump
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

                try { _inputChannel?.Writer.TryComplete(); } catch { }
                _inputChannel = null;

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

                try { _demuxer?.Dispose(); } catch { }
                _demuxer = null;

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
