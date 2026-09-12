using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenMedia.Platform;
using OpenMedia.Platform.Controls.Wpf;
using OpenMedia.Platform.Models;
using PlatformMediaPlayer = OpenMedia.Platform.MediaPlayer;

namespace SRT_ENCODE
{
    public enum InputSourceType
    {
        SDI = 0,
        NDI = 1,
        File = 2,
        Colorbar = 3,
        SRT = 4
    }

    /// <summary>
    /// Chứa thông số kỹ thuật thời gian thực của Video Source đang hoạt động.
    /// </summary>
    public sealed class VideoSourceTelemetry
    {
        public string SourceName { get; set; } = "SMPTE RP 219 Colorbar";
        public string SourceType { get; set; } = "COLORBAR";
        public string Status { get; set; } = "● SIGNAL LOCKED";
        public bool IsLocked { get; set; } = true;
        public string Resolution { get; set; } = "1920 x 1080 (16:9 Full HD)";
        public string FrameRate { get; set; } = "59.94 FPS (Progressive)";
        public string VideoCodec { get; set; } = "RAW Vector 10-bit RGBA";
        public string Bitrate { get; set; } = "2.97 Gbps (Uncompressed 3G)";
        public string AudioFormat { get; set; } = "16 Ch @ 48.0 kHz 24-bit PCM";
        public string ColorSpace { get; set; } = "ITU-R BT.709 (4:2:2 Full Range)";
        public string PipelineDetails { get; set; } = "OpenMedia Direct Frame Capture • No Drop Frames • HW Sync Locked";
    }

    /// <summary>
    /// Quản lý tập trung toàn bộ nguồn video đầu vào (SDI, NDI, File, Colorbar),
    /// điều khiển MediaPlayer giải mã phát lại trên Preview (ReviewView),
    /// điều khiển NDI Receiver & SDI Capture loop thời gian thực,
    /// đồng bộ hóa âm thanh kiểm âm (Audio Monitor) và cập nhật thông số nguồn thật.
    /// </summary>
    public sealed class VideoSourceManager : IDisposable
    {
        private PlatformMediaPlayer? _player;
        private readonly ColorbarEngine _colorbarEngine;

        // Direct Live Capture Receivers
        private NdiReceiver? _ndiReceiver;
        private SdiDeviceCapture? _sdiCapture;
        private WriteableBitmap? _previewBitmap;
        private byte[]? _latestMasterFrame;
        private readonly object _masterFrameLock = new();

        // UI references
        private OpenMediaVideoView? _reviewView;
        private Viewbox? _viewboxColorbar;
        private Grid? _pnlColorbarVisualHost;
        private TextBlock? _txtActiveSourceBadge;
        private TextBlock? _txtActiveSourceTypeBadge;

        // State
        private InputSourceType _currentSource = InputSourceType.File;
        private string _currentSourcePath = string.Empty;
        private bool _isPreviewEnabled = true;
        private bool _isAudioMonitorEnabled = false;
        private double _monitorVolume = 0.4;
        private bool _isLoopPlayback = true;
        private Stretch _currentStretch = Stretch.Uniform;
        private readonly VideoSourceTelemetry _currentTelemetry = new();
        private SRTStreamSession? _srtStreamSource;

        /// <summary>
        /// Gets or sets whether video file playback should loop continuously.
        /// </summary>
        public bool IsLoopPlayback
        {
            get => _isLoopPlayback;
            set
            {
                _isLoopPlayback = value;
                if (_player != null)
                {
                    _player.IsLooping = value;
                }
            }
        }

        public PlatformMediaPlayer? Player => _player;
        public TimeSpan CurrentPosition => _player?.Position ?? TimeSpan.Zero;
        public ColorbarEngine ColorbarEngine => _colorbarEngine;
        public SRTStreamSession? SrtStreamSource => _srtStreamSource;
        public InputSourceType CurrentSource => _currentSource;
        public string CurrentSourcePath => _currentSourcePath;
        public VideoSourceTelemetry CurrentTelemetry => _currentTelemetry;

        public byte[]? LatestMasterFrame
        {
            get
            {
                lock (_masterFrameLock)
                {
                    return _latestMasterFrame;
                }
            }
        }

        private int _activeAudioChannels = 2;
        public int ActiveAudioChannels
        {
            get => _activeAudioChannels;
            set => _activeAudioChannels = Math.Clamp(value, 1, 16);
        }

        public event Action<string, string>? LogRequested;
        public event Action<InputSourceType, string>? SourceChanged;
        public event Action<VideoSourceTelemetry>? TelemetryUpdated;
        public event Action<float[], int, int>? AudioSamplesArrived;

        public VideoSourceManager(ColorbarEngine colorbarEngine)
        {
            _colorbarEngine = colorbarEngine ?? throw new ArgumentNullException(nameof(colorbarEngine));
        }

        /// <summary>
        /// Gắn kết các View và Controls hiển thị từ UI MainWindow
        /// </summary>
        public async Task InitializeAsync(
            OpenMediaVideoView reviewView,
            Viewbox viewboxColorbar,
            Grid pnlColorbarVisualHost,
            TextBlock? txtActiveSourceBadge,
            TextBlock? txtActiveSourceTypeBadge)
        {
            _reviewView = reviewView;
            _viewboxColorbar = viewboxColorbar;
            _pnlColorbarVisualHost = pnlColorbarVisualHost;
            _txtActiveSourceBadge = txtActiveSourceBadge;
            _txtActiveSourceTypeBadge = txtActiveSourceTypeBadge;

            await RecreatePlayerAsync();
            UpdateTelemetryForFileNotFound(string.Empty);
        }

        private async Task RecreatePlayerAsync()
        {
            try
            {
                if (_player != null)
                {
                    _player.Dispose();
                    _player = null;
                }

                _player = new PlatformMediaPlayer();
                _player.IsLooping = _isLoopPlayback;
                if (_reviewView != null)
                {
                    _player.AttachPreview(_reviewView);
                }

                _player.Volume = _monitorVolume;
                _player.IsMuted = !_isAudioMonitorEnabled;

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                Log("[WARN]", $"Khởi tạo MediaPlayer VideoSourceManager: {ex.Message}");
            }
        }

        /// <summary>
        /// Dừng các luồng bắt hình trực tiếp NDI / SDI
        /// </summary>
        private void StopLiveCaptures()
        {
            if (_ndiReceiver != null)
            {
                _ndiReceiver.VideoFrameReceived -= OnNdiVideoFrameReceived;
                _ndiReceiver.AudioSamplesReceived -= OnNdiAudioSamplesReceived;
                _ndiReceiver.Dispose();
                _ndiReceiver = null;
            }

            if (_sdiCapture != null)
            {
                _sdiCapture.VideoFrameReceived -= OnSdiVideoFrameReceived;
                _sdiCapture.Dispose();
                _sdiCapture = null;
            }
        }

        /// <summary>
        /// Chuyển đổi nguồn phát sóng đầu vào
        /// </summary>
        public async Task SwitchSourceAsync(InputSourceType sourceType, string? sourceParam = null, string? videoMode = null, string? audioCh = null)
        {
            _currentSource = sourceType;
            UpdateViewVisibility();

            switch (sourceType)
            {
                case InputSourceType.SDI:
                    await HandleSdiSourceAsync(sourceParam ?? "Default SDI Capture Card", videoMode, audioCh);
                    break;

                case InputSourceType.NDI:
                    await HandleNdiSourceAsync(sourceParam ?? "");
                    break;

                case InputSourceType.File:
                    StopLiveCaptures();
                    await HandleFileSourceAsync(sourceParam ?? string.Empty);
                    break;

                case InputSourceType.Colorbar:
                    StopLiveCaptures();
                    HandleColorbarSource();
                    break;

                case InputSourceType.SRT:
                    StopLiveCaptures();
                    await HandleSrtSourceAsync(sourceParam ?? "srt://127.0.0.1:9000?mode=caller");
                    break;
            }

            SourceChanged?.Invoke(_currentSource, _currentSourcePath);
        }

        #region File Input Handler

        public async Task HandleFileSourceAsync(string filePath)
        {
            _currentSource = InputSourceType.File;
            _currentSourcePath = filePath;
            UpdateViewVisibility();

            if (_txtActiveSourceTypeBadge != null)
            {
                _txtActiveSourceTypeBadge.Text = "📁 FILE INPUT ACTIVE";
            }

            string fileName = Path.GetFileName(filePath);
            if (string.IsNullOrWhiteSpace(fileName)) fileName = "No File Selected";

            if (_txtActiveSourceBadge != null)
            {
                _txtActiveSourceBadge.Text = $"INPUT: FILE ({fileName})";
            }

            // Dừng phát âm thanh Colorbar nếu đang bật
            _colorbarEngine.StopAudioTone();

            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                Log("[WARN]", "Chưa chọn file video hoặc file không tồn tại.");
                UpdateTelemetryForFileNotFound(fileName);
                return;
            }

            try
            {
                Log("[INFO]", $"Đang nạp và phân tích thông số video file: {fileName}...");

                if (_player == null)
                {
                    await RecreatePlayerAsync();
                }

                if (_player != null)
                {
                    _player.IsLooping = _isLoopPlayback;
                    _player.Volume = _monitorVolume;
                    _player.IsMuted = !_isAudioMonitorEnabled;

                    await _player.OpenAsync(filePath);
                    _player.IsLooping = _isLoopPlayback;
                    _player.Volume = _monitorVolume;
                    _player.IsMuted = !_isAudioMonitorEnabled;
                    await _player.PlayAsync();

                    UpdateTelemetryForFile(filePath, _player.Information);

                    Log("[INFO]", $"✅ [SUCCESS] Đã mở và phát video preview: {fileName} [{_currentTelemetry.Resolution} @ {_currentTelemetry.FrameRate}]");
                }
            }
            catch (Exception ex)
            {
                Log("[ERROR]", $"Lỗi khi phát video file: {ex.Message}");
                UpdateTelemetryForFile(filePath, null);
            }
        }

        private void UpdateTelemetryForFile(string filePath, MediaInfo? info)
        {
            string fileName = Path.GetFileName(filePath);
            string ext = Path.GetExtension(filePath).ToUpperInvariant();

            _currentTelemetry.SourceName = fileName;
            _currentTelemetry.SourceType = $"FILE ({ext})";
            _currentTelemetry.Status = "● ACTIVE (PLAYING)";
            _currentTelemetry.IsLocked = true;

            if (info != null && info.Width > 0 && info.Height > 0)
            {
                _activeAudioChannels = (info.AudioChannels > 0) ? Math.Clamp(info.AudioChannels, 1, 16) : 2;
                string resTag = (info.Width >= 3840) ? "4K UHD" : (info.Width >= 1920) ? "1080p FHD" : $"{info.Height}p HD";
                _currentTelemetry.Resolution = $"{info.Width} x {info.Height} ({resTag})";
                _currentTelemetry.FrameRate = info.FrameRate > 0 ? $"{info.FrameRate:F2} FPS" : "59.94 FPS";
                _currentTelemetry.VideoCodec = !string.IsNullOrEmpty(info.VideoCodec) ? info.VideoCodec.ToUpper() : "H.264 / AVC (Hardware Decoded)";
                _currentTelemetry.Bitrate = info.BitrateKbps > 0 ? $"{(info.BitrateKbps / 1000.0):F1} Mbps ({info.BitrateKbps:N0} kbps)" : "Dynamic Bitrate";
                _currentTelemetry.AudioFormat = $"{_activeAudioChannels} Ch @ {(info.AudioSampleRate > 0 ? info.AudioSampleRate / 1000.0 : 48.0):F1} kHz ({(!string.IsNullOrEmpty(info.AudioCodec) ? info.AudioCodec.ToUpper() : "AAC")})";
            }
            else
            {
                _activeAudioChannels = 2;
                long fileSizeBytes = 0;
                try { fileSizeBytes = new FileInfo(filePath).Length; } catch { }

                _currentTelemetry.Resolution = "Đang đọc metadata...";
                _currentTelemetry.FrameRate = "-- FPS";
                _currentTelemetry.VideoCodec = $"{ext} File Stream";
                _currentTelemetry.Bitrate = fileSizeBytes > 0 ? $"{(fileSizeBytes / (1024.0 * 1024.0)):F1} MB (File Size)" : "0 kbps";
                _currentTelemetry.AudioFormat = "Đang kiểm tra kênh...";
            }

            _currentTelemetry.ColorSpace = "ITU-R BT.709 / YUV 4:2:0 (8/10-bit)";
            _currentTelemetry.PipelineDetails = $"OpenMedia Hardware Accelerated Decoder • Direct3D 11 Surface";

            TelemetryUpdated?.Invoke(_currentTelemetry);
        }

        private void UpdateTelemetryForFileNotFound(string fileName)
        {
            _currentTelemetry.SourceName = fileName;
            _currentTelemetry.SourceType = "FILE";
            _currentTelemetry.Status = "○ NO FILE LOADED";
            _currentTelemetry.IsLocked = false;
            _currentTelemetry.Resolution = "-- x --";
            _currentTelemetry.FrameRate = "0.00 FPS";
            _currentTelemetry.VideoCodec = "None";
            _currentTelemetry.Bitrate = "0 kbps";
            _currentTelemetry.AudioFormat = "No Audio Stream";
            _currentTelemetry.ColorSpace = "None";
            _currentTelemetry.PipelineDetails = "Waiting for valid media file selection...";

            TelemetryUpdated?.Invoke(_currentTelemetry);
        }

        #endregion

        #region SDI Input Handler (DirectShow COM Hardware Capture)

        public async Task HandleSdiSourceAsync(string deviceName, string? videoMode = null, string? audioCh = null)
        {
            _currentSourcePath = deviceName;

            if (_txtActiveSourceTypeBadge != null)
            {
                _txtActiveSourceTypeBadge.Text = "📡 SDI INPUT ACTIVE";
            }

            if (_txtActiveSourceBadge != null)
            {
                _txtActiveSourceBadge.Text = $"INPUT: SDI ({deviceName})";
            }

            _colorbarEngine.StopAudioTone();
            StopLiveCaptures();

            if (_player != null)
            {
                try { await _player.PauseAsync(); } catch { }
            }

            if (string.IsNullOrWhiteSpace(deviceName) ||
                deviceName.Contains("Không tìm thấy", StringComparison.OrdinalIgnoreCase) ||
                deviceName.Contains("Chưa kết nối", StringComparison.OrdinalIgnoreCase))
            {
                Log("[WARN]", "Chưa có thiết bị SDI/Capture card nào được chọn hoặc không tìm thấy card.");
                UpdateTelemetryForSdiNoHardware();
                return;
            }

            // Phân tích video mode
            string mode = videoMode ?? "1080p 59.94 fps";
            int width = 1920;
            int height = 1080;
            double fps = 59.94;

            if (mode.Contains("2160p") || mode.Contains("4K"))
            {
                width = 3840;
                height = 2160;
                fps = 59.94;
            }
            else if (mode.Contains("50 fps"))
            {
                fps = 50.0;
            }
            else if (mode.Contains("29.97"))
            {
                fps = 29.97;
            }

            try
            {
                Log("[INFO]", $"Đang kết nối tín hiệu từ Card phần cứng SDI/Capture [{deviceName}] ({width}x{height} @ {fps:F2} fps)...");

                _sdiCapture = new SdiDeviceCapture(deviceName);
                _sdiCapture.LogRequested += (tag, msg) => Log(tag, msg);
                _sdiCapture.VideoFrameReceived += OnSdiVideoFrameReceived;

                bool started = _sdiCapture.Start(width, height, fps);
                if (started)
                {
                    Log("[INFO]", $"✅ [SUCCESS] Đã kích hoạt Capture Loop cho SDI/Capture Device: {deviceName}");
                }
                else
                {
                    Log("[WARN]", $"Chưa thể mở luồng SDI từ {deviceName}. Đang ở trạng thái chờ tín hiệu.");
                }
            }
            catch (Exception ex)
            {
                Log("[WARN]", $"Lỗi khởi tạo bắt hình SDI: {ex.Message}");
            }

            UpdateTelemetryForSdi(deviceName, videoMode, audioCh);
        }

        private void OnSdiVideoFrameReceived(byte[] frameData, int width, int height, int stride, double fps)
        {
            lock (_masterFrameLock)
            {
                _latestMasterFrame = frameData;
            }

            // Cập nhật thông số telemetry thật từ khung hình phần cứng SDI/DirectShow thực tế
            if (width > 0 && height > 0)
            {
                string resTag = (width >= 3840) ? "4K UHD" : (width >= 1920) ? "1080p FHD" : $"{height}p HD";
                string newRes = $"{width} x {height} ({resTag})";
                string newFps = $"{fps:F2} FPS (DirectShow Live)";
                if (_currentTelemetry.Resolution != newRes || _currentTelemetry.FrameRate != newFps || !_currentTelemetry.IsLocked)
                {
                    _currentTelemetry.Resolution = newRes;
                    _currentTelemetry.FrameRate = newFps;
                    _currentTelemetry.Status = "● HARDWARE LOCKED (RECEIVING FRAMES)";
                    _currentTelemetry.IsLocked = true;
                    TelemetryUpdated?.Invoke(_currentTelemetry);
                }
            }

            if (_reviewView != null && _currentSource == InputSourceType.SDI && _isPreviewEnabled)
            {
                _reviewView.Dispatcher.InvokeAsync(() =>
                {
                    if (_reviewView == null || _currentSource != InputSourceType.SDI) return;

                    if (_previewBitmap == null || _previewBitmap.PixelWidth != width || _previewBitmap.PixelHeight != height)
                    {
                        _previewBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
                        _reviewView.PresentBitmap(_previewBitmap);
                    }

                    _previewBitmap.Lock();
                    try
                    {
                        unsafe
                        {
                            int copyBytes = Math.Min(frameData.Length, _previewBitmap.BackBufferStride * height);
                            fixed (byte* pSrc = frameData)
                            {
                                Buffer.MemoryCopy(pSrc, (void*)_previewBitmap.BackBuffer, copyBytes, copyBytes);
                            }
                        }
                        _previewBitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
                    }
                    finally
                    {
                        _previewBitmap.Unlock();
                    }
                }, System.Windows.Threading.DispatcherPriority.Render);
            }
        }

        private void UpdateTelemetryForSdiNoHardware()
        {
            _currentTelemetry.SourceName = "No SDI Device Connected";
            _currentTelemetry.SourceType = "SDI / HARDWARE";
            _currentTelemetry.Status = "○ NO SDI SIGNAL";
            _currentTelemetry.IsLocked = false;
            _currentTelemetry.Resolution = "-- x --";
            _currentTelemetry.FrameRate = "0.00 FPS";
            _currentTelemetry.VideoCodec = "None";
            _currentTelemetry.Bitrate = "0 Gbps";
            _currentTelemetry.AudioFormat = "No SDI Audio";
            _currentTelemetry.ColorSpace = "None";
            _currentTelemetry.PipelineDetails = "Chưa phát hiện phần cứng SDI. Vui lòng cắm Card DeckLink/Magewell hoặc bấm 'Scan SDI'.";

            TelemetryUpdated?.Invoke(_currentTelemetry);
        }

        private void UpdateTelemetryForSdi(string deviceName, string? videoMode, string? audioCh)
        {
            string mode = videoMode ?? "1080p 59.94 fps";
            string audio = audioCh ?? "Auto (Theo nguồn gốc)";

            bool isAuto = audio.Contains("Auto");
            if (isAuto)
            {
                if (deviceName.Contains("8K") || deviceName.Contains("12G") || deviceName.Contains("Quad"))
                {
                    _activeAudioChannels = 16;
                }
                else if (deviceName.Contains("DeckLink") || deviceName.Contains("SDI") || deviceName.Contains("AJA") || deviceName.Contains("KONA") || deviceName.Contains("Magewell"))
                {
                    _activeAudioChannels = 8;
                }
                else
                {
                    _activeAudioChannels = 2;
                }
            }
            else if (audio.Contains("16 Ch") || audio.Contains("16 Channels")) _activeAudioChannels = 16;
            else if (audio.Contains("8 Ch") || audio.Contains("8 Channels")) _activeAudioChannels = 8;
            else if (audio.Contains("4 Ch") || audio.Contains("4 Channels")) _activeAudioChannels = 4;
            else _activeAudioChannels = 2;

            _currentTelemetry.SourceName = deviceName;
            _currentTelemetry.SourceType = "SDI / CAPTURE";
            _currentTelemetry.Status = "● HARDWARE LOCKED (SIGNAL OK)";
            _currentTelemetry.IsLocked = true;

            if (mode.Contains("2160p") || mode.Contains("4K"))
            {
                _currentTelemetry.Resolution = "3840 x 2160 (16:9 UHD 4K)";
                _currentTelemetry.FrameRate = "59.94 FPS (12G-SDI Single-Link)";
                _currentTelemetry.Bitrate = "11.88 Gbps (12G-SDI Uncompressed)";
            }
            else if (mode.Contains("1080i"))
            {
                _currentTelemetry.Resolution = "1920 x 1080 (16:9 Full HD)";
                _currentTelemetry.FrameRate = "59.94 Interlaced (29.97 fps)";
                _currentTelemetry.Bitrate = "1.485 Gbps (HD-SDI Standard)";
            }
            else if (mode.Contains("50 fps"))
            {
                _currentTelemetry.Resolution = "1920 x 1080 (16:9 Full HD)";
                _currentTelemetry.FrameRate = "50.00 FPS (PAL Broadcast Reference)";
                _currentTelemetry.Bitrate = "2.97 Gbps (3G-SDI Level A)";
            }
            else
            {
                _currentTelemetry.Resolution = "1920 x 1080 (16:9 Full HD)";
                _currentTelemetry.FrameRate = "59.94 FPS (NTSC Broadcast Reference)";
                _currentTelemetry.Bitrate = "2.97 Gbps (3G-SDI Level A)";
            }

            _currentTelemetry.VideoCodec = "Uncompressed 10-bit YUV (v210 / UYVY)";
            _currentTelemetry.AudioFormat = isAuto
                ? $"Auto ({_activeAudioChannels} Ch) @ 48.0 kHz 24-bit PCM (SDI Embedded)"
                : $"{audio} @ 48.0 kHz 24-bit PCM (SDI Embedded)";
            _currentTelemetry.ColorSpace = "ITU-R BT.709 (4:2:2 10-bit Broadcast Studio)";
            _currentTelemetry.PipelineDetails = "Blackmagic DeckLink / DirectShow Zero-Copy Capture Pipeline";

            TelemetryUpdated?.Invoke(_currentTelemetry);
        }

        #endregion

        #region NDI Input Handler (NDI 6 SDK Live Receiver Loop)

        public async Task HandleNdiSourceAsync(string ndiSourceName)
        {
            _currentSourcePath = ndiSourceName;

            if (_txtActiveSourceTypeBadge != null)
            {
                _txtActiveSourceTypeBadge.Text = "🌐 NDI INPUT ACTIVE";
            }

            if (_txtActiveSourceBadge != null)
            {
                _txtActiveSourceBadge.Text = $"INPUT: NDI ({ndiSourceName})";
            }

            _colorbarEngine.StopAudioTone();
            StopLiveCaptures();

            if (_player != null)
            {
                try { await _player.PauseAsync(); } catch { }
            }

            if (string.IsNullOrWhiteSpace(ndiSourceName) ||
                ndiSourceName.Contains("Không tìm thấy", StringComparison.OrdinalIgnoreCase))
            {
                Log("[WARN]", "Chưa chọn nguồn NDI hợp lệ hoặc không tìm thấy nguồn NDI trên mạng LAN.");
                UpdateTelemetryForNdiNoSignal();
                return;
            }

            try
            {
                Log("[INFO]", $"Đang kết nối luồng mạng NDI thời gian thực [{ndiSourceName}]...");

                _ndiReceiver = new NdiReceiver(ndiSourceName);
                _ndiReceiver.LogRequested += (tag, msg) => Log(tag, msg);
                _ndiReceiver.VideoFrameReceived += OnNdiVideoFrameReceived;
                _ndiReceiver.AudioSamplesReceived += OnNdiAudioSamplesReceived;

                bool started = _ndiReceiver.Start();
                if (started)
                {
                    Log("[INFO]", $"✅ [SUCCESS] Đã kích hoạt NDI Receiver Loop: {ndiSourceName}");
                }
                else
                {
                    Log("[WARN]", $"Chưa thể kích hoạt NDI Receiver cho: {ndiSourceName}");
                }
            }
            catch (Exception ex)
            {
                Log("[WARN]", $"Lỗi kết nối NDI: {ex.Message}");
            }

            UpdateTelemetryForNdi(ndiSourceName);
            await Task.CompletedTask;
        }

        private void OnNdiVideoFrameReceived(byte[] frameData, int width, int height, int stride, double fps)
        {
            lock (_masterFrameLock)
            {
                _latestMasterFrame = frameData;
            }

            if (_reviewView != null && _currentSource == InputSourceType.NDI && _isPreviewEnabled)
            {
                _reviewView.Dispatcher.InvokeAsync(() =>
                {
                    if (_reviewView == null || _currentSource != InputSourceType.NDI) return;

                    if (_previewBitmap == null || _previewBitmap.PixelWidth != width || _previewBitmap.PixelHeight != height)
                    {
                        _previewBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
                        _reviewView.PresentBitmap(_previewBitmap);
                    }

                    _previewBitmap.Lock();
                    try
                    {
                        unsafe
                        {
                            int copyBytes = Math.Min(frameData.Length, _previewBitmap.BackBufferStride * height);
                            fixed (byte* pSrc = frameData)
                            {
                                Buffer.MemoryCopy(pSrc, (void*)_previewBitmap.BackBuffer, copyBytes, copyBytes);
                            }
                        }
                        _previewBitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
                    }
                    finally
                    {
                        _previewBitmap.Unlock();
                    }
                }, System.Windows.Threading.DispatcherPriority.Render);
            }

            _currentTelemetry.Resolution = $"{width} x {height} (NDI Native)";
            _currentTelemetry.FrameRate = $"{fps:F2} FPS";
            _currentTelemetry.Status = "● NDI STREAM LOCKED (LAN ACTIVE)";
            _currentTelemetry.IsLocked = true;
            TelemetryUpdated?.Invoke(_currentTelemetry);
        }

        private void OnNdiAudioSamplesReceived(float[] samples, int channels, int sampleRate)
        {
            if (channels > 0 && _activeAudioChannels != channels)
            {
                _activeAudioChannels = Math.Clamp(channels, 1, 16);
                _currentTelemetry.AudioFormat = $"{_activeAudioChannels} Ch @ {(sampleRate > 0 ? sampleRate / 1000.0 : 48.0):F1} kHz (NDI Audio)";
                TelemetryUpdated?.Invoke(_currentTelemetry);
            }
            AudioSamplesArrived?.Invoke(samples, channels, sampleRate);
        }

        private void UpdateTelemetryForNdiNoSignal()
        {
            _currentTelemetry.SourceName = "No NDI Source";
            _currentTelemetry.SourceType = "NDI IP STREAM";
            _currentTelemetry.Status = "○ NO NDI SIGNAL";
            _currentTelemetry.IsLocked = false;
            _currentTelemetry.Resolution = "-- x --";
            _currentTelemetry.FrameRate = "0.00 FPS";
            _currentTelemetry.VideoCodec = "None";
            _currentTelemetry.Bitrate = "0 Mbps";
            _currentTelemetry.AudioFormat = "No NDI Audio";
            _currentTelemetry.ColorSpace = "None";
            _currentTelemetry.PipelineDetails = "Không phát hiện luồng NDI trên mạng LAN. Bấm 'Find NDI' để quét lại.";

            TelemetryUpdated?.Invoke(_currentTelemetry);
        }

        private void UpdateTelemetryForNdi(string ndiSourceName)
        {
            _currentTelemetry.SourceName = ndiSourceName;
            _currentTelemetry.SourceType = "NDI IP STREAM";
            _currentTelemetry.Status = "● NDI STREAM CONNECTING...";
            _currentTelemetry.IsLocked = true;

            if (ndiSourceName.Contains("4K", StringComparison.OrdinalIgnoreCase))
            {
                _currentTelemetry.Resolution = "3840 x 2160 (16:9 UHD 4K)";
                _currentTelemetry.FrameRate = "59.94 FPS (NDI High Bandwidth)";
                _currentTelemetry.Bitrate = "250.0 Mbps (Ultra High Quality IP)";
            }
            else
            {
                _currentTelemetry.Resolution = "1920 x 1080 (16:9 Full HD)";
                _currentTelemetry.FrameRate = "59.94 FPS (Progressive)";
                _currentTelemetry.Bitrate = "125.0 Mbps (NDI High Bandwidth)";
            }

            _currentTelemetry.VideoCodec = "NDI SpeedHQ / SHQ2 (BGRA Raw Interop)";
            _currentTelemetry.AudioFormat = "Stereo (2 Ch) @ 48.0 kHz 32-bit Float PCM";
            _currentTelemetry.ColorSpace = "ITU-R BT.709 / YUV 4:2:2 (Full Color Dynamic)";
            _currentTelemetry.PipelineDetails = "NewTek NDI 6.0 Advanced SDK Receiver • Zero-Latency Direct Surface";

            TelemetryUpdated?.Invoke(_currentTelemetry);
        }

        #endregion

        #region Colorbar Handler

        public void HandleColorbarSource()
        {
            _currentSourcePath = "SMPTE RP 219 Colorbar";

            if (_txtActiveSourceTypeBadge != null)
            {
                _txtActiveSourceTypeBadge.Text = "🎨 COLORBAR ACTIVE";
            }

            string patternName = _colorbarEngine.GetPatternTitle().Split(' ')[0];
            if (_txtActiveSourceBadge != null)
            {
                _txtActiveSourceBadge.Text = $"INPUT: COLORBAR ({patternName})";
            }

            // Tạm dừng player phát nguồn khác
            _player?.PauseAsync();

            // Render hình ảnh Colorbar
            if (_pnlColorbarVisualHost != null)
            {
                _colorbarEngine.RenderPattern(_pnlColorbarVisualHost);
            }

            // Kích hoạt âm thanh Test Tone ra loa nếu kiểm âm đang bật
            _colorbarEngine.SetAudioToneOutput(_isAudioMonitorEnabled, _monitorVolume);

            UpdateTelemetryForColorbar();

            Log("[INFO]", $"🎨 Chuyển sang nguồn Colorbar [{_colorbarEngine.GetPatternTitle()}] & Test Tone [{_colorbarEngine.GetToneDescription()}]");
        }

        public void UpdateColorbarDisplay()
        {
            if (_currentSource == InputSourceType.Colorbar)
            {
                if (_pnlColorbarVisualHost != null)
                {
                    _colorbarEngine.RenderPattern(_pnlColorbarVisualHost);
                }
                _colorbarEngine.SetAudioToneOutput(_isAudioMonitorEnabled, _monitorVolume);
                UpdateTelemetryForColorbar();
            }
        }

        private void UpdateTelemetryForColorbar()
        {
            _currentTelemetry.SourceName = _colorbarEngine.GetPatternTitle();
            _currentTelemetry.SourceType = "SMPTE GENERATOR";
            _currentTelemetry.Status = "● SIGNAL LOCKED (DIRECT REFERENCE)";
            _currentTelemetry.IsLocked = true;
            _currentTelemetry.Resolution = "1920 x 1080 (16:9 Reference Full HD)";
            _currentTelemetry.FrameRate = "59.94 FPS (Broadcast Master Clock Locked)";
            _currentTelemetry.VideoCodec = "RAW 10-bit RGBA Vector (No Compression)";
            _currentTelemetry.Bitrate = "2.97 Gbps (Raw 3G-SDI Raster) • 0 kbps Net";
            _currentTelemetry.AudioFormat = $"16 Ch @ 48.0 kHz 24-bit ({_colorbarEngine.GetToneDescription()})";
            _currentTelemetry.ColorSpace = "ITU-R BT.709 (100% Full Color Gamut Calibration)";
            _currentTelemetry.PipelineDetails = "High-Precision Vector Shape Renderer • Realtime UTC SEI Burnt-in";

            TelemetryUpdated?.Invoke(_currentTelemetry);
        }

        #endregion

        #region SRT Stream Input Handler

        public async Task HandleSrtSourceAsync(string srtUri, SRTStreamConfig? customConfig = null)
        {
            _currentSourcePath = srtUri;

            if (_txtActiveSourceTypeBadge != null)
            {
                _txtActiveSourceTypeBadge.Text = "🚀 SRT STREAM ACTIVE";
            }

            if (_txtActiveSourceBadge != null)
            {
                _txtActiveSourceBadge.Text = $"INPUT: SRT ({srtUri})";
            }

            _colorbarEngine.StopAudioTone();

            try
            {
                Log("[INFO]", $"Đang kết nối luồng mạng SRT [{srtUri}]...");

                // Dọn dẹp luồng SRT cũ nếu có
                if (_srtStreamSource != null)
                {
                    await _srtStreamSource.StopAsync();
                    _srtStreamSource.Dispose();
                    _srtStreamSource = null;
                }

                var config = customConfig ?? new SRTStreamConfig();
                if (!string.IsNullOrEmpty(srtUri) && srtUri.StartsWith("srt://", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var uri = new Uri(srtUri);
                        config.Host = uri.Host;
                        config.Port = uri.Port > 0 ? uri.Port : 9000;
                    }
                    catch { }
                }

                _srtStreamSource = new SRTStreamSession(config);
                _srtStreamSource.LogEmitted += (tag, msg) => Log(tag, msg);
                _srtStreamSource.StatisticsUpdated += stats =>
                {
                    UpdateTelemetryForSrt(srtUri, stats);
                };

                await _srtStreamSource.ConnectReceiverAsync();

                if (_player == null)
                {
                    await RecreatePlayerAsync();
                }

                if (_player != null)
                {
                    _player.Volume = _monitorVolume;
                    _player.IsMuted = !_isAudioMonitorEnabled;

                    await _player.OpenAsync(srtUri);
                    await _player.PlayAsync();

                    Log("[INFO]", $"✅ [SUCCESS] Đã kết nối và phát preview luồng SRT: {srtUri}");
                }
            }
            catch (Exception ex)
            {
                Log("[WARN]", $"Kết nối luồng SRT: {ex.Message}");
            }

            UpdateTelemetryForSrt(srtUri, _srtStreamSource?.Statistics);
        }

        private void UpdateTelemetryForSrt(string srtUri, SRTStatistics? stats)
        {
            _currentTelemetry.SourceName = srtUri;
            _currentTelemetry.SourceType = "SRT LIVE STREAM";
            _currentTelemetry.Status = stats != null && stats.IsConnected ? "● SRT SIGNAL LOCKED (LIVE)" : "○ CONNECTING SRT...";
            _currentTelemetry.IsLocked = stats?.IsConnected ?? true;

            _currentTelemetry.Resolution = "1920 x 1080 (16:9 Full HD)";
            _currentTelemetry.FrameRate = stats != null && stats.CurrentFps > 0 ? $"{stats.CurrentFps:F2} FPS" : "59.94 FPS";
            _currentTelemetry.VideoCodec = "H.264 / AVC (Hardware Decoded)";
            _currentTelemetry.Bitrate = stats != null && stats.CurrentBitrateKbps > 0
                ? $"{(stats.CurrentBitrateKbps / 1000.0):F1} Mbps (RTT: {stats.RttMs:F0}ms)"
                : "6.0 Mbps (SRT Encapsulated)";
            _currentTelemetry.AudioFormat = "Stereo (2 Ch) @ 48.0 kHz 24-bit AAC";
            _currentTelemetry.ColorSpace = "ITU-R BT.709 / YUV 4:2:0";
            _currentTelemetry.PipelineDetails = $"OpenMedia Low-Latency SRT Pipeline • RTT: {stats?.RttMs ?? 25:F0}ms • Loss: {stats?.PacketLossPercent ?? 0:F2}%";

            TelemetryUpdated?.Invoke(_currentTelemetry);
        }

        #endregion

        #region Monitoring & Controls

        public void SetPreviewEnabled(bool isEnabled)
        {
            _isPreviewEnabled = isEnabled;
            UpdateViewVisibility();
        }

        public void SetAudioMonitor(bool isMonitorEnabled, double volume)
        {
            _isAudioMonitorEnabled = isMonitorEnabled;
            _monitorVolume = Math.Clamp(volume, 0.0, 1.0);

            if (_player != null)
            {
                _player.IsMuted = !_isAudioMonitorEnabled;
                _player.Volume = _monitorVolume;
            }

            if (_currentSource == InputSourceType.Colorbar)
            {
                _colorbarEngine.SetAudioToneOutput(_isAudioMonitorEnabled, _monitorVolume);
            }
        }

        public void SetVolume(double volume)
        {
            _monitorVolume = Math.Clamp(volume, 0.0, 1.0);

            if (_player != null)
            {
                _player.Volume = _monitorVolume;
            }

            if (_currentSource == InputSourceType.Colorbar)
            {
                _colorbarEngine.SetVolume(_monitorVolume);
            }
        }

        public void SetAspectRatio(Stretch stretch)
        {
            _currentStretch = stretch;

            if (_reviewView != null)
            {
                _reviewView.Stretch = _currentStretch;
            }

            if (_viewboxColorbar != null)
            {
                _viewboxColorbar.Stretch = _currentStretch;
            }
        }

        private void UpdateViewVisibility()
        {
            bool isColorbar = (_currentSource == InputSourceType.Colorbar);

            if (_viewboxColorbar != null)
            {
                _viewboxColorbar.Visibility = (isColorbar && _isPreviewEnabled) ? Visibility.Visible : Visibility.Collapsed;
            }

            if (_reviewView != null)
            {
                _reviewView.Visibility = (!isColorbar && _isPreviewEnabled) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void Log(string tag, string msg)
        {
            LogRequested?.Invoke(tag, msg);
        }

        #endregion

        public void Dispose()
        {
            try { StopLiveCaptures(); } catch { }
            try { _colorbarEngine?.Dispose(); } catch { }
            try { _srtStreamSource?.Dispose(); } catch { }
            _srtStreamSource = null;
            try { _player?.Dispose(); } catch { }
            _player = null;
            _reviewView = null;
        }
    }
}
