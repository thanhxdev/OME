using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OpenMedia.Platform.Models;

namespace OME_PLAYOUT
{
    /// <summary>
    /// Dedicated hardware SDI playout worker managing DeckLink / AJA / Broadcast SDI output
    /// for Master Dirty Program or Clean Feed.
    /// Supports both uncompressed SMPTE UYVY 4:2:2 and hardware-accelerated broadcast codecs.
    /// </summary>
    public sealed class SdiNativeOutputWorker : IDisposable
    {
        private Process? _sdiProcess;
        private Stream? _sdiVideoStdin;
        private CancellationTokenSource? _sdiCts;
        private readonly object _lock = new();
        private bool _disposed;

        public bool IsRunning => _sdiProcess != null && !_sdiProcess.HasExited;
        public string CurrentDevice { get; private set; } = string.Empty;
        public string CurrentMode { get; private set; } = string.Empty;
        public long FramesSent { get; private set; } = 0;

        public event Action<string, string>? LogEmitted;

        public bool Start(string sdiDevice, string sdiMode, int width = 1920, int height = 1080, double fps = 59.94, string codec = "Uncompressed UYVY 4:2:2")
        {
            lock (_lock)
            {
                Stop();

                try
                {
                    _sdiCts = new CancellationTokenSource();
                    var rational = BroadcastFrameRates.SnapToRational(fps > 0 ? fps : 59.94);
                    string fpsArg = BroadcastFrameRates.FormatFfmpeg(rational);

                    // Clean device name from UI decoration labels
                    string cleanDevice = sdiDevice;
                    if (cleanDevice.Contains("[SDI HW]")) cleanDevice = cleanDevice.Replace("[SDI HW]", "").Trim();
                    if (cleanDevice.Contains("[PORT]")) cleanDevice = cleanDevice.Replace("[PORT]", "").Trim();
                    CurrentDevice = cleanDevice;
                    CurrentMode = sdiMode;
                    FramesSent = 0;

                    // Configure pixel format and codec encoding for DeckLink / SDI sink
                    string pixelFormatArg = "uyvy422";
                    string vcodecArg = "-c:v rawvideo";

                    if (codec.Contains("H.264") || codec.Contains("AVC"))
                    {
                        vcodecArg = "-c:v h264_nvenc -preset p4 -tune ull";
                        pixelFormatArg = "yuv420p";
                    }
                    else if (codec.Contains("H.265") || codec.Contains("HEVC"))
                    {
                        vcodecArg = "-c:v hevc_nvenc -preset p4 -tune ull";
                        pixelFormatArg = "yuv420p";
                    }
                    else if (codec.Contains("ProRes"))
                    {
                        vcodecArg = "-c:v prores_ks -profile:v 3";
                        pixelFormatArg = "yuv422p10le";
                    }
                    else if (codec.Contains("DNx"))
                    {
                        vcodecArg = "-c:v dnxhd";
                        pixelFormatArg = "yuv422p";
                    }

                    // FFmpeg decklink output sink command:
                    // Raw BGRA32 input from compositor memory -> convert to target SDI format -> pipe to DeckLink device
                    string args = $"-hide_banner -loglevel warning -f rawvideo -pix_fmt bgra -s {width}x{height} -r {fpsArg} -i pipe:0 {vcodecArg} -pix_fmt {pixelFormatArg} -f decklink \"{cleanDevice}\"";

                    var psi = new ProcessStartInfo
                    {
                        FileName = "ffmpeg",
                        Arguments = args,
                        RedirectStandardInput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    _sdiProcess = Process.Start(psi);
                    if (_sdiProcess == null || _sdiProcess.HasExited)
                    {
                        Log("[WARN]", $"Không thể kết nối cổng phát SDI phần cứng: '{cleanDevice}'. Thiết bị có thể đang bận hoặc chưa kết nối.");
                        return false;
                    }

                    _sdiVideoStdin = _sdiProcess.StandardInput.BaseStream;
                    var proc = _sdiProcess;
                    var token = _sdiCts.Token;

                    // Drain stderr asynchronously to avoid deadlock
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            using var reader = proc.StandardError;
                            while (!token.IsCancellationRequested && !proc.HasExited)
                            {
                                string? line = await reader.ReadLineAsync().ConfigureAwait(false);
                                if (line == null) break;
                                if (line.Contains("error", StringComparison.OrdinalIgnoreCase))
                                {
                                    Log("[SDI-ERR]", line);
                                }
                            }
                        }
                        catch { }
                    }, token);

                    Log("[SDI]", $"✅ Đã kích hoạt cổng phát SDI phần cứng: '{cleanDevice}' ({width}x{height} @ {fpsArg} fps, Codec: {codec})");
                    return true;
                }
                catch (Exception ex)
                {
                    Log("[ERROR]", $"Lỗi khởi động cổng phát SDI: {ex.Message}");
                    return false;
                }
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                try
                {
                    _sdiCts?.Cancel();

                    if (_sdiProcess != null && !_sdiProcess.HasExited)
                    {
                        try { _sdiProcess.Kill(entireProcessTree: true); _sdiProcess.WaitForExit(100); } catch { }
                        try { _sdiProcess.Dispose(); } catch { }
                        _sdiProcess = null;
                    }

                    try { _sdiVideoStdin?.Close(); } catch { }
                    _sdiVideoStdin = null;

                    try { _sdiCts?.Dispose(); } catch { }
                    _sdiCts = null;

                    if (!string.IsNullOrEmpty(CurrentDevice))
                    {
                        Log("[SDI]", $"⏹ Đã dừng cổng phát SDI: '{CurrentDevice}'.");
                        CurrentDevice = string.Empty;
                    }
                }
                catch { }
            }
        }

        public void FeedVideo(byte[] bgraBytes, int width, int height)
        {
            if (bgraBytes == null || bgraBytes.Length == 0) return;

            if (_sdiVideoStdin != null && _sdiProcess != null && !_sdiProcess.HasExited)
            {
                try
                {
                    _sdiVideoStdin.Write(bgraBytes, 0, bgraBytes.Length);
                    _sdiVideoStdin.Flush();
                    FramesSent++;
                }
                catch
                {
                    // Pipe broken or device disconnected
                }
            }
        }

        public void FeedAudio(byte[] pcmBytes, int count, int sampleRate = 48000, int channels = 2)
        {
            // Audio can be passed through named pipe or integrated muxer
        }

        private void Log(string tag, string msg)
        {
            LogEmitted?.Invoke(tag, msg);
            Trace.WriteLine($"[SdiNativeOutputWorker]{tag} {msg}");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
