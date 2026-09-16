using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using OpenMedia.Platform;
using OpenMedia.Platform.Controls.Wpf;
using OpenMedia.Platform.Models;

namespace WEBRTC_ENCODE
{
    public partial class MainWindow : Window
    {
        // ─── Engine & Playback State ────────────────────────────────
        private bool _isStreaming = false;
        private DateTime _streamStartTime = DateTime.MinValue;

        // ─── WebRTC Engine Components ───────────────────────────────
        private readonly WebRtcSignalingClient _signalingClient = new();
        private readonly RtpStreamSender _rtpSender = new();
        private readonly AudioInputDevice _audioInputDevice = new();
        private Process? _videoEncodeProcess;
        private Process? _audioEncodeProcess;
        private CancellationTokenSource? _transmissionCts;
        private bool _isMicEnabled = false;
        private double _micGainMultiplier = 1.0;

        // ─── Video Source Management & Colorbar Engine ──────────────
        private readonly ColorbarEngine _colorbarEngine = new();
        private readonly VideoSourceManager _sourceManager;
        private readonly MasterClockProvider _masterClock = MasterClockProvider.Instance;

        // ─── Frame Buffer for Master Program Video Bus ─────────────
        private readonly object _programFrameLock = new();
        private byte[]? _currentProgramFrameBytes;
        private List<GpuEncoderOption> _availableEncoders = new();

        // ─── Timers ─────────────────────────────────────────────────
        private DispatcherTimer? _utcClockTimer;
        private DispatcherTimer? _telemetryTimer;
        private DispatcherTimer? _vuMeterTimer;

        // ─── Video Transmission Telemetry Tracking ─────────────────
        private long _realEncodedFrames = 0;
        private long _realDroppedFrames = 0;
        private long _lastReportedFrames = 0;
        private DateTime _lastFpsCalcTime = DateTime.UtcNow;
        private double _currentRealFps = 0.0;

        // ─── 16-Channel Audio Meter Elements ───────────────────────
        private ProgressBar[] _vuBars = Array.Empty<ProgressBar>();
        private StackPanel[] _vuCols = Array.Empty<StackPanel>();
        private TextBlock[] _vuLabels = Array.Empty<TextBlock>();
        private System.Windows.Shapes.Ellipse[] _vuClipLeds = Array.Empty<System.Windows.Shapes.Ellipse>();
        private readonly double[] _channelLevels16 = new double[16];
        private readonly double[] _channelRmsLevels16 = new double[16];
        private readonly bool[] _channelClipping16 = new bool[16];
        private readonly AudioMeterService _audioMeterService = new();
        private readonly AudioOutputDevice _audioOutputDevice = new();
        private volatile bool _isAudioMuted = true;
        private double _lastNonZeroVolume = 0.4;
        private double _monitorVolume = 0.0;
        private volatile int _selectedStreamAudioChannels = 0;

        // ─── Live Source Audio Jitter Buffer for RTP Transmission ───
        private readonly AudioJitterBuffer _sourceAudioJitterBuffer = new(capacityBytes: 1920000, preRollBytes: 15360);

        // ─── Intercom Communication State & Opus Codec ─────────────
        private volatile bool _isPttActive = false;
        private double _intercomReceiveVolume = 0.75;
        private readonly IntercomOpusCodec _intercomCodec = new();
        private DispatcherTimer? _intercomWatchdogTimer;
        private volatile bool _isIntercomExplicitlyActive = false;
        private string _lastIntercomSender = "Director";

        // ─── Logging Buffer & Init Guard ───────────────────────────
        private readonly List<string> _pendingLogs = new();
        private bool _isInitialized = false;
        private volatile bool _isClosing = false;
        private readonly string[] _defaultCameraNames = new[]
        {
            "Máy quay 01", "Máy quay 02", "Máy quay 03", "Máy quay 04",
            "Máy quay 05", "Máy quay 06", "Máy quay 07", "Máy quay 08",
            "Máy quay 09", "Máy quay 10"
        };

        public MainWindow()
        {
            InitializeComponent();

            _intercomWatchdogTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            _intercomWatchdogTimer.Tick += (s, e) =>
            {
                _intercomWatchdogTimer.Stop();
                if (!_isClosing && !_isIntercomExplicitlyActive)
                {
                    SetIntercomStatusUI(_lastIntercomSender, false);
                }
            };

            _sourceManager = new VideoSourceManager(_colorbarEngine);
            _sourceManager.SourceChanged += (src, path) => _sourceAudioJitterBuffer.Reset();
            _sourceManager.PlaybackLooped += () => _sourceAudioJitterBuffer.Reset();
            _sourceManager.LogRequested += (tag, msg) => LogEvent(tag, msg);
            _sourceManager.TelemetryUpdated += UpdateSourceTelemetryUI;
            _sourceManager.AudioSamplesArrived += (samples, channels, sampleRate) =>
            {
                try
                {
                    if (channels > 0 && _sourceManager.ActiveAudioChannels != channels)
                    {
                        _sourceManager.ActiveAudioChannels = channels;
                        UpdateVuMeterColumns();
                    }
                    _audioMeterService.TapPcmDirect(samples, channels, sampleRate);
                    EnqueueSourceAudio(samples, channels, sampleRate);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AudioSamplesArrived] Error: {ex.Message}");
                }
            };
            _sourceManager.AudioPcmChunkArrived += (pcmData, volume) =>
            {
                try
                {
                    if (!_isAudioMuted && _monitorVolume > 0 && pcmData != null && pcmData.Length > 0)
                    {
                        _audioOutputDevice.PlayPcm(pcmData, 0, pcmData.Length, _monitorVolume);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AudioPcmChunkArrived] Error: {ex.Message}");
                }
            };

            // Setup WebRTC Signaling Hooks
            _signalingClient.LogEmitted += (tag, msg) => LogEvent(tag, msg);
            _signalingClient.Connected += OnSignalingConnected;
            _signalingClient.Disconnected += OnSignalingDisconnected;
            _signalingClient.TallyStateChanged += OnTallyStateChanged;
            _signalingClient.CodecChangeRequested += OnRemoteCodecChangeRequested;
            _signalingClient.IntercomAudioReceived += OnIntercomAudioReceived;
            _signalingClient.IntercomStateChanged += OnIntercomStateChanged;
            _signalingClient.DecoderReadyReceived += OnDecoderReadyReceived;
            _signalingClient.TelemetryProvider = GetTelemetrySnapshot;

            // Setup RTP Sender Hooks
            _rtpSender.LogEmitted += (tag, msg) => LogEvent(tag, msg);
            _rtpSender.KeyframeRequested += OnKeyframeRequested;

            _isInitialized = true;
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        #region Initialization & Lifecycle

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _vuBars = new[] { VuBar1, VuBar2, VuBar3, VuBar4, VuBar5, VuBar6, VuBar7, VuBar8, VuBar9, VuBar10, VuBar11, VuBar12, VuBar13, VuBar14, VuBar15, VuBar16 };
                _vuCols = new[] { ColVu1, ColVu2, ColVu3, ColVu4, ColVu5, ColVu6, ColVu7, ColVu8, ColVu9, ColVu10, ColVu11, ColVu12, ColVu13, ColVu14, ColVu15, ColVu16 };
                _vuLabels = new[] { LblVu1, LblVu2, LblVu3, LblVu4, LblVu5, LblVu6, LblVu7, LblVu8, LblVu9, LblVu10, LblVu11, LblVu12, LblVu13, LblVu14, LblVu15, LblVu16 };
                _vuClipLeds = new[] { ClipLed1, ClipLed2, ClipLed3, ClipLed4, ClipLed5, ClipLed6, ClipLed7, ClipLed8, ClipLed9, ClipLed10, ClipLed11, ClipLed12, ClipLed13, ClipLed14, ClipLed15, ClipLed16 };

                if (_pendingLogs.Count > 0 && TxtLogConsole != null)
                {
                    var sb = new StringBuilder();
                    foreach (var line in _pendingLogs) sb.Append(line);
                    TxtLogConsole.Text = sb.ToString() + TxtLogConsole.Text;
                    _pendingLogs.Clear();
                }

                // Setup VideoSourceManager & Initial Preview Player
                await _sourceManager.InitializeAsync(ReviewView, ViewboxColorbar, PnlColorbarVisualHost, TxtActiveSourceBadge, TxtActiveSourceTypeBadge);
                _sourceManager.SetAudioMonitor(!_isAudioMuted, SldMonitorVolume?.Value ?? 0.0);

                // Setup Clocks & Monitoring Timers
                _utcClockTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
                _utcClockTimer.Tick += UtcClockTimer_Tick;
                _utcClockTimer.Start();

                _telemetryTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                _telemetryTimer.Tick += TelemetryTimer_Tick;
                _telemetryTimer.Start();

                _vuMeterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
                _vuMeterTimer.Tick += VuMeterTimer_Tick;
                _vuMeterTimer.Start();

                // Auto-detect hardware GPU encoders
                ScanHardwareEncoders();

                // Scan and populate hardware microphones & speakers
                ScanMicrophoneDevices();
                ScanSpeakerDevices();
                _audioInputDevice.DataAvailable += OnMicrophoneDataAvailable;

                // Auto-scan NDI sources on LAN
                _ = ScanNdiSourcesAsync();

                // Auto-connect signaling in background
                _ = _signalingClient.StartAsync();

                // Initial Stream Audio Channels
                if (CmbStreamAudioChannels != null)
                {
                    _selectedStreamAudioChannels = CmbStreamAudioChannels.SelectedIndex switch
                    {
                        1 => 1,
                        2 => 2,
                        3 => 4,
                        4 => 8,
                        5 => 16,
                        _ => 0
                    };
                }

                // Initial source: Default to Media File Input (idx 2) with empty file path (or CLI argument if provided)
                string initFilePath = string.Empty;
                var args = Environment.GetCommandLineArgs();
                if (args.Length > 1 && File.Exists(args[1]))
                {
                    initFilePath = args[1];
                }

                TxtFilePath.Text = initFilePath;
                CmbInputSource.SelectedIndex = 2;
                PnlFileConfig.Visibility = Visibility.Visible;
                PnlColorbarConfig.Visibility = Visibility.Collapsed;
                TxtActiveSourceBadge.Text = "INPUT: MEDIA FILE";
                TxtActiveSourceTypeBadge.Text = "📁 FILE ACTIVE";
                ViewboxColorbar.Visibility = Visibility.Collapsed;

                if (!string.IsNullOrEmpty(initFilePath))
                {
                    await _sourceManager.HandleFileSourceAsync(initFilePath);
                }
                else
                {
                    await _sourceManager.SwitchSourceAsync(InputSourceType.File, string.Empty);
                }
                UpdateVuMeterColumns();

                LogEvent("[SYSTEM]", "Khởi động thành công OME WebRTC Professional Live Encoder (.NET 10). Mặc định: Media File Input (Path rỗng), Audio: Auto.");
            }
            catch (Exception ex)
            {
                LogEvent("[ERROR]", $"Lỗi khởi tạo MainWindow: {ex.Message}");
            }
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            _isClosing = true;
            _isStreaming = false;

            _utcClockTimer?.Stop();
            _telemetryTimer?.Stop();
            _vuMeterTimer?.Stop();

            _audioInputDevice.Stop();
            _audioInputDevice.Dispose();
            _audioOutputDevice.Dispose();

            _transmissionCts?.Cancel();
            _transmissionCts?.Dispose();

            KillStreamingProcesses();

            _rtpSender.Dispose();
            _signalingClient.Dispose();
            _sourceManager.Dispose();
            _colorbarEngine.Dispose();

            _sourceAudioJitterBuffer.Reset();
        }

        #endregion

        #region Signaling & Tally Handlers

        private void OnSignalingConnected()
        {
            Dispatcher.Invoke(() =>
            {
                LedSignalingStatus.Fill = new SolidColorBrush(Color.FromRgb(22, 163, 74));
                TxtSignalingStatus.Text = $"Signaling: Connected ({_signalingClient.CameraId})";
                BtnToggleSignaling.Content = "🔌 Disconnect";
            });
        }

        private void OnSignalingDisconnected(string reason)
        {
            Dispatcher.Invoke(() =>
            {
                LedSignalingStatus.Fill = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                TxtSignalingStatus.Text = "Signaling: Disconnected";
                BtnToggleSignaling.Content = "🔗 Connect Signaling";
            });
        }

        private void OnTallyStateChanged(string cameraId, string tallyState)
        {
            Dispatcher.Invoke(() =>
            {
                switch (tallyState.ToLowerInvariant())
                {
                    case "on-air":
                    case "program":
                        BrdVideoContainer.BorderBrush = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                        BrdVideoContainer.BorderThickness = new Thickness(3);
                        BrdOnAirBanner.Visibility = Visibility.Visible;
                        LedTallyStatus.Fill = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                        TxtTallyStatus.Text = "TALLY: ON-AIR (PGM)";
                        TxtTallyStatus.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
                        BadgeTallyFeedback.BorderBrush = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                        break;

                    case "preview":
                        BrdVideoContainer.BorderBrush = new SolidColorBrush(Color.FromRgb(22, 163, 74));
                        BrdVideoContainer.BorderThickness = new Thickness(3);
                        BrdOnAirBanner.Visibility = Visibility.Collapsed;
                        LedTallyStatus.Fill = new SolidColorBrush(Color.FromRgb(22, 163, 74));
                        TxtTallyStatus.Text = "TALLY: PREVIEW (PVW)";
                        TxtTallyStatus.Foreground = new SolidColorBrush(Color.FromRgb(74, 222, 128));
                        BadgeTallyFeedback.BorderBrush = new SolidColorBrush(Color.FromRgb(22, 163, 74));
                        break;

                    default:
                        BrdVideoContainer.BorderBrush = new SolidColorBrush(Color.FromRgb(45, 45, 48));
                        BrdVideoContainer.BorderThickness = new Thickness(1);
                        BrdOnAirBanner.Visibility = Visibility.Collapsed;
                        LedTallyStatus.Fill = new SolidColorBrush(Color.FromRgb(100, 100, 100));
                        TxtTallyStatus.Text = "TALLY: OFF-AIR";
                        TxtTallyStatus.Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140));
                        BadgeTallyFeedback.BorderBrush = new SolidColorBrush(Color.FromRgb(68, 68, 74));
                        break;
                }
            });
        }

        private void OnRemoteCodecChangeRequested(string codec, int bitrate, string resolution, int fps)
        {
            Dispatcher.Invoke(async () =>
            {
                LogEvent("[REMOTE]", $"Áp dụng cấu hình Codec từ xa: {codec.ToUpper()}, Bitrate: {bitrate}k");
                SldTargetBitrate.Value = bitrate;

                if (codec.Contains("265") || codec.Contains("hevc"))
                {
                    CmbVideoCodec.SelectedIndex = 1;
                }
                else
                {
                    CmbVideoCodec.SelectedIndex = 0;
                }

                if (_isStreaming)
                {
                    LogEvent("[RESTART]", "Tự động khởi động lại Encoder với Codec mới...");
                    StopTransmissionInternal();
                    await Task.Delay(500);
                    await StartTransmissionInternalAsync();
                }

                await _signalingClient.SendCodecChangedNotificationAsync(codec, bitrate, resolution, fps);
            });
        }

        private void OnDecoderReadyReceived(string cameraId, int videoPort, int audioPort, bool isSinglePort)
        {
            if (_isClosing || Dispatcher.HasShutdownStarted) return;
            Dispatcher.InvokeAsync(() =>
            {
                int camIndex = CmbCameraId.SelectedIndex;
                if (camIndex < 0) camIndex = 0;
                string currentCamId = $"cam-{(camIndex + 1):D2}";

                if (string.Equals(cameraId, currentCamId, StringComparison.OrdinalIgnoreCase))
                {
                    int oldVideoPort = _rtpSender.VideoPort;
                    int oldAudioPort = _rtpSender.AudioPort;

                    if (oldVideoPort != videoPort || oldAudioPort != audioPort)
                    {
                        LogEvent("[PORT-SYNC]", $"🎯 Decoder đã sẵn sàng lắng nghe tại Video UDP {videoPort}, Audio UDP {audioPort}. Tự động đồng bộ đích đến!");
                        _rtpSender.VideoPort = videoPort;
                        _rtpSender.AudioPort = audioPort > 0 ? audioPort : videoPort;
                        _rtpSender.IsSinglePortMode = isSinglePort;

                        string modeText = isSinglePort ? "(1-Port BUNDLE)" : "(2-Port Split)";
                        string displayName = TxtCameraDisplayName?.Text?.Trim() ?? "";
                        TxtTargetSummary.Text = $"WebRTC RTP {modeText} -> {TxtSfuHost.Text}:{videoPort} ({currentCamId} - {displayName})";
                        TxtPortStatus.Text = $"Đã đồng bộ với Decoder: UDP {videoPort}" + (isSinglePort ? " (BUNDLE)" : $" & {audioPort} (SPLIT)");
                    }
                }
            });
        }

        private void OnIntercomStateChanged(string from, bool active)
        {
            Dispatcher.Invoke(() =>
            {
                _lastIntercomSender = string.IsNullOrWhiteSpace(from) ? "Director" : from;
                _isIntercomExplicitlyActive = active;
                if (!active)
                {
                    _intercomWatchdogTimer?.Stop();
                }
                SetIntercomStatusUI(_lastIntercomSender, active);
            });
        }

        private void SetIntercomStatusUI(string from, bool active)
        {
            if (_isClosing) return;

            if (BadgeIntercom != null)
            {
                BadgeIntercom.Visibility = Visibility.Visible;
                if (active)
                {
                    BadgeIntercom.Background = new SolidColorBrush(Color.FromRgb(69, 10, 10)); // Deep Red
                    BadgeIntercom.BorderBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Bright Red
                    if (LedIntercomStatus != null)
                        LedIntercomStatus.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red LED
                    if (TxtIntercomInfo != null)
                    {
                        TxtIntercomInfo.Text = $"INTERCOM: {from.ToUpper()} (ON)";
                        TxtIntercomInfo.Foreground = new SolidColorBrush(Color.FromRgb(254, 202, 202));
                    }
                }
                else
                {
                    BadgeIntercom.Background = new SolidColorBrush(Color.FromRgb(34, 34, 40)); // Dark Gray
                    BadgeIntercom.BorderBrush = new SolidColorBrush(Color.FromRgb(68, 68, 74)); // Gray border
                    if (LedIntercomStatus != null)
                        LedIntercomStatus.Fill = new SolidColorBrush(Color.FromRgb(85, 85, 85)); // Off LED
                    if (TxtIntercomInfo != null)
                    {
                        TxtIntercomInfo.Text = $"INTERCOM: {from.ToUpper()} (OFF)";
                        TxtIntercomInfo.Foreground = new SolidColorBrush(Color.FromRgb(136, 136, 136));
                    }
                }
            }

            if (TxtDirectorStatus != null)
            {
                if (active)
                {
                    TxtDirectorStatus.Text = $"📢 DIRECTOR ({from.ToUpper()}): ON-AIR";
                    TxtDirectorStatus.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Bright red
                }
                else
                {
                    TxtDirectorStatus.Text = "📢 DIRECTOR: OFF-AIR";
                    TxtDirectorStatus.Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128)); // Gray
                }
            }
        }

        private void OnIntercomAudioReceived(string from, byte[] audioData)
        {
            Dispatcher.Invoke(() =>
            {
                _lastIntercomSender = string.IsNullOrWhiteSpace(from) ? "Director" : from;
                SetIntercomStatusUI(_lastIntercomSender, true);

                // Debounce watchdog: resets 1.5s countdown cleanly on every packet without overlapping timers!
                _intercomWatchdogTimer?.Stop();
                _intercomWatchdogTimer?.Start();
            });

            if (audioData != null && audioData.Length > 0)
            {
                try
                {
                    // Tự động nhận diện Opus packet (< 1000 bytes) hoặc legacy uncompressed PCM
                    byte[] pcmToPlay;
                    if (audioData.Length < 1000)
                    {
                        pcmToPlay = _intercomCodec.DecodeToStereoPcm(audioData, 0, audioData.Length);
                    }
                    else
                    {
                        pcmToPlay = audioData;
                    }

                    if (pcmToPlay.Length > 0)
                    {
                        _audioOutputDevice.PlayPcm(pcmToPlay, 0, pcmToPlay.Length, _intercomReceiveVolume);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[IntercomAudio] Playback error: {ex.Message}");
                }
            }
        }

        private void OnKeyframeRequested()
        {
            LogEvent("[RTP]", "🔑 SFU/Decoder yêu cầu khung hình IDR Keyframe ngay lập tức!");
        }

        private (double bitrateKbps, double fps, double packetLoss, double rttMs, int droppedFrames) GetTelemetrySnapshot()
        {
            double bitrate = _rtpSender.CurrentBitrateKbps > 0 ? _rtpSender.CurrentBitrateKbps : (SldTargetBitrate?.Value ?? 8000);
            double fps = _currentRealFps > 0 ? _currentRealFps : 30.0;
            double loss = _rtpSender.CurrentLossPercent;
            double rtt = _rtpSender.CurrentRttMs;
            int dropped = (int)Interlocked.Read(ref _realDroppedFrames);
            return (bitrate, fps, loss, rtt, dropped);
        }

        #endregion

        #region Transmission Pipeline (FFmpeg Encode -> RtpStreamSender)

        private async void BtnStartStreaming_Click(object sender, RoutedEventArgs e)
        {
            await StartTransmissionInternalAsync();
        }

        private void BtnStopStreaming_Click(object sender, RoutedEventArgs e)
        {
            StopTransmissionInternal();
        }

        private async Task StartTransmissionInternalAsync()
        {
            if (_isStreaming) return;

            try
            {
                int camIndex = CmbCameraId.SelectedIndex;
                if (camIndex < 0) camIndex = 0;
                string assignedCameraId = $"cam-{(camIndex + 1):D2}";
                string cameraDisplayName = TxtCameraDisplayName?.Text?.Trim() ?? "Sân khấu chính";
                string sfuHost = TxtSfuHost.Text.Trim();
                if (string.IsNullOrEmpty(sfuHost)) sfuHost = "127.0.0.1";

                string codecStr = (CmbVideoCodec.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "H.264";
                var rtpCodec = (codecStr.Contains("265") || codecStr.Contains("HEVC")) ? VideoRtpCodec.H265 : VideoRtpCodec.H264;
                string rawCodecName = rtpCodec == VideoRtpCodec.H265 ? "h265" : "h264";

                bool isSinglePort = CmbPortMode.SelectedIndex == 1;

                // 1. Kiểm tra & kết nối Signaling Server nếu chưa kết nối
                if (!_signalingClient.IsConnected)
                {
                    TxtPortStatus.Text = "Đang kết nối Signaling Server...";
                    _signalingClient.ServerUrl = TxtSignalingUrl.Text.Trim();
                    _signalingClient.SessionId = TxtSessionId.Text.Trim();
                    _signalingClient.CameraId = assignedCameraId;
                    _signalingClient.CameraDisplayName = cameraDisplayName;
                    await _signalingClient.StartAsync();

                    var waitStart = DateTime.UtcNow;
                    while (!_signalingClient.IsConnected && (DateTime.UtcNow - waitStart).TotalMilliseconds < 3000)
                    {
                        await Task.Delay(100);
                    }
                }

                // 2. Yêu cầu cấp Port động từ Server qua WebSocket Signaling
                TxtPortStatus.Text = "Đang yêu cầu cấp port động từ SFU...";
                PortAllocationResult allocResult;
                try
                {
                    allocResult = await _signalingClient.RequestPortAllocationAsync(assignedCameraId, isSinglePort, 4000);
                }
                catch (Exception ex)
                {
                    LogEvent("[WARN]", $"Không thể xin cấp port từ server ({ex.Message}), sử dụng fallback port.");
                    int fallbackVideoPort = 10000 + (camIndex * 4);
                    int fallbackAudioPort = isSinglePort ? fallbackVideoPort : fallbackVideoPort + 2;
                    allocResult = new PortAllocationResult
                    {
                        CameraId = assignedCameraId,
                        IsSinglePort = isSinglePort,
                        VideoPort = fallbackVideoPort,
                        AudioPort = fallbackAudioPort,
                        SfuHost = sfuHost
                    };
                }

                // 3. Cập nhật port đã cấp vào Sender và giao diện
                _rtpSender.EnableEchoCancellation = ChkAntiEcho.IsChecked == true;
                _rtpSender.ConfigureCamera(camIndex, sfuHost, rtpCodec, isSinglePort);
                _rtpSender.VideoPort = allocResult.VideoPort;
                _rtpSender.AudioPort = allocResult.AudioPort;
                _rtpSender.IsSinglePortMode = isSinglePort;
                _rtpSender.CameraDisplayName = cameraDisplayName;
                _rtpSender.CameraId = assignedCameraId;

                TxtPortStatus.Text = $"Đã cấp tự động: UDP {allocResult.VideoPort}" + (isSinglePort ? " (BUNDLE)" : $" & {allocResult.AudioPort} (SPLIT)");
                string modeText = isSinglePort ? "(1-Port BUNDLE)" : "(2-Port Split)";
                TxtTargetSummary.Text = $"WebRTC RTP {modeText} -> {sfuHost}:{allocResult.VideoPort} ({assignedCameraId} - {cameraDisplayName})";

                // 4. Bắt đầu đẩy luồng RTP
                if (!_rtpSender.Start())
                {
                    LogEvent("[ERROR]", "Không thể khởi động RTP Sender.");
                    return;
                }

                _transmissionCts?.Cancel();
                _transmissionCts?.Dispose();
                _transmissionCts = new CancellationTokenSource();
                var token = _transmissionCts.Token;

                _isStreaming = true;
                _streamStartTime = DateTime.UtcNow;
                _realEncodedFrames = 0;
                _realDroppedFrames = 0;
                _lastReportedFrames = 0;
                _currentRealFps = 0.0;
                _lastFpsCalcTime = DateTime.UtcNow;

                // Triệt tiêu toàn bộ âm thanh preview tích luỹ trước khi bấm phát sóng để đồng bộ chặt chẽ với video live
                _sourceAudioJitterBuffer.Reset();

                RefreshMasterProgramFrameBuffer();
                StartVideoEncodeWorker(token, rtpCodec);
                StartAudioTransmissionWorker(token);

                BtnStartStreaming.IsEnabled = false;
                BtnStopStreaming.IsEnabled = true;
                LedEngineStatus.Fill = new SolidColorBrush(Color.FromRgb(22, 163, 74));
                TxtEngineStatus.Text = "Engine: Transmitting RTP";

                LogEvent("[TRANSMIT]", $"🚀 Đã bắt đầu phát sóng WebRTC RTP tới SFU ({sfuHost}:{allocResult.VideoPort}, Codec: {rtpCodec}, Audio: {GetEffectiveAudioChannels()} Ch)");

                // 5. Broadcast thông báo camera stream đã publish tới các Decoder trong studio
                _ = _signalingClient.SendStreamPublishedAsync(assignedCameraId, cameraDisplayName, isSinglePort, allocResult.VideoPort, allocResult.AudioPort, rawCodecName, GetEffectiveAudioChannels());
            }
            catch (Exception ex)
            {
                LogEvent("[ERROR]", $"Lỗi khởi động phát sóng: {ex.Message}");
            }
        }

        private void StopTransmissionInternal()
        {
            if (!_isStreaming) return;

            _isStreaming = false;
            _currentRealFps = 0.0;
            _transmissionCts?.Cancel();

            KillStreamingProcesses();
            _rtpSender.Stop();

            BtnStartStreaming.IsEnabled = true;
            BtnStopStreaming.IsEnabled = false;
            LedEngineStatus.Fill = new SolidColorBrush(Color.FromRgb(220, 38, 38));
            TxtEngineStatus.Text = "Engine: Ready";

            if (TxtPortStatus != null) TxtPortStatus.Text = "Trạng thái Port: Sẵn sàng tự động cấp";

            LogEvent("[TRANSMIT]", "⏹ Đã dừng phát sóng WebRTC RTP.");
        }

        private void RenderColorbarProgramFrame()
        {
            try
            {
                var visual = PnlColorbarPattern;
                if (visual == null) return;

                int width = 1920;
                int height = 1080;
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    var brush = new VisualBrush(visual)
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
            catch { }
        }

        private void RefreshMasterProgramFrameBuffer()
        {
            try
            {
                if (_sourceManager.CurrentSource == InputSourceType.NDI || 
                    _sourceManager.CurrentSource == InputSourceType.SDI ||
                    _sourceManager.CurrentSource == InputSourceType.File)
                {
                    byte[]? liveFrame = _sourceManager.LatestMasterFrame;
                    if (liveFrame != null && liveFrame.Length > 0)
                    {
                        lock (_programFrameLock)
                        {
                            _currentProgramFrameBytes = liveFrame;
                        }
                        return;
                    }
                }
                else if (_sourceManager.CurrentSource == InputSourceType.Colorbar)
                {
                    if (_currentProgramFrameBytes == null)
                    {
                        if (Dispatcher.CheckAccess())
                        {
                            RenderColorbarProgramFrame();
                        }
                        else
                        {
                            Dispatcher.Invoke(RenderColorbarProgramFrame);
                        }
                    }
                }
            }
            catch { }
        }

        private void StartVideoEncodeWorker(CancellationToken token, VideoRtpCodec rtpCodec)
        {
            GpuEncoderOption? chosenEncoder = null;
            int selectedIdx = CmbHardwareEncoder.SelectedIndex;
            if (selectedIdx >= 0 && selectedIdx < _availableEncoders.Count)
            {
                chosenEncoder = _availableEncoders[selectedIdx];
            }
            string codecKey = chosenEncoder?.CodecKey ?? "CPU";
            int bitrateKbps = (int)SldTargetBitrate.Value;

            string vcodecArg;
            if (rtpCodec == VideoRtpCodec.H265)
            {
                switch (codecKey)
                {
                    case "QSV":
                        vcodecArg = "-c:v hevc_qsv -preset veryfast -forced_idr 1 -g 30 -bf 0";
                        break;
                    case "NVENC":
                        vcodecArg = "-c:v hevc_nvenc -preset p1 -tune ll -g 30 -bf 0 -forced-idr 1";
                        break;
                    case "AMF":
                        vcodecArg = "-c:v hevc_amf -quality speed -rc cbr -g 30 -bf 0";
                        break;
                    case "MF":
                        vcodecArg = "-c:v hevc_mf -rate_control cbr -g 30";
                        break;
                    default:
                        vcodecArg = "-c:v libx265 -preset ultrafast -tune zerolatency -g 30 -bf 0";
                        break;
                }
            }
            else
            {
                switch (codecKey)
                {
                    case "QSV":
                        vcodecArg = "-c:v h264_qsv -preset veryfast -forced_idr 1 -g 30 -bf 0";
                        break;
                    case "NVENC":
                        vcodecArg = "-c:v h264_nvenc -preset p1 -tune ll -g 30 -bf 0 -forced-idr 1";
                        break;
                    case "AMF":
                        vcodecArg = "-c:v h264_amf -quality speed -rc cbr -g 30 -bf 0";
                        break;
                    case "MF":
                        vcodecArg = "-c:v h264_mf -rate_control cbr -g 30";
                        break;
                    default:
                        vcodecArg = "-c:v libx264 -preset ultrafast -tune zerolatency -g 30 -bf 0";
                        break;
                }
            }

            string formatArg = rtpCodec == VideoRtpCodec.H265 ? "hevc" : "h264";
            string ffmpegArgs = $"-hide_banner -loglevel error -f rawvideo -pix_fmt bgra -s 1920x1080 -r 30 -i pipe:0 {vcodecArg} -b:v {bitrateKbps}k -maxrate {bitrateKbps}k -bufsize {bitrateKbps}k -an -sn -dn -f {formatArg} pipe:1";

            var psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = ffmpegArgs,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                _videoEncodeProcess = Process.Start(psi);
                if (_videoEncodeProcess == null)
                {
                    LogEvent("[ERROR]", "Không thể chạy FFmpeg Video Encoder.");
                    return;
                }

                var proc = _videoEncodeProcess;

                // Task 1: Bơm raw BGRA frames vào stdin
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var stdin = proc.StandardInput.BaseStream;
                        var sw = Stopwatch.StartNew();
                        long frameCount = 0;

                        while (!token.IsCancellationRequested && _isStreaming && !proc.HasExited)
                        {
                            RefreshMasterProgramFrameBuffer();

                            byte[]? frame;
                            lock (_programFrameLock)
                            {
                                frame = _currentProgramFrameBytes;
                            }

                            if (frame != null && frame.Length > 0)
                            {
                                await stdin.WriteAsync(frame.AsMemory(0, frame.Length), token).ConfigureAwait(false);
                                await stdin.FlushAsync(token).ConfigureAwait(false);
                                frameCount++;
                                Interlocked.Increment(ref _realEncodedFrames);
                            }

                            double targetMs = frameCount * (1000.0 / 30.0);
                            double elapsedMs = sw.Elapsed.TotalMilliseconds;
                            int sleepMs = (int)(targetMs - elapsedMs);
                            if (sleepMs > 0)
                            {
                                await Task.Delay(sleepMs, token).ConfigureAwait(false);
                            }
                            else if (sleepMs < -66) // Pipeline fell behind by > 2 frames
                            {
                                Interlocked.Increment(ref _realDroppedFrames);
                            }
                        }
                    }
                    catch { }
                }, token);

                // Task 2: Đọc raw NAL units từ stdout và gửi qua RtpStreamSender
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var stdout = proc.StandardOutput.BaseStream;
                        byte[] buffer = new byte[65536];

                        while (!token.IsCancellationRequested && _isStreaming && !proc.HasExited)
                        {
                            int read = await stdout.ReadAsync(buffer.AsMemory(0, buffer.Length), token).ConfigureAwait(false);
                            if (read > 0)
                            {
                                _rtpSender.SendVideoFrame(buffer, read, 3000);
                            }
                            else
                            {
                                await Task.Delay(2, token).ConfigureAwait(false);
                            }
                        }
                    }
                    catch { }
                }, token);

                // Task 3: Đọc stderr để tránh pipe deadlock và ghi log lỗi
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var reader = proc.StandardError;
                        string? errLine;
                        while (!token.IsCancellationRequested && (errLine = await reader.ReadLineAsync(token).ConfigureAwait(false)) != null)
                        {
                            if (!string.IsNullOrWhiteSpace(errLine))
                            {
                                LogEvent("[FFMPEG-V]", errLine);
                            }
                        }
                    }
                    catch { }
                }, token);
            }
            catch (Exception ex)
            {
                LogEvent("[ERROR]", $"Lỗi khởi chạy Video Worker: {ex.Message}");
            }
        }

        private void StartAudioTransmissionWorker(CancellationToken token)
        {
            _ = Task.Run(async () =>
            {
                var sw = Stopwatch.StartNew();
                long audioPacketIndex = 0;
                const int samplesPerFrame = 960; // 20ms @ 48kHz
                const double msPerFrame = 20.0;
                byte[] frameBuffer = Array.Empty<byte>();

                while (!token.IsCancellationRequested && _isStreaming)
                {
                    try
                    {
                        int currentChannels = GetEffectiveAudioChannels();
                        int bytesPerFrame = samplesPerFrame * currentChannels * 2;
                        if (frameBuffer.Length != bytesPerFrame)
                        {
                            frameBuffer = new byte[bytesPerFrame];
                        }

                        if (_sourceManager.CurrentSource == InputSourceType.Colorbar)
                        {
                            // Generate continuous test tone 1kHz / 400Hz / EBU Ident for configured channels
                            byte[] pcm = _colorbarEngine.GeneratePcmFrame(samplesPerFrame, currentChannels);
                            Buffer.BlockCopy(pcm, 0, frameBuffer, 0, Math.Min(pcm.Length, frameBuffer.Length));
                        }
                        else
                        {
                            // File, SDI, NDI: Extract from Elastic Audio Jitter Buffer
                            bool hasData = _sourceAudioJitterBuffer.ReadFrame(frameBuffer, bytesPerFrame);
                            if (!hasData)
                            {
                                Array.Clear(frameBuffer, 0, bytesPerFrame);
                            }
                        }

                        // Gửi frame âm thanh đa kênh sạch qua WebRTC RTP (Clean Broadcast Feed)

                        // Gửi frame âm thanh đa kênh qua WebRTC RTP
                        _rtpSender.SendAudioFrame(frameBuffer, bytesPerFrame, samplesPerFrame);

                        // Tap vào VU meter service để hiển thị tất cả các kênh (bao gồm cả kênh Mic ở vị trí cuối)
                        _audioMeterService.ProcessPcmBytes(frameBuffer, 0, bytesPerFrame, 16, currentChannels, 48000, isFloat: false);

                        audioPacketIndex++;
                        double targetMs = audioPacketIndex * msPerFrame;
                        double elapsedMs = sw.Elapsed.TotalMilliseconds;
                        int sleepMs = (int)(targetMs - elapsedMs);
                        if (sleepMs > 0)
                        {
                            await Task.Delay(sleepMs, token).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch { }
                }
            }, token);
        }

        private void EnqueueSourceAudio(float[] samples, int channels, int sampleRate)
        {
            if (samples == null || samples.Length == 0 || channels <= 0) return;

            int targetChannels = GetEffectiveAudioChannels();
            int sampleCount = samples.Length / channels;
            if (sampleCount == 0) return;

            double resampleRatio = 48000.0 / (sampleRate > 0 ? sampleRate : 48000.0);
            int outSampleCount = (int)Math.Round(sampleCount * resampleRatio);
            if (outSampleCount <= 0) return;

            byte[] pcmBytes = new byte[outSampleCount * targetChannels * 2];
            for (int i = 0; i < outSampleCount; i++)
            {
                double srcIdx = i / resampleRatio;
                int srcI = Math.Clamp((int)srcIdx, 0, sampleCount - 1);
                int baseOffset = i * targetChannels * 2;

                if (targetChannels == 1)
                {
                    // Downmix to mono
                    float monoVal = channels == 1
                        ? samples[srcI * channels]
                        : (samples[srcI * channels] + samples[srcI * channels + 1]) * 0.5f;
                    short sMono = (short)Math.Clamp((int)(monoVal * 32767.0f), short.MinValue, short.MaxValue);
                    pcmBytes[baseOffset + 0] = (byte)(sMono & 0xFF);
                    pcmBytes[baseOffset + 1] = (byte)((sMono >> 8) & 0xFF);
                }
                else
                {
                    for (int ch = 0; ch < targetChannels; ch++)
                    {
                        float val = 0.0f;
                        if (ch < channels)
                        {
                            val = samples[srcI * channels + ch];
                        }
                        else if (ch == 1 && channels == 1)
                        {
                            val = samples[srcI * channels];
                        }

                        short sVal = (short)Math.Clamp((int)(val * 32767.0f), short.MinValue, short.MaxValue);
                        int byteIdx = baseOffset + (ch * 2);
                        pcmBytes[byteIdx + 0] = (byte)(sVal & 0xFF);
                        pcmBytes[byteIdx + 1] = (byte)((sVal >> 8) & 0xFF);
                    }
                }
            }

            _sourceAudioJitterBuffer.Write(pcmBytes, 0, pcmBytes.Length);
        }

        private void KillStreamingProcesses()
        {
            try
            {
                if (_videoEncodeProcess != null && !_videoEncodeProcess.HasExited)
                {
                    _videoEncodeProcess.Kill();
                    _videoEncodeProcess.Dispose();
                    _videoEncodeProcess = null;
                }
            }
            catch { }

            try
            {
                if (_audioEncodeProcess != null && !_audioEncodeProcess.HasExited)
                {
                    _audioEncodeProcess.Kill();
                    _audioEncodeProcess.Dispose();
                    _audioEncodeProcess = null;
                }
            }
            catch { }
        }

        #endregion

        #region UI Event Handlers & Control Callbacks

        private void BtnToggleSignaling_Click(object sender, RoutedEventArgs e)
        {
            if (_signalingClient.IsConnected)
            {
                _ = _signalingClient.StopAsync();
            }
            else
            {
                _signalingClient.ServerUrl = TxtSignalingUrl.Text.Trim();
                _signalingClient.SessionId = TxtSessionId.Text.Trim();
                _signalingClient.CameraDisplayName = TxtCameraDisplayName?.Text?.Trim() ?? "";
                _ = _signalingClient.StartAsync();
            }
        }

        private void TxtCameraDisplayName_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitialized) return;
            string displayName = TxtCameraDisplayName?.Text?.Trim() ?? "";
            _signalingClient.CameraDisplayName = displayName;
            _rtpSender.CameraDisplayName = displayName;

            int idx = CmbCameraId?.SelectedIndex ?? 0;
            if (idx < 0) idx = 0;
            string camId = $"cam-{(idx + 1):D2}";
            bool isSinglePort = CmbPortMode?.SelectedIndex == 1;
            string modeText = isSinglePort ? "(1-Port BUNDLE)" : "(2-Port Split)";

            if (!_isStreaming)
            {
                if (TxtTargetSummary != null)
                {
                    TxtTargetSummary.Text = $"WebRTC RTP {modeText} -> {TxtSfuHost.Text}:Auto ({camId} - {displayName})";
                }
            }
            else
            {
                if (_rtpSender.IsRunning)
                {
                    _rtpSender.SendRtcpSdesPacket(displayName);
                }
                if (_signalingClient.IsConnected)
                {
                    _ = _signalingClient.SendCameraMetaUpdateAsync(displayName);
                }
            }
        }

        private void CmbCameraId_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;
            int idx = CmbCameraId.SelectedIndex;
            if (idx < 0) idx = 0;

            string camId = $"cam-{(idx + 1):D2}";
            bool isSinglePort = CmbPortMode?.SelectedIndex == 1;

            if (TxtCameraDisplayName != null)
            {
                string currentName = TxtCameraDisplayName.Text.Trim();
                bool isDefault = string.IsNullOrEmpty(currentName) || Array.IndexOf(_defaultCameraNames, currentName) >= 0 || currentName.StartsWith("cam-");
                if (isDefault && idx < _defaultCameraNames.Length)
                {
                    TxtCameraDisplayName.Text = _defaultCameraNames[idx];
                }
            }

            string displayName = TxtCameraDisplayName?.Text?.Trim() ?? "";

            if (!_isStreaming)
            {
                if (TxtPortStatus != null)
                {
                    TxtPortStatus.Text = $"Trạng thái Port: Sẵn sàng tự động cấp {(isSinglePort ? "(1 Port BUNDLE)" : "(2 Ports SPLIT)")}";
                }
                string modeText = isSinglePort ? "(1-Port BUNDLE)" : "(2-Port Split)";
                TxtTargetSummary.Text = $"WebRTC RTP {modeText} -> {TxtSfuHost.Text}:Auto ({camId} - {displayName})";
            }

            _signalingClient.CameraId = camId;
            _signalingClient.CameraDisplayName = displayName;
            if (_signalingClient.IsConnected)
            {
                _ = _signalingClient.StartAsync();
            }
        }

        private void CmbPortMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;
            bool isSinglePort = CmbPortMode.SelectedIndex == 1;

            int idx = CmbCameraId.SelectedIndex;
            if (idx < 0) idx = 0;
            string camId = $"cam-{(idx + 1):D2}";
            string displayName = TxtCameraDisplayName?.Text?.Trim() ?? "";

            if (!_isStreaming)
            {
                if (TxtPortStatus != null)
                {
                    TxtPortStatus.Text = $"Trạng thái Port: Sẵn sàng tự động cấp {(isSinglePort ? "(1 Port BUNDLE)" : "(2 Ports SPLIT)")}";
                }
                string modeText = isSinglePort ? "(1-Port BUNDLE)" : "(2-Port Split)";
                TxtTargetSummary.Text = $"WebRTC RTP {modeText} -> {TxtSfuHost.Text}:Auto ({camId} - {displayName})";
            }
            LogEvent("[RTP-CONFIG]", $"Thiết lập chế độ truyền: {(isSinglePort ? "1 Port duy nhất (Muxed BUNDLE A/V)" : "2 Ports riêng biệt (Split A/V)")}");
        }

        private void ChkAntiEcho_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            bool enabled = ChkAntiEcho.IsChecked == true;
            _rtpSender.EnableEchoCancellation = enabled;
            LogEvent("[AEC]", enabled 
                ? "Đã KÍCH HOẠT WebRTC 2.0 Echo Guard: Chống vòng lặp phản hồi âm thanh (Anti-Loopback)." 
                : "Đã TẮT Echo Guard.");
        }

        private void CmbVideoCodec_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;
            string codecStr = (CmbVideoCodec.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "H.264";
            TxtCodecSummary.Text = $"{codecStr} ({SldTargetBitrate.Value:0} kbps)";
        }

        private void CmbHardwareEncoder_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;
            string hw = CmbHardwareEncoder.SelectedItem?.ToString() ?? "NVENC";
            LogEvent("[HARDWARE]", $"Chọn GPU Encoder: {hw}");
        }

        private void SldTargetBitrate_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized) return;
            int val = (int)e.NewValue;
            if (TxtTargetBitrateInput != null) TxtTargetBitrateInput.Text = val.ToString();
            if (TxtBitrateDisplay != null) TxtBitrateDisplay.Text = $"({val / 1000.0:0.0} Mbps)";
            if (TxtCodecSummary != null)
            {
                string codecStr = (CmbVideoCodec.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "H.264";
                TxtCodecSummary.Text = $"{codecStr} ({val} kbps)";
            }
        }

        private void TxtTargetBitrateInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitialized) return;
            if (int.TryParse(TxtTargetBitrateInput.Text, out int parsed) && parsed >= 500 && parsed <= 50000)
            {
                if (SldTargetBitrate != null && Math.Abs(SldTargetBitrate.Value - parsed) > 10)
                {
                    SldTargetBitrate.Value = parsed;
                }
            }
        }

        private int GetSelectedStreamAudioChannels()
        {
            return _selectedStreamAudioChannels;
        }

        private int GetEffectiveAudioChannels()
        {
            int selected = _selectedStreamAudioChannels;
            if (selected > 0)
            {
                return selected;
            }
            int srcChannels = _sourceManager?.ActiveAudioChannels ?? 2;
            if (srcChannels < 1) srcChannels = 2;
            return Math.Clamp(srcChannels, 1, 16);
        }

        private void UpdateVuMeterColumns()
        {
            if (!Dispatcher.CheckAccess())
            {
                try { Dispatcher.BeginInvoke(UpdateVuMeterColumns); } catch { }
                return;
            }
            if (_vuCols == null || _vuCols.Length == 0) return;

            int activeChannels = GetEffectiveAudioChannels();

            if (TxtVuChannelCountBadge != null)
            {
                TxtVuChannelCountBadge.Text = activeChannels switch
                {
                    1 => "1-CH MONO",
                    2 => "2-CH STEREO",
                    _ => $"{activeChannels}-CH EMBEDDED"
                };
            }

            for (int i = 0; i < _vuCols.Length; i++)
            {
                bool isVisible = (i < activeChannels);
                _vuCols[i].Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
                if (isVisible)
                {
                    if (_vuLabels != null && i < _vuLabels.Length)
                    {
                        _vuLabels[i].Text = (i + 1).ToString();
                        _vuLabels[i].Foreground = new SolidColorBrush(Color.FromRgb(204, 204, 204));
                    }
                }
            }
        }

        private void CmbStreamAudioChannels_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;
            int idx = CmbStreamAudioChannels?.SelectedIndex ?? 0;
            _selectedStreamAudioChannels = idx switch
            {
                1 => 1,  // Mono
                2 => 2,  // Stereo (2 channels)
                3 => 4,  // 4 channels
                4 => 8,  // 8 channels
                5 => 16, // 16 channels
                _ => 0   // Auto (theo nguồn phát)
            };
            int channels = GetEffectiveAudioChannels();
            if (TxtStreamAudioSummary != null)
            {
                TxtStreamAudioSummary.Text = idx switch
                {
                    1 => "Opus 64 kbps @ 48.0 kHz Mono (PT 111)",
                    2 => "Opus 128 kbps @ 48.0 kHz Stereo (PT 111)",
                    3 => "Opus 256 kbps @ 48.0 kHz 4-Ch (PT 111)",
                    4 => "Opus 384 kbps @ 48.0 kHz 8-Ch (PT 111)",
                    5 => "Opus 512 kbps @ 48.0 kHz 16-Ch (PT 111)",
                    _ => $"Opus {channels * 64} kbps @ 48.0 kHz Auto ({channels}-Ch, PT 111)"
                };
            }

            UpdateVuMeterColumns();

            if (_sourceManager.CurrentSource == InputSourceType.Colorbar)
            {
                _colorbarEngine.GetAudioToneLevels16(_channelLevels16);
            }

            _sourceAudioJitterBuffer.Reset();
            LogEvent("[AUDIO]", $"Cấu hình Audio Channels: {(idx == 0 ? $"Auto ({channels}-Ch theo nguồn)" : $"{channels}-Ch cố định")}");
        }

        private async void CmbInputSource_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;
            int idx = CmbInputSource.SelectedIndex;

            PnlFileConfig.Visibility = idx == 2 ? Visibility.Visible : Visibility.Collapsed;
            PnlNdiConfig.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
            PnlSdiConfig.Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
            PnlColorbarConfig.Visibility = idx == 3 ? Visibility.Visible : Visibility.Collapsed;

            switch (idx)
            {
                case 0:
                    await _sourceManager.SwitchSourceAsync(InputSourceType.SDI);
                    TxtActiveSourceBadge.Text = "INPUT: SDI CAPTURE";
                    TxtActiveSourceTypeBadge.Text = "📡 SDI ACTIVE";
                    ViewboxColorbar.Visibility = Visibility.Collapsed;
                    break;
                case 1:
                    await _sourceManager.SwitchSourceAsync(InputSourceType.NDI);
                    TxtActiveSourceBadge.Text = "INPUT: NDI NETWORK";
                    TxtActiveSourceTypeBadge.Text = "🌐 NDI ACTIVE";
                    ViewboxColorbar.Visibility = Visibility.Collapsed;
                    break;
                case 2:
                    await _sourceManager.SwitchSourceAsync(InputSourceType.File, TxtFilePath.Text);
                    TxtActiveSourceBadge.Text = "INPUT: MEDIA FILE";
                    TxtActiveSourceTypeBadge.Text = "📁 FILE ACTIVE";
                    ViewboxColorbar.Visibility = Visibility.Collapsed;
                    break;
                case 3:
                default:
                    await _sourceManager.SwitchSourceAsync(InputSourceType.Colorbar);
                    TxtActiveSourceBadge.Text = "INPUT: COLORBAR & TONE";
                    TxtActiveSourceTypeBadge.Text = "🎨 COLORBAR ACTIVE";
                    ViewboxColorbar.Visibility = Visibility.Visible;
                    break;
            }
        }

        private async void BtnBrowseFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Chọn tệp Video phát sóng",
                Filter = "Broadcast Media (*.mp4;*.mov;*.mkv;*.ts;*.mxf)|*.mp4;*.mov;*.mkv;*.ts;*.mxf|All Files (*.*)|*.*"
            };

            if (dlg.ShowDialog() == true)
            {
                TxtFilePath.Text = dlg.FileName;
                await _sourceManager.HandleFileSourceAsync(dlg.FileName);
                LogEvent("[FILE]", $"Đã chọn tệp phát sóng: {Path.GetFileName(dlg.FileName)}");
            }
        }

        private async void TxtFilePath_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                string path = TxtFilePath.Text.Trim().Trim('"', '\'');
                if (File.Exists(path))
                {
                    await _sourceManager.HandleFileSourceAsync(path);
                    LogEvent("[FILE]", $"Đã chọn tệp phát sóng: {Path.GetFileName(path)}");
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
                LogEvent("[NDI]", "🔍 Đang quét các luồng NDI khả dụng trên mạng LAN...");
                BtnScanNdi.IsEnabled = false;
                CmbNdiSources.Items.Clear();

                var sources = await NdiNativeFinder.FindSourcesAsync(1500);
                if (sources.Count > 0)
                {
                    foreach (var src in sources)
                    {
                        CmbNdiSources.Items.Add(src);
                    }
                    CmbNdiSources.SelectedIndex = 0;
                    LogEvent("[NDI]", $"✅ Đã tìm thấy {sources.Count} nguồn NDI trên mạng LAN.");
                }
                else
                {
                    LogEvent("[WARN]", "Không tìm thấy luồng NDI nào đang phát trên mạng LAN. Bổ sung các nguồn dự phòng (OBS/vMix).");
                    CmbNdiSources.Items.Add("OBS-Studio (Local NDI Output)");
                    CmbNdiSources.Items.Add("vMix - Master Output 1");
                    CmbNdiSources.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                LogEvent("[ERROR]", $"Lỗi khi quét NDI: {ex.Message}");
                if (CmbNdiSources.Items.Count == 0)
                {
                    CmbNdiSources.Items.Add("OBS-Studio (Local NDI Output)");
                    CmbNdiSources.SelectedIndex = 0;
                }
            }
            finally
            {
                BtnScanNdi.IsEnabled = true;
            }
        }

        private async void BtnScanSdi_Click(object sender, RoutedEventArgs e)
        {
            LogEvent("[SDI]", "Đang quét các thiết bị SDI DeckLink/AJA...");
            CmbSdiDevices.Items.Clear();
            var devs = await HardwareDeviceScanner.ScanDevicesAsync();
            if (devs.Count > 0)
            {
                foreach (var d in devs) CmbSdiDevices.Items.Add(d.DisplayLabel);
            }
            else
            {
                CmbSdiDevices.Items.Add("DeckLink Quad 2 (1) - SDI In 1");
            }
            CmbSdiDevices.SelectedIndex = 0;
        }

        private async void CmbNdiSources_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized || CmbNdiSources.SelectedItem == null) return;
            if (_sourceManager.CurrentSource == InputSourceType.NDI)
            {
                await _sourceManager.HandleNdiSourceAsync(CmbNdiSources.SelectedItem.ToString() ?? "");
            }
        }

        private async void CmbSdiDevices_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized || CmbSdiDevices.SelectedItem == null) return;
            if (_sourceManager.CurrentSource == InputSourceType.SDI)
            {
                await _sourceManager.HandleSdiSourceAsync(CmbSdiDevices.SelectedItem.ToString() ?? "");
            }
        }

        private void CmbColorbarPattern_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;
            int idx = CmbColorbarPattern.SelectedIndex;
            _colorbarEngine.CurrentPattern = idx switch
            {
                1 => ColorbarPatternType.Ebu100Percent,
                2 => ColorbarPatternType.GridAlignment,
                _ => ColorbarPatternType.SmpteRp219
            };
            _sourceManager.UpdateColorbarDisplay();
        }

        private void CmbAudioTone_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;
            int idx = CmbAudioTone.SelectedIndex;
            _colorbarEngine.CurrentTone = idx switch
            {
                1 => AudioTestToneType.Sine1kHzMinus20dBFS,
                2 => AudioTestToneType.Glits400Hz,
                _ => AudioTestToneType.Sine1kHzMinus18dBFS
            };
        }

        #region Microphone Capture & Streaming

        private void ScanMicrophoneDevices()
        {
            try
            {
                CmbMicrophoneDevice.Items.Clear();
                var devices = AudioInputDevice.GetInputDevices();
                if (devices.Count > 0)
                {
                    foreach (var dev in devices)
                    {
                        CmbMicrophoneDevice.Items.Add($"[{dev.Id}] {dev.Name}");
                    }
                    CmbMicrophoneDevice.SelectedIndex = 0;
                }
                else
                {
                    CmbMicrophoneDevice.Items.Add("Không tìm thấy Microphone");
                    CmbMicrophoneDevice.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                LogEvent("[WARN]", $"Lỗi quét thiết bị Micro: {ex.Message}");
            }
        }

        private void BtnRefreshMicDevices_Click(object sender, RoutedEventArgs e)
        {
            ScanMicrophoneDevices();
            LogEvent("[AUDIO]", "Đã quét lại danh sách thiết bị Microphone trên máy.");
        }

        private void CmbMicrophoneDevice_DropDownOpened(object sender, EventArgs e)
        {
            ScanMicrophoneDevices();
        }

        private void ScanSpeakerDevices(bool preserveSelection = true)
        {
            try
            {
                int currentSelectedId = -2;
                if (preserveSelection && CmbSpeakerDevice.SelectedItem is string curItem && curItem.StartsWith("["))
                {
                    int closeIdx = curItem.IndexOf(']');
                    if (closeIdx > 1 && int.TryParse(curItem.Substring(1, closeIdx - 1), out int parsedId))
                    {
                        currentSelectedId = parsedId;
                    }
                }

                CmbSpeakerDevice.Items.Clear();
                var devices = AudioOutputDevice.GetOutputDevices();
                if (devices.Count > 0)
                {
                    int selectIdx = 0;
                    for (int i = 0; i < devices.Count; i++)
                    {
                        var dev = devices[i];
                        CmbSpeakerDevice.Items.Add($"[{dev.Id}] {dev.Name}");
                        if (dev.Id == currentSelectedId)
                        {
                            selectIdx = i;
                        }
                    }
                    CmbSpeakerDevice.SelectedIndex = selectIdx;
                }
                else
                {
                    CmbSpeakerDevice.Items.Add("Không tìm thấy Loa/Tai nghe");
                    CmbSpeakerDevice.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                LogEvent("[WARN]", $"Lỗi quét thiết bị Loa/Tai nghe: {ex.Message}");
            }
        }

        private void BtnRefreshSpeakerDevices_Click(object sender, RoutedEventArgs e)
        {
            ScanSpeakerDevices(preserveSelection: false);
            LogEvent("[AUDIO]", "🔄 Đã quét lại danh sách thiết bị Loa / Tai nghe phát.");
        }

        private void CmbSpeakerDevice_DropDownOpened(object sender, EventArgs e)
        {
            // Auto scan speaker devices upon opening dropdown
            ScanSpeakerDevices(preserveSelection: true);
        }

        private void CmbSpeakerDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized || CmbSpeakerDevice.SelectedItem == null) return;
            try
            {
                string itemText = CmbSpeakerDevice.SelectedItem.ToString() ?? "";
                if (itemText.StartsWith("["))
                {
                    int closeIdx = itemText.IndexOf(']');
                    if (closeIdx > 1 && int.TryParse(itemText.Substring(1, closeIdx - 1), out int devId))
                    {
                        bool ok = _audioOutputDevice.ChangeDevice(devId);
                        LogEvent("[AUDIO]", ok 
                            ? $"🔊 Đã chọn lối phát âm thanh Intercom: {itemText}" 
                            : $"⚠️ Không thể chuyển lối phát âm thanh {itemText}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogEvent("[WARN]", $"Lỗi áp dụng thiết bị phát âm thanh: {ex.Message}");
            }
        }

        private void ChkEnableMic_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            _isMicEnabled = ChkEnableMic.IsChecked == true;

            CmbMicrophoneDevice.IsEnabled = _isMicEnabled;
            SldMicGain.IsEnabled = _isMicEnabled;

            if (PnlMicBadge != null && TxtMicBadge != null)
            {
                PnlMicBadge.Background = _isMicEnabled ? new SolidColorBrush(Color.FromRgb(6, 78, 59)) : new SolidColorBrush(Color.FromRgb(38, 38, 38));
                PnlMicBadge.BorderBrush = _isMicEnabled ? new SolidColorBrush(Color.FromRgb(5, 150, 105)) : new SolidColorBrush(Color.FromRgb(82, 82, 82));
                TxtMicBadge.Text = _isMicEnabled ? "INTERCOM ARMED" : "INTERCOM DISABLED";
                TxtMicBadge.Foreground = _isMicEnabled ? new SolidColorBrush(Color.FromRgb(52, 211, 153)) : new SolidColorBrush(Color.FromRgb(158, 158, 158));
            }

            if (_isMicEnabled)
            {
                int devId = -1;
                string? selected = CmbMicrophoneDevice.SelectedItem?.ToString();
                if (!string.IsNullOrEmpty(selected) && selected.StartsWith("[") && selected.Contains("]"))
                {
                    int end = selected.IndexOf(']');
                    if (int.TryParse(selected.Substring(1, end - 1), out int parsedId))
                    {
                        devId = parsedId;
                    }
                }
                if (devId < 0 && CmbMicrophoneDevice.SelectedIndex >= 0)
                {
                    devId = CmbMicrophoneDevice.SelectedIndex;
                }

                bool ok = _audioInputDevice.Start(devId);
                if (!ok && devId != -1)
                {
                    ok = _audioInputDevice.Start(-1);
                    if (ok)
                    {
                        LogEvent("[INTERCOM]", "🎙️ Đã kích hoạt Micro Intercom mặc định hệ thống (WAVE_MAPPER).");
                    }
                }

                LogEvent("[INTERCOM]", ok 
                    ? $"🎙️ Đã BẬT Micro Intercom Headset (Thiết bị ID: {devId}). Sẵn sàng đàm thoại 2 chiều qua WebSocket (Giữ Spacebar hoặc click TALK để nói)." 
                    : $"⚠️ Không thể mở thiết bị Micro ID {devId}.");
            }
            else
            {
                SetPttActive(false);
                _audioInputDevice.Stop();
                LogEvent("[INTERCOM]", "🔇 Đã TẮT Micro Intercom.");
            }

            UpdateVuMeterColumns();
        }

        private void CmbMicrophoneDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized || !_isMicEnabled) return;
            int devId = -1;
            string? selected = CmbMicrophoneDevice.SelectedItem?.ToString();
            if (!string.IsNullOrEmpty(selected) && selected.StartsWith("[") && selected.Contains("]"))
            {
                int end = selected.IndexOf(']');
                if (int.TryParse(selected.Substring(1, end - 1), out int parsedId))
                {
                    devId = parsedId;
                }
            }
            if (devId < 0 && CmbMicrophoneDevice.SelectedIndex >= 0)
            {
                devId = CmbMicrophoneDevice.SelectedIndex;
            }

            _audioInputDevice.Stop();
            bool ok = _audioInputDevice.Start(devId);
            if (!ok && devId != -1)
            {
                _audioInputDevice.Start(-1);
            }
            LogEvent("[INTERCOM]", $"Chuyển sang Micro Intercom thiết bị ID {devId}.");
        }

        private void SldMicGain_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized) return;
            _micGainMultiplier = e.NewValue / 100.0;
            if (TxtMicGainVal != null) TxtMicGainVal.Text = $"{(int)e.NewValue}%";
        }

        private void OnMicrophoneDataAvailable(byte[] pcmData)
        {
            if (!_isMicEnabled || !_isPttActive || pcmData == null || pcmData.Length == 0) return;

            int sampleCount = pcmData.Length / 4; // 16-bit stereo input (4 bytes per frame)
            if (sampleCount <= 0) return;

            // Chuyển đổi và chuẩn hóa thành 16-bit Stereo 48kHz cho Intercom WebSocket
            byte[] stereoBytes = new byte[sampleCount * 4];

            for (int i = 0; i < sampleCount; i++)
            {
                short left = (short)(pcmData[i * 4] | (pcmData[i * 4 + 1] << 8));
                short right = (short)(pcmData[i * 4 + 2] | (pcmData[i * 4 + 3] << 8));
                int mixed = (int)((left + right) * 0.5 * _micGainMultiplier);
                short clamped = (short)Math.Clamp(mixed, short.MinValue, short.MaxValue);

                // L
                stereoBytes[i * 4 + 0] = (byte)(clamped & 0xFF);
                stereoBytes[i * 4 + 1] = (byte)((clamped >> 8) & 0xFF);
                // R
                stereoBytes[i * 4 + 2] = (byte)(clamped & 0xFF);
                stereoBytes[i * 4 + 3] = (byte)((clamped >> 8) & 0xFF);
            }

            // Nén qua Opus Voice (VOIP 32 kbps) -> Giảm kích thước từ 3840 bytes xuống ~80-100 bytes (>97% bandwidth savings)
            byte[] opusPacket = _intercomCodec.EncodePcm(stereoBytes, 0, stereoBytes.Length);
            if (opusPacket.Length > 0)
            {
                _ = _signalingClient.SendIntercomAudioAsync("studio-decoder-1", opusPacket, "opus");
            }
        }

        #region Intercom PTT & Keyboard Shortcuts

        private long _pttMouseDownTick = 0;
        private bool _wasPttActiveOnMouseDown = false;

        private void SetPttActive(bool active)
        {
            if (active && !_isMicEnabled) return;
            if (_isPttActive == active) return;
            _isPttActive = active;
            UpdatePttUi(active);
            if (!active && Mouse.Captured != null)
            {
                Mouse.Capture(null);
            }
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space && !(FocusManager.GetFocusedElement(this) is TextBox))
            {
                if (!e.IsRepeat)
                {
                    SetPttActive(true);
                }
                e.Handled = true; // Luôn chặn lặp phím Space để WPF không kích hoạt hover/click kẹt chuột
            }
        }

        private void MainWindow_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space && !(FocusManager.GetFocusedElement(this) is TextBox))
            {
                SetPttActive(false);
                e.Handled = true;
                if (Mouse.Captured != null)
                {
                    Mouse.Capture(null);
                }
            }
        }

        private void BtnPttTalk_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isMicEnabled) return;
            _pttMouseDownTick = Environment.TickCount64;
            _wasPttActiveOnMouseDown = _isPttActive;
            SetPttActive(true);
            e.Handled = true;
        }

        private void BtnPttTalk_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (Mouse.Captured != null)
            {
                Mouse.Capture(null);
            }

            if (!_isMicEnabled) return;

            long elapsed = Environment.TickCount64 - _pttMouseDownTick;
            if (elapsed > 350)
            {
                // Giữ lâu hơn 350ms: Chế độ PTT (Nhả chuột là tắt nói)
                SetPttActive(false);
            }
            else
            {
                // Bấm nhả nhanh (< 350ms): Chế độ Toggle (Nếu trước đó đã bật thì tắt, nếu chưa bật thì giữ bật)
                if (_wasPttActiveOnMouseDown)
                {
                    SetPttActive(false);
                }
                else
                {
                    SetPttActive(true);
                }
            }
            e.Handled = true;
        }

        private void BtnPttTalk_MouseLeave(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _isPttActive)
            {
                // Đang giữ chuột mà rê ra ngoài button thì nhả PTT
                SetPttActive(false);
            }
            if (Mouse.Captured != null)
            {
                Mouse.Capture(null);
            }
        }

        private void UpdatePttUi(bool active)
        {
            if (BtnPttTalk == null || TxtPttLabel == null) return;
            if (active)
            {
                BtnPttTalk.Background = new SolidColorBrush(Color.FromRgb(220, 38, 38)); // Red
                BtnPttTalk.BorderBrush = new SolidColorBrush(Color.FromRgb(248, 113, 113));
                TxtPttLabel.Text = "🔴 TRANSMITTING (TALK)";
                TxtPttLabel.Foreground = Brushes.White;
            }
            else
            {
                BtnPttTalk.Background = new SolidColorBrush(Color.FromRgb(31, 41, 55)); // Dark gray
                BtnPttTalk.BorderBrush = new SolidColorBrush(Color.FromRgb(55, 65, 81));
                TxtPttLabel.Text = "🎙️ TALK (PTT)";
                TxtPttLabel.Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175));
            }
        }

        private void SldIntercomReceiveVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized) return;
            _intercomReceiveVolume = e.NewValue / 100.0;
            if (TxtIntercomReceiveVolumeVal != null)
            {
                TxtIntercomReceiveVolumeVal.Text = $"{(int)e.NewValue}%";
            }
        }

        #endregion

        #endregion



        private void ChkNtpSync_Changed(object sender, RoutedEventArgs e)
        {
            bool isNtp = ChkNtpSync.IsChecked == true;
            BadgeNtpSync.Visibility = isNtp ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void BtnSyncNtp_Click(object sender, RoutedEventArgs e)
        {
            string host = TxtNtpServer.Text.Trim();
            LogEvent("[NTP]", $"Đang đồng bộ với máy chủ thời gian: {host}...");
            var res = await NtpClient.QueryTimeAsync(host);
            if (res.Success)
            {
                LogEvent("[NTP]", $"Đồng bộ NTP thành công! Độ lệch đồng hồ: {res.GetFormattedOffset()}");
            }
            else
            {
                LogEvent("[WARN]", $"Không thể kết nối máy chủ NTP ({res.ErrorMessage}). Sử dụng đồng hồ Windows High-Res.");
            }
        }

        private void ChkEnableVideoPreview_Changed(object sender, RoutedEventArgs e)
        {
            bool show = ChkEnableVideoPreview.IsChecked == true;
            PnlPreviewDisabled.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        }

        private void ChkEnableAudioMonitor_Changed(object sender, RoutedEventArgs e)
        {
            bool show = ChkEnableAudioMonitor.IsChecked == true;
            OverlayAudioVu.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ChkShowTelemetry_Changed(object sender, RoutedEventArgs e)
        {
            bool show = ChkShowTelemetry.IsChecked == true;
            OverlayTelemetry.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnAspectMode_Click(object sender, RoutedEventArgs e)
        {
            if (ReviewView.Stretch == Stretch.Uniform)
            {
                ReviewView.Stretch = Stretch.Fill;
                BtnAspectMode.Content = "📐 Aspect: Scale";
            }
            else
            {
                ReviewView.Stretch = Stretch.Uniform;
                BtnAspectMode.Content = "📐 Aspect: Fit";
            }
        }

        private void BtnAudioMute_Click(object sender, RoutedEventArgs e)
        {
            _isAudioMuted = !_isAudioMuted;
            if (_isAudioMuted)
            {
                _lastNonZeroVolume = _monitorVolume > 0.05 ? _monitorVolume : 0.4;
                _monitorVolume = 0;
                SldMonitorVolume.Value = 0;
                TxtAudioMuteIcon.Text = "🔇";
                TxtAudioMuteIcon.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                _sourceManager.SetAudioMonitor(false, 0);
            }
            else
            {
                double restoreVol = _lastNonZeroVolume > 0.05 ? _lastNonZeroVolume : 0.4;
                _monitorVolume = restoreVol;
                SldMonitorVolume.Value = restoreVol;
                TxtAudioMuteIcon.Text = "🔊";
                TxtAudioMuteIcon.Foreground = new SolidColorBrush(Color.FromRgb(34, 197, 94));
                _sourceManager.SetAudioMonitor(true, restoreVol);
            }
        }

        private void SldMonitorVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized) return;
            _monitorVolume = e.NewValue;
            if (TxtMonitorVolumeVal != null) TxtMonitorVolumeVal.Text = $"{(int)(e.NewValue * 100)}%";
            _sourceManager.SetAudioMonitor(!_isAudioMuted, e.NewValue);
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            TxtLogConsole.Text = string.Empty;
        }

        private void BtnScanHardwareEncoder_Click(object sender, RoutedEventArgs e)
        {
            ScanHardwareEncoders();
            LogEvent("[HARDWARE]", "🔄 Đã quét lại danh sách GPU Hardware Acceleration Encoders.");
        }

        private void ScanHardwareEncoders()
        {
            CmbHardwareEncoder.Items.Clear();
            _availableEncoders = GpuHardwareScanner.ScanAvailableEncoders();

            int selectedIdx = 0;
            for (int i = 0; i < _availableEncoders.Count; i++)
            {
                var enc = _availableEncoders[i];
                CmbHardwareEncoder.Items.Add(enc.DisplayTitle);
                if (enc.IsPreferred)
                {
                    selectedIdx = i;
                }
            }

            if (CmbHardwareEncoder.Items.Count > 0)
            {
                CmbHardwareEncoder.SelectedIndex = selectedIdx;
                var chosen = _availableEncoders[selectedIdx];
                LogEvent("[GPU-SCAN]", $"Đã phát hiện {CmbHardwareEncoder.Items.Count} encoder. Mặc định chọn: {chosen.DisplayTitle}");
            }
        }

        private void UpdateSourceTelemetryUI(VideoSourceTelemetry telem)
        {
            if (telem == null) return;

            void Apply()
            {
                if (TxtActiveSourceBadge != null) TxtActiveSourceBadge.Text = $"INPUT: {telem.SourceType} ({telem.SourceName})";
            }

            if (_isClosing || Dispatcher.HasShutdownStarted) return;
            if (Dispatcher.CheckAccess()) Apply();
            else try { Dispatcher.BeginInvoke(Apply); } catch { }
        }

        #endregion

        #region Periodic Timers & VU Meter Analysis

        private void UtcClockTimer_Tick(object? sender, EventArgs e)
        {
            var now = DateTime.UtcNow;
            TxtMasterUtcTime.Text = now.ToString("HH:mm:ss.fff");

            int frame = (int)((now.Millisecond / 1000.0) * 30);
            TxtMasterSmpteTime.Text = $"{now:HH\\:mm\\:ss}:{frame:D2}";
            TxtColorbarUtcTime.Text = now.ToString("HH:mm:ss.fff");

            // Render Colorbar Program Frame on UI thread during transmission to capture live UTC clock & text cards
            if (_isStreaming && _sourceManager.CurrentSource == InputSourceType.Colorbar)
            {
                RenderColorbarProgramFrame();
            }
        }

        private void TelemetryTimer_Tick(object? sender, EventArgs e)
        {
            var now = DateTime.UtcNow;

            // 1. Calculate real FPS & frame drops over moving window
            if (_isStreaming)
            {
                double elapsedFpsSec = (now - _lastFpsCalcTime).TotalSeconds;
                if (elapsedFpsSec >= 0.4)
                {
                    long curFrames = Interlocked.Read(ref _realEncodedFrames);
                    long delta = curFrames - _lastReportedFrames;
                    if (delta < 0) delta = 0;
                    _currentRealFps = delta / elapsedFpsSec;
                    _lastReportedFrames = curFrames;
                    _lastFpsCalcTime = now;
                }
            }
            else
            {
                _currentRealFps = 0.0;
            }

            // 2. Fetch live metrics from RtpStreamSender
            double rtt = _rtpSender.CurrentRttMs;
            double loss = _rtpSender.CurrentLossPercent;
            double jitter = _rtpSender.JitterMs;
            double totalBitrate = _rtpSender.CurrentBitrateKbps;
            double videoBitrate = _rtpSender.VideoBitrateKbps;
            double audioBitrate = _rtpSender.AudioBitrateKbps;
            int nack = _rtpSender.NackCount;
            int pli = _rtpSender.PliCount;
            long dropped = Interlocked.Read(ref _realDroppedFrames);

            // 3. Compute Composite Transmission QoS Score (0 - 100%)
            double qosScore = 100.0;
            if (!_isStreaming)
            {
                qosScore = 100.0;
            }
            else
            {
                qosScore -= Math.Min(40.0, loss * 10.0);
                if (rtt > 30.0) qosScore -= Math.Min(30.0, (rtt - 30.0) * 0.25);
                if (_currentRealFps < 28.0) qosScore -= Math.Min(30.0, (28.0 - _currentRealFps) * 3.0);
                if (dropped > 0) qosScore -= Math.Min(20.0, dropped * 2.0);
                qosScore = Math.Clamp(qosScore, 0.0, 100.0);
            }

            string qosText;
            Color qosBgColor;
            Color qosFgColor;

            if (!_isStreaming)
            {
                qosText = "● STANDBY (100%)";
                qosBgColor = Color.FromRgb(30, 41, 59);
                qosFgColor = Color.FromRgb(148, 163, 184);
            }
            else if (qosScore >= 90.0)
            {
                qosText = $"● EXCELLENT ({qosScore:0}%)";
                qosBgColor = Color.FromRgb(27, 94, 32);
                qosFgColor = Color.FromRgb(105, 240, 174);
            }
            else if (qosScore >= 75.0)
            {
                qosText = $"● GOOD ({qosScore:0}%)";
                qosBgColor = Color.FromRgb(46, 125, 50);
                qosFgColor = Color.FromRgb(174, 234, 0);
            }
            else if (qosScore >= 50.0)
            {
                qosText = $"▲ FAIR ({qosScore:0}%)";
                qosBgColor = Color.FromRgb(245, 127, 23);
                qosFgColor = Color.FromRgb(255, 245, 157);
            }
            else
            {
                qosText = $"✖ POOR ({qosScore:0}%)";
                qosBgColor = Color.FromRgb(183, 28, 28);
                qosFgColor = Color.FromRgb(255, 138, 128);
            }

            // Brush colors for quality thresholds
            var rttBrush = new SolidColorBrush(rtt < 40 ? Color.FromRgb(0, 230, 118) : (rtt < 100 ? Color.FromRgb(255, 213, 79) : Color.FromRgb(255, 82, 82)));
            var lossBrush = new SolidColorBrush(loss < 0.2 ? Color.FromRgb(0, 230, 118) : (loss < 1.5 ? Color.FromRgb(255, 213, 79) : Color.FromRgb(255, 82, 82)));
            var fpsBrush = new SolidColorBrush(_isStreaming ? (_currentRealFps >= 28 ? Color.FromRgb(255, 255, 255) : (_currentRealFps >= 20 ? Color.FromRgb(255, 213, 79) : Color.FromRgb(255, 82, 82))) : Color.FromRgb(170, 170, 170));

            // 4. Update On-Screen WebRTC Telemetry HUD Overlay
            if (OverlayTelemetry != null && OverlayTelemetry.Visibility == Visibility.Visible)
            {
                if (BrdHudQosBadge != null) BrdHudQosBadge.Background = new SolidColorBrush(qosBgColor);
                if (TxtHudQos != null)
                {
                    TxtHudQos.Text = qosText;
                    TxtHudQos.Foreground = new SolidColorBrush(qosFgColor);
                }

                if (TxtHudRtt != null)
                {
                    TxtHudRtt.Text = $"{rtt:0} ms";
                    TxtHudRtt.Foreground = rttBrush;
                }

                if (TxtHudLoss != null)
                {
                    TxtHudLoss.Text = $"{loss:0.0} %";
                    TxtHudLoss.Foreground = lossBrush;
                }

                if (TxtHudJitter != null) TxtHudJitter.Text = $"{jitter:0.0} ms";

                if (TxtHudBitrate != null) TxtHudBitrate.Text = $"{totalBitrate:0} kbps";
                if (TxtHudBitrateDetail != null) TxtHudBitrateDetail.Text = $"V: {videoBitrate:0}k | A: {audioBitrate:0}k";

                if (TxtHudFps != null)
                {
                    TxtHudFps.Text = $"{_currentRealFps:0.0} FPS (Drop: {dropped})";
                    TxtHudFps.Foreground = fpsBrush;
                }

                if (TxtHudNack != null) TxtHudNack.Text = $"{nack} pkts";
                if (TxtHudPli != null) TxtHudPli.Text = $"{pli} reqs";
                if (TxtHudPts != null) TxtHudPts.Text = now.ToString("HH:mm:ss.fff");
            }

            // 5. Update Tab 1 Live Telemetry & QoS Stats Card
            if (BrdTabQosBadge != null) BrdTabQosBadge.Background = new SolidColorBrush(qosBgColor);
            if (TxtTabQos != null)
            {
                TxtTabQos.Text = qosText;
                TxtTabQos.Foreground = new SolidColorBrush(qosFgColor);
            }

            if (TxtTabRtt != null)
            {
                TxtTabRtt.Text = $"{rtt:0} ms";
                TxtTabRtt.Foreground = rttBrush;
            }

            if (TxtTabLoss != null)
            {
                TxtTabLoss.Text = $"{loss:0.00} %";
                TxtTabLoss.Foreground = lossBrush;
            }

            if (TxtTabLossDetail != null)
            {
                TxtTabLossDetail.Text = $"{nack} NACKs / {_rtpSender.TotalPacketsSent} Pkts";
            }

            if (TxtTabFps != null)
            {
                TxtTabFps.Text = $"{_currentRealFps:0.0} FPS";
                TxtTabFps.Foreground = fpsBrush;
            }

            if (TxtTabDropDetail != null)
            {
                TxtTabDropDetail.Text = $"Dropped: {dropped} frames";
            }

            if (TxtTabBitrate != null)
            {
                TxtTabBitrate.Text = $"{totalBitrate:0} kbps";
            }

            if (TxtTabBitrateBreakdown != null)
            {
                TxtTabBitrateBreakdown.Text = $"Video: {videoBitrate:0}k | Audio: {audioBitrate:0}k";
            }

            // 6. Update Bottom Master Bar Duration & Data Counters
            if (_isStreaming)
            {
                var duration = now - _streamStartTime;
                TxtSessionDuration.Text = duration.ToString(@"hh\:mm\:ss");

                double sentMb = _rtpSender.TotalBytesSent / (1024.0 * 1024.0);
                TxtTotalBytesSent.Text = $"{sentMb:0.00} MB";
            }
        }

        private void VuMeterTimer_Tick(object? sender, EventArgs e)
        {
            if (OverlayAudioVu.Visibility != Visibility.Visible) return;

            int activeChannels = GetEffectiveAudioChannels();

            if (TxtVuChannelCountBadge != null)
            {
                TxtVuChannelCountBadge.Text = activeChannels switch
                {
                    1 => "1-CH MONO",
                    2 => "2-CH STEREO",
                    4 => "4-CH EMBEDDED",
                    8 => "8-CH EMBEDDED",
                    16 => "16-CH EMBEDDED",
                    _ => $"{activeChannels}-CH EMBEDDED"
                };
            }

            if (_sourceManager.CurrentSource == InputSourceType.Colorbar && !_isStreaming)
            {
                _colorbarEngine.GetAudioToneLevels16(_channelLevels16);
            }
            else
            {
                _audioMeterService.GetPreviewAudioLevels(_channelLevels16, _channelRmsLevels16, _channelClipping16, isAudioActive: true);
            }

            for (int i = 0; i < Math.Min(_channelLevels16.Length, _vuBars.Length); i++)
            {
                if (i >= _vuCols.Length) continue;

                // 1. Chỉ visible các channels theo lựa chọn người sử dụng / auto
                if (i < activeChannels)
                {
                    if (_vuCols[i].Visibility != Visibility.Visible)
                    {
                        _vuCols[i].Visibility = Visibility.Visible;
                    }

                    double db = _channelLevels16[i];
                    if (db < -60.0) db = -60.0;
                    if (db > 0.0) db = 0.0;

                    _vuBars[i].Value = db;
                    _vuClipLeds[i].Fill = _channelClipping16[i]
                        ? new SolidColorBrush(Color.FromRgb(244, 67, 54))
                        : new SolidColorBrush(Color.FromRgb(42, 42, 46));

                    // 2. Disable và làm mờ các kênh không có tín hiệu âm thanh (db <= -58.0 dBFS)
                    bool hasSignal = db > -58.0;
                    _vuCols[i].IsEnabled = hasSignal;
                    _vuCols[i].Opacity = hasSignal ? 1.0 : 0.35;
                }
                else
                {
                    if (_vuCols[i].Visibility != Visibility.Collapsed)
                    {
                        _vuCols[i].Visibility = Visibility.Collapsed;
                    }
                }
            }
        }

        #endregion

        #region Logging

        public void LogEvent(string tag, string message)
        {
            if (_isClosing) return;

            string line = $"[{DateTime.Now:HH:mm:ss.fff}] {tag} {message}\n";

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => LogEvent(tag, message)));
                return;
            }

            if (TxtLogConsole == null || !_isInitialized)
            {
                _pendingLogs.Add(line);
                return;
            }

            TxtLogConsole.Text = line + TxtLogConsole.Text;
            if (TxtLogConsole.Text.Length > 25000)
            {
                TxtLogConsole.Text = TxtLogConsole.Text.Substring(0, 20000);
            }
        }

        #endregion
    }
}
