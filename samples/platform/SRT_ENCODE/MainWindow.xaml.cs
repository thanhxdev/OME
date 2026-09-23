using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using OpenMedia.Platform;
using OpenMedia.Platform.Controls.Wpf;
using OpenMedia.Platform.Models;
using OpenMedia.Platform.Telemetry;
using PlatformMediaPlayer = OpenMedia.Platform.MediaPlayer;

namespace SRT_ENCODE
{
    public partial class MainWindow : Window
    {
        private SRTTelemetryReporter? _telemetryReporter;
        // ─── Engine & Playback State ────────────────────────────────
        private VideoMixer? _mixer;
        private SRTStreamSession? _srtStream;
        private bool _isStreaming = false;
        private DateTime _streamStartTime = DateTime.MinValue;
        private ulong _totalBytesTransferred = 0;

        // ─── Video Source Management & Colorbar Engine ──────────────
        private readonly ColorbarEngine _colorbarEngine = new();
        private readonly VideoSourceManager _sourceManager;
        private readonly MasterClockProvider _masterClock = MasterClockProvider.Instance;

        // ─── Timers ─────────────────────────────────────────────────
        private DispatcherTimer? _utcClockTimer;
        private DispatcherTimer? _telemetryTimer;
        private DispatcherTimer? _vuMeterTimer;

        // ─── Metrics & Stats Simulation / Polling ───────────────────
        private double _currentRttMs = 0.0;
        private double _currentPacketLoss = 0.0;
        private double _currentBitrateKbps = 0.0;
        private double _currentFps = 0.0;
        private int _droppedFramesCount = 0;
        private ulong _workerBytesSent = 0;
        private ulong _lastWorkerBytesSent = 0;
        private long _workerFramesSent = 0;
        private long _lastWorkerFramesSent = 0;
        private DateTime _lastWorkerBitrateSampleTime = DateTime.UtcNow;
        private CancellationTokenSource? _transmissionCts;
        private MpegTsMuxer? _currentMuxer;
        private Process? _streamProcess;

        // ─── SRT Auto-Reconnect Supervisor State ────────────────────
        private bool _isTransmissionActive = false;
        private CancellationTokenSource? _reconnectCts;
        private int _reconnectAttempt = 0;
        private SRTStreamConfig? _activeSrtConfig;
        private volatile bool _isClosing = false;

        // ─── Logging Buffer & Init Guard ───────────────────────────
        private readonly List<string> _pendingLogs = new();
        private bool _isInitialized = false;

        // ─── 16-Channel Audio Meter Elements ───────────────────────
        private ProgressBar[] _vuBars = Array.Empty<ProgressBar>();
        private StackPanel[] _vuCols = Array.Empty<StackPanel>();
        private TextBlock[] _vuLabels = Array.Empty<TextBlock>();
        private System.Windows.Shapes.Ellipse[] _vuClipLeds = Array.Empty<System.Windows.Shapes.Ellipse>();
        private readonly double[] _channelLevels16 = new double[16];
        private readonly double[] _channelRmsLevels16 = new double[16];
        private readonly bool[] _channelClipping16 = new bool[16];
        private readonly AudioMeterService _audioMeterService = new();
        private bool _isAudioMuted = true;
        private double _lastNonZeroVolume = 0.7;


        public MainWindow()
        {
            InitializeComponent();
            _sourceManager = new VideoSourceManager(_colorbarEngine);
            _sourceManager.LogRequested += (tag, msg) => LogEvent(tag, msg);
            _sourceManager.TelemetryUpdated += UpdateSourceTelemetryUI;
            _sourceManager.AudioSamplesArrived += (samples, channels, sampleRate) =>
            {
                if (channels > 0 && _sourceManager.ActiveAudioChannels != channels)
                {
                    _sourceManager.ActiveAudioChannels = channels;
                }
                _audioMeterService.TapPcmDirect(samples, channels, sampleRate);
            };
            _isInitialized = true;
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        #region Initialization & Lifecycle

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Initialize 16-Channel VU Meter Arrays
                _vuBars = new[] { VuBar1, VuBar2, VuBar3, VuBar4, VuBar5, VuBar6, VuBar7, VuBar8, VuBar9, VuBar10, VuBar11, VuBar12, VuBar13, VuBar14, VuBar15, VuBar16 };
                _vuCols = new[] { ColVu1, ColVu2, ColVu3, ColVu4, ColVu5, ColVu6, ColVu7, ColVu8, ColVu9, ColVu10, ColVu11, ColVu12, ColVu13, ColVu14, ColVu15, ColVu16 };
                _vuLabels = new[] { LblVu1, LblVu2, LblVu3, LblVu4, LblVu5, LblVu6, LblVu7, LblVu8, LblVu9, LblVu10, LblVu11, LblVu12, LblVu13, LblVu14, LblVu15, LblVu16 };
                _vuClipLeds = new[] { ClipLed1, ClipLed2, ClipLed3, ClipLed4, ClipLed5, ClipLed6, ClipLed7, ClipLed8, ClipLed9, ClipLed10, ClipLed11, ClipLed12, ClipLed13, ClipLed14, ClipLed15, ClipLed16 };

                // Flush any early pending logs (Newest on Top)
                if (_pendingLogs.Count > 0 && TxtLogConsole != null)
                {
                    var sb = new StringBuilder();
                    foreach (var line in _pendingLogs)
                    {
                        sb.Append(line);
                    }
                    TxtLogConsole.Text = sb.ToString() + TxtLogConsole.Text;
                    _pendingLogs.Clear();
                    ScrollerLogs?.ScrollToHome();
                }

                LogEvent("[INFO]", "Ứng dụng Encoder đang khởi chạy...");
                TxtEngineStatus.Text = "Engine: Connecting...";

                // Start High-precision Master UTC Clock
                StartMasterUtcClock();
                _masterClock.SyncStatusChanged += res =>
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        if (res.Success && TxtNtpOffset != null)
                        {
                            TxtNtpOffset.Text = res.GetFormattedOffset();
                        }
                    });
                };

                // Tự động đồng bộ Master NTP nếu được kích hoạt (mặc định khởi chạy là disabled)
                if (ChkNtpSync?.IsChecked == true)
                {
                    string defaultNtp = TxtNtpServer?.Text?.Trim() ?? "time.google.com";
                    if (string.IsNullOrEmpty(defaultNtp)) defaultNtp = "time.google.com";
                    _masterClock.StartPeriodicSync(defaultNtp, 30);
                }
                else
                {
                    _masterClock.StopPeriodicSync();
                    if (TxtNtpOffset != null)
                    {
                        TxtNtpOffset.Text = "Disabled (Free-Run)";
                        TxtNtpOffset.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
                    }
                }
                if (BadgeNtpSync != null)
                {
                    BadgeNtpSync.Visibility = (ChkNtpSync?.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;
                }

                // Start Audio VU Meter & Telemetry timers
                StartVuMeterTimer();
                StartTelemetryTimer();

                // Connect to OpenMedia Runtime
                string? serverPath = FindServerExecutable();
                var options = new RuntimeOptions
                {
                    ServerPath = serverPath
                };

                if (!string.IsNullOrEmpty(serverPath))
                {
                    LogEvent("[INFO]", $"Tìm thấy OpenMediaServer: {serverPath}");
                }

                bool ready = await OpenMediaRuntime.InitializeAsync(options);
                if (ready)
                {
                    LedEngineStatus.Fill = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Green
                    TxtEngineStatus.Text = $"Engine: Connected (v{OpenMediaRuntime.EngineVersion})";
                    LogEvent("[INFO]", "Đã kết nối thành công với OpenMedia.Platform Core Engine.");
                }
                else
                {
                    LedEngineStatus.Fill = new SolidColorBrush(Color.FromRgb(255, 193, 7)); // Yellow / Standalone
                    TxtEngineStatus.Text = "Engine: Standalone / Direct Pipeline";
                    LogEvent("[WARN]", "OpenMediaServer nền chưa bật, ứng dụng chuyển sang chế độ Direct Engine Pipeline.");
                }

                // Initial scan for hardware encoders & input devices
                await ScanHardwareEncodersAsync();
                await ScanSdiDevicesAsync();
                await ScanNdiSourcesAsync();

                // Nạp cấu hình phiên làm việc đã lưu (hoặc mặc định)
                LoadAndApplySettings();

                // Setup VideoSourceManager & Initial Preview Player
                _sourceManager.IsLoopPlayback = ChkLoopFile?.IsChecked == true;
                _sourceManager.SetAudioMonitor(!_isAudioMuted, SldMonitorVolume?.Value ?? 0.7);
                UpdateAudioMuteState(logChange: false);
                await _sourceManager.InitializeAsync(ReviewView, ViewboxColorbar, PnlColorbarVisualHost, TxtActiveSourceBadge, TxtActiveSourceTypeBadge);
                await InitializePreviewPlayerAsync();

                // Apply initial UI states
                UpdateColorbarDisplay();
                RefreshMasterProgramFrameBuffer();
                UpdateCodecCapabilities();
                UpdateTargetSummary();
                UpdateUltraLowLatencyState();
                UpdateEncryptionState();

                LogEvent("[INFO]", "Hệ thống đã sẵn sàng phát sóng (Broadcast Ready).");
            }
            catch (Exception ex)
            {
                LogEvent("[ERROR]", $"Lỗi khởi tạo: {ex.Message}");
            }
        }

        private async Task InitializePreviewPlayerAsync()
        {
            try
            {
                int initialIndex = CmbInputSource?.SelectedIndex ?? 2;
                string? initialParam = initialIndex switch
                {
                    0 => CmbSdiDevices?.SelectedItem?.ToString(),
                    1 => CmbNdiSources?.SelectedItem?.ToString(),
                    2 => TxtFilePath?.Text,
                    _ => null
                };

                await _sourceManager.SwitchSourceAsync((InputSourceType)initialIndex, initialParam);
            }
            catch (Exception ex)
            {
                LogEvent("[WARN]", $"Khởi tạo Review Player: {ex.Message}");
            }
        }

        private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_isClosing) return;

            try
            {
                // 1. Huỷ bỏ đóng trực tiếp của WPF, lưu cấu hình và ẩn cửa sổ ngay lập tức để tối ưu UX
                e.Cancel = true;
                _isClosing = true;
                SaveCurrentSettings();
                Hide();

                // 2. Dừng ngay toàn bộ Timers UI & Master Clock
                _utcClockTimer?.Stop();
                _utcClockTimer = null;
                _telemetryTimer?.Stop();
                _telemetryTimer = null;
                _vuMeterTimer?.Stop();
                _vuMeterTimer = null;
                try { _masterClock.Dispose(); } catch { }

                // 3. Dừng an toàn luồng truyền dẫn SRT & tiến trình muxer con
                try { _transmissionCts?.Cancel(); } catch { }
                try { _reconnectCts?.Cancel(); } catch { }
                StopTransmissionInternal();

                // 4. Detach Direct3D ReviewView để giải phóng DirectX surface an toàn trên UI thread
                try { ReviewView?.Detach(); } catch { }

                // 5. Giải phóng tuần tự các service và OpenMediaRuntime trong background với timeout an toàn
                await Task.Run(async () =>
                {
                    try
                    {
                        var stopTask = Task.Run(() =>
                        {
                            try { _sourceManager?.Dispose(); } catch { }
                            try { _audioMeterService?.Dispose(); } catch { }
                            try { _mixer?.Dispose(); } catch { }
                            try { OpenMediaRuntime.Shutdown(); } catch { }
                        });

                        await Task.WhenAny(stopTask, Task.Delay(1500)).ConfigureAwait(false);
                    }
                    catch { }
                }).ConfigureAwait(false);
            }
            catch { }
            finally
            {
                Environment.Exit(0);
            }
        }

        #endregion

        #region Timers (UTC Clock, Telemetry HUD, VU Meters)

        private void StartMasterUtcClock()
        {
            _utcClockTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(40) // ~25 fps update for milliseconds & SMPTE frame sync
            };
            _utcClockTimer.Tick += (s, e) =>
            {
                var nowUtc = _masterClock.CurrentUtcTime;
                string utcStr = nowUtc.ToString("HH:mm:ss.fff");
                string smpteStr = _masterClock.GetFormattedSmpteTimecode(25);
                TxtMasterUtcTime.Text = utcStr;
                if (TxtMasterSmpteTime != null)
                {
                    TxtMasterSmpteTime.Text = smpteStr;
                }
                TxtHudPts.Text = $"{utcStr} [{smpteStr}]";
                if (TxtColorbarUtcTime != null)
                {
                    TxtColorbarUtcTime.Text = utcStr;
                }

                if (_sourceManager.CurrentSource == InputSourceType.File && TxtActiveSourceBadge != null)
                {
                    string fname = !string.IsNullOrEmpty(_sourceManager.CurrentSourcePath) ? Path.GetFileName(_sourceManager.CurrentSourcePath) : "No File Selected";
                    var pos = _sourceManager.CurrentPosition;
                    var dur = _sourceManager.CurrentDuration;
                    string timeInfo = dur > TimeSpan.Zero
                        ? $"{pos:hh\\:mm\\:ss\\.fff} / {dur:hh\\:mm\\:ss}"
                        : $"{pos:hh\\:mm\\:ss\\.fff}";
                    TxtActiveSourceBadge.Text = $"INPUT: FILE ({fname} [{timeInfo}])";
                }

                if (_isStreaming)
                {
                    RefreshMasterProgramFrameBuffer();
                }

                if (_isStreaming && _streamStartTime != DateTime.MinValue)
                {
                    var duration = _masterClock.CurrentUtcTime - _streamStartTime;
                    TxtSessionDuration.Text = $"{(int)duration.TotalHours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}";
                }
            };
            _utcClockTimer.Start();
        }

        private void StartTelemetryTimer()
        {
            _telemetryTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1) // 1 second update
            };
            _telemetryTimer.Tick += (s, e) =>
            {
                UpdateRealtimeTelemetry();
            };
            _telemetryTimer.Start();
        }

        private void StartVuMeterTimer()
        {
            _vuMeterTimer = new DispatcherTimer(DispatcherPriority.Normal)
            {
                Interval = TimeSpan.FromMilliseconds(80) // Smooth VU needle animation
            };
            _vuMeterTimer.Tick += (s, e) =>
            {
                UpdateAudioVuLevels();
            };
            _vuMeterTimer.Start();
        }


        private void UpdateRealtimeTelemetry()
        {
            // Trích xuất FPS từ thông số nguồn đang phát
            double sourceFps = 30.0;
            if (!string.IsNullOrWhiteSpace(_sourceManager.CurrentTelemetry.FrameRate))
            {
                string rawFps = _sourceManager.CurrentTelemetry.FrameRate.Replace("FPS", "").Trim();
                int spaceIdx = rawFps.IndexOf(' ');
                if (spaceIdx > 0) rawFps = rawFps.Substring(0, spaceIdx).Trim();
                if (double.TryParse(rawFps, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedFps) && parsedFps > 0)
                {
                    sourceFps = parsedFps;
                }
            }

            double targetFps = GetSelectedStreamFps();
            bool isAutoFps = CmbStreamFrameRate?.SelectedItem is ComboBoxItem item && (item.Content?.ToString() ?? "").Contains("Auto", StringComparison.OrdinalIgnoreCase);
            double displayFps = isAutoFps ? sourceFps : targetFps;

            if (!_isStreaming)
            {
                // Trạng thái PREVIEW / STANDBY: Hiển thị đúng FPS người dùng đã chọn / nhịp Pacing của Program Bus
                TxtHudRtt.Text = "-- ms (Standby)";
                TxtHudRtt.Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140));

                TxtHudLoss.Text = "-- % (Standby)";
                TxtHudLoss.Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140));

                TxtHudBitrate.Text = "0 kbps (Standby)";
                TxtHudBitrate.Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140));

                bool isPlaying = (_sourceManager.Player != null && _sourceManager.Player.State == OpenMedia.Platform.PlaybackState.Playing)
                    || (_sourceManager.CurrentSource == InputSourceType.File && _sourceManager.IsDirectFilePlaybackRunning)
                    || (CmbInputSource?.SelectedIndex == 3 && _colorbarEngine.IsAudioTonePlaying)
                    || (_sourceManager.CurrentSource == InputSourceType.NDI && _sourceManager.CurrentTelemetry.IsLocked)
                    || (_sourceManager.CurrentSource == InputSourceType.SDI && _sourceManager.CurrentTelemetry.IsLocked);

                if (isPlaying)
                {
                    TxtHudFps.Text = isAutoFps ? $"{displayFps:F2} FPS (Auto Preview)" : $"{displayFps:F2} FPS (Preview)";
                    TxtHudFps.Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255));
                }
                else
                {
                    TxtHudFps.Text = $"{displayFps:F2} FPS (Standby)";
                    TxtHudFps.Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140));
                }

                if (TxtHudEncryption != null)
                {
                    bool isEnc = ChkEnableEncryption?.IsChecked == true;
                    TxtHudEncryption.Text = isEnc ? "AES-256 (Armed)" : "None";
                    TxtHudEncryption.Foreground = isEnc ? new SolidColorBrush(Color.FromRgb(0, 230, 118)) : new SolidColorBrush(Color.FromRgb(136, 136, 136));
                }
                return;
            }

            // Trạng thái TRANSMITTING (LIVE): Tính toán Throughput và FPS thật từ dữ liệu phát sinh
            DateTime now = DateTime.UtcNow;
            double elapsedSec = (now - _lastWorkerBitrateSampleTime).TotalSeconds;
            if (elapsedSec >= 0.5)
            {
                if (_workerBytesSent >= _lastWorkerBytesSent)
                {
                    ulong deltaBytes = _workerBytesSent - _lastWorkerBytesSent;
                    double dynamicThroughputKbps = (deltaBytes * 8.0 / 1000.0) / elapsedSec;
                    _currentBitrateKbps = dynamicThroughputKbps;
                }
                else
                {
                    _currentBitrateKbps = 0.0;
                }

                if (_workerFramesSent >= _lastWorkerFramesSent)
                {
                    long deltaFrames = _workerFramesSent - _lastWorkerFramesSent;
                    _currentFps = deltaFrames > 0 ? (deltaFrames / elapsedSec) : 0.0;
                }
                else
                {
                    _currentFps = 0.0;
                }

                _lastWorkerBytesSent = _workerBytesSent;
                _lastWorkerFramesSent = _workerFramesSent;
                _lastWorkerBitrateSampleTime = now;
            }

            // Nếu SRT Socket có thống kê native từ libsrt
            if (_srtStream?.Statistics != null && _srtStream.Statistics.IsConnected)
            {
                if (_srtStream.Statistics.RttMs >= 0)
                {
                    _currentRttMs = _srtStream.Statistics.RttMs;
                }
                if (_srtStream.Statistics.PacketLossPercent >= 0)
                {
                    _currentPacketLoss = _srtStream.Statistics.PacketLossPercent;
                }
                if (_srtStream.Statistics.CurrentBitrateKbps > 0)
                {
                    _currentBitrateKbps = _srtStream.Statistics.CurrentBitrateKbps;
                }
            }

            // Auto Latency calculation: Latency = 3 * RTT (min 120ms)
            if (ChkAutoLatency.IsChecked == true && _currentRttMs > 0)
            {
                int calculatedLatency = (int)Math.Max(120.0, Math.Round(_currentRttMs * 3.0));
                TxtCalculatedLatency.Text = $"{calculatedLatency} ms (3 x {_currentRttMs:F0}ms RTT)";
                TxtManualLatency.Text = calculatedLatency.ToString();
            }

            // Cập nhật UI HUD số liệu thời gian thực
            TxtHudRtt.Text = _currentRttMs > 0 ? $"{_currentRttMs:F0} ms" : "< 1 ms (LAN/Local)";
            TxtHudRtt.Foreground = new SolidColorBrush(Color.FromRgb(0, 230, 118));

            TxtHudBitrate.Text = $"{_currentBitrateKbps:N0} kbps";
            TxtHudBitrate.Foreground = _currentBitrateKbps > 0 
                ? new SolidColorBrush(Color.FromRgb(0, 230, 118)) 
                : new SolidColorBrush(Color.FromRgb(244, 67, 54));

            TxtHudLoss.Text = $"{_currentPacketLoss:F2} %";
            if (_currentPacketLoss >= 5.0)
            {
                TxtHudLoss.Foreground = new SolidColorBrush(Color.FromRgb(244, 67, 54)); // Warning Red
                LogEvent("[WARN]", $"[ALERT] SRT Packet Loss vượt ngưỡng an toàn (>5%): {_currentPacketLoss:F1}%");
            }
            else if (_currentPacketLoss >= 2.0)
            {
                TxtHudLoss.Foreground = new SolidColorBrush(Color.FromRgb(255, 193, 7)); // Yellow
            }
            else
            {
                TxtHudLoss.Foreground = new SolidColorBrush(Color.FromRgb(0, 230, 118)); // Green
            }

            TxtHudFps.Text = $"{_currentFps:F1} FPS (Target: {targetFps:F1})";
            TxtHudFps.Foreground = _currentFps > 0 
                ? new SolidColorBrush(Color.FromRgb(255, 255, 255)) 
                : new SolidColorBrush(Color.FromRgb(244, 67, 54));

            if (TxtHudEncryption != null)
            {
                bool isEnc = ChkEnableEncryption?.IsChecked == true;
                TxtHudEncryption.Text = isEnc ? "AES-256 (LIVE)" : "None";
                TxtHudEncryption.Foreground = isEnc ? new SolidColorBrush(Color.FromRgb(0, 230, 118)) : new SolidColorBrush(Color.FromRgb(136, 136, 136));
            }

            // Total data formatted (từ SRT socket hoặc worker bytes)
            ulong totalBytes = Math.Max(_totalBytesTransferred, _workerBytesSent);
            double totalMb = totalBytes / (1024.0 * 1024.0);
            if (totalMb >= 1024.0)
            {
                TxtTotalBytesSent.Text = $"{totalMb / 1024.0:F2} GB";
            }
            else
            {
                TxtTotalBytesSent.Text = $"{totalMb:F2} MB";
            }
        }

        private void UpdateAudioVuLevels()
        {
            int activeSourceIndex = CmbInputSource?.SelectedIndex ?? 2;
            bool isColorbar = (activeSourceIndex == 3);

            bool isSourcePlaying = _sourceManager.CurrentSource switch
            {
                InputSourceType.Colorbar => _colorbarEngine.IsAudioTonePlaying,
                InputSourceType.NDI => true,
                InputSourceType.SDI => true,
                _ => (_sourceManager.Player != null && _sourceManager.Player.State == OpenMedia.Platform.PlaybackState.Playing)
            };

            // Xác định số kênh âm thanh hoạt động theo nguồn gốc và chế độ chọn
            string sdiCh = (CmbSdiAudioCh?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Auto (Theo nguồn gốc)";
            bool isAutoMode = sdiCh.Contains("Auto");

            int configuredChannels = Math.Max(2, _sourceManager.ActiveAudioChannels);
            if (activeSourceIndex == 0) // SDI / Capture Card
            {
                if (isAutoMode)
                {
                    configuredChannels = Math.Clamp(_sourceManager.ActiveAudioChannels, 2, 16);
                }
                else if (sdiCh.Contains("16 Channels") || sdiCh.Contains("16 Ch")) configuredChannels = 16;
                else if (sdiCh.Contains("8 Channels") || sdiCh.Contains("8 Ch")) configuredChannels = 8;
                else if (sdiCh.Contains("4 Channels") || sdiCh.Contains("4 Ch")) configuredChannels = 4;
                else configuredChannels = 2;
            }
            else if (activeSourceIndex == 2) // Media File
            {
                // Tự động nhận diện số kênh từ container file video gốc
                if (_sourceManager.Player?.Information != null && _sourceManager.Player.Information.AudioChannels > 0)
                {
                    int fileCh = Math.Clamp(_sourceManager.Player.Information.AudioChannels, 1, 16);
                    _sourceManager.ActiveAudioChannels = fileCh;
                    configuredChannels = fileCh;
                }
                else if (_audioMeterService.ActiveChannelCount > 0)
                {
                    configuredChannels = Math.Clamp(_audioMeterService.ActiveChannelCount, 1, 16);
                }
                else
                {
                    configuredChannels = Math.Clamp(_sourceManager.ActiveAudioChannels, 1, 16);
                }
            }
            else if (activeSourceIndex == 1) // NDI
            {
                configuredChannels = Math.Clamp(_sourceManager.ActiveAudioChannels, 1, 16);
            }
            else // Colorbar Test Tone
            {
                configuredChannels = Math.Clamp(_sourceManager.ActiveAudioChannels, 2, 16);
            }

            if (isSourcePlaying)
            {
                // Tapping luồng PCM tương ứng của từng loại nguồn
                switch ((InputSourceType)activeSourceIndex)
                {
                    case InputSourceType.Colorbar:
                        _audioMeterService.TapColorbarTone(_colorbarEngine.CurrentTone, SldMonitorVolume?.Value ?? 1.0, configuredChannels);
                        break;

                    case InputSourceType.File:
                    case InputSourceType.SRT:
                        _ = _audioMeterService.TapPlayerAudioAsync(_sourceManager.Player, configuredChannels);
                        break;

                    case InputSourceType.NDI:
                    case InputSourceType.SDI:
                        // Âm thanh NDI/SDI được đẩy trực tiếp qua AudioSamplesArrived -> TapPcmDirect
                        break;
                }
            }

            // Trích xuất mảng số liệu Peak, RMS và Clipping từ AudioMeterService
            _audioMeterService.GetPreviewAudioLevels(_channelLevels16, _channelRmsLevels16, _channelClipping16, isSourcePlaying);

            // Mute các kênh vượt quá số kênh hoạt động
            for (int i = configuredChannels; i < 16; i++)
            {
                _channelLevels16[i] = -60.0;
                _channelRmsLevels16[i] = -60.0;
                _channelClipping16[i] = false;
            }

            // Cập nhật nhãn tổng số kênh
            if (TxtVuChannelCountBadge != null)
            {
                bool isSourceAuto = (activeSourceIndex == 0 && isAutoMode) || (activeSourceIndex == 1) || (activeSourceIndex == 2);
                string tag = configuredChannels switch
                {
                    1 => isSourceAuto ? "AUTO: 1-CH MONO" : "1-CH MONO",
                    2 => isSourceAuto ? "AUTO: 2-CH STEREO (L/R)" : "2-CH STEREO (L/R)",
                    4 => isSourceAuto ? "AUTO: 4-CH MULTI" : "4-CH SDI EMBEDDED",
                    6 => isSourceAuto ? "AUTO: 6-CH (5.1 SURROUND)" : "6-CH MULTI",
                    8 => isSourceAuto ? "AUTO: 8-CH EMBEDDED" : "8-CH SDI EMBEDDED",
                    16 => isSourceAuto ? "AUTO: 16-CH EMBEDDED" : "16-CH SDI EMBEDDED",
                    _ => isSourceAuto ? $"AUTO: {configuredChannels}-CH" : $"{configuredChannels}-CH MULTI"
                };
                TxtVuChannelCountBadge.Text = tag;
            }

            // Kiểm tra xem toàn bộ các kênh có tín hiệu âm thanh thực tế hay không
            const double NoiseFloorCutoff = -55.0;
            bool anyChannelHasSignal = false;
            for (int i = 0; i < configuredChannels; i++)
            {
                if (_channelLevels16[i] > NoiseFloorCutoff)
                {
                    anyChannelHasSignal = true;
                    break;
                }
            }

            // Cập nhật từng cột trong 16 cột đo âm thanh
            for (int i = 0; i < 16; i++)
            {
                bool isChannelConfigured = (i < configuredChannels);
                UpdateChannelVu16(i, _channelLevels16[i], _channelRmsLevels16[i], _channelClipping16[i], isChannelConfigured, configuredChannels, isSourcePlaying);
            }

            // Cập nhật tóm tắt thông số Peak (nếu hiển thị)
            if (TxtAudioPeakSummary != null && TxtAudioPeakSummary.Visibility == Visibility.Visible)
            {
                if (isSourcePlaying && anyChannelHasSignal)
                {
                    string lStr = _channelLevels16[0] > NoiseFloorCutoff ? $"{_channelLevels16[0]:F1} dB" : "-∞";
                    string rStr = (configuredChannels > 1 && _channelLevels16[1] > NoiseFloorCutoff) ? $"{_channelLevels16[1]:F1} dB" : "-∞";
                    TxtAudioPeakSummary.Text = (configuredChannels == 2) 
                        ? $"Peak: L {lStr} | R {rStr}" 
                        : $"Peak: CH1 {lStr} | CH2 {rStr}";
                    TxtAudioPeakSummary.Foreground = new SolidColorBrush(Color.FromRgb(78, 201, 176)); // Cyan Green
                }
                else
                {
                    TxtAudioPeakSummary.Text = isSourcePlaying ? "Peak: SILENT (NO SIGNAL)" : "Peak: DISABLED (OFF)";
                    TxtAudioPeakSummary.Foreground = new SolidColorBrush(Color.FromRgb(136, 136, 136)); // Muted Grey
                }
            }
        }

        private void UpdateChannelVu16(int index, double peakDb, double rmsDb, bool isClip, bool isChannelEnabled, int totalActiveChannels, bool isSourcePlaying = true)
        {
            if (index < 0 || index >= _vuBars.Length || index >= _vuCols.Length || index >= _vuLabels.Length) return;

            // Auto-scale layout: Hiển thị đúng các kênh được kích hoạt (tối thiểu 2 kênh L và R)
            _vuCols[index].Visibility = isChannelEnabled ? Visibility.Visible : Visibility.Collapsed;

            // Nhãn hiển thị kênh (L/R cho Stereo, 1..16 cho Multi-channel)
            if (totalActiveChannels == 2)
            {
                if (index == 0) _vuLabels[0].Text = "L";
                else if (index == 1) _vuLabels[1].Text = "R";
            }
            else
            {
                _vuLabels[index].Text = (index + 1).ToString();
            }

            const double NoiseFloorCutoff = -55.0;
            bool hasSignal = isSourcePlaying && isChannelEnabled && (peakDb > NoiseFloorCutoff);

            // Đèn LED báo Clip (0 dBFS)
            if (index < _vuClipLeds.Length && _vuClipLeds[index] != null)
            {
                _vuClipLeds[index].Fill = (hasSignal && isClip)
                    ? new SolidColorBrush(Color.FromRgb(244, 67, 54))  // Bright Red Clip Warning
                    : new SolidColorBrush(Color.FromRgb(38, 38, 42));   // Inactive Dark
            }

            if (!isChannelEnabled)
            {
                // Kênh hoàn toàn không sử dụng trong cấu hình hiện tại
                _vuCols[index].Opacity = 0.15;
                _vuBars[index].Value = -60.0;
                _vuBars[index].Foreground = new SolidColorBrush(Color.FromRgb(25, 25, 25));
                _vuLabels[index].Foreground = new SolidColorBrush(Color.FromRgb(65, 65, 65));
            }
            else if (!hasSignal)
            {
                // KÊNH ĐƯỢC BẬT NHƯNG KHÔNG CÓ TÍN HIỆU ÂM THANH (SILENT / NO AUDIO / STOPPED):
                // Chuyển sang trạng thái DISABLED / STANDBY rõ rệt (Dimmed mờ, thanh bar màu xám tối, label mờ, giá trị đáy -60dB)
                _vuCols[index].Opacity = 0.35;
                _vuBars[index].Value = -60.0;
                _vuBars[index].Foreground = new SolidColorBrush(Color.FromRgb(45, 45, 48));
                _vuLabels[index].Foreground = new SolidColorBrush(Color.FromRgb(110, 110, 115));
            }
            else
            {
                // KÊNH CÓ TÍN HIỆU ÂM THANH HOẠT ĐỘNG BÌNH THƯỜNG:
                // Sáng đầy đủ 100% Opacity, hiển thị màu theo chuẩn Broadcast (Xanh lá -> Vàng -> Đỏ khi Clip)
                _vuCols[index].Opacity = 1.0;
                _vuBars[index].Value = peakDb;
                var colorBrush = GetVuMeterColorBrush(peakDb, isClip);
                _vuBars[index].Foreground = colorBrush;
                _vuLabels[index].Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255));
            }
        }

        private static SolidColorBrush GetVuMeterColorBrush(double db, bool isClip = false)
        {
            if (isClip || db >= -1.0)
            {
                return new SolidColorBrush(Color.FromRgb(244, 67, 54)); // Red (Clip / Peak Alert)
            }
            if (db >= -18.0)
            {
                return new SolidColorBrush(Color.FromRgb(255, 193, 7)); // Yellow (Standard Program Range)
            }
            return new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Green (Normal range)
        }

        #endregion

        #region Input Source Selection (SDI, NDI, File, Colorbar)

        private async void BtnScanSdi_Click(object sender, RoutedEventArgs e)
        {
            await ScanSdiDevicesAsync();
        }

        private async Task ScanSdiDevicesAsync()
        {
            try
            {
                LogEvent("[INFO]", "Đang dò quét cổng SDI IN phần cứng (DeckLink FFmpeg Probe / DirectShow Fallback)...");
                CmbSdiDevices.Items.Clear();

                var devices = await SdiHardwareScanner.ScanInputsAsync();

                if (devices.Count > 0)
                {
                    foreach (var dev in devices)
                    {
                        CmbSdiDevices.Items.Add(dev.DisplayLabel);
                    }
                    CmbSdiDevices.SelectedIndex = 0;
                    int sdiCount = devices.Count(d => d.IsPhysicalHardware);
                    LogEvent("[INFO]", $"✅ Tìm thấy {devices.Count} thiết bị SDI ({sdiCount} cổng phần cứng vật lý).");
                }
                else
                {
                    CmbSdiDevices.Items.Add("[Không phát hiện cổng SDI]");
                    CmbSdiDevices.SelectedIndex = 0;
                    LogEvent("[WARN]", "Không phát hiện cổng SDI phần cứng nào trên máy trạm.");
                }
            }
            catch (Exception ex)
            {
                LogEvent("[WARN]", $"Lỗi quét SDI: {ex.Message}");
                if (CmbSdiDevices.Items.Count == 0)
                {
                    CmbSdiDevices.Items.Add("[Không phát hiện cổng SDI]");
                    CmbSdiDevices.SelectedIndex = 0;
                }
            }
        }

        private async void BtnScanNdi_Click(object sender, RoutedEventArgs e)
        {
            await ScanNdiSourcesAsync();
        }

        private async Task ScanNdiSourcesAsync()
        {
            try
            {
                LogEvent("[INFO]", "Đang quét các luồng NDI thời gian thực trên mạng LAN (NDI 6 SDK mDNS Finder)...");
                CmbNdiSources.Items.Clear();

                var discoveredSources = await NdiNativeFinder.FindSourcesAsync(800);

                if (discoveredSources.Count > 0)
                {
                    foreach (var src in discoveredSources)
                    {
                        CmbNdiSources.Items.Add(src);
                    }
                    CmbNdiSources.SelectedIndex = 0;
                    LogEvent("[INFO]", $"✅ Đã phát hiện {discoveredSources.Count} luồng NDI Network Stream thời gian thực trên mạng LAN.");
                }
                else
                {
                    CmbNdiSources.Items.Add("[Không tìm thấy nguồn NDI nào trên mạng]");
                    CmbNdiSources.SelectedIndex = 0;
                    LogEvent("[WARN]", "Không tìm thấy nguồn NDI nào đang phát trên mạng LAN.");
                }
            }
            catch (Exception ex)
            {
                LogEvent("[WARN]", $"Lỗi quét NDI: {ex.Message}");
                if (CmbNdiSources.Items.Count == 0)
                {
                    CmbNdiSources.Items.Add("[Không tìm thấy nguồn NDI nào trên mạng]");
                    CmbNdiSources.SelectedIndex = 0;
                }
            }
        }

        private async void BtnBrowseFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Chọn Video File Phát Sóng",
                Filter = "Broadcast Video Files (*.mp4;*.mov;*.mkv;*.ts)|*.mp4;*.mov;*.mkv;*.ts|All Files (*.*)|*.*"
            };

            if (dlg.ShowDialog() == true)
            {
                TxtFilePath.Text = dlg.FileName;
                LogEvent("[INFO]", $"Đã chọn video tập tin: {Path.GetFileName(dlg.FileName)}");
                _sourceManager.IsLoopPlayback = ChkLoopFile?.IsChecked == true;
                if (CmbInputSource != null && CmbInputSource.SelectedIndex != 2)
                {
                    CmbInputSource.SelectedIndex = 2;
                }
                else
                {
                    await _sourceManager.SwitchSourceAsync(InputSourceType.File, dlg.FileName);
                }
            }
        }

        private void ChkLoopFile_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            bool isLoop = ChkLoopFile?.IsChecked == true;
            _sourceManager.IsLoopPlayback = isLoop;
            LogEvent("[MEDIA]", isLoop ? "🔁 Đã BẬT chế độ lặp video (Auto Loop Playback)." : "▶️ Đã TẮT chế độ lặp video (Play Once).");
        }

        private async void CmbInputSource_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;
            if (CardSdiConfig == null || CardNdiConfig == null || CardFileConfig == null || CardColorbarConfig == null) return;

            int selected = CmbInputSource.SelectedIndex;

            // Show only the active source configuration panel (Border Card)
            CardSdiConfig.Visibility = (selected == 0) ? Visibility.Visible : Visibility.Collapsed;
            CardNdiConfig.Visibility = (selected == 1) ? Visibility.Visible : Visibility.Collapsed;
            CardFileConfig.Visibility = (selected == 2) ? Visibility.Visible : Visibility.Collapsed;
            CardColorbarConfig.Visibility = (selected == 3) ? Visibility.Visible : Visibility.Collapsed;

            string? param = selected switch
            {
                0 => CmbSdiDevices?.SelectedItem?.ToString() ?? "Blackmagic DeckLink 8K Pro (SDI 1 - 1080p59.94)",
                1 => CmbNdiSources?.SelectedItem?.ToString() ?? "STUDIO-MCR-01 (Main Program Feed)",
                2 => TxtFilePath?.Text ?? string.Empty,
                _ => null
            };

            string? mode = (CmbSdiVideoMode?.SelectedItem as ComboBoxItem)?.Content?.ToString();
            string? ch = (CmbSdiAudioCh?.SelectedItem as ComboBoxItem)?.Content?.ToString();

            await _sourceManager.SwitchSourceAsync((InputSourceType)selected, param, mode, ch);
        }

        private async void CmbSdiDevices_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized || CmbInputSource?.SelectedIndex != 0) return;
            string? dev = CmbSdiDevices.SelectedItem?.ToString();
            string? mode = (CmbSdiVideoMode?.SelectedItem as ComboBoxItem)?.Content?.ToString();
            string? ch = (CmbSdiAudioCh?.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (!string.IsNullOrEmpty(dev))
            {
                await _sourceManager.HandleSdiSourceAsync(dev, mode, ch);
            }
        }

        private async void CmbSdiVideoMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized || CmbInputSource?.SelectedIndex != 0) return;
            string? dev = CmbSdiDevices?.SelectedItem?.ToString() ?? "Blackmagic DeckLink 8K Pro (SDI 1 - 1080p59.94)";
            string? mode = (CmbSdiVideoMode?.SelectedItem as ComboBoxItem)?.Content?.ToString();
            string? ch = (CmbSdiAudioCh?.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (!string.IsNullOrEmpty(dev))
            {
                await _sourceManager.HandleSdiSourceAsync(dev, mode, ch);
            }
        }

        private async void CmbSdiAudioCh_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized || CmbInputSource?.SelectedIndex != 0) return;
            string? dev = CmbSdiDevices?.SelectedItem?.ToString() ?? "Blackmagic DeckLink 8K Pro (SDI 1 - 1080p59.94)";
            string? mode = (CmbSdiVideoMode?.SelectedItem as ComboBoxItem)?.Content?.ToString();
            string? ch = (CmbSdiAudioCh?.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (!string.IsNullOrEmpty(dev))
            {
                await _sourceManager.HandleSdiSourceAsync(dev, mode, ch);
            }

            var (_, _, summary) = GetStreamAudioConfig();
            UpdateStreamAudioSummaryUI(summary);

            if (_isStreaming)
            {
                await RestartMasterProgramStreamingWorkerAsync();
            }
        }

        private async void CmbNdiSources_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized || CmbInputSource?.SelectedIndex != 1) return;
            string? ndi = CmbNdiSources.SelectedItem?.ToString();
            if (!string.IsNullOrEmpty(ndi))
            {
                await _sourceManager.HandleNdiSourceAsync(ndi);
            }
        }

        private void UpdateSourceTelemetryUI(VideoSourceTelemetry telem)
        {
            if (telem == null) return;

            void Apply()
            {
                if (TxtSourceActiveName != null) TxtSourceActiveName.Text = telem.SourceName;
                if (TxtSourceSignalStatus != null) TxtSourceSignalStatus.Text = telem.Status;
                if (BadgeSourceLockStatus != null)
                {
                    BadgeSourceLockStatus.Background = telem.IsLocked
                        ? new SolidColorBrush(Color.FromRgb(0x1B, 0x5E, 0x20))
                        : new SolidColorBrush(Color.FromRgb(0xB7, 0x1C, 0x1C));
                }
                if (TxtSourceResolution != null) TxtSourceResolution.Text = telem.Resolution;
                if (TxtSourceFps != null) TxtSourceFps.Text = telem.FrameRate;
                if (TxtSourceVideoCodec != null) TxtSourceVideoCodec.Text = telem.VideoCodec;
                if (TxtSourceAudioInfo != null) TxtSourceAudioInfo.Text = telem.AudioFormat;
                if (TxtSourceColorSpace != null) TxtSourceColorSpace.Text = telem.ColorSpace;
                if (TxtSourcePipelineDetails != null) TxtSourcePipelineDetails.Text = telem.PipelineDetails;
            }

            if (_isClosing || Dispatcher.HasShutdownStarted) return;

            if (Dispatcher.CheckAccess())
            {
                Apply();
            }
            else
            {
                try { Dispatcher.BeginInvoke(Apply); } catch { }
            }
        }

        private byte[]? _currentProgramFrameBytes = null;
        private readonly object _programFrameLock = new();
        private static readonly byte[] _fallbackBlackFrame = InitializeFallbackBlackFrame();

        private static byte[] InitializeFallbackBlackFrame()
        {
            byte[] frame = new byte[1920 * 1080 * 4];
            for (int i = 3; i < frame.Length; i += 4)
            {
                frame[i] = 0xFF; // Opacity 100% (Solid Black BGRA32)
            }
            return frame;
        }

        private void RefreshMasterProgramFrameBuffer()
        {
            if (_isClosing || Dispatcher.HasShutdownStarted) return;

            if (!Dispatcher.CheckAccess())
            {
                try { Dispatcher.BeginInvoke(RefreshMasterProgramFrameBuffer); } catch { }
                return;
            }

            try
            {
                // Ưu tiên nạp trực tiếp byte buffer từ luồng NDI / SDI / File thực tế (Zero Render Latency, không tốn RenderTargetBitmap)
                if (_sourceManager.CurrentSource == InputSourceType.NDI || _sourceManager.CurrentSource == InputSourceType.SDI || _sourceManager.CurrentSource == InputSourceType.File)
                {
                    byte[]? rawLiveFrame = _sourceManager.LatestMasterFrame;
                    if (rawLiveFrame != null && rawLiveFrame.Length > 0)
                    {
                        lock (_programFrameLock)
                        {
                            _currentProgramFrameBytes = rawLiveFrame;
                        }
                        return;
                    }
                }

                FrameworkElement? visualToCapture = null;
                if (_sourceManager.CurrentSource == InputSourceType.Colorbar)
                {
                    visualToCapture = (FrameworkElement?)PnlColorbarPattern ?? (FrameworkElement?)PnlColorbarVisualHost;
                }
                else
                {
                    visualToCapture = (FrameworkElement?)ReviewView ?? (FrameworkElement?)PnlMasterProgramBus;
                }

                if (visualToCapture == null) return;

                int width = 1920;
                int height = 1080;

                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    var brush = new VisualBrush(visualToCapture)
                    {
                        Stretch = Stretch.Uniform,
                        AlignmentX = AlignmentX.Center,
                        AlignmentY = AlignmentY.Center
                    };
                    dc.DrawRectangle(brush, null, new Rect(0, 0, width, height));
                }

                var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);

                int stride = width * 4;
                byte[] raw = new byte[height * stride];
                rtb.CopyPixels(raw, stride, 0);

                lock (_programFrameLock)
                {
                    _currentProgramFrameBytes = raw;
                }
            }
            catch (Exception ex)
            {
                LogEvent("[WARN]", $"Không thể chụp khung hình Master Program: {ex.Message}");
            }
        }

        private void UpdateColorbarDisplay()
        {
            _sourceManager.UpdateColorbarDisplay();
            if (TxtColorbarIdentTitle != null)
            {
                TxtColorbarIdentTitle.Text = _colorbarEngine.GetPatternTitle();
            }
            if (TxtColorbarIdentTone != null)
            {
                TxtColorbarIdentTone.Text = $"AUDIO: {_colorbarEngine.GetToneDescription()}";
            }
        }

        private void CmbColorbarPattern_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;
            if (CmbColorbarPattern == null) return;

            _colorbarEngine.CurrentPattern = CmbColorbarPattern.SelectedIndex switch
            {
                0 => ColorbarPatternType.SmpteRp219,
                1 => ColorbarPatternType.Ebu100Percent,
                2 => ColorbarPatternType.GridAlignment,
                _ => ColorbarPatternType.SmpteRp219
            };

            if (CmbInputSource?.SelectedIndex == 3)
            {
                UpdateColorbarDisplay();
                RefreshMasterProgramFrameBuffer();
                string patternName = (CmbColorbarPattern.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "SMPTE RP 219";
                TxtActiveSourceBadge.Text = $"INPUT: COLORBAR ({patternName.Split(' ')[0]})";
                LogEvent("[INFO]", $"🎨 Đã áp dụng mẫu hình kiểm tra (WYSIWYG 1080p): {patternName}");
            }

            if (_currentMuxer != null)
            {
                _currentMuxer.CurrentPattern = _colorbarEngine.CurrentPattern;
            }
        }

        private async void CmbAudioTone_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;
            if (CmbAudioTone == null) return;

            _colorbarEngine.CurrentTone = CmbAudioTone.SelectedIndex switch
            {
                0 => AudioTestToneType.Sine1kHzMinus18dBFS,
                1 => AudioTestToneType.Sine1kHzMinus20dBFS,
                2 => AudioTestToneType.Glits400Hz,
                3 => AudioTestToneType.EbuToneIdent,
                _ => AudioTestToneType.Sine1kHzMinus18dBFS
            };

            if (CmbInputSource?.SelectedIndex == 3)
            {
                UpdateColorbarDisplay();
                RefreshMasterProgramFrameBuffer();
            }

            if (_currentMuxer != null)
            {
                _currentMuxer.CurrentTone = _colorbarEngine.CurrentTone;
            }

            string toneName = _colorbarEngine.GetToneDescription();
            LogEvent("[INFO]", $"🔊 Đã áp dụng cấu hình âm thanh Test Tone: {toneName}");

            var (_, _, summary) = GetStreamAudioConfig();
            UpdateStreamAudioSummaryUI(summary);

            // Nếu đang Live Stream ở chế độ Colorbar, tự động chuyển đổi luồng phát âm thanh mới
            if (_isStreaming && _sourceManager.CurrentSource == InputSourceType.Colorbar)
            {
                await RestartMasterProgramStreamingWorkerAsync();
            }
        }

        private async Task RestartMasterProgramStreamingWorkerAsync()
        {
            try
            {
                _transmissionCts?.Cancel();
                if (_streamProcess != null && !_streamProcess.HasExited)
                {
                    try { _streamProcess.Kill(true); } catch { }
                    _streamProcess.Dispose();
                    _streamProcess = null;
                }

                await Task.Delay(60);

                if (_isStreaming && _srtStream != null && _srtStream.IsRunning)
                {
                    StartMasterProgramStreamingProcess();
                }
            }
            catch (Exception ex)
            {
                LogEvent("[WARN]", $"Lỗi chuyển đổi luồng Master Program: {ex.Message}");
            }
        }

        #endregion

        #region SRT Protocol Configuration & Encryption

        private readonly List<SRTGroupMemberConfig> _dynamicGroupMembers = new();
        private List<NicInfo> _cachedNics = new();

        /// <summary>
        /// Quét danh sách Card mạng (NIC) và điền đồng bộ vào tất cả các ComboBox NIC trên form.
        /// </summary>
        private void RefreshAllNicLists(string? selSingle = null, string? selMemberA = null, string? selMemberB = null, string? selNewMember = null)
        {
            try
            {
                _cachedNics = NetworkInterfaceScanner.GetAvailableNetworkInterfaces();
                NetworkInterfaceScanner.PopulateNicComboBox(CmbSingleSourceNic, selSingle, _cachedNics);
                NetworkInterfaceScanner.PopulateNicComboBox(CmbMemberANic, selMemberA, _cachedNics);
                NetworkInterfaceScanner.PopulateNicComboBox(CmbMemberBNic, selMemberB, _cachedNics);
                NetworkInterfaceScanner.PopulateNicComboBox(CmbNewMemberNic, selNewMember, _cachedNics);
            }
            catch (Exception ex)
            {
                LogEvent("[WARN]", $"Quét card mạng: {ex.Message}");
            }
        }

        private void BtnScanNic_Click(object sender, RoutedEventArgs e)
        {
            RefreshAllNicLists();
            int activeNicCount = Math.Max(0, _cachedNics.Count - 1);
            LogEvent("[NETWORK]", $"🔄 Đã quét lại danh sách Card mạng (tìm thấy {activeNicCount} NIC IPv4 đang hoạt động).");
        }

        private void ChkGroupSocket_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            bool isGroup = ChkGroupSocket?.IsChecked == true;
            if (PnlGroupSocketConfig != null)
            {
                PnlGroupSocketConfig.Visibility = isGroup ? Visibility.Visible : Visibility.Collapsed;
            }
            if (PnlSingleStreamConfig != null)
            {
                PnlSingleStreamConfig.Visibility = isGroup ? Visibility.Collapsed : Visibility.Visible;
            }
            if (ChkGroupSocket != null)
            {
                ChkGroupSocket.Content = isGroup
                    ? "🛡️ SMPTE 2022-7: BẬT"
                    : "⚪ SMPTE 2022-7: TẮT";
            }
            if (BadgeGroupSocket != null)
            {
                BadgeGroupSocket.Visibility = isGroup ? Visibility.Visible : Visibility.Collapsed;
            }
            LogEvent("[SRT]", isGroup 
                ? "🛡️ Đã BẬT cơ chế Group Socket chuẩn SMPTE 2022-7 (Đa đường truyền Hitless Redundancy & Non-disruptive dynamic member attachment)." 
                : "🌐 Đã TẮT Group Socket, quay về chế độ Single Socket mặc định.");

            SaveCurrentSettings();
        }

        private void CmbGroupType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;
            string sel = (CmbGroupType?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Broadcast";
            LogEvent("[SRT]", $"Cập nhật cơ chế Group Socket: {sel}");
        }

        private void AddDynamicMemberUiCard(SRTGroupMemberConfig member)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(22, 28, 36)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0, 150, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 6)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var spInfo = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var header = new TextBlock
            {
                Text = $"MEMBER: {member.Name.ToUpper()} ({member.Host}:{member.Port})",
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 210, 255))
            };
            string nicDesc = string.IsNullOrWhiteSpace(member.LocalInterfaceIp) || member.LocalInterfaceIp == "0.0.0.0"
                ? "Card NIC: 0.0.0.0 (Mặc định - Tự động)"
                : $"Card NIC: {member.LocalInterfaceIp}";
            var subtext = new TextBlock
            {
                Text = nicDesc,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 160)),
                Margin = new Thickness(0, 2, 0, 0)
            };
            spInfo.Children.Add(header);
            spInfo.Children.Add(subtext);
            Grid.SetColumn(spInfo, 0);
            grid.Children.Add(spInfo);

            var btnDelete = new Button
            {
                Content = "🗑️ Xoá Member",
                Background = new SolidColorBrush(Color.FromRgb(211, 47, 47)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 10.5,
                Padding = new Thickness(10, 4, 10, 4),
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Gỡ bỏ Member Socket này (ngắt an toàn kể cả khi đang phát LIVE)"
            };
            btnDelete.Click += async (s, ev) =>
            {
                await RemoveDynamicMemberAsync(member, card);
            };
            Grid.SetColumn(btnDelete, 1);
            grid.Children.Add(btnDelete);

            card.Child = grid;
            StackDynamicMembers?.Children.Add(card);
        }

        private async Task RemoveDynamicMemberAsync(SRTGroupMemberConfig member, Border card)
        {
            _dynamicGroupMembers.Remove(member);
            if (StackDynamicMembers != null && card != null)
            {
                StackDynamicMembers.Children.Remove(card);
            }
            LogEvent("[GROUP_SMPTE2022_7]", $"Đã xóa member socket khỏi cấu hình: {member.Name} ({member.Host}:{member.Port})");

            // Nếu phiên phát sóng đang LIVE, ngắt kết nối an toàn trong thời gian thực mà KHÔNG làm gián đoạn luồng
            if (_isStreaming && _srtStream != null && _srtStream.IsRunning)
            {
                LogEvent("[GROUP_SMPTE2022_7]", $"⚡ Phiên phát đang LIVE: Đang gỡ bỏ Member {member.Name} (ID: {member.Id}) khỏi Group ngầm không làm gián đoạn luồng...");
                try
                {
                    bool removed = await _srtStream.RemoveMemberSocketAsync(member.Id);
                    if (removed)
                    {
                        LogEvent("[GROUP_SMPTE2022_7]", $"✅ Đã ngắt kết nối an toàn Member {member.Name} khỏi phiên phát sóng.");
                    }
                }
                catch (Exception ex)
                {
                    LogEvent("[ERROR]", $"Lỗi gỡ bỏ member socket khi đang phát sóng: {ex.Message}");
                }
            }

            SaveCurrentSettings();
        }

        private async void BtnAddGroupMember_Click(object sender, RoutedEventArgs e)
        {
            string name = TxtNewMemberName?.Text?.Trim() ?? "Path C (Backup)";
            string host = TxtNewMemberHost?.Text?.Trim() ?? "127.0.0.1";
            if (!int.TryParse(TxtNewMemberPort?.Text?.Trim(), out int port))
            {
                port = 9004;
            }
            string localNic = (CmbNewMemberNic?.SelectedValue as string) ?? "";
            if (localNic == "0.0.0.0") localNic = "";

            var member = new SRTGroupMemberConfig(name, host, port, localNic);
            _dynamicGroupMembers.Add(member);

            AddDynamicMemberUiCard(member);

            LogEvent("[GROUP_SMPTE2022_7]", $"Đã cấu hình thêm member socket mới: {name} ({host}:{port}) [NIC: {(string.IsNullOrEmpty(localNic) ? "0.0.0.0" : localNic)}]");

            // Nếu đang phát sóng trực tiếp, tự động gắn kết nối member socket này vào Group đang chạy KHÔNG ngắt luồng
            if (_isStreaming && _srtStream != null && _srtStream.IsRunning)
            {
                LogEvent("[GROUP_SMPTE2022_7]", $"⚡ Phiên phát đang LIVE: Tự động kết nối và gắn member socket {name} vào Group ngầm (Zero-Disruption)!");
                await _srtStream.AddMemberSocketAsync(member);
            }

            SaveCurrentSettings();
        }

        /// <summary>
        /// Nạp và khôi phục toàn bộ cấu hình từ file JSON (hoặc thiết lập mặc định nếu chạy lần đầu).
        /// </summary>
        private void LoadAndApplySettings()
        {
            try
            {
                var settings = AppSettingsManager.LoadSettings();

                // 1. Toggle SMPTE 2022-7
                bool isGroup = settings.IsSmpte2022_7Enabled;
                if (ChkGroupSocket != null)
                {
                    ChkGroupSocket.IsChecked = isGroup;
                    ChkGroupSocket.Content = isGroup
                        ? "🛡️ SMPTE 2022-7: BẬT"
                        : "⚪ SMPTE 2022-7: TẮT";
                }
                if (PnlGroupSocketConfig != null)
                {
                    PnlGroupSocketConfig.Visibility = isGroup ? Visibility.Visible : Visibility.Collapsed;
                }
                if (PnlSingleStreamConfig != null)
                {
                    PnlSingleStreamConfig.Visibility = isGroup ? Visibility.Collapsed : Visibility.Visible;
                }
                if (BadgeGroupSocket != null)
                {
                    BadgeGroupSocket.Visibility = isGroup ? Visibility.Visible : Visibility.Collapsed;
                }

                // 2. Điền và chọn Card mạng (NIC)
                RefreshAllNicLists(settings.SingleSourceNicIp, settings.MemberANicIp, settings.MemberBNicIp);

                // 3. Thông số Single Stream
                if (TxtSrtIp != null && !string.IsNullOrEmpty(settings.SrtIp)) TxtSrtIp.Text = settings.SrtIp;
                if (TxtSrtPort != null && settings.SrtPort > 0) TxtSrtPort.Text = settings.SrtPort.ToString();
                if (TxtSrtStreamId != null && settings.StreamId != null) TxtSrtStreamId.Text = settings.StreamId;

                // 4. Thông số SMPTE 2022-7
                if (CmbGroupType != null && settings.GroupTypeIndex >= 0 && settings.GroupTypeIndex < CmbGroupType.Items.Count)
                    CmbGroupType.SelectedIndex = settings.GroupTypeIndex;
                if (TxtDifferentialDelay != null && settings.DifferentialDelayMs > 0)
                    TxtDifferentialDelay.Text = settings.DifferentialDelayMs.ToString();
                if (TxtMemberAHost != null && !string.IsNullOrEmpty(settings.MemberAHost)) TxtMemberAHost.Text = settings.MemberAHost;
                if (TxtMemberAPort != null && settings.MemberAPort > 0) TxtMemberAPort.Text = settings.MemberAPort.ToString();
                if (TxtMemberBHost != null && !string.IsNullOrEmpty(settings.MemberBHost)) TxtMemberBHost.Text = settings.MemberBHost;
                if (TxtMemberBPort != null && settings.MemberBPort > 0) TxtMemberBPort.Text = settings.MemberBPort.ToString();

                // 5. Khôi phục các Dynamic Members
                _dynamicGroupMembers.Clear();
                StackDynamicMembers?.Children.Clear();
                if (settings.DynamicMembers != null)
                {
                    foreach (var dm in settings.DynamicMembers)
                    {
                        var member = new SRTGroupMemberConfig(dm.Name, dm.Host, dm.Port, dm.NicIp == "0.0.0.0" ? "" : dm.NicIp)
                        {
                            Id = string.IsNullOrEmpty(dm.Id) ? Guid.NewGuid().ToString("N") : dm.Id
                        };
                        _dynamicGroupMembers.Add(member);
                        AddDynamicMemberUiCard(member);
                    }
                }

                // 6. Thông số Video / Codec / Encoder
                if (settings.BitrateKbps > 0)
                {
                    if (SldTargetBitrate != null) SldTargetBitrate.Value = settings.BitrateKbps;
                    if (TxtTargetBitrateInput != null) TxtTargetBitrateInput.Text = settings.BitrateKbps.ToString();
                }
                if (CmbVideoCodec != null && settings.VideoCodecIndex >= 0 && settings.VideoCodecIndex < CmbVideoCodec.Items.Count)
                {
                    CmbVideoCodec.SelectedIndex = settings.VideoCodecIndex;
                }
                if (CmbHardwareEncoder != null && !string.IsNullOrEmpty(settings.HardwareEncoder))
                {
                    for (int i = 0; i < CmbHardwareEncoder.Items.Count; i++)
                    {
                        string? itemStr = CmbHardwareEncoder.Items[i]?.ToString();
                        if (itemStr != null && itemStr.Contains(settings.HardwareEncoder, StringComparison.OrdinalIgnoreCase))
                        {
                            CmbHardwareEncoder.SelectedIndex = i;
                            break;
                        }
                    }
                }
                if (CmbStreamFrameRate != null && settings.StreamFrameRateIndex >= 0 && settings.StreamFrameRateIndex < CmbStreamFrameRate.Items.Count)
                {
                    CmbStreamFrameRate.SelectedIndex = settings.StreamFrameRateIndex;
                }
                if (CmbRateControl != null && settings.RateControlIndex >= 0 && settings.RateControlIndex < CmbRateControl.Items.Count)
                {
                    CmbRateControl.SelectedIndex = settings.RateControlIndex;
                }
                if (CmbEncoderPreset != null && settings.EncoderPresetIndex >= 0 && settings.EncoderPresetIndex < CmbEncoderPreset.Items.Count)
                {
                    CmbEncoderPreset.SelectedIndex = settings.EncoderPresetIndex;
                }
                if (ChkUltraLowLatency != null)
                {
                    ChkUltraLowLatency.IsChecked = settings.UltraLowLatency;
                }

                // 7. Thông số Protocol & Encryption
                if (CmbSrtMode != null && settings.SrtModeIndex >= 0 && settings.SrtModeIndex < CmbSrtMode.Items.Count)
                {
                    CmbSrtMode.SelectedIndex = settings.SrtModeIndex;
                }
                if (TxtManualLatency != null && settings.LatencyMs > 0)
                {
                    TxtManualLatency.Text = settings.LatencyMs.ToString();
                }
                if (ChkAutoLatency != null)
                {
                    ChkAutoLatency.IsChecked = settings.AutoLatency;
                }
                if (ChkEnableEncryption != null)
                {
                    ChkEnableEncryption.IsChecked = settings.EncryptionEnabled;
                }
                if (TxtSrtPassphrase != null && !string.IsNullOrEmpty(settings.Passphrase))
                {
                    TxtSrtPassphrase.Password = settings.Passphrase;
                }
                if (CmbKeyLength != null && settings.KeyLengthIndex >= 0 && settings.KeyLengthIndex < CmbKeyLength.Items.Count)
                {
                    CmbKeyLength.SelectedIndex = settings.KeyLengthIndex;
                }

                // 8. Audio & NTP
                if (CmbStreamAudioChannels != null && settings.AudioChannelsIndex >= 0 && settings.AudioChannelsIndex < CmbStreamAudioChannels.Items.Count)
                {
                    CmbStreamAudioChannels.SelectedIndex = settings.AudioChannelsIndex;
                }
                if (ChkNtpSync != null)
                {
                    ChkNtpSync.IsChecked = settings.NtpSyncEnabled;
                }
                if (TxtNtpServer != null && !string.IsNullOrEmpty(settings.NtpServer))
                {
                    TxtNtpServer.Text = settings.NtpServer;
                }

                // 9. Telemetry Monitor
                if (ChkEnableTelemetry != null)
                {
                    ChkEnableTelemetry.IsChecked = settings.IsTelemetryMonitorEnabled;
                }
                if (PnlTelemetryConfig != null)
                {
                    PnlTelemetryConfig.Visibility = settings.IsTelemetryMonitorEnabled ? Visibility.Visible : Visibility.Collapsed;
                }
                if (TxtTelemetryServerUrl != null && !string.IsNullOrEmpty(settings.TelemetryServerUrl))
                {
                    TxtTelemetryServerUrl.Text = settings.TelemetryServerUrl;
                }
                if (TxtTelemetryNodeName != null && !string.IsNullOrEmpty(settings.TelemetryNodeName))
                {
                    TxtTelemetryNodeName.Text = settings.TelemetryNodeName;
                }

                LogEvent("[SETTINGS]", "✅ Đã nạp thành công cấu hình phiên làm việc.");
            }
            catch (Exception ex)
            {
                LogEvent("[WARN]", $"Lỗi nạp cấu hình: {ex.Message}");
            }
        }

        /// <summary>
        /// Lưu cấu hình hiện tại của UI xuống ổ đĩa dạng JSON.
        /// </summary>
        private void SaveCurrentSettings()
        {
            try
            {
                var settings = new SrtEncodeSettings
                {
                    IsSmpte2022_7Enabled = ChkGroupSocket?.IsChecked == true,
                    SrtIp = TxtSrtIp?.Text?.Trim() ?? "127.0.0.1",
                    SrtPort = int.TryParse(TxtSrtPort?.Text?.Trim(), out int sp) ? sp : 9000,
                    StreamId = TxtSrtStreamId?.Text?.Trim() ?? "live/cam1/feed",
                    SingleSourceNicIp = (CmbSingleSourceNic?.SelectedValue as string) ?? "0.0.0.0",

                    GroupTypeIndex = CmbGroupType?.SelectedIndex ?? 0,
                    DifferentialDelayMs = int.TryParse(TxtDifferentialDelay?.Text?.Trim(), out int dd) ? dd : 50,
                    MemberAHost = TxtMemberAHost?.Text?.Trim() ?? "127.0.0.1",
                    MemberAPort = int.TryParse(TxtMemberAPort?.Text?.Trim(), out int maPort) ? maPort : 9000,
                    MemberANicIp = (CmbMemberANic?.SelectedValue as string) ?? "0.0.0.0",
                    MemberBHost = TxtMemberBHost?.Text?.Trim() ?? "127.0.0.1",
                    MemberBPort = int.TryParse(TxtMemberBPort?.Text?.Trim(), out int mbPort) ? mbPort : 9002,
                    MemberBNicIp = (CmbMemberBNic?.SelectedValue as string) ?? "0.0.0.0",

                    DynamicMembers = _dynamicGroupMembers.Select(m => new DynamicMemberSetting
                    {
                        Id = m.Id,
                        Name = m.Name,
                        Host = m.Host,
                        Port = m.Port,
                        NicIp = string.IsNullOrEmpty(m.LocalInterfaceIp) ? "0.0.0.0" : m.LocalInterfaceIp
                    }).ToList(),

                    BitrateKbps = (int)(SldTargetBitrate?.Value ?? 6000),
                    VideoCodecIndex = CmbVideoCodec?.SelectedIndex ?? 0,
                    HardwareEncoder = CmbHardwareEncoder?.SelectedItem?.ToString() ?? "Intel QuickSync Video (QSV)",
                    StreamFrameRateIndex = CmbStreamFrameRate?.SelectedIndex ?? 0,
                    RateControlIndex = CmbRateControl?.SelectedIndex ?? 0,
                    EncoderPresetIndex = CmbEncoderPreset?.SelectedIndex ?? 0,
                    UltraLowLatency = ChkUltraLowLatency?.IsChecked == true,

                    SrtModeIndex = CmbSrtMode?.SelectedIndex ?? 0,
                    LatencyMs = int.TryParse(TxtManualLatency?.Text?.Trim(), out int lat) ? lat : 120,
                    AutoLatency = ChkAutoLatency?.IsChecked == true,
                    EncryptionEnabled = ChkEnableEncryption?.IsChecked == true,
                    Passphrase = TxtSrtPassphrase?.Password ?? string.Empty,
                    KeyLengthIndex = CmbKeyLength?.SelectedIndex ?? 2,

                    AudioChannelsIndex = CmbStreamAudioChannels?.SelectedIndex ?? 0,
                    NtpSyncEnabled = ChkNtpSync?.IsChecked == true,
                    NtpServer = TxtNtpServer?.Text?.Trim() ?? "time.google.com",

                    IsTelemetryMonitorEnabled = ChkEnableTelemetry?.IsChecked == true,
                    TelemetryServerUrl = TxtTelemetryServerUrl?.Text?.Trim() ?? "http://127.0.0.1:8088",
                    TelemetryNodeName = TxtTelemetryNodeName?.Text?.Trim() ?? "ENC_CAM_01"
                };

                AppSettingsManager.SaveSettings(settings);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Settings] Lỗi lưu cấu hình: {ex.Message}");
            }
        }

        private void ChkEnableTelemetry_Changed(object sender, RoutedEventArgs e)
        {
            bool isEnabled = ChkEnableTelemetry?.IsChecked == true;
            if (PnlTelemetryConfig != null)
                PnlTelemetryConfig.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;

            if (isEnabled)
            {
                if (TxtTelemetryStatus != null) TxtTelemetryStatus.Text = "🟡 Chờ phát sóng luồng...";
                if (_isTransmissionActive)
                {
                    StartTelemetryReporting();
                }
            }
            else
            {
                StopTelemetryReporting();
                if (LedTelemetryPulse != null) LedTelemetryPulse.Fill = new SolidColorBrush(Color.FromRgb(85, 85, 85));
                if (TxtTelemetryStatus != null) TxtTelemetryStatus.Text = "⚪ Tắt (Chưa kích hoạt giám sát)";
            }

            SaveCurrentSettings();
        }

        private void TelemetryInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitialized) return;
            SaveCurrentSettings();
        }

        private void StartTelemetryReporting()
        {
            if (ChkEnableTelemetry?.IsChecked != true) return;
            StopTelemetryReporting();

            string url = TxtTelemetryServerUrl?.Text?.Trim() ?? "http://127.0.0.1:8088";
            string nodeName = TxtTelemetryNodeName?.Text?.Trim() ?? "ENC_CAM_01";

            _telemetryReporter = new SRTTelemetryReporter(() =>
            {
                var packet = new SRTTelemetryPacket
                {
                    NodeName = nodeName,
                    NodeType = "Encoder",
                    IsConnected = _isTransmissionActive && (_srtStream?.IsRunning == true),
                    Fps = _currentFps > 0 ? _currentFps : (_srtStream?.Statistics?.CurrentFps ?? 0),
                    BitrateKbps = _currentBitrateKbps > 0 ? _currentBitrateKbps : (_srtStream?.Statistics?.CurrentBitrateKbps ?? 0),
                    RttMs = _currentRttMs > 0 ? _currentRttMs : (_srtStream?.Statistics?.RttMs ?? 0),
                    LossPercent = _currentPacketLoss > 0 ? _currentPacketLoss : (_srtStream?.Statistics?.PacketLossPercent ?? 0),
                    UptimeSeconds = _streamStartTime != DateTime.MinValue ? (DateTime.UtcNow - _streamStartTime).TotalSeconds : 0,
                    StreamUri = _activeSrtConfig?.ToSrtUri() ?? string.Empty
                };

                if (_srtStream != null && _srtStream.Config.GroupSocketEnabled)
                {
                    var groupStats = _srtStream.GroupStats;
                    packet.IsSmpte2022_7Active = true;
                    var members = _srtStream.MemberStatuses;
                    packet.PathAConnected = members.Count > 0 && members[0].IsConnected;
                    packet.PathBConnected = members.Count > 1 && members[1].IsConnected;
                    packet.PathAPackets = (long)groupStats.PathAPackets;
                    packet.PathBPackets = (long)groupStats.PathBPackets;
                    packet.MergedPackets = (long)groupStats.RecoveredFromRedundantPath;
                    packet.DroppedDuplicates = (long)groupStats.DuplicatesDropped;
                    packet.DifferentialDelayMs = _srtStream.Config.HitlessDifferentialDelayMs;
                }

                return packet;
            })
            {
                ServerUrl = url,
                NodeName = nodeName,
                NodeType = "Encoder"
            };

            _telemetryReporter.HeartbeatPulse += (success, error) =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (success)
                    {
                        if (LedTelemetryPulse != null) LedTelemetryPulse.Fill = new SolidColorBrush(Color.FromRgb(0, 230, 118)); // Green
                        if (TxtTelemetryStatus != null) TxtTelemetryStatus.Text = $"🟢 Đang gửi nhịp tim ({DateTime.Now:HH:mm:ss})";
                    }
                    else
                    {
                        if (LedTelemetryPulse != null) LedTelemetryPulse.Fill = new SolidColorBrush(Color.FromRgb(244, 67, 54)); // Red
                        if (TxtTelemetryStatus != null) TxtTelemetryStatus.Text = $"🔴 Lỗi gửi: {error}";
                    }
                });
            };

            _telemetryReporter.Start();
        }

        private void StopTelemetryReporting()
        {
            if (_telemetryReporter != null)
            {
                _telemetryReporter.Stop();
                _telemetryReporter.Dispose();
                _telemetryReporter = null;
            }
        }

        private void UpdateGroupMemberUI(SRTGroupMemberStatus status)
        {
            if (status == null) return;
            if (status.Name.Contains("Path A", StringComparison.OrdinalIgnoreCase))
            {
                if (LedMemberA != null)
                    LedMemberA.Fill = status.IsConnected ? new SolidColorBrush(Color.FromRgb(76, 175, 80)) : new SolidColorBrush(Color.FromRgb(244, 67, 54));
                if (TxtMemberAStatus != null)
                    TxtMemberAStatus.Text = status.IsConnected ? $"LIVE ({status.RttMs:F0}ms / {status.PacketLossPercent:F1}% loss)" : status.StatusText;
            }
            else if (status.Name.Contains("Path B", StringComparison.OrdinalIgnoreCase))
            {
                if (LedMemberB != null)
                    LedMemberB.Fill = status.IsConnected ? new SolidColorBrush(Color.FromRgb(76, 175, 80)) : new SolidColorBrush(Color.FromRgb(244, 67, 54));
                if (TxtMemberBStatus != null)
                    TxtMemberBStatus.Text = status.IsConnected ? $"LIVE ({status.RttMs:F0}ms / {status.PacketLossPercent:F1}% loss)" : status.StatusText;
            }
        }

        private void UpdateGroupStatsUI(SMPTE2022_7Stats stats)
        {
            if (stats == null) return;
            if (TxtGroupProtectionStatus != null)
            {
                TxtGroupProtectionStatus.Text = stats.ConnectedMembersCount >= 2 
                    ? "🛡️ SMPTE 2022-7: ARMED (100% Hitless Protection - Đa Đường Truyền)" 
                    : (stats.ConnectedMembersCount == 1 ? "⚠️ SMPTE 2022-7: SINGLE LINK (Đang Tự Động Kết Nối Member Phụ)" : "❌ SMPTE 2022-7: NO LINKS CONNECTED");
                TxtGroupProtectionStatus.Foreground = stats.ConnectedMembersCount >= 2
                    ? new SolidColorBrush(Color.FromRgb(0, 230, 118))
                    : (stats.ConnectedMembersCount == 1 ? new SolidColorBrush(Color.FromRgb(255, 179, 0)) : new SolidColorBrush(Color.FromRgb(244, 67, 54)));
            }
            if (TxtGroupStatsDetail != null)
            {
                TxtGroupStatsDetail.Text = $"Path A: {stats.PathAPackets:N0} pkts | Path B: {stats.PathBPackets:N0} pkts | Duplicates Dropped: {stats.DuplicatesDropped:N0} | Recovered: {stats.RecoveredFromRedundantPath:N0}";
            }
            if (TxtGroupLinkCount != null)
            {
                TxtGroupLinkCount.Text = $"{stats.ConnectedMembersCount}/{stats.TotalMembersCount} Links Active";
            }
            if (TxtBadgeGroupSocketText != null)
            {
                TxtBadgeGroupSocketText.Text = $"SMPTE 2022-7 ({stats.ConnectedMembersCount}/{stats.TotalMembersCount} LINKS)";
            }
        }

        private void ChkAutoLatency_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            bool isAuto = ChkAutoLatency.IsChecked == true;
            if (TxtManualLatency != null)
            {
                TxtManualLatency.IsEnabled = !isAuto;
            }

            if (isAuto)
            {
                int calculatedLatency = (int)Math.Max(120.0, Math.Round(_currentRttMs * 3.0));
                if (TxtCalculatedLatency != null)
                    TxtCalculatedLatency.Text = $"{calculatedLatency} ms (3 x {_currentRttMs:F0}ms RTT)";
                if (TxtManualLatency != null)
                    TxtManualLatency.Text = calculatedLatency.ToString();
                LogEvent("[SRT]", "Kích hoạt chế độ Auto Latency (3 x RTT, min 120ms).");
            }
            else
            {
                LogEvent("[SRT]", "Chuyển sang chế độ nhập Latency thủ công.");
            }
        }

        private double GetSelectedStreamFps()
        {
            if (CmbStreamFrameRate?.SelectedItem is ComboBoxItem item)
            {
                string text = item.Content?.ToString() ?? "";
                if (text.Contains("Auto", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(_sourceManager.CurrentTelemetry.FrameRate))
                    {
                        string rawFps = _sourceManager.CurrentTelemetry.FrameRate.Replace("FPS", "").Trim();
                        int spaceIdx = rawFps.IndexOf(' ');
                        if (spaceIdx > 0) rawFps = rawFps.Substring(0, spaceIdx).Trim();
                        if (double.TryParse(rawFps, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedFps) && parsedFps > 0)
                        {
                            return parsedFps;
                        }
                    }
                    return 59.94;
                }
                if (text.StartsWith("60")) return 60.0;
                if (text.StartsWith("59.94")) return 59.94;
                if (text.StartsWith("50")) return 50.0;
                if (text.StartsWith("30")) return 30.0;
                if (text.StartsWith("29.97")) return 29.97;
                if (text.StartsWith("25")) return 25.0;
                if (text.StartsWith("24")) return 24.0;
            }
            return 59.94;
        }

        private void CmbStreamFrameRate_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;
            double fps = GetSelectedStreamFps();
            double intervalMs = 1000.0 / fps;
            if (TxtFrameIntervalSummary != null)
            {
                TxtFrameIntervalSummary.Text = $"{intervalMs:F2} ms / Frame (Broadcast Paced)";
            }
            LogEvent("[CONFIG]", $"Tốc độ khung hình truyền dẫn SRT cập nhật: {fps:F2} FPS ({intervalMs:F2} ms / frame)");
            UpdateRealtimeTelemetry();
            if (_isStreaming && _srtStream != null && _srtStream.IsRunning)
            {
                _ = RestartMasterProgramStreamingWorkerAsync();
            }
        }

        private void ChkEnableEncryption_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            UpdateEncryptionState();
        }

        private void UpdateEncryptionState()
        {
            if (PnlEncryptionConfig == null || TxtHudEncryption == null) return;

            bool isEncrypted = ChkEnableEncryption.IsChecked == true;
            PnlEncryptionConfig.Visibility = isEncrypted ? Visibility.Visible : Visibility.Collapsed;

            if (isEncrypted)
            {
                string keyLen = (CmbKeyLength.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "AES-256";
                TxtHudEncryption.Text = $"AES-256 (Protected)";
                TxtHudEncryption.Foreground = new SolidColorBrush(Color.FromRgb(0, 230, 118));
                LogEvent("[SRT]", "Đã bật mã hóa bảo mật luồng phát SRT (AES Encryption ENABLED).");
            }
            else
            {
                TxtHudEncryption.Text = "None (Plaintext)";
                TxtHudEncryption.Foreground = new SolidColorBrush(Color.FromRgb(158, 158, 158));
                LogEvent("[SRT]", "Đã tắt mã hóa bảo mật luồng phát SRT (Encryption OFF).");
            }
        }

        #endregion

        #region Ultra Low-Latency Preset

        private void ChkUltraLowLatency_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            UpdateUltraLowLatencyState();
        }

        private void UpdateUltraLowLatencyState()
        {
            if (BadgeLowLatency == null || TxtBframesVal == null || CmbEncoderPreset == null || TxtGopVal == null || CmbRateControl == null) return;

            bool isUll = ChkUltraLowLatency.IsChecked == true;
            BadgeLowLatency.Visibility = isUll ? Visibility.Visible : Visibility.Collapsed;

            if (isUll)
            {
                // Force Ultra Low-Latency parameters:
                // 1. B-Frames = 0
                // 2. Encoder Preset = Low-Latency / Zerolatency
                // 3. GOP = 1.0s
                // 4. Rate Control = CBR
                TxtBframesVal.Text = "0 (FORCED OFF - NO DELAY)";
                TxtBframesVal.Foreground = new SolidColorBrush(Color.FromRgb(0, 230, 118));

                CmbEncoderPreset.SelectedIndex = 0; // Low-Latency / Zerolatency
                CmbEncoderPreset.IsEnabled = false;

                TxtGopVal.Text = "1.0 Second (60 Frames @ 60fps)";
                TxtGopVal.Foreground = new SolidColorBrush(Color.FromRgb(0, 230, 118));

                CmbRateControl.SelectedIndex = 0; // CBR
                CmbRateControl.IsEnabled = false;

                LogEvent("[INFO]", "⚡ Kích hoạt chế độ Ultra Low-Latency Engine: B-Frames=0, Zerolatency, GOP=1s, CBR.");
            }
            else
            {
                TxtBframesVal.Text = "2 (Standard B-Frames)";
                TxtBframesVal.Foreground = new SolidColorBrush(Color.FromRgb(255, 193, 7));

                CmbEncoderPreset.IsEnabled = true;
                TxtGopVal.Text = "2.0 Seconds (120 Frames @ 60fps)";
                CmbRateControl.IsEnabled = true;

                LogEvent("[INFO]", "Tắt chế độ Ultra Low-Latency. Chuyển sang cấu hình Standard Encoding.");
            }

            UpdateTargetSummary();
        }

        private bool _isUpdatingBitrate = false;

        private void SldTargetBitrate_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized || _isUpdatingBitrate) return;
            try
            {
                _isUpdatingBitrate = true;
                int bitrateKbps = (int)e.NewValue;

                if (TxtTargetBitrateInput != null && TxtTargetBitrateInput.Text != bitrateKbps.ToString())
                {
                    TxtTargetBitrateInput.Text = bitrateKbps.ToString();
                }

                if (TxtBitrateDisplay != null)
                {
                    TxtBitrateDisplay.Text = $"({(bitrateKbps / 1000.0):F1} Mbps)";
                }

                UpdateTargetSummary();
            }
            finally
            {
                _isUpdatingBitrate = false;
            }
        }

        private void TxtTargetBitrateInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitialized || _isUpdatingBitrate) return;
            if (TxtTargetBitrateInput == null || SldTargetBitrate == null) return;

            string text = TxtTargetBitrateInput.Text.Trim();
            if (int.TryParse(text, out int bitrateKbps))
            {
                try
                {
                    _isUpdatingBitrate = true;

                    // Expand slider upper range dynamically if user enters high bitrate (e.g. up to 100 Mbps)
                    if (bitrateKbps > SldTargetBitrate.Maximum)
                    {
                        SldTargetBitrate.Maximum = Math.Max(25000, bitrateKbps);
                    }

                    if (bitrateKbps >= SldTargetBitrate.Minimum && bitrateKbps <= SldTargetBitrate.Maximum)
                    {
                        if (Math.Abs(SldTargetBitrate.Value - bitrateKbps) > 0.5)
                        {
                            SldTargetBitrate.Value = bitrateKbps;
                        }
                    }

                    if (TxtBitrateDisplay != null)
                    {
                        TxtBitrateDisplay.Text = $"({(bitrateKbps / 1000.0):F1} Mbps)";
                    }

                    UpdateTargetSummary();
                }
                finally
                {
                    _isUpdatingBitrate = false;
                }
            }
        }

        private void CmbHardwareEncoder_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;
            UpdateCodecCapabilities();
            UpdateTargetSummary();
        }

        private void CmbVideoCodec_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;
            UpdateTargetSummary();
        }

        private void VideoPipelineMode_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            bool isPassthrough = RbPassthrough?.IsChecked == true;

            if (PnlVideoCodecContainer != null)
            {
                PnlVideoCodecContainer.Visibility = isPassthrough ? Visibility.Collapsed : Visibility.Visible;
            }

            if (PnlPassthroughNotice != null)
            {
                PnlPassthroughNotice.Visibility = isPassthrough ? Visibility.Visible : Visibility.Collapsed;
            }

            UpdateTargetSummary();

            if (isPassthrough)
            {
                LogEvent("[PIPELINE]", "⚡ Đã chuyển sang chế độ Direct Passthrough (Truyền trực tiếp luồng bitstream gốc, bỏ qua Video Encoder).");
            }
            else
            {
                string codecStr = (CmbVideoCodec?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "H.264 / AVC";
                LogEvent("[PIPELINE]", $"🎛️ Đã chuyển sang chế độ Encoder Pipeline (Mã hóa thời gian thực qua {codecStr}).");
            }
        }

        private void UpdateCodecCapabilities()
        {
            if (CmbHardwareEncoder == null || CmbVideoCodec == null) return;

            string selectedEncoderText = (CmbHardwareEncoder.SelectedItem as ComboBoxItem)?.Content?.ToString() 
                                        ?? CmbHardwareEncoder.SelectedItem?.ToString() 
                                        ?? "";
            bool supportsH265 = EvaluateH265Support(selectedEncoderText);
            bool supportsAv1 = EvaluateAv1Support(selectedEncoderText);

            if (CmbItemH265 != null)
            {
                if (supportsH265)
                {
                    CmbItemH265.IsEnabled = true;
                    CmbItemH265.Content = "H.265 / HEVC (Ultra High Efficiency - Hardware Supported)";
                    CmbItemH265.Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255));
                }
                else
                {
                    CmbItemH265.IsEnabled = false;
                    CmbItemH265.Content = "H.265 / HEVC (Không hỗ trợ bởi Engine/GPU đã chọn)";
                    CmbItemH265.Foreground = new SolidColorBrush(Color.FromRgb(128, 128, 128));

                    // If H.265 was selected, automatically revert to H.264
                    if (CmbVideoCodec.SelectedIndex == 1)
                    {
                        CmbVideoCodec.SelectedIndex = 0;
                        LogEvent("[CODEC]", $"⚠️ {selectedEncoderText} không hỗ trợ mã hóa H.265. Tự động chuyển Video Codec về H.264 / AVC.");
                    }
                }
            }

            if (CmbItemAV1 != null)
            {
                if (supportsAv1)
                {
                    CmbItemAV1.IsEnabled = true;
                    CmbItemAV1.Content = "AV1 (AOMedia Video 1 Next-Gen - Hardware Supported)";
                    CmbItemAV1.Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255));
                }
                else
                {
                    CmbItemAV1.IsEnabled = false;
                    CmbItemAV1.Content = "AV1 (Không hỗ trợ bởi Hardware Engine đã chọn)";
                    CmbItemAV1.Foreground = new SolidColorBrush(Color.FromRgb(128, 128, 128));

                    // If AV1 was selected, automatically revert to H.264
                    if (CmbVideoCodec.SelectedIndex == 2)
                    {
                        CmbVideoCodec.SelectedIndex = 0;
                        LogEvent("[CODEC]", $"⚠️ {selectedEncoderText} không hỗ trợ phần cứng mã hóa AV1. Tự động chuyển Video Codec về H.264 / AVC.");
                    }
                }
            }
        }

        private static bool EvaluateH265Support(string encoderText)
        {
            if (string.IsNullOrWhiteSpace(encoderText)) return true;

            // 1. Software CPU (x264 zerolatency) is AVC H.264 only
            if (encoderText.Contains("Software", StringComparison.OrdinalIgnoreCase) || encoderText.Contains("x264", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // 2. NVIDIA NVENC
            if (encoderText.Contains("NVENC", StringComparison.OrdinalIgnoreCase) || encoderText.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
            {
                // GPUs without HEVC encode support: GT 1030, GT 710, GT 730, GTX 750, Kepler, Fermi
                if (encoderText.Contains("GT 1030", StringComparison.OrdinalIgnoreCase) ||
                    encoderText.Contains("GT 710", StringComparison.OrdinalIgnoreCase) ||
                    encoderText.Contains("GT 730", StringComparison.OrdinalIgnoreCase) ||
                    encoderText.Contains("GTX 750", StringComparison.OrdinalIgnoreCase) ||
                    encoderText.Contains("GTX 745", StringComparison.OrdinalIgnoreCase) ||
                    encoderText.Contains("GT 6", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                return true;
            }

            // 3. Intel QuickSync Video (QSV)
            if (encoderText.Contains("QuickSync", StringComparison.OrdinalIgnoreCase) || encoderText.Contains("QSV", StringComparison.OrdinalIgnoreCase) || encoderText.Contains("Intel", StringComparison.OrdinalIgnoreCase))
            {
                // Legacy Intel GPUs without HEVC encode support: HD Graphics 4000, 4400, 4600, 2500, 3000, 2000
                if (encoderText.Contains("HD Graphics 4", StringComparison.OrdinalIgnoreCase) ||
                    encoderText.Contains("HD Graphics 3", StringComparison.OrdinalIgnoreCase) ||
                    encoderText.Contains("HD Graphics 2", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                return true; // Skylake Gen 9+, Kaby Lake, Coffee Lake, Alder Lake, Arc (140T, A-Series) all support H.265
            }

            // 4. AMD AMF Video Engine
            if (encoderText.Contains("AMD", StringComparison.OrdinalIgnoreCase) || encoderText.Contains("AMF", StringComparison.OrdinalIgnoreCase))
            {
                // Legacy AMD GPUs with VCE 1.0 (HD 7000, HD 8000, R7 240, R7 250) only support H.264
                if (encoderText.Contains("HD 7", StringComparison.OrdinalIgnoreCase) ||
                    encoderText.Contains("HD 8", StringComparison.OrdinalIgnoreCase) ||
                    encoderText.Contains("R7 240", StringComparison.OrdinalIgnoreCase) ||
                    encoderText.Contains("R7 250", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                return true;
            }

            return true;
        }

        private static bool EvaluateAv1Support(string encoderText)
        {
            if (string.IsNullOrWhiteSpace(encoderText)) return false;

            if (encoderText.Contains("Không phát hiện GPU", StringComparison.OrdinalIgnoreCase) || 
                encoderText.Contains("No Hardware Detected", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // 1. Software CPU (x264 zerolatency) does not support HW AV1
            if (encoderText.Contains("Software", StringComparison.OrdinalIgnoreCase) || encoderText.Contains("x264", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // 2. NVIDIA NVENC (AV1 encode requires Ada Lovelace / RTX 40-series, Blackwell / RTX 50-series, L4, L40)
            if (encoderText.Contains("NVENC", StringComparison.OrdinalIgnoreCase) || encoderText.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
            {
                if (encoderText.Contains("RTX 40", StringComparison.OrdinalIgnoreCase) ||
                    encoderText.Contains("RTX 50", StringComparison.OrdinalIgnoreCase) ||
                    encoderText.Contains("Ada", StringComparison.OrdinalIgnoreCase) ||
                    encoderText.Contains("Blackwell", StringComparison.OrdinalIgnoreCase) ||
                    encoderText.Contains("L40", StringComparison.OrdinalIgnoreCase) ||
                    encoderText.Contains("L4", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                return false;
            }

            // 3. Intel QuickSync Video (QSV) (AV1 encode requires Intel Arc A-Series, Intel Core Ultra / Meteor Lake / Lunar Lake / Arrow Lake / Arc 140T / Arc 140V)
            if (encoderText.Contains("QuickSync", StringComparison.OrdinalIgnoreCase) || encoderText.Contains("QSV", StringComparison.OrdinalIgnoreCase) || encoderText.Contains("Intel", StringComparison.OrdinalIgnoreCase))
            {
                if (encoderText.Contains("Arc", StringComparison.OrdinalIgnoreCase) ||
                    encoderText.Contains("Core Ultra", StringComparison.OrdinalIgnoreCase) ||
                    encoderText.Contains("Xe LPG", StringComparison.OrdinalIgnoreCase) ||
                    encoderText.Contains("Xe2", StringComparison.OrdinalIgnoreCase) ||
                    encoderText.Contains("A380", StringComparison.OrdinalIgnoreCase) ||
                    encoderText.Contains("A580", StringComparison.OrdinalIgnoreCase) ||
                    encoderText.Contains("A750", StringComparison.OrdinalIgnoreCase) ||
                    encoderText.Contains("A770", StringComparison.OrdinalIgnoreCase) ||
                    encoderText.Contains("140T", StringComparison.OrdinalIgnoreCase) ||
                    encoderText.Contains("140V", StringComparison.OrdinalIgnoreCase) ||
                    encoderText.Contains("130V", StringComparison.OrdinalIgnoreCase) ||
                    encoderText.Contains("140H", StringComparison.OrdinalIgnoreCase) ||
                    encoderText.Contains("155H", StringComparison.OrdinalIgnoreCase) ||
                    encoderText.Contains("185H", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                return false;
            }

            // 4. AMD AMF Video Engine (AV1 encode requires RDNA 3 / Radeon RX 7000 series, 780M/760M/890M/880M or newer)
            if (encoderText.Contains("AMD", StringComparison.OrdinalIgnoreCase) || encoderText.Contains("AMF", StringComparison.OrdinalIgnoreCase))
            {
                if (encoderText.Contains("RX 7", StringComparison.OrdinalIgnoreCase) ||
                    encoderText.Contains("RX 8", StringComparison.OrdinalIgnoreCase) ||
                    encoderText.Contains("780M", StringComparison.OrdinalIgnoreCase) ||
                    encoderText.Contains("760M", StringComparison.OrdinalIgnoreCase) ||
                    encoderText.Contains("890M", StringComparison.OrdinalIgnoreCase) ||
                    encoderText.Contains("880M", StringComparison.OrdinalIgnoreCase) ||
                    encoderText.Contains("RDNA 3", StringComparison.OrdinalIgnoreCase) ||
                    encoderText.Contains("RDNA 4", StringComparison.OrdinalIgnoreCase) ||
                    encoderText.Contains("Radeon 7", StringComparison.OrdinalIgnoreCase) ||
                    encoderText.Contains("Radeon 8", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                return false;
            }

            return false;
        }

        private async void BtnScanHardwareEncoder_Click(object sender, RoutedEventArgs e)
        {
            await ScanHardwareEncodersAsync();
            UpdateCodecCapabilities();
            UpdateTargetSummary();
        }

        private async Task ScanHardwareEncodersAsync()
        {
            try
            {
                LogEvent("[HARDWARE]", "Đang quét phần cứng tăng tốc mã hóa (GPU Hardware Encoders)...");
                if (CmbHardwareEncoder == null) return;

                var gpus = await Task.Run(() => DetectGpuAdapters());

                // Xác thực tính sẵn sàng thực tế của từng bộ mã hóa GPU qua FFmpeg (loại bỏ hoàn toàn GPU ảo / phantom devices)
                var nvencProbe = Task.Run(() => IsEncoderAvailable("h264_nvenc"));
                var qsvProbe = Task.Run(() => IsEncoderAvailable("h264_qsv"));
                var amfProbe = Task.Run(() => IsEncoderAvailable("h264_amf"));
                await Task.WhenAll(nvencProbe, qsvProbe, amfProbe);

                bool hasNvidia = nvencProbe.Result;
                bool hasIntel = qsvProbe.Result;
                bool hasAmd = amfProbe.Result;

                CmbHardwareEncoder.Items.Clear();
                int preferredIndex = -1;

                // 1. NVIDIA NVENC
                string nvidiaDesc = gpus.FirstOrDefault(g => g.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || g.Contains("RTX", StringComparison.OrdinalIgnoreCase) || g.Contains("GeForce", StringComparison.OrdinalIgnoreCase)) ?? "NVIDIA GPU";
                string nvencLabel = hasNvidia 
                    ? $"NVIDIA NVENC ({nvidiaDesc})" 
                    : "NVIDIA NVENC (Không khả dụng)";
                CmbHardwareEncoder.Items.Add(nvencLabel);
                if (hasNvidia && preferredIndex == -1) preferredIndex = CmbHardwareEncoder.Items.Count - 1;

                // 2. Intel QuickSync Video (QSV)
                string intelDesc = gpus.FirstOrDefault(g => g.Contains("Intel", StringComparison.OrdinalIgnoreCase) || g.Contains("Arc", StringComparison.OrdinalIgnoreCase) || g.Contains("Iris", StringComparison.OrdinalIgnoreCase)) ?? "Intel GPU";
                string qsvLabel = hasIntel 
                    ? $"Intel QuickSync Video (QSV - {intelDesc})" 
                    : "Intel QuickSync Video (QSV - Không khả dụng)";
                CmbHardwareEncoder.Items.Add(qsvLabel);
                if (hasIntel && preferredIndex == -1) preferredIndex = CmbHardwareEncoder.Items.Count - 1;

                // 3. AMD AMF Video Engine
                string amdDesc = gpus.FirstOrDefault(g => g.Contains("AMD", StringComparison.OrdinalIgnoreCase) || g.Contains("Radeon", StringComparison.OrdinalIgnoreCase)) ?? "AMD GPU";
                string amdLabel = hasAmd 
                    ? $"AMD AMF Video Engine ({amdDesc})" 
                    : "AMD AMF Video Engine (Không khả dụng)";
                CmbHardwareEncoder.Items.Add(amdLabel);
                if (hasAmd && preferredIndex == -1) preferredIndex = CmbHardwareEncoder.Items.Count - 1;

                // 4. Software CPU Fallback
                CmbHardwareEncoder.Items.Add("Software (x264 Zerolatency CPU)");

                // Chọn encoder phần cứng đầu tiên đã được kiểm chứng hoạt động, hoặc fallback về CPU
                if (preferredIndex == -1)
                {
                    preferredIndex = CmbHardwareEncoder.Items.Count - 1; // Fallback to Software CPU
                }

                CmbHardwareEncoder.SelectedIndex = preferredIndex;
                UpdateCodecCapabilities();
                UpdateTargetSummary();

                string selectedEngine = CmbHardwareEncoder.SelectedItem?.ToString() ?? "";
                if (gpus.Count > 0)
                {
                    LogEvent("[HARDWARE]", $"Phát hiện phần cứng GPU: {string.Join(", ", gpus)}");
                    LogEvent("[HARDWARE]", $"✅ Đã xác thực & chọn Hardware Engine: {selectedEngine}");
                }
                else
                {
                    LogEvent("[HARDWARE]", $"Đã nạp danh sách Hardware Encoders mặc định: {selectedEngine}");
                }
            }
            catch (Exception ex)
            {
                LogEvent("[WARN]", $"Lỗi quét phần cứng encoder: {ex.Message}");
            }
        }

        private static bool IsEncoderAvailable(string encoder)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = $"-hide_banner -loglevel error -f lavfi -i testsrc=duration=1 -frames:v 1 -c:v {encoder} -f null -",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc == null) return false;
                if (!proc.WaitForExit(1500))
                {
                    try { proc.Kill(); } catch { }
                    return false;
                }
                return proc.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        private static List<string> DetectGpuAdapters()
        {
            var gpuList = new List<string>();
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
                if (key != null)
                {
                    foreach (var subKeyName in key.GetSubKeyNames())
                    {
                        if (subKeyName.StartsWith("000"))
                        {
                            using var subKey = key.OpenSubKey(subKeyName);
                            var driverDesc = subKey?.GetValue("DriverDesc") as string;
                            if (!string.IsNullOrEmpty(driverDesc) && !driverDesc.Contains("Basic Display", StringComparison.OrdinalIgnoreCase))
                            {
                                if (!gpuList.Contains(driverDesc))
                                {
                                    gpuList.Add(driverDesc);
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fallback silently if registry access is restricted
            }

            return gpuList;
        }

        #endregion

        #region NTP & Wall-Clock Synchronization

        private void ChkNtpSync_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            bool isNtp = ChkNtpSync.IsChecked == true;
            if (BadgeNtpSync != null)
            {
                BadgeNtpSync.Visibility = isNtp ? Visibility.Visible : Visibility.Collapsed;
            }

            if (isNtp)
            {
                LogEvent("[NTP]", "🕒 BẬT Multi-Camera NTP Synchronization: Kích hoạt đồng bộ Master Clock định kỳ và nhúng Timecode.");
                string host = TxtNtpServer?.Text?.Trim() ?? "time.google.com";
                if (string.IsNullOrEmpty(host)) host = "time.google.com";
                _masterClock.StartPeriodicSync(host, 30);
            }
            else
            {
                _masterClock.StopPeriodicSync();
                if (TxtNtpOffset != null)
                {
                    TxtNtpOffset.Text = "Disabled (Free-Run)";
                    TxtNtpOffset.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
                }
                LogEvent("[NTP]", "TẮT Multi-Camera NTP Synchronization.");
            }
        }

        private async void BtnSyncNtp_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            string host = TxtNtpServer?.Text?.Trim() ?? "time.google.com";
            if (string.IsNullOrEmpty(host)) host = "time.google.com";

            try
            {
                LogEvent("[NTP]", $"Đang gửi gói tin UDP SNTP truy vấn thời gian thực tới [{host}]...");
                if (btn != null) btn.IsEnabled = false;

                var ntpResult = await _masterClock.SyncWithServerAsync(host, 3500);
                if (ntpResult.Success)
                {
                    TxtNtpOffset.Text = ntpResult.GetFormattedOffset();
                    LogEvent("[NTP]", $"✅ Đồng bộ NTP thành công! Offset: {ntpResult.OffsetMs:+0.00;-0.00} ms, RTT: {ntpResult.RoundTripDelayMs:F1} ms, Server UTC: {ntpResult.ServerUtcTime:HH:mm:ss.fff}.");
                    _masterClock.StartPeriodicSync(host, 30);
                }
                else
                {
                    TxtNtpOffset.Text = "Lỗi kết nối NTP";
                    LogEvent("[WARN]", $"Không thể đồng bộ NTP với [{host}]: {ntpResult.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                TxtNtpOffset.Text = "Lỗi truy vấn";
                LogEvent("[WARN]", $"Lỗi truy vấn NTP: {ex.Message}");
            }
            finally
            {
                if (btn != null) btn.IsEnabled = true;
            }
        }

        #endregion

        #region Transmission Control (Start / Stop SRT)

        private SRTStreamConfig BuildSrtStreamConfig()
        {
            string ip = TxtSrtIp.Text.Trim();
            if (!int.TryParse(TxtSrtPort.Text.Trim(), out int port))
            {
                port = 9000;
            }

            string modeStr = (CmbSrtMode.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Caller";
            SRTMode srtMode = SRTMode.Caller;
            if (modeStr.Contains("Listener", StringComparison.OrdinalIgnoreCase)) srtMode = SRTMode.Listener;
            else if (modeStr.Contains("Rendezvous", StringComparison.OrdinalIgnoreCase)) srtMode = SRTMode.Rendezvous;

            // Thu thập đầy đủ thông số cấu hình luồng SRT
            int latency = 120;
            if (int.TryParse(TxtManualLatency?.Text?.Trim(), out int parsedLat))
            {
                latency = parsedLat;
            }

            int keyLength = CmbKeyLength?.SelectedIndex switch
            {
                0 => 16, // AES-128
                1 => 24, // AES-192
                2 => 32, // AES-256
                _ => 32
            };

            string codecStr = (CmbVideoCodec?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "H.264 / AVC";
            string hwEncoder = CmbHardwareEncoder?.SelectedItem?.ToString() ?? "NVIDIA NVENC";
            string rateControl = (CmbRateControl?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "CBR (Constant Bitrate)";
            string preset = (CmbEncoderPreset?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Low-Latency / Zerolatency";
            bool isUll = ChkUltraLowLatency?.IsChecked == true;
            bool isEncrypted = ChkEnableEncryption?.IsChecked == true;
            bool isNtpSync = ChkNtpSync?.IsChecked == true;
            string streamId = TxtSrtStreamId?.Text?.Trim() ?? string.Empty;
            string passphrase = TxtSrtPassphrase?.Password ?? string.Empty;
            int bitrateKbps = (int)(SldTargetBitrate?.Value ?? 6000);

            VideoCodecType codecType = VideoCodecType.H264_AVC;
            if (codecStr.Contains("H.265") || codecStr.Contains("HEVC")) codecType = VideoCodecType.H265_HEVC;
            else if (codecStr.Contains("AV1")) codecType = VideoCodecType.AV1;

            string normalizedCodec = codecType switch
            {
                VideoCodecType.H265_HEVC => "H.265 / HEVC",
                VideoCodecType.AV1 => "AV1",
                _ => "H.264 / AVC"
            };

            double targetFps = GetSelectedStreamFps();
            var fpsRational = BroadcastFrameRates.SnapToRational(targetFps);

            var streamInfo = new MediaStreamInfo
            {
                VideoCodec = codecType switch
                {
                    VideoCodecType.H265_HEVC => "h265",
                    VideoCodecType.AV1 => "av1",
                    _ => "h264"
                },
                Width = 1920,
                Height = 1080,
                FrameRateNum = fpsRational.num,
                FrameRateDen = fpsRational.den,
                BitrateKbps = bitrateKbps,
                AudioCodec = "aac",
                AudioChannels = Math.Clamp(_sourceManager.ActiveAudioChannels, 1, 16),
                AudioSampleRate = 48000,
                AudioBitrateKbps = 192,
                EncoderName = "OpenMedia-SRT"
            };

            string fullStreamId = streamInfo.SerializeToStreamId(streamId);

            bool isGroup = ChkGroupSocket?.IsChecked == true;
            var groupType = CmbGroupType?.SelectedIndex == 1 
                ? SRTGroupType.Backup_ActiveStandby 
                : SRTGroupType.Broadcast_SMPTE2022_7;
            int diffDelay = 50;
            if (int.TryParse(TxtDifferentialDelay?.Text?.Trim(), out int parsedDelay))
            {
                diffDelay = Math.Clamp(parsedDelay, 10, 1000);
            }

            var groupMembers = new List<SRTGroupMemberConfig>();
            string singleNic = (CmbSingleSourceNic?.SelectedValue as string) ?? "";
            if (singleNic == "0.0.0.0") singleNic = "";

            if (isGroup)
            {
                // Member 1: Path A (Primary)
                string hostA = TxtMemberAHost?.Text?.Trim() ?? ip;
                if (!int.TryParse(TxtMemberAPort?.Text?.Trim(), out int portA)) portA = port;
                string localA = (CmbMemberANic?.SelectedValue as string) ?? "";
                if (localA == "0.0.0.0") localA = "";
                groupMembers.Add(new SRTGroupMemberConfig("Path A (Primary)", hostA, portA, localA, 10));

                // Member 2: Path B (Secondary / 4G)
                string hostB = TxtMemberBHost?.Text?.Trim() ?? ip;
                if (!int.TryParse(TxtMemberBPort?.Text?.Trim(), out int portB)) portB = port + 2;
                string localB = (CmbMemberBNic?.SelectedValue as string) ?? "";
                if (localB == "0.0.0.0") localB = "";
                groupMembers.Add(new SRTGroupMemberConfig("Path B (Secondary / 4G)", hostB, portB, localB, 10));

                // Thêm các member bổ sung đã gắn động
                foreach (var dm in _dynamicGroupMembers)
                {
                    if (!groupMembers.Any(m => m.Id == dm.Id))
                    {
                        groupMembers.Add(dm);
                    }
                }
            }

            return new SRTStreamConfig
            {
                Host = ip,
                Port = port,
                PrimaryInterfaceIp = singleNic,
                Mode = srtMode,
                StreamId = fullStreamId,
                LatencyMs = latency,
                AutoLatency = ChkAutoLatency?.IsChecked == true,
                EncryptionEnabled = isEncrypted,
                Passphrase = passphrase,
                KeyLength = keyLength,
                VideoCodec = normalizedCodec,
                BitrateKbps = bitrateKbps,
                HardwareEncoder = hwEncoder,
                RateControl = rateControl,
                EncoderPreset = preset,
                UltraLowLatency = isUll,
                GopSeconds = isUll ? 1.0 : 2.0,
                BFrames = isUll ? 0 : 2,
                NtpSyncEnabled = isNtpSync,
                NtpServer = TxtNtpServer?.Text?.Trim() ?? "time.google.com",
                FrameRate = streamInfo.FrameRateDouble,
                AudioChannels = Math.Clamp(_sourceManager.ActiveAudioChannels, 1, 16),
                AudioSampleRate = 48000,
                AudioBitrateKbps = 192,
                AudioCodec = "AAC",
                GroupSocketEnabled = isGroup,
                GroupType = groupType,
                GroupMembers = groupMembers,
                HitlessDifferentialDelayMs = diffDelay
            };
        }

        private void BtnStartStreaming_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _activeSrtConfig = BuildSrtStreamConfig();
                SaveCurrentSettings();
                _isTransmissionActive = true;
                _reconnectAttempt = 0;
                StartTelemetryReporting();

                BtnStartStreaming.IsEnabled = false;
                BtnStopStreaming.IsEnabled = true;

                LedSrtStatus.Fill = new SolidColorBrush(Color.FromRgb(255, 179, 0)); // Amber
                TxtSrtStatus.Text = "SRT: CONNECTING...";
                TxtSrtStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 179, 0));

                UpdateTargetSummary();

                _reconnectCts?.Cancel();
                _reconnectCts?.Dispose();
                _reconnectCts = new CancellationTokenSource();

                var token = _reconnectCts.Token;
                _ = Task.Run(() => TransmissionSupervisorLoopAsync(token), token);
            }
            catch (Exception ex)
            {
                LogEvent("[ERROR]", $"Lỗi khởi động phát sóng SRT: {ex.Message}");
                StopTransmissionInternal();
            }
        }

        private async Task TransmissionSupervisorLoopAsync(CancellationToken token)
        {
            LogEvent("[SRT]", "Khởi động tiến trình truyền dẫn SRT (Cơ chế Auto-Reconnect vô hạn KÍCH HOẠT).");

            while (!token.IsCancellationRequested && _isTransmissionActive)
            {
                try
                {
                    bool isListener = _activeSrtConfig?.Mode == SRTMode.Listener;

                    if (!isListener)
                    {
                        // ─── CALLER MODE ──────────────────────────────────────────────
                        bool isConnected = _srtStream != null && _srtStream.IsRunning && 
                            (_srtStream.Statistics.IsConnected || (_activeSrtConfig?.GroupSocketEnabled == true && _srtStream.GroupStats.ConnectedMembersCount > 0));

                        if (!isConnected)
                        {
                            _reconnectAttempt++;

                            await Dispatcher.InvokeAsync(() =>
                            {
                                LedSrtStatus.Fill = new SolidColorBrush(Color.FromRgb(255, 179, 0)); // Amber
                                TxtSrtStatus.Text = _reconnectAttempt <= 1
                                    ? "SRT: CONNECTING..."
                                    : $"SRT: RECONNECTING (#{_reconnectAttempt})...";
                                TxtSrtStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 179, 0));
                            });

                            if (_reconnectAttempt == 1)
                            {
                                LogEvent("[SRT]", $"Khởi tạo kết nối SRT ({_activeSrtConfig?.Mode} -> {_activeSrtConfig?.Host}:{_activeSrtConfig?.Port})...");
                            }
                            else
                            {
                                LogEvent("[WARN]", $"⚠️ Mất kết nối / Handshake SRT chưa thành công. Tự động kết nối lại lần #{_reconnectAttempt}...");
                            }

                            // Thu hồi phiên SRT cũ trước khi tạo phiên mới
                            await CleanupSrtSessionOnlyAsync();

                            if (_activeSrtConfig != null)
                            {
                                var newSession = new SRTStreamSession(_activeSrtConfig);
                                newSession.LogEmitted += (tag, msg) => LogEvent(tag, msg);
                                newSession.ErrorOccurred += err => LogEvent("[ERROR]", err);
                                newSession.MemberStatusChanged += status =>
                                {
                                    Dispatcher.InvokeAsync(() => UpdateGroupMemberUI(status));
                                };
                                newSession.GroupStatsUpdated += gStats =>
                                {
                                    Dispatcher.InvokeAsync(() => UpdateGroupStatsUI(gStats));
                                };
                                newSession.StatisticsUpdated += stats =>
                                {
                                    _currentRttMs = stats.RttMs;
                                    _currentPacketLoss = stats.PacketLossPercent;
                                    if (stats.CurrentBitrateKbps > 0)
                                    {
                                        _currentBitrateKbps = stats.CurrentBitrateKbps;
                                    }
                                    if (stats.CurrentFps > 0)
                                    {
                                        _currentFps = stats.CurrentFps;
                                    }
                                    _totalBytesTransferred = stats.TotalBytesTransferred;
                                };

                                bool started = await newSession.StartTransmissionAsync();
                                bool hasConnection = newSession.Statistics.IsConnected || (_activeSrtConfig.GroupSocketEnabled && newSession.GroupStats.ConnectedMembersCount > 0);
                                if (started && hasConnection)
                                {
                                    _srtStream = newSession;
                                    _isStreaming = true;
                                    _reconnectAttempt = 0;
                                    _streamStartTime = DateTime.UtcNow;

                                    await Dispatcher.InvokeAsync(() =>
                                    {
                                        LedSrtStatus.Fill = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Green
                                        TxtSrtStatus.Text = _activeSrtConfig.GroupSocketEnabled 
                                            ? "SRT: TRANSMITTING (GROUP LIVE)" 
                                            : "SRT: TRANSMITTING (LIVE)";
                                        TxtSrtStatus.Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80));
                                        UpdateTargetSummary();
                                        EnsureStreamingWorkerRunning(token);
                                    });

                                    LogEvent("[SRT]", _activeSrtConfig.GroupSocketEnabled
                                        ? "✅ [INFO] SRT Group Socket Connected thành công (SMPTE 2022-7 ARMED). Bắt đầu truyền dẫn luồng LIVE."
                                        : "✅ [INFO] SRT Connected thành công. Bắt đầu truyền dẫn luồng LIVE.");
                                }
                                else
                                {
                                    try { await newSession.StopAsync(); } catch { }
                                    newSession.Dispose();
                                }
                            }

                            if (!token.IsCancellationRequested && _isTransmissionActive)
                            {
                                // Delay 1.0 - 1.3 giây giữa các lần thử lại để tránh xung đột lockstep
                                await Task.Delay(1000 + Random.Shared.Next(0, 300), token);
                            }
                        }
                        else
                        {
                            // Đang kết nối ổn định: Kiểm tra nếu streaming worker FFmpeg chưa chạy thì kích hoạt
                            if (_streamProcess == null || _streamProcess.HasExited)
                            {
                                await Dispatcher.InvokeAsync(() => EnsureStreamingWorkerRunning(token));
                            }

                            await Task.Delay(1000, token);
                        }
                    }
                    else
                    {
                        // ─── LISTENER MODE ────────────────────────────────────────────
                        bool isListening = _srtStream != null && _srtStream.IsRunning && 
                            (_srtStream.NativeOutput?.IsOpen == true || (_activeSrtConfig?.GroupSocketEnabled == true && _srtStream.GroupStats.ConnectedMembersCount > 0));

                        if (!isListening)
                        {
                            _reconnectAttempt++;

                            await Dispatcher.InvokeAsync(() =>
                            {
                                LedSrtStatus.Fill = new SolidColorBrush(Color.FromRgb(255, 179, 0)); // Amber
                                TxtSrtStatus.Text = $"SRT: BINDING (Port {_activeSrtConfig?.Port})...";
                                TxtSrtStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 179, 0));
                            });

                            await CleanupSrtSessionOnlyAsync();

                            if (_activeSrtConfig != null)
                            {
                                var newSession = new SRTStreamSession(_activeSrtConfig);
                                newSession.LogEmitted += (tag, msg) => LogEvent(tag, msg);
                                newSession.ErrorOccurred += err => LogEvent("[ERROR]", err);
                                newSession.MemberStatusChanged += status =>
                                {
                                    Dispatcher.InvokeAsync(() => UpdateGroupMemberUI(status));
                                };
                                newSession.GroupStatsUpdated += gStats =>
                                {
                                    Dispatcher.InvokeAsync(() => UpdateGroupStatsUI(gStats));
                                };
                                newSession.StatisticsUpdated += stats =>
                                {
                                    _currentRttMs = stats.RttMs;
                                    _currentPacketLoss = stats.PacketLossPercent;
                                    if (stats.CurrentBitrateKbps > 0)
                                    {
                                        _currentBitrateKbps = stats.CurrentBitrateKbps;
                                    }
                                    if (stats.CurrentFps > 0)
                                    {
                                        _currentFps = stats.CurrentFps;
                                    }
                                    _totalBytesTransferred = stats.TotalBytesTransferred;
                                };

                                bool started = await newSession.StartTransmissionAsync();
                                bool hasListening = newSession.NativeOutput?.IsOpen == true || (_activeSrtConfig.GroupSocketEnabled && newSession.GroupStats.ConnectedMembersCount > 0);
                                if (started && hasListening)
                                {
                                    _srtStream = newSession;
                                    _isStreaming = true;
                                    _reconnectAttempt = 0;

                                    await Dispatcher.InvokeAsync(() =>
                                    {
                                        LedSrtStatus.Fill = new SolidColorBrush(Color.FromRgb(255, 179, 0)); // Amber
                                        TxtSrtStatus.Text = $"SRT: LISTENING (Port {_activeSrtConfig?.Port})...";
                                        TxtSrtStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 179, 0));
                                        UpdateTargetSummary();
                                        EnsureStreamingWorkerRunning(token);
                                    });

                                    LogEvent("[SRT]", $"✅ [INFO] SRT Output đang lắng nghe trên cổng {_activeSrtConfig?.Port}, sẵn sàng kết nối.");
                                }
                                else
                                {
                                    try { await newSession.StopAsync(); } catch { }
                                    newSession.Dispose();
                                }
                            }

                            if (!token.IsCancellationRequested && _isTransmissionActive)
                            {
                                await Task.Delay(1500, token);
                            }
                        }
                        else
                        {
                            // Listener đang mở, cập nhật trạng thái theo client kết nối
                            bool clientConnected = _srtStream.Statistics.IsConnected;

                            await Dispatcher.InvokeAsync(() =>
                            {
                                if (clientConnected)
                                {
                                    LedSrtStatus.Fill = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Green
                                    TxtSrtStatus.Text = "SRT: TRANSMITTING (LIVE)";
                                    TxtSrtStatus.Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80));
                                }
                                else
                                {
                                    LedSrtStatus.Fill = new SolidColorBrush(Color.FromRgb(255, 179, 0)); // Amber
                                    TxtSrtStatus.Text = $"SRT: LISTENING (Port {_activeSrtConfig?.Port})...";
                                    TxtSrtStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 179, 0));
                                }
                            });

                            if (_streamProcess == null || _streamProcess.HasExited)
                            {
                                await Dispatcher.InvokeAsync(() => EnsureStreamingWorkerRunning(token));
                            }

                            await Task.Delay(1000, token);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogEvent("[WARN]", $"Giám sát truyền dẫn SRT: {ex.Message}");
                    if (!token.IsCancellationRequested && _isTransmissionActive)
                    {
                        await Task.Delay(1500, token);
                    }
                }
            }
        }

        private async Task CleanupSrtSessionOnlyAsync()
        {
            if (_srtStream != null)
            {
                try
                {
                    await _srtStream.StopAsync();
                }
                catch { }
                _srtStream.Dispose();
                _srtStream = null;
            }
        }

        private void EnsureStreamingWorkerRunning(CancellationToken token = default)
        {
            if (_streamProcess != null && !_streamProcess.HasExited)
            {
                return;
            }

            _transmissionCts?.Cancel();
            _transmissionCts?.Dispose();
            _transmissionCts = new CancellationTokenSource();

            StartMasterProgramStreamingProcess(_transmissionCts.Token);
        }

        #region Stream Audio Channels Configuration

        private (int channels, string audioArgs, string summary) GetStreamAudioConfig(bool isPassthrough = false)
        {
            int configuredChannels = _sourceManager.ActiveAudioChannels;
            int selIdx = CmbStreamAudioChannels?.SelectedIndex ?? 0;
            int targetChannels = selIdx switch
            {
                1 => 1,
                2 => 2,
                3 => 4,
                4 => 6,
                5 => 8,
                6 => 16,
                _ => configuredChannels > 0 ? configuredChannels : 2
            };

            targetChannels = Math.Clamp(targetChannels, 1, 16);

            int bitrate = targetChannels switch
            {
                1 => 128,
                2 => 192,
                4 => 384,
                6 => 448,
                8 => 512,
                16 => 768,
                _ => 192
            };

            string layout = targetChannels switch
            {
                1 => "mono",
                2 => "stereo",
                4 => "quad",
                6 => "5.1",
                8 => "7.1",
                16 => "hexadecagonal",
                _ => "stereo"
            };

            string channelDesc = targetChannels switch
            {
                1 => "1 Kênh (Mono)",
                2 => "2 Kênh (Stereo L/R)",
                4 => "4 Kênh (Quad Multi-track)",
                6 => "6 Kênh (5.1 Surround)",
                8 => "8 Kênh (7.1 Surround)",
                16 => "16 Kênh (EBU Broadcast Discrete)",
                _ => $"{targetChannels} Kênh"
            };

            string audioArgs = isPassthrough
                ? "-c:a copy"
                : $"-c:a aac -b:a {bitrate}k -ac {targetChannels} -ar 48000";

            string summary = $"{channelDesc} • {bitrate} kbps • 48 kHz";
            return (targetChannels, audioArgs, summary);
        }

        private void UpdateStreamAudioSummaryUI(string summary)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (TxtStreamAudioSummary != null)
                {
                    TxtStreamAudioSummary.Text = summary;
                }
            });
        }

        private void CmbStreamAudioChannels_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;
            var (_, _, summary) = GetStreamAudioConfig(RbPassthrough?.IsChecked == true);
            UpdateStreamAudioSummaryUI(summary);

            if (_isTransmissionActive)
            {
                RestartStreamingWorker();
            }
        }

        private void RestartStreamingWorker()
        {
            try
            {
                _transmissionCts?.Cancel();
                if (_streamProcess != null && !_streamProcess.HasExited)
                {
                    _streamProcess.Kill(true);
                    _streamProcess.Dispose();
                    _streamProcess = null;
                }
            }
            catch { }

            EnsureStreamingWorkerRunning(_transmissionCts?.Token ?? default);
        }

        #endregion

        private void StartMasterProgramStreamingProcess(CancellationToken token = default)
        {
            bool isPassthrough = RbPassthrough?.IsChecked == true;
            string codecStr = (CmbVideoCodec?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "H.264 / AVC";
            string hwEncoder = CmbHardwareEncoder?.SelectedItem?.ToString() ?? "NVIDIA NVENC";
            bool isUll = ChkUltraLowLatency?.IsChecked == true;
            int bitrateKbps = (int)(SldTargetBitrate?.Value ?? 6000);

            VideoCodecType codecType = VideoCodecType.H264_AVC;
            if (codecStr.Contains("H.265") || codecStr.Contains("HEVC")) codecType = VideoCodecType.H265_HEVC;
            else if (codecStr.Contains("AV1")) codecType = VideoCodecType.AV1;

            string normalizedCodec = codecType switch
            {
                VideoCodecType.H265_HEVC => "H.265 / HEVC",
                VideoCodecType.AV1 => "AV1",
                _ => "H.264 / AVC"
            };

            bool isHwUnavailable = hwEncoder.Contains("Không khả dụng", StringComparison.OrdinalIgnoreCase);
            string vcodecArg;
            if (isHwUnavailable)
            {
                vcodecArg = codecType switch
                {
                    VideoCodecType.H265_HEVC => "-c:v libx265 -preset veryfast",
                    VideoCodecType.AV1 => "-c:v libsvtav1 -preset 8",
                    _ => "-c:v libx264 -preset veryfast"
                };
            }
            else if (codecType == VideoCodecType.H265_HEVC)
            {
                if (hwEncoder.Contains("NVENC", StringComparison.OrdinalIgnoreCase)) vcodecArg = "-c:v hevc_nvenc";
                else if (hwEncoder.Contains("QSV", StringComparison.OrdinalIgnoreCase) || hwEncoder.Contains("QuickSync", StringComparison.OrdinalIgnoreCase)) vcodecArg = "-c:v hevc_qsv";
                else if (hwEncoder.Contains("AMF", StringComparison.OrdinalIgnoreCase)) vcodecArg = "-c:v hevc_amf";
                else vcodecArg = "-c:v libx265 -preset veryfast";
            }
            else if (codecType == VideoCodecType.AV1)
            {
                if (hwEncoder.Contains("NVENC", StringComparison.OrdinalIgnoreCase)) vcodecArg = "-c:v av1_nvenc";
                else vcodecArg = "-c:v libsvtav1 -preset 8";
            }
            else
            {
                if (hwEncoder.Contains("NVENC", StringComparison.OrdinalIgnoreCase)) vcodecArg = "-c:v h264_nvenc";
                else if (hwEncoder.Contains("QSV", StringComparison.OrdinalIgnoreCase) || hwEncoder.Contains("QuickSync", StringComparison.OrdinalIgnoreCase)) vcodecArg = "-c:v h264_qsv";
                else if (hwEncoder.Contains("AMF", StringComparison.OrdinalIgnoreCase)) vcodecArg = "-c:v h264_amf";
                else vcodecArg = "-c:v libx264 -preset veryfast";
            }

            double targetFps = GetSelectedStreamFps();
            var rational = BroadcastFrameRates.SnapToRational(targetFps);
            string fpsStr = BroadcastFrameRates.FormatFfmpeg(rational);
            int gopSize = (int)Math.Round((rational.num / (double)rational.den) * (isUll ? 1.0 : 2.0));
            string lowLatencyArg = isUll ? $"-tune zerolatency -bf 0 -g {gopSize} -keyint_min {gopSize} -sc_threshold 0" : $"-g {gopSize} -keyint_min {gopSize} -sc_threshold 0";
            string fpsArg = $"-r {fpsStr}";
            string ffmpegArgs;

            InputSourceType currentSource = _sourceManager.CurrentSource;
            string currentFilePath = !string.IsNullOrWhiteSpace(_sourceManager.CurrentSourcePath) ? _sourceManager.CurrentSourcePath : TxtFilePath?.Text?.Trim() ?? "";

            var (streamChs, audioArgs, audioSummary) = GetStreamAudioConfig(isPassthrough);
            UpdateStreamAudioSummaryUI(audioSummary);

            string layout = streamChs switch
            {
                1 => "mono",
                2 => "stereo",
                4 => "quad",
                6 => "5.1",
                8 => "7.1",
                16 => "hexadecagonal",
                _ => "stereo"
            };

            bool isDirectFileSource = currentSource == InputSourceType.File && !string.IsNullOrWhiteSpace(currentFilePath) && File.Exists(currentFilePath) && isPassthrough;

            if (currentSource == InputSourceType.File && !string.IsNullOrWhiteSpace(currentFilePath) && File.Exists(currentFilePath))
            {
                double currentSec = _sourceManager.CurrentPosition.TotalSeconds;
                string seekArg = currentSec > 0.05 ? $"-ss {currentSec:F3} " : "";

                if (isPassthrough)
                {
                    ffmpegArgs = $"-hide_banner -loglevel error {seekArg}-re -stream_loop -1 -avoid_negative_ts make_zero -fflags +genpts -i \"{currentFilePath}\" -c:v copy {audioArgs} -f mpegts -mpegts_flags resend_headers+pat_pmt_at_frames -flush_packets 1 -muxdelay 0.1 -muxpreload 0.1 -pcr_period 20 pipe:1";
                    LogEvent("[PIPELINE]", $"🎬 Nạp nguồn Video File (Direct Bitstream Passthrough @ {TimeSpan.FromSeconds(currentSec):hh\\:mm\\:ss\\.fff}) kèm Audio ({streamChs} Ch): {Path.GetFileName(currentFilePath)}");
                }
                else
                {
                    // Hướng 1: Đồng bộ Video qua Master Program Bus (Task 1 bơm nhịp 40ms chính xác tuyệt đối qua pipe:0)
                    // Âm thanh trích xuất từ file và đồng bộ cứng qua bộ lọc aresample (A/V Sync Lock)
                    bool hasFileAudio = _sourceManager.ActiveAudioChannels > 0;
                    string audioInputArg = hasFileAudio 
                        ? $"{seekArg}-stream_loop -1 -i \"{currentFilePath}\""
                        : $"-f lavfi -i \"anullsrc=channel_layout={layout}:sample_rate=48000\"";
                    string audioFilterArg = hasFileAudio
                        ? "-af \"aresample=async=1000:min_hard_comp=0.100000:first_pts=0\""
                        : "";

                    ffmpegArgs = $"-hide_banner -loglevel error -f rawvideo -pix_fmt bgra -s 1920x1080 {fpsArg} -i pipe:0 {audioInputArg} -map 0:v:0 -map 1:a:0? {vcodecArg} -b:v {bitrateKbps}k -maxrate {bitrateKbps}k -bufsize {bitrateKbps * 2}k {lowLatencyArg} {audioArgs} {audioFilterArg} {fpsArg} -f mpegts -mpegts_flags resend_headers+pat_pmt_at_frames -flush_packets 1 -muxdelay 0.1 -pcr_period 20 pipe:1";
                    LogEvent("[PIPELINE]", $"🎬 Nạp nguồn Master Program Bus Video File ({Path.GetFileName(currentFilePath)} @ {fpsStr} FPS @ {TimeSpan.FromSeconds(currentSec):hh\\:mm\\:ss\\.fff}) kèm Audio ({streamChs} Ch) vào Video Encoder ({normalizedCodec} via {hwEncoder})");
                }
            }
            else if (currentSource == InputSourceType.NDI)
            {
                // Master PGM Output với nguồn NDI Live: Video từ NDI Receiver BGRA frame buffer
                ffmpegArgs = $"-hide_banner -loglevel error -f rawvideo -pix_fmt bgra -s 1920x1080 {fpsArg} -i pipe:0 -f lavfi -i \"anullsrc=channel_layout={layout}:sample_rate=48000\" -map 0:v:0 -map 1:a:0 {vcodecArg} -b:v {bitrateKbps}k -maxrate {bitrateKbps}k -bufsize {bitrateKbps * 2}k {lowLatencyArg} {audioArgs} {fpsArg} -f mpegts -mpegts_flags resend_headers+pat_pmt_at_frames -flush_packets 1 -muxdelay 0.1 -pcr_period 20 pipe:1";
                LogEvent("[PIPELINE]", $"🌐 Nạp nguồn Master PGM NDI Stream ({_sourceManager.CurrentSourcePath}) @ {fpsStr} FPS kèm Audio ({streamChs} Ch) vào Video Encoder ({normalizedCodec} via {hwEncoder})");
            }
            else if (currentSource == InputSourceType.SDI)
            {
                // Master PGM Output với nguồn SDI Live: Video từ SDI Device Capture BGRA frame buffer
                ffmpegArgs = $"-hide_banner -loglevel error -f rawvideo -pix_fmt bgra -s 1920x1080 {fpsArg} -i pipe:0 -f lavfi -i \"anullsrc=channel_layout={layout}:sample_rate=48000\" -map 0:v:0 -map 1:a:0 {vcodecArg} -b:v {bitrateKbps}k -maxrate {bitrateKbps}k -bufsize {bitrateKbps * 2}k {lowLatencyArg} {audioArgs} {fpsArg} -f mpegts -mpegts_flags resend_headers+pat_pmt_at_frames -flush_packets 1 -muxdelay 0.1 -pcr_period 20 pipe:1";
                LogEvent("[PIPELINE]", $"📡 Nạp nguồn Master PGM SDI/Capture ({_sourceManager.CurrentSourcePath}) @ {fpsStr} FPS kèm Audio ({streamChs} Ch) vào Video Encoder ({normalizedCodec} via {hwEncoder})");
            }
            else
            {
                // Master PGM Output với nguồn Colorbar (hoặc Live Pattern): Video là luồng WYSIWYG, Audio là Test Tone chuẩn EBU/SMPTE
                int toneFreq = _colorbarEngine.CurrentTone switch
                {
                    AudioTestToneType.Glits400Hz => 400,
                    _ => 1000
                };
                ffmpegArgs = $"-hide_banner -loglevel error -f rawvideo -pix_fmt bgra -s 1920x1080 {fpsArg} -i pipe:0 -f lavfi -i \"sine=frequency={toneFreq}:sample_rate=48000\" -map 0:v:0 -map 1:a:0 {vcodecArg} -b:v {bitrateKbps}k -maxrate {bitrateKbps}k -bufsize {bitrateKbps * 2}k {lowLatencyArg} {audioArgs} {fpsArg} -f mpegts -mpegts_flags resend_headers+pat_pmt_at_frames -flush_packets 1 -muxdelay 0.1 -pcr_period 20 pipe:1";
                LogEvent("[PIPELINE]", $"🎨 Nạp nguồn Master PGM Colorbar (WYSIWYG 1920x1080 @ {fpsStr} FPS, Tone: {toneFreq} Hz) kèm Audio ({streamChs} Ch) vào Video Encoder ({normalizedCodec} via {hwEncoder})");
            }

            LogEvent("[SRT]", $"Khởi động Streaming Worker chuẩn Broadcast Master PGM Output ({normalizedCodec} @ {targetFps:F2} FPS via {hwEncoder})");

            var psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = ffmpegArgs,
                RedirectStandardInput = !isDirectFileSource,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            _streamProcess = Process.Start(psi);
            if (_streamProcess == null)
            {
                LogEvent("[ERROR]", "Không thể khởi chạy tiến trình phát luồng FFmpeg cho Master PGM Output.");
                return;
            }

            var process = _streamProcess;

            // Task 0: Thoát bộ đệm stderr của FFmpeg để ngăn ngừa Pipe Deadlock và ghi nhận lỗi
            _ = Task.Run(async () =>
            {
                try
                {
                    using var reader = process.StandardError;
                    while (!token.IsCancellationRequested && !process.HasExited)
                    {
                        string? line = await reader.ReadLineAsync().ConfigureAwait(false);
                        if (line == null) break;
                        if (line.Contains("Error", StringComparison.OrdinalIgnoreCase) || 
                            line.Contains("Fatal", StringComparison.OrdinalIgnoreCase) ||
                            line.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                            line.Contains("CUDA", StringComparison.OrdinalIgnoreCase))
                        {
                            LogEvent("[FFMPEG-ERR]", line);
                        }
                    }
                }
                catch { }
            }, token);

            // Task 1: Bơm khung hình 1920x1080 BGRA từ Master Program Bus vào stdin của FFmpeg ở nhịp targetFps (dành cho nguồn Live: SDI, NDI, Colorbar)
            if (!isDirectFileSource)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var stdin = process.StandardInput.BaseStream;
                        var stopwatch = Stopwatch.StartNew();
                        long frameCount = 0;

                        while (!token.IsCancellationRequested && _isTransmissionActive && !process.HasExited)
                        {
                            byte[]? frameData = _sourceManager.LatestMasterFrame;
                            if (frameData == null || frameData.Length != 1920 * 1080 * 4)
                            {
                                lock (_programFrameLock)
                                {
                                    frameData = _currentProgramFrameBytes;
                                }
                            }

                            // Nếu cả 2 nguồn tạm thời chưa sẵn sàng, bơm khung hình fallback đen có alpha chuẩn để FFmpeg không bị đói gói
                            if (frameData == null || frameData.Length != 1920 * 1080 * 4)
                            {
                                frameData = _fallbackBlackFrame;
                            }

                            if (frameData != null && frameData.Length > 0)
                            {
                                await stdin.WriteAsync(frameData.AsMemory(0, frameData.Length), token).ConfigureAwait(false);
                                await stdin.FlushAsync(token).ConfigureAwait(false);
                                frameCount++;
                                Interlocked.Increment(ref _workerFramesSent);
                            }

                            double targetTimeMs = frameCount * (1000.0 / targetFps);
                            await MasterClockProvider.PreciseWaitUntilAsync(stopwatch, targetTimeMs, token).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        LogEvent("[WARN]", $"Luồng cấp dữ liệu Video stdin: {ex.Message}");
                    }
                }, token);
            }

            // Task 2: Đọc các gói tin MPEG-TS từ stdout của FFmpeg và gửi qua SRT
            _ = Task.Run(async () =>
            {
                try
                {
                    using var stdout = process.StandardOutput.BaseStream;
                    byte[] buffer = new byte[7 * 188]; // 1316 bytes (7 TS packets)
                    var fpsStopwatch = Stopwatch.StartNew();
                    long directFileFrameCounter = 0;

                    int consecutiveFailures = 0;
                    while (!token.IsCancellationRequested && _isTransmissionActive && !process.HasExited)
                    {
                        int totalRead = 0;
                        while (totalRead < buffer.Length)
                        {
                            int read = await stdout.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), token).ConfigureAwait(false);
                            if (read <= 0) break;
                            totalRead += read;
                        }

                        if (totalRead > 0)
                        {
                            _workerBytesSent += (ulong)totalRead;
                            if (_srtStream != null && _srtStream.IsRunning && _srtStream.Statistics.IsConnected)
                            {
                                bool sent = _srtStream.SendData(buffer, totalRead, 0, true);
                                if (!sent)
                                {
                                    consecutiveFailures++;
                                    if (consecutiveFailures >= 5)
                                    {
                                        _srtStream.MarkDisconnected("Mất kết nối đường truyền SRT (Send socket failure)");
                                        consecutiveFailures = 0;
                                    }
                                }
                                else
                                {
                                    consecutiveFailures = 0;
                                }
                            }

                            if (isDirectFileSource)
                            {
                                long expectedFrames = (long)(fpsStopwatch.Elapsed.TotalSeconds * targetFps);
                                while (directFileFrameCounter < expectedFrames)
                                {
                                    directFileFrameCounter++;
                                    Interlocked.Increment(ref _workerFramesSent);
                                }
                            }
                        }
                        else
                        {
                            await Task.Delay(1, token).ConfigureAwait(false);
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    LogEvent("[WARN]", $"Luồng phát MPEG-TS Master PGM: {ex.Message}");
                }
            }, token);
        }

        private void BtnStopStreaming_Click(object sender, RoutedEventArgs e)
        {
            _isTransmissionActive = false;
            _reconnectCts?.Cancel();
            StopTransmissionInternal();
            LogEvent("[SRT]", "Đã dừng luồng phát sóng SRT theo yêu cầu người dùng.");
        }

        private void StopTransmissionInternal()
        {
            _isTransmissionActive = false;
            _isStreaming = false;
            _currentMuxer = null;
            StopTelemetryReporting();

            try { _reconnectCts?.Cancel(); } catch { }
            try { _transmissionCts?.Cancel(); } catch { }

            if (_streamProcess != null)
            {
                try
                {
                    if (!_streamProcess.HasExited)
                    {
                        _streamProcess.Kill(true);
                    }
                }
                catch { }
                try { _streamProcess.Dispose(); } catch { }
                _streamProcess = null;
            }

            if (_srtStream != null)
            {
                var srt = _srtStream;
                _srtStream = null;
                try
                {
                    var stopTask = srt.StopAsync();
                    stopTask.Wait(300);
                }
                catch { }
                try { srt.Dispose(); } catch { }
            }

            try { _reconnectCts?.Dispose(); } catch { }
            _reconnectCts = null;

            try { _transmissionCts?.Dispose(); } catch { }
            _transmissionCts = null;

            _reconnectAttempt = 0;
            if (!_isClosing)
            {
                void ResetUi()
                {
                    BtnStartStreaming.IsEnabled = true;
                    BtnStopStreaming.IsEnabled = false;

                    LedSrtStatus.Fill = new SolidColorBrush(Color.FromRgb(158, 158, 158)); // Grey
                    TxtSrtStatus.Text = "SRT: Idle";
                    TxtSrtStatus.Foreground = new SolidColorBrush(Color.FromRgb(204, 204, 204));
                }

                if (Dispatcher.CheckAccess())
                {
                    ResetUi();
                }
                else
                {
                    try { Dispatcher.Invoke(ResetUi); } catch { }
                }
            }
        }


        private void UpdateTargetSummary()
        {
            if (TxtTargetSummary == null || TxtCodecSummary == null) return;

            string ip = TxtSrtIp?.Text ?? "127.0.0.1";
            string port = TxtSrtPort?.Text ?? "9000";
            string mode = (CmbSrtMode?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Caller";
            bool isPassthrough = RbPassthrough?.IsChecked == true;
            string codec = (CmbVideoCodec?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "H.264";
            double bitrate = SldTargetBitrate?.Value ?? 6000;
            bool isUll = ChkUltraLowLatency?.IsChecked == true;

            TxtTargetSummary.Text = $"SRT {mode.Split(' ')[0]} -> {ip}:{port}";
            if (isPassthrough)
            {
                TxtCodecSummary.Text = "⚡ PASSTHROUGH (Direct Stream)";
            }
            else
            {
                TxtCodecSummary.Text = $"{codec.Split(' ')[0]} @ {bitrate:N0} kbps {(isUll ? "(ULL B=0)" : "")}";
            }
        }

        #endregion

        #region Preview Controls & Monitoring

        private void ChkEnableVideoPreview_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            bool isEnabled = ChkEnableVideoPreview.IsChecked == true;
            _sourceManager.SetPreviewEnabled(isEnabled);

            if (PnlPreviewDisabled != null)
            {
                PnlPreviewDisabled.Visibility = isEnabled ? Visibility.Collapsed : Visibility.Visible;
            }

            LogEvent("[INFO]", isEnabled ? "Bật Video Preview." : "Tắt Video Preview để tối ưu GPU.");
        }

        private void ChkEnableAudioMonitor_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            bool isVuVisible = ChkEnableAudioMonitor.IsChecked == true;

            if (OverlayAudioVu != null)
            {
                OverlayAudioVu.Visibility = isVuVisible ? Visibility.Visible : Visibility.Collapsed;
            }
            LogEvent("[INFO]", isVuVisible ? "Hiển thị đồng hồ đo Audio VU Meter." : "Ẩn đồng hồ đo Audio VU Meter.");
        }

        private void BtnAudioMute_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            _isAudioMuted = !_isAudioMuted;

            if (!_isAudioMuted && SldMonitorVolume.Value <= 0.001)
            {
                // Nếu đang 0% mà unmute, khôi phục mức volume hợp lý (trước đó hoặc 70%)
                SldMonitorVolume.Value = _lastNonZeroVolume > 0.05 ? _lastNonZeroVolume : 0.7;
            }

            UpdateAudioMuteState(logChange: true);
        }

        private void UpdateAudioMuteState(bool logChange = false)
        {
            _sourceManager.SetAudioMonitor(!_isAudioMuted, SldMonitorVolume.Value);

            if (TxtAudioMuteIcon != null)
            {
                TxtAudioMuteIcon.Text = _isAudioMuted ? "🔇" : "🔊";
                TxtAudioMuteIcon.Foreground = _isAudioMuted
                    ? new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)) // Red for Muted
                    : new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76)); // Green for Active
            }

            if (BtnAudioMute != null)
            {
                BtnAudioMute.ToolTip = _isAudioMuted
                    ? "Loa kiểm âm (Monitor Speaker): Đang tắt tiếng (MUTED) - Click để Bật tiếng (UNMUTE)"
                    : "Loa kiểm âm (Monitor Speaker): Đang bật tiếng (ACTIVE) - Click để Tắt tiếng (MUTE)";
            }

            if (logChange)
            {
                LogEvent("[AUDIO]", _isAudioMuted
                    ? "🔇 Đã tắt tiếng loa kiểm âm (Speaker Mute)."
                    : $"🔊 Đã bật tiếng loa kiểm âm (Speaker Unmute) (Âm lượng: {(int)(SldMonitorVolume.Value * 100)}%).");
            }
        }

        private void ChkShowTelemetry_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (OverlayTelemetry != null)
            {
                OverlayTelemetry.Visibility = ChkShowTelemetry.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void SldMonitorVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized) return;
            if (TxtMonitorVolumeVal != null)
            {
                TxtMonitorVolumeVal.Text = $"{(int)(e.NewValue * 100)}%";
            }

            if (e.NewValue > 0.001)
            {
                _lastNonZeroVolume = e.NewValue;
                if (_isAudioMuted)
                {
                    _isAudioMuted = false;
                    UpdateAudioMuteState(logChange: false);
                }
            }
            else if (!_isAudioMuted)
            {
                _isAudioMuted = true;
                UpdateAudioMuteState(logChange: false);
            }

            UpdatePreviewMonitorAudio();
        }

        private void UpdatePreviewMonitorAudio()
        {
            if (!_isInitialized) return;
            double baseVol = SldMonitorVolume?.Value ?? 0.7;
            if (_isAudioMuted)
            {
                _sourceManager.SetVolume(0.0);
                return;
            }

            _sourceManager.SetVolume(Math.Clamp(baseVol, 0.0, 1.0));
        }

        private int _aspectModeIndex = 0; // 0: Aspect Fit (Uniform), 1: Aspect Scale (UniformToFill)

        private void BtnAspectMode_Click(object sender, RoutedEventArgs e)
        {
            _aspectModeIndex = (_aspectModeIndex + 1) % 2;
            UpdateAspectMode();
        }

        private void UpdateAspectMode()
        {
            if (BtnAspectMode == null) return;

            Stretch currentStretch = _aspectModeIndex switch
            {
                0 => Stretch.Uniform,         // Aspect Fit: Giữ nguyên tỉ lệ khung hình gốc (không méo hình)
                1 => Stretch.UniformToFill,   // Aspect Scale: Phóng to đồng đều tỉ lệ lấp đầy toàn bộ khung xem trước (không méo hình)
                _ => Stretch.Uniform
            };

            _sourceManager.SetAspectRatio(currentStretch);

            switch (_aspectModeIndex)
            {
                case 0:
                    BtnAspectMode.Content = "📐 Aspect: Fit";
                    BtnAspectMode.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
                    LogEvent("[DISPLAY]", "Chuyển tỉ lệ hiển thị: ASPECT FIT (Giữ đúng tỉ lệ gốc khung hình).");
                    break;

                case 1:
                    BtnAspectMode.Content = "📐 Aspect: Scale";
                    BtnAspectMode.Foreground = new SolidColorBrush(Color.FromRgb(0x4E, 0xC9, 0xB0));
                    LogEvent("[DISPLAY]", "Chuyển tỉ lệ hiển thị: ASPECT SCALE (Lấp đầy toàn bộ khung hình, giữ đúng tỉ lệ).");
                    break;
            }
        }

        #endregion

        #region Logging & Utilities

        public void LogEvent(string tag, string message)
        {
            if (_isClosing || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                return;

            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            string logLine = $"[{timestamp}] {tag} {message}\n";

            void PrependLog()
            {
                if (_isClosing) return;
                if (TxtLogConsole != null)
                {
                    // Newest on Top
                    TxtLogConsole.Text = logLine + TxtLogConsole.Text;

                    // Keep buffer clean for long sessions
                    if (TxtLogConsole.Text.Length > 50000)
                    {
                        TxtLogConsole.Text = TxtLogConsole.Text.Substring(0, 40000);
                    }

                    ScrollerLogs?.ScrollToHome();
                }
                else
                {
                    _pendingLogs.Insert(0, logLine);
                }
            }

            if (Dispatcher.CheckAccess())
            {
                PrependLog();
            }
            else
            {
                try
                {
                    Dispatcher.BeginInvoke(PrependLog, DispatcherPriority.Background);
                }
                catch { }
            }
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            TxtLogConsole.Clear();
            LogEvent("[INFO]", "Đã xóa sạch nhật ký console.");
        }

        private static string? FindServerExecutable()
        {
            // 1. Dùng trực tiếp ServerDiscovery từ OpenMedia.Platform
            try
            {
                string? discovered = OpenMedia.Platform.Internal.ServerDiscovery.Discover();
                if (!string.IsNullOrEmpty(discovered) && File.Exists(discovered))
                {
                    return discovered;
                }
            }
            catch { }

            // 2. Tra cứu từ biến môi trường OPENMEDIA_SERVER_PATH hoặc OPENMEDIA_SDK_DIR
            string? envServerPath = Environment.GetEnvironmentVariable("OPENMEDIA_SERVER_PATH");
            if (!string.IsNullOrEmpty(envServerPath) && File.Exists(envServerPath))
            {
                return envServerPath;
            }

            string? sdkDir = Environment.GetEnvironmentVariable("OPENMEDIA_SDK_DIR");
            if (!string.IsNullOrEmpty(sdkDir))
            {
                string p1 = Path.Combine(sdkDir, "bin", "OpenMediaServer.exe");
                if (File.Exists(p1)) return p1;
                string p2 = Path.Combine(sdkDir, "OpenMediaServer.exe");
                if (File.Exists(p2)) return p2;
            }

            // 3. Tra cứu từ Windows Registry HKLM\Software\OpenMedia\SDK (Path) hoặc HKLM\Software\OpenMedia (ServerPath / InstallPath)
            try
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"Software\OpenMedia\SDK"))
                {
                    if (key != null)
                    {
                        string? regPath = key.GetValue("Path") as string;
                        if (!string.IsNullOrEmpty(regPath))
                        {
                            string binPath = Path.Combine(regPath, "bin", "OpenMediaServer.exe");
                            if (File.Exists(binPath)) return binPath;
                            string rootPath = Path.Combine(regPath, "OpenMediaServer.exe");
                            if (File.Exists(rootPath)) return rootPath;
                        }
                    }
                }

                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"Software\OpenMedia"))
                {
                    if (key != null)
                    {
                        string? serverPath = key.GetValue("ServerPath") as string;
                        if (!string.IsNullOrEmpty(serverPath) && File.Exists(serverPath)) return serverPath;

                        string? installPath = key.GetValue("InstallPath") as string;
                        if (!string.IsNullOrEmpty(installPath))
                        {
                            string binPath = Path.Combine(installPath, "bin", "OpenMediaServer.exe");
                            if (File.Exists(binPath)) return binPath;
                        }
                    }
                }
            }
            catch { }

            // 4. Tra cứu đường dẫn mặc định trong Program Files
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var defaultPaths = new[]
            {
                Path.Combine(programFiles, "OpenMedia", "SDK", "bin", "OpenMediaServer.exe"),
                Path.Combine(programFiles, "OpenMedia", "bin", "OpenMediaServer.exe"),
                Path.Combine(programFiles, "OpenMedia", "OpenMediaServer.exe")
            };
            foreach (var dp in defaultPaths)
            {
                if (File.Exists(dp)) return dp;
            }

            // 5. Tra cứu trong thư mục chạy của ứng dụng (Co-located)
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var localCandidates = new[]
            {
                Path.Combine(baseDir, "OpenMediaServer.exe"),
                Path.Combine(baseDir, "bin", "OpenMediaServer.exe"),
                Path.Combine(baseDir, "OpenMediaServer", "OpenMediaServer.exe")
            };
            foreach (var lc in localCandidates)
            {
                if (File.Exists(lc)) return lc;
            }

            // 6. Tra cứu trong các thư mục build trong môi trường phát triển (Dev Fallback)
            string current = baseDir;
            for (int i = 0; i < 6; i++)
            {
                if (string.IsNullOrEmpty(current)) break;
                var devCandidates = new[]
                {
                    Path.Combine(current, "build", "bin", "Release", "OpenMediaServer.exe"),
                    Path.Combine(current, "build", "bin", "Debug", "OpenMediaServer.exe"),
                    Path.Combine(current, "build-production", "bin", "Release", "OpenMediaServer.exe"),
                    Path.Combine(current, "build-demo", "bin", "Release", "OpenMediaServer.exe"),
                    Path.Combine(current, "build-demo", "bin", "Debug", "OpenMediaServer.exe"),
                    Path.Combine(current, "dist", "sdk", "bin", "OpenMediaServer.exe"),
                    Path.Combine(current, "dist", "sdk_staging", "bin", "OpenMediaServer.exe"),
                    Path.Combine(current, "dist", "production", "bin", "OpenMediaServer.exe")
                };
                foreach (var dc in devCandidates)
                {
                    if (File.Exists(dc)) return dc;
                }
                var parent = Directory.GetParent(current);
                if (parent == null) break;
                current = parent.FullName;
            }

            return null;
        }

        #endregion

    }
}
