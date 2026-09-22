using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using OpenMedia.Platform;
using OpenMedia.Platform.Models;

namespace SRT_DECODE
{
    /// <summary>
    /// Dedicated output worker managing NDI, SDI, SRT Bridge, and File Recording
    /// for a single broadcast channel (Master Program or ISO Cam 1..10).
    /// </summary>
    public sealed class BroadcastOutputWorker : IDisposable
    {
        private readonly int _channelIndex; // -1 for Master PGM, 0..9 for ISO cams
        private readonly string _channelName;

        // ─── NDI Engine ─────────────────────────────────────────────
        private NdiNativeSender? _ndiSender;
        private readonly object _ndiLock = new();

        // ─── SDI Engine ─────────────────────────────────────────────
        private Process? _sdiProcess;
        private Stream? _sdiVideoStdin;
        private CancellationTokenSource? _sdiCts;
        private readonly object _sdiLock = new();

        // ─── SRT Bridge Engine ──────────────────────────────────────
        private Process? _srtProcess;
        private Stream? _srtVideoStdin;
        private NamedPipeServerStream? _srtAudioPipe;
        private CancellationTokenSource? _srtCts;
        private readonly object _srtLock = new();

        // ─── File Recording Engine ──────────────────────────────────
        private Process? _recProcess;
        private Stream? _recVideoStdin;
        private NamedPipeServerStream? _recAudioPipe;
        private CancellationTokenSource? _recCts;
        private readonly object _recLock = new();
        private string? _currentRecFilePath;
        private DateTime _recStartTime = DateTime.MinValue;
        private ulong _recBytes = 0;

        public string ChannelName => _channelName;
        public int ChannelIndex => _channelIndex;
        public bool IsNdiActive => _ndiSender != null && _ndiSender.IsRunning;
        public bool IsSdiActive => _sdiProcess != null && !_sdiProcess.HasExited;
        public bool IsSrtBridgeActive => _srtProcess != null && !_srtProcess.HasExited;
        public bool IsRecordingActive => _recProcess != null && !_recProcess.HasExited;

        public TimeSpan RecordingDuration => IsRecordingActive && _recStartTime != DateTime.MinValue
            ? DateTime.UtcNow - _recStartTime
            : TimeSpan.Zero;

        public ulong RecordedBytes
        {
            get
            {
                if (IsRecordingActive && !string.IsNullOrEmpty(_currentRecFilePath))
                {
                    try
                    {
                        var fi = new FileInfo(_currentRecFilePath);
                        if (fi.Exists) return (ulong)fi.Length;
                    }
                    catch { }
                }
                return _recBytes;
            }
        }

        public event Action<string, string>? LogEmitted;

        public BroadcastOutputWorker(int channelIndex, string channelName)
        {
            _channelIndex = channelIndex;
            _channelName = channelName;
        }

        #region NDI Output

        public bool StartNdi(string streamName)
        {
            lock (_ndiLock)
            {
                StopNdi();
                _ndiSender = new NdiNativeSender(streamName);
                _ndiSender.LogEmitted += (tag, msg) => Log(tag, msg);
                return _ndiSender.Start();
            }
        }

        public void StopNdi()
        {
            lock (_ndiLock)
            {
                if (_ndiSender != null)
                {
                    _ndiSender.Stop();
                    _ndiSender.Dispose();
                    _ndiSender = null;
                }
            }
        }

        #endregion

        #region SDI Output (DeckLink / Hardware)

        public bool StartSdi(string sdiDevice, string sdiMode, int width = 1920, int height = 1080, double fps = 59.94)
        {
            lock (_sdiLock)
            {
                StopSdi();

                try
                {
                    _sdiCts = new CancellationTokenSource();
                    var rational = BroadcastFrameRates.SnapToRational(fps > 0 ? fps : 59.94);
                    string fpsArg = BroadcastFrameRates.FormatFfmpeg(rational);

                    // Clean device name
                    string cleanDevice = SdiHardwareScanner.CleanDeviceName(sdiDevice);
                    if (string.IsNullOrWhiteSpace(cleanDevice) || cleanDevice.Contains("Không phát hiện", StringComparison.OrdinalIgnoreCase))
                    {
                        Log("[WARN]", "Chưa chọn thiết bị SDI output hợp lệ.");
                        return false;
                    }

                    // FFmpeg command to DeckLink sink or DirectShow video renderer
                    // -f decklink -pix_fmt uyvy422 "DeckLink Port"
                    string args = $"-hide_banner -loglevel warning -f rawvideo -pix_fmt bgra -s {width}x{height} -r {fpsArg} -i pipe:0 -pix_fmt uyvy422 -f decklink \"{cleanDevice}\"";

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
                        Log("[WARN]", $"Không thể mở cổng phát SDI phần cứng: '{cleanDevice}'. Thiết bị có thể đang bận hoặc chưa kết nối.");
                        return false;
                    }

                    _sdiVideoStdin = _sdiProcess.StandardInput.BaseStream;
                    var proc = _sdiProcess;
                    var token = _sdiCts.Token;

                    // Drain stderr
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
                                    Log("[SDI-ERR]", $"[{_channelName}] {line}");
                                }
                            }
                        }
                        catch { }
                    }, token);

                    Log("[SDI]", $"✅ Đã mở cổng phát SDI: {cleanDevice} ({width}x{height} @ {fpsArg} fps)");
                    return true;
                }
                catch (Exception ex)
                {
                    Log("[ERROR]", $"Lỗi khởi động SDI output [{_channelName}]: {ex.Message}");
                    return false;
                }
            }
        }

        public void StopSdi()
        {
            lock (_sdiLock)
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

                    Log("[SDI]", $"Đã dừng cổng phát SDI cho {_channelName}.");
                }
                catch { }
            }
        }

        #endregion

        #region SRT Re-transmitter Bridge

        public bool StartSrtBridge(string host, int port, string codec, int bitrateKbps, int width = 1920, int height = 1080, double fps = 59.94, SRTMode mode = SRTMode.Caller)
        {
            lock (_srtLock)
            {
                StopSrtBridge();

                try
                {
                    _srtCts = new CancellationTokenSource();
                    var token = _srtCts.Token;

                    var rational = BroadcastFrameRates.SnapToRational(fps > 0 ? fps : 59.94);
                    string fpsArg = BroadcastFrameRates.FormatFfmpeg(rational);
                    int gop = (int)Math.Round((rational.num / (double)rational.den) * 2.0);
                    string pipeGuid = Guid.NewGuid().ToString("N").Substring(0, 8);
                    string audioPipeName = $"ome_srt_a_{_channelIndex}_{pipeGuid}";

                    // Map codec string
                    string vcodec = "h264_nvenc";
                    if (codec.Contains("HEVC", StringComparison.OrdinalIgnoreCase) || codec.Contains("265", StringComparison.OrdinalIgnoreCase))
                    {
                        vcodec = "hevc_nvenc";
                    }
                    else if (codec.Contains("Passthrough", StringComparison.OrdinalIgnoreCase))
                    {
                        vcodec = "copy";
                    }

                    int b = bitrateKbps > 0 ? bitrateKbps : 8000;
                    string srtUri = mode == SRTMode.Listener 
                        ? $"srt://0.0.0.0:{port}?mode=listener&latency=200000"
                        : $"srt://{host}:{port}?mode=caller&latency=200000";

                    // Named pipe for audio
                    _srtAudioPipe = new NamedPipeServerStream(audioPipeName, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                    // FFmpeg command reading video from pipe:0 and audio from named pipe
                    string args = $"-hide_banner -loglevel warning -y " +
                                  $"-f rawvideo -pix_fmt bgra -s {width}x{height} -r {fpsArg} -i pipe:0 " +
                                  $"-f s16le -ar 48000 -ac 2 -i \\\\.\\pipe\\{audioPipeName} " +
                                  $"-c:v {vcodec} -b:v {b}k -maxrate {b}k -bufsize {b * 2}k -pix_fmt yuv420p -g {gop} " +
                                  $"-c:a aac -b:a 192k -ar 48000 " +
                                  $"-f mpegts \"{srtUri}\"";

                    var psi = new ProcessStartInfo
                    {
                        FileName = "ffmpeg",
                        Arguments = args,
                        RedirectStandardInput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    _srtProcess = Process.Start(psi);
                    if (_srtProcess == null || _srtProcess.HasExited)
                    {
                        Log("[ERROR]", $"Không thể khởi chạy SRT Re-transmitter cho {_channelName} tới {srtUri}");
                        return false;
                    }

                    _srtVideoStdin = _srtProcess.StandardInput.BaseStream;
                    var proc = _srtProcess;

                    // Drain stderr
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            using var reader = proc.StandardError;
                            while (!token.IsCancellationRequested && !proc.HasExited)
                            {
                                string? line = await reader.ReadLineAsync().ConfigureAwait(false);
                                if (line == null) break;
                                if (line.Contains("error", StringComparison.OrdinalIgnoreCase) || line.Contains("failed", StringComparison.OrdinalIgnoreCase))
                                {
                                    Log("[SRT-BRIDGE-ERR]", $"[{_channelName}] {line}");
                                }
                            }
                        }
                        catch { }
                    }, token);

                    // Connect audio pipe
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _srtAudioPipe.WaitForConnectionAsync(token).ConfigureAwait(false);
                        }
                        catch { }
                    }, token);

                    Log("[BRIDGE]", $"✅ SRT Re-transmitter đã kích hoạt: {_channelName} ➔ {srtUri} ({vcodec} @ {b} kbps - Mode: {mode})");
                    return true;
                }
                catch (Exception ex)
                {
                    Log("[ERROR]", $"Lỗi khởi động SRT Re-transmitter [{_channelName}]: {ex.Message}");
                    return false;
                }
            }
        }

        public void StopSrtBridge()
        {
            lock (_srtLock)
            {
                try
                {
                    _srtCts?.Cancel();

                    if (_srtProcess != null && !_srtProcess.HasExited)
                    {
                        try { _srtProcess.Kill(entireProcessTree: true); _srtProcess.WaitForExit(100); } catch { }
                        try { _srtProcess.Dispose(); } catch { }
                        _srtProcess = null;
                    }

                    try { _srtVideoStdin?.Close(); } catch { }
                    _srtVideoStdin = null;

                    if (_srtAudioPipe != null)
                    {
                        try { _srtAudioPipe.Dispose(); } catch { }
                        _srtAudioPipe = null;
                    }

                    try { _srtCts?.Dispose(); } catch { }
                    _srtCts = null;

                    Log("[BRIDGE]", $"Đã dừng SRT Re-transmitter cho {_channelName}.");
                }
                catch { }
            }
        }

        #endregion

        #region File Recording

        public bool StartRecording(string folder, string format, VideoCodecConfig codecConfig, int width = 1920, int height = 1080, double fps = 59.94)
        {
            lock (_recLock)
            {
                StopRecording();

                try
                {
                    if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                    {
                        folder = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
                        Directory.CreateDirectory(folder);
                    }

                    _recCts = new CancellationTokenSource();
                    var token = _recCts.Token;

                    string ext = "mp4";
                    if (format.Contains("MOV", StringComparison.OrdinalIgnoreCase) || format.Contains("ProRes", StringComparison.OrdinalIgnoreCase))
                    {
                        ext = "mov";
                    }
                    else if (format.Contains("TS", StringComparison.OrdinalIgnoreCase))
                    {
                        ext = "ts";
                    }

                    string filename = $"OME_REC_{_channelName.Replace(" ", "_")}_{DateTime.Now:yyyyMMdd_HHmmss}.{ext}";
                    _currentRecFilePath = Path.Combine(folder, filename);
                    _recStartTime = DateTime.UtcNow;
                    _recBytes = 0;

                    var rational = BroadcastFrameRates.SnapToRational(fps > 0 ? fps : 59.94);
                    string fpsArg = BroadcastFrameRates.FormatFfmpeg(rational);
                    string pipeGuid = Guid.NewGuid().ToString("N").Substring(0, 8);
                    string audioPipeName = $"ome_rec_a_{_channelIndex}_{pipeGuid}";

                    // Determine video codec arguments
                    string vArgs;
                    string aArgs = "-c:a aac -b:a 192k -ar 48000";

                    int bitrate = 8000;
                    if (int.TryParse(codecConfig.Bitrate.Split(' ')[0], out int bVal))
                    {
                        bitrate = bVal * 1000;
                    }

                    if (ext == "mov" || codecConfig.Codec.Contains("ProRes", StringComparison.OrdinalIgnoreCase))
                    {
                        vArgs = "-c:v prores_ks -profile:v 3 -pix_fmt yuv422p10le";
                        aArgs = "-c:a pcm_s16le -ar 48000";
                    }
                    else if (codecConfig.Codec.Contains("HEVC", StringComparison.OrdinalIgnoreCase) || codecConfig.Codec.Contains("265", StringComparison.OrdinalIgnoreCase))
                    {
                        vArgs = $"-c:v hevc_nvenc -b:v {bitrate}k -maxrate {bitrate}k -bufsize {bitrate * 2}k -pix_fmt yuv420p";
                    }
                    else if (ext == "ts")
                    {
                        vArgs = $"-c:v h264_nvenc -b:v {bitrate}k -maxrate {bitrate}k -bufsize {bitrate * 2}k -pix_fmt yuv420p -f mpegts";
                    }
                    else
                    {
                        // Default MP4 H.264
                        vArgs = $"-c:v h264_nvenc -b:v {bitrate}k -maxrate {bitrate}k -bufsize {bitrate * 2}k -pix_fmt yuv420p -movflags +faststart";
                    }

                    _recAudioPipe = new NamedPipeServerStream(audioPipeName, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                    string args = $"-hide_banner -loglevel warning -y " +
                                  $"-f rawvideo -pix_fmt bgra -s {width}x{height} -r {fpsArg} -i pipe:0 " +
                                  $"-f s16le -ar 48000 -ac 2 -i \\\\.\\pipe\\{audioPipeName} " +
                                  $"{vArgs} {aArgs} \"{_currentRecFilePath}\"";

                    var psi = new ProcessStartInfo
                    {
                        FileName = "ffmpeg",
                        Arguments = args,
                        RedirectStandardInput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    _recProcess = Process.Start(psi);
                    if (_recProcess == null || _recProcess.HasExited)
                    {
                        Log("[ERROR]", $"Không thể khởi chạy bộ ghi file FFmpeg cho {_channelName}: {_currentRecFilePath}");
                        return false;
                    }

                    _recVideoStdin = _recProcess.StandardInput.BaseStream;
                    var proc = _recProcess;

                    // Drain stderr
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
                                    Log("[REC-ERR]", $"[{_channelName}] {line}");
                                }
                            }
                        }
                        catch { }
                    }, token);

                    // Connect audio pipe
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _recAudioPipe.WaitForConnectionAsync(token).ConfigureAwait(false);
                        }
                        catch { }
                    }, token);

                    Log("[REC]", $"💾 BẮT ĐẦU GHI LƯU: {_currentRecFilePath} ({format})");
                    return true;
                }
                catch (Exception ex)
                {
                    Log("[ERROR]", $"Lỗi khởi động ghi file [{_channelName}]: {ex.Message}");
                    return false;
                }
            }
        }

        public void StopRecording()
        {
            lock (_recLock)
            {
                if (_recProcess == null && _currentRecFilePath == null) return;

                try
                {
                    _recCts?.Cancel();

                    // Closing stdin cleanly allows FFmpeg to write container trailer (moov atom)
                    if (_recVideoStdin != null)
                    {
                        try { _recVideoStdin.Flush(); _recVideoStdin.Close(); } catch { }
                        _recVideoStdin = null;
                    }

                    if (_recAudioPipe != null)
                    {
                        try { _recAudioPipe.Flush(); _recAudioPipe.Dispose(); } catch { }
                        _recAudioPipe = null;
                    }

                    if (_recProcess != null)
                    {
                        try
                        {
                            if (!_recProcess.WaitForExit(300))
                            {
                                _recProcess.Kill(entireProcessTree: true);
                            }
                        }
                        catch { }
                        try { _recProcess.Dispose(); } catch { }
                        _recProcess = null;
                    }

                    try { _recCts?.Dispose(); } catch { }
                    _recCts = null;

                    var duration = RecordingDuration;
                    ulong size = RecordedBytes;
                    Log("[REC]", $"💾 ĐÃ DỪNG GHI FILE {_channelName}. File: {_currentRecFilePath} ({size / (1024.0 * 1024.0):F2} MB, {duration:hh\\:mm\\:ss})");

                    _recStartTime = DateTime.MinValue;
                    _currentRecFilePath = null;
                }
                catch (Exception ex)
                {
                    Log("[WARN]", $"Lỗi dừng ghi file [{_channelName}]: {ex.Message}");
                }
            }
        }

        #endregion

        #region Frame & Audio Ingestion

        public void FeedNdiVideo(byte[] bgraBytes, int width, int height, double fps = 59.94)
        {
            if (bgraBytes == null || bgraBytes.Length == 0) return;
            if (_ndiSender != null && _ndiSender.IsRunning)
            {
                _ndiSender.SendVideoFrame(bgraBytes, width, height, fps);
            }
        }

        public void FeedVideoFrame(byte[] bgraBytes, int width, int height, double fps = 59.94, bool sendToNdi = true)
        {
            if (bgraBytes == null || bgraBytes.Length == 0) return;

            // 1. NDI Output
            if (sendToNdi && _ndiSender != null && _ndiSender.IsRunning)
            {
                _ndiSender.SendVideoFrame(bgraBytes, width, height, fps);
            }

            // 2. SDI Output
            if (_sdiVideoStdin != null && _sdiProcess != null && !_sdiProcess.HasExited)
            {
                try
                {
                    _sdiVideoStdin.Write(bgraBytes, 0, bgraBytes.Length);
                    _sdiVideoStdin.Flush();
                }
                catch { }
            }

            // 3. SRT Bridge Output
            if (_srtVideoStdin != null && _srtProcess != null && !_srtProcess.HasExited)
            {
                try
                {
                    _srtVideoStdin.Write(bgraBytes, 0, bgraBytes.Length);
                    _srtVideoStdin.Flush();
                }
                catch { }
            }

            // 4. File Recording Output
            if (_recVideoStdin != null && _recProcess != null && !_recProcess.HasExited)
            {
                try
                {
                    _recVideoStdin.Write(bgraBytes, 0, bgraBytes.Length);
                    _recVideoStdin.Flush();
                }
                catch { }
            }
        }

        public void FeedAudioPcm(byte[] pcmBytes, int count, int sampleRate = 48000, int channels = 2)
        {
            if (pcmBytes == null || count <= 0) return;

            // 1. NDI Output
            if (_ndiSender != null && _ndiSender.IsRunning)
            {
                _ndiSender.SendAudioFrame(pcmBytes, count, sampleRate, channels);
            }

            // 2. SRT Bridge Audio Pipe
            if (_srtAudioPipe != null && _srtAudioPipe.IsConnected)
            {
                try
                {
                    _srtAudioPipe.Write(pcmBytes, 0, count);
                    _srtAudioPipe.Flush();
                }
                catch { }
            }

            // 3. File Recording Audio Pipe
            if (_recAudioPipe != null && _recAudioPipe.IsConnected)
            {
                try
                {
                    _recAudioPipe.Write(pcmBytes, 0, count);
                    _recAudioPipe.Flush();
                }
                catch { }
            }
        }

        #endregion

        private void Log(string tag, string message)
        {
            LogEmitted?.Invoke(tag, message);
            Trace.WriteLine($"[BroadcastOutputWorker]{tag} {message}");
        }

        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopNdi();
            StopSdi();
            StopSrtBridge();
            StopRecording();
        }
    }
}
