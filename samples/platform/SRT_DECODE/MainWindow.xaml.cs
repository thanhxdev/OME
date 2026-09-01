using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using OpenMedia.Platform;
using OpenMedia.Platform.Controls.Wpf;
using OpenMedia.Platform.Models;

namespace SRT_DECODE
{
    public partial class MainWindow : Window
    {
        // ─── Subsystem Engines ──────────────────────────────────────
        private readonly NtpSyncEngine _syncEngine = new();
        private readonly MultiStreamReceiverEngine _receiverEngine;
        private readonly AudioMonitoringManager _audioManager = new();
        private readonly BroadcastOutputManager _outputManager = new();

        // ─── Timers ─────────────────────────────────────────────────
        private DispatcherTimer? _masterClockTimer;
        private DispatcherTimer? _telemetryTimer;
        private DispatcherTimer? _vuMeterTimer;

        // ─── State Variables ────────────────────────────────────────
        private int _currentProgramIndex = 0; // 0: Cam1, 1: Cam2, 2: Cam3, 3: Cam4
        private int _currentPreviewIndex = 1; // 1: Cam2
        private bool _isSingleStreamMode = false;
        private bool _isTransitioning = false;
        private bool _isInitialized = false;
        private readonly StringBuilder _logBuffer = new();

        // ─── Control Reference Arrays ───────────────────────────────
        private Border[] _cellBorders = Array.Empty<Border>();
        private Border[] _tallyBadges = Array.Empty<Border>();
        private TextBlock[] _tallyTexts = Array.Empty<TextBlock>();
        private Ellipse[] _ledIndicators = Array.Empty<Ellipse>();
        private Button[] _pgmButtons = Array.Empty<Button>();
        private Border[] _hudOverlays = Array.Empty<Border>();
        private Border[] _fallbacks = Array.Empty<Border>();
        private TextBlock[] _statusTexts = Array.Empty<TextBlock>();
        private ProgressBar[] _vuBarsL = Array.Empty<ProgressBar>();
        private ProgressBar[] _vuBarsR = Array.Empty<ProgressBar>();

        // ─── Video Presentation Bitmaps ─────────────────────────────
        private readonly WriteableBitmap?[] _camBitmaps = new WriteableBitmap?[4];

        public MainWindow()
        {
            _receiverEngine = new MultiStreamReceiverEngine(_syncEngine);

            // Wire log events
            _syncEngine.LogEmitted += LogEvent;
            _receiverEngine.LogEmitted += LogEvent;
            _audioManager.LogEmitted += LogEvent;
            _outputManager.LogEmitted += LogEvent;

            // Wire receiver updates
            _receiverEngine.ChannelUpdated += OnReceiverChannelUpdated;
            _receiverEngine.FrameReady += OnFrameReady;
            _audioManager.CamLevelsUpdated += OnAudioLevelsUpdated;
            _audioManager.ProgramLevelsUpdated += OnProgramLevelsUpdated;

            InitializeComponent();
            _isInitialized = true;

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        #region Initialization & Lifecycle

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Cache UI Element references for fast indexing
                _cellBorders = new[] { CellCam1, CellCam2, CellCam3, CellCam4 };
                _tallyBadges = new[] { TallyCam1, TallyCam2, TallyCam3, TallyCam4 };
                _tallyTexts = new[] { (TextBlock)TallyCam1.Child, (TextBlock)TallyCam2.Child, (TextBlock)TallyCam3.Child, (TextBlock)TallyCam4.Child };
                _ledIndicators = new[] { LedCam1, LedCam2, LedCam3, LedCam4 };
                _pgmButtons = new[] { BtnPgmCam1, BtnPgmCam2, BtnPgmCam3, BtnPgmCam4 };
                _hudOverlays = new[] { HudOverlayCam1, HudOverlayCam2, HudOverlayCam3, HudOverlayCam4 };
                _fallbacks = new[] { FallbackCam1, FallbackCam2, FallbackCam3, FallbackCam4 };
                _statusTexts = new[] { TxtStatusCam1, TxtStatusCam2, TxtStatusCam3, TxtStatusCam4 };
                _vuBarsL = new[] { VuCam1L, VuCam2L, VuCam3L, VuCam4L };
                _vuBarsR = new[] { VuCam1R, VuCam2R, VuCam3R, VuCam4R };

                // Initialize WriteableBitmaps for Quad-View video rendering
                for (int i = 0; i < 4; i++)
                {
                    _camBitmaps[i] = new WriteableBitmap(1920, 1080, 96, 96, PixelFormats.Bgra32, null);
                }
                VideoViewCam1.PresentBitmap(_camBitmaps[0]);
                VideoViewCam2.PresentBitmap(_camBitmaps[1]);
                VideoViewCam3.PresentBitmap(_camBitmaps[2]);
                VideoViewCam4.PresentBitmap(_camBitmaps[3]);

                LogEvent("[INFO]", "Ứng dụng OME Broadcast Multi-SRT Decoder & Studio Sync đang khởi chạy...");
                TxtEngineStatus.Text = "Engine: Initializing Platform...";

                // Initialize OpenMedia Runtime Engine
                bool runtimeInit = await OpenMediaRuntime.InitializeAsync(new RuntimeOptions { AutoLaunch = true });
                if (runtimeInit)
                {
                    TxtEngineStatus.Text = "Engine: OpenMedia.Platform Active (DirectX 11 D3D11 Shared Textures)";
                    TxtEngineStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
                    LogEvent("[ENGINE]", "Khởi tạo OpenMedia.Platform thành công với GPU D3D11 Zero-Copy Pipeline.");
                }
                else
                {
                    TxtEngineStatus.Text = "Engine: Standalone / Host Mode";
                    TxtEngineStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
                    LogEvent("[WARN]", "OpenMedia Native Engine chưa phát hiện IPC server, chuyển sang chế độ Standalone Host.");
                }

                // Start Timers
                StartMasterClockTimer();
                StartTelemetryTimer();
                StartVuMeterTimer();

                // Initial UI Tally Sync
                UpdateTallyIndicators();
                LogEvent("[INFO]", "Hệ thống Master Control Room đã sẵn sàng nhận luồng SRT (Cam 1-4).");
            }
            catch (Exception ex)
            {
                LogEvent("[ERROR]", $"Lỗi khởi tạo ứng dụng: {ex.Message}");
            }
        }

        private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                _masterClockTimer?.Stop();
                _telemetryTimer?.Stop();
                _vuMeterTimer?.Stop();

                await _receiverEngine.DisposeAsync();
                _syncEngine.Dispose();
                _outputManager.Dispose();
                OpenMediaRuntime.Shutdown();
            }
            catch { }
        }

        #endregion

        #region Master Clock & Telemetry Timers

        private void StartMasterClockTimer()
        {
            _masterClockTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(20) // 50 Hz UI clock
            };
            _masterClockTimer.Tick += (s, e) =>
            {
                var now = DateTime.UtcNow;
                if (_syncEngine.LastNtpResult?.Success == true)
                {
                    now = now.AddMilliseconds(_syncEngine.LastNtpResult.OffsetMs);
                }
                TxtMasterUtcClock.Text = now.ToString("HH:mm:ss.fff");

                // Update Recording duration display
                if (_outputManager.RecordingEnabled)
                {
                    _outputManager.UpdateRecordingStats();
                    TxtRecTime.Text = _outputManager.RecordingDuration.ToString(@"hh\:mm\:ss");
                    TxtRecSize.Text = $"{_outputManager.RecordedBytes / (1024.0 * 1024.0):F2} MB";
                }
            };
            _masterClockTimer.Start();
        }

        private void StartTelemetryTimer()
        {
            _telemetryTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1) // 1s refresh interval
            };
            _telemetryTimer.Tick += (s, e) =>
            {
                UpdateTelemetryUI();
            };
            _telemetryTimer.Start();
        }

        private void StartVuMeterTimer()
        {
            _vuMeterTimer = new DispatcherTimer(DispatcherPriority.Normal)
            {
                Interval = TimeSpan.FromMilliseconds(33) // ~30 fps VU animation
            };
            _vuMeterTimer.Tick += (s, e) =>
            {
                bool[] active = new bool[4];
                for (int i = 0; i < 4; i++)
                {
                    active[i] = _receiverEngine.Channels[i].IsConnected;
                }
                _audioManager.ProcessAudioTick(active, _currentProgramIndex);
            };
            _vuMeterTimer.Start();
        }

        #endregion

        #region Telemetry & Diagnostics Refresh

        private void UpdateTelemetryUI()
        {
            double totalBitrateKbps = 0;
            ulong totalBytes = 0;
            var syncSnapshot = _syncEngine.GetSnapshot();

            for (int i = 0; i < 4; i++)
            {
                var ch = _receiverEngine.Channels[i];
                var sync = syncSnapshot[i];

                totalBitrateKbps += ch.CurrentBitrateKbps;
                totalBytes += ch.TotalBytesReceived;

                // Update HUD Overlay in Video Cell
                switch (i)
                {
                    case 0:
                        HudRttCam1.Text = $"RTT: {ch.CurrentRttMs:F0} ms";
                        HudLossCam1.Text = $"Loss: {ch.CurrentPacketLoss:F2}%";
                        HudLossCam1.Foreground = ch.CurrentPacketLoss > 3.0 ? Brushes.Red : Brushes.LightGreen;
                        HudBitrateCam1.Text = $"Bitrate: {ch.CurrentBitrateKbps:F0} kbps";
                        HudDriftCam1.Text = $"Drift: {sync.GetFormattedDrift()}";
                        DiagRttCam1.Text = $"⏱️ RTT: {ch.CurrentRttMs:F1} ms";
                        DiagLossCam1.Text = $"📉 Loss: {ch.CurrentPacketLoss:F2} %";
                        DiagLossCam1.Foreground = ch.CurrentPacketLoss > 3.0 ? Brushes.Red : Brushes.LightGreen;
                        DiagBitrateCam1.Text = $"🚀 Ingest: {ch.CurrentBitrateKbps:F0} kbps";
                        DiagHealthCam1.Text = $"⏳ Health: {ch.BufferHealthPercent:F0}% ({(ch.BufferHealthPercent > 80 ? "Stable" : "Jittering")})";
                        PbBufferCam1.Value = ch.BufferHealthPercent;
                        TxtDriftValCam1.Text = $"Δt: {sync.GetFormattedDrift()} ({sync.LockState})";
                        TxtDriftValCam1.Foreground = sync.LockState == SyncLockState.Locked ? Brushes.LightGreen : Brushes.Orange;
                        break;
                    case 1:
                        HudRttCam2.Text = $"RTT: {ch.CurrentRttMs:F0} ms";
                        HudLossCam2.Text = $"Loss: {ch.CurrentPacketLoss:F2}%";
                        HudLossCam2.Foreground = ch.CurrentPacketLoss > 3.0 ? Brushes.Red : Brushes.LightGreen;
                        HudBitrateCam2.Text = $"Bitrate: {ch.CurrentBitrateKbps:F0} kbps";
                        HudDriftCam2.Text = $"Drift: {sync.GetFormattedDrift()}";
                        DiagRttCam2.Text = $"⏱️ RTT: {ch.CurrentRttMs:F1} ms";
                        DiagLossCam2.Text = $"📉 Loss: {ch.CurrentPacketLoss:F2} %";
                        DiagLossCam2.Foreground = ch.CurrentPacketLoss > 3.0 ? Brushes.Red : Brushes.LightGreen;
                        DiagBitrateCam2.Text = $"🚀 Ingest: {ch.CurrentBitrateKbps:F0} kbps";
                        DiagHealthCam2.Text = $"⏳ Health: {ch.BufferHealthPercent:F0}% ({(ch.BufferHealthPercent > 80 ? "Stable" : "Jittering")})";
                        PbBufferCam2.Value = ch.BufferHealthPercent;
                        TxtDriftValCam2.Text = $"Δt: {sync.GetFormattedDrift()} ({sync.LockState})";
                        TxtDriftValCam2.Foreground = sync.LockState == SyncLockState.Locked ? Brushes.LightGreen : Brushes.Orange;
                        break;
                    case 2:
                        HudRttCam3.Text = $"RTT: {ch.CurrentRttMs:F0} ms";
                        HudLossCam3.Text = $"Loss: {ch.CurrentPacketLoss:F2}%";
                        HudLossCam3.Foreground = ch.CurrentPacketLoss > 3.0 ? Brushes.Red : Brushes.LightGreen;
                        HudBitrateCam3.Text = $"Bitrate: {ch.CurrentBitrateKbps:F0} kbps";
                        HudDriftCam3.Text = $"Drift: {sync.GetFormattedDrift()}";
                        DiagRttCam3.Text = $"⏱️ RTT: {ch.CurrentRttMs:F1} ms";
                        DiagLossCam3.Text = $"📉 Loss: {ch.CurrentPacketLoss:F2} %";
                        DiagLossCam3.Foreground = ch.CurrentPacketLoss > 3.0 ? Brushes.Red : Brushes.LightGreen;
                        DiagBitrateCam3.Text = $"🚀 Ingest: {ch.CurrentBitrateKbps:F0} kbps";
                        DiagHealthCam3.Text = $"⏳ Health: {ch.BufferHealthPercent:F0}% ({(ch.BufferHealthPercent > 80 ? "Stable" : "Jittering")})";
                        PbBufferCam3.Value = ch.BufferHealthPercent;
                        TxtDriftValCam3.Text = $"Δt: {sync.GetFormattedDrift()} ({sync.LockState})";
                        TxtDriftValCam3.Foreground = sync.LockState == SyncLockState.Locked ? Brushes.LightGreen : Brushes.Orange;
                        break;
                    case 3:
                        HudRttCam4.Text = $"RTT: {ch.CurrentRttMs:F0} ms";
                        HudLossCam4.Text = $"Loss: {ch.CurrentPacketLoss:F2}%";
                        HudLossCam4.Foreground = ch.CurrentPacketLoss > 3.0 ? Brushes.Red : Brushes.LightGreen;
                        HudBitrateCam4.Text = $"Bitrate: {ch.CurrentBitrateKbps:F0} kbps";
                        HudDriftCam4.Text = $"Drift: {sync.GetFormattedDrift()}";
                        DiagRttCam4.Text = $"⏱️ RTT: {ch.CurrentRttMs:F1} ms";
                        DiagLossCam4.Text = $"📉 Loss: {ch.CurrentPacketLoss:F2} %";
                        DiagLossCam4.Foreground = ch.CurrentPacketLoss > 3.0 ? Brushes.Red : Brushes.LightGreen;
                        DiagBitrateCam4.Text = $"🚀 Ingest: {ch.CurrentBitrateKbps:F0} kbps";
                        DiagHealthCam4.Text = $"⏳ Health: {ch.BufferHealthPercent:F0}% ({(ch.BufferHealthPercent > 80 ? "Stable" : "Jittering")})";
                        PbBufferCam4.Value = ch.BufferHealthPercent;
                        TxtDriftValCam4.Text = $"Δt: {sync.GetFormattedDrift()} ({sync.LockState})";
                        TxtDriftValCam4.Foreground = sync.LockState == SyncLockState.Locked ? Brushes.LightGreen : Brushes.Orange;
                        break;
                }
            }

            // Bottom status updates
            TxtTotalIngestBitrate.Text = $"Total Ingest: {totalBitrateKbps / 1000.0:F2} Mbps";
            TxtTotalBytesTransferred.Text = $"Received: {totalBytes / (1024.0 * 1024.0):F2} MB";

            // Master Sync Lock state
            if (_syncEngine.MasterSyncEnabled)
            {
                TxtSyncLockState.Text = "SYNC: NTP MASTER LOCKED";
                TxtSyncLockState.Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
                LedSyncLock.Fill = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
            }
            else
            {
                TxtSyncLockState.Text = "SYNC: FREE-RUN";
                TxtSyncLockState.Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
                LedSyncLock.Fill = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
            }
        }

        #endregion

        #region Receiver Channel Event Handlers

        private void OnReceiverChannelUpdated(int index, ReceiverChannelState state)
        {
            Dispatcher.Invoke(() =>
            {
                if (index < 0 || index >= 4) return;

                // Update Status text and LED
                _statusTexts[index].Text = state.StatusMessage;
                _ledIndicators[index].Fill = state.IsConnected 
                    ? new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)) 
                    : new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));

                // Show fallback placeholder if disconnected
                if (!state.IsConnected)
                {
                    _fallbacks[index].Visibility = Visibility.Visible;
                }

                // Update toggle button text in config tab
                Button? btnToggle = index switch
                {
                    0 => BtnToggleCam1,
                    1 => BtnToggleCam2,
                    2 => BtnToggleCam3,
                    3 => BtnToggleCam4,
                    _ => null
                };

                if (btnToggle != null)
                {
                    btnToggle.Content = state.IsRunning ? $"Stop Cam {index + 1}" : $"Start Cam {index + 1}";
                    btnToggle.Background = state.IsRunning 
                        ? new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)) 
                        : new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC));
                }
            });
        }

        private void OnFrameReady(int channelIndex, byte[] frameBytes, int width, int height)
        {
            if (channelIndex < 0 || channelIndex >= 4) return;

            Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    var bmp = _camBitmaps[channelIndex];
                    if (bmp == null || bmp.PixelWidth != width || bmp.PixelHeight != height)
                    {
                        bmp = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
                        _camBitmaps[channelIndex] = bmp;
                        var view = channelIndex switch
                        {
                            0 => VideoViewCam1,
                            1 => VideoViewCam2,
                            2 => VideoViewCam3,
                            3 => VideoViewCam4,
                            _ => null
                        };
                        view?.PresentBitmap(bmp);
                    }

                    int stride = width * 4;
                    bmp.WritePixels(new Int32Rect(0, 0, width, height), frameBytes, stride, 0);

                    // Ensure video is visible and fallback placeholder is hidden
                    if (_fallbacks[channelIndex].Visibility != Visibility.Collapsed)
                    {
                        _fallbacks[channelIndex].Visibility = Visibility.Collapsed;
                    }
                }
                catch { }
            }, DispatcherPriority.Render);
        }

        private async void BtnToggleCam_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tagStr && int.TryParse(tagStr, out int index))
            {
                // Apply UI inputs to config before connecting
                ApplyFormInputsToChannel(index);

                var ch = _receiverEngine.Channels[index];
                if (ch.IsRunning)
                {
                    await _receiverEngine.StopChannelAsync(index);
                }
                else
                {
                    await _receiverEngine.StartChannelAsync(index);
                }
            }
        }

        private async void BtnConnectAll_Click(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < 4; i++)
            {
                ApplyFormInputsToChannel(i);
            }
            await _receiverEngine.StartAllAsync();
        }

        private async void BtnDisconnectAll_Click(object sender, RoutedEventArgs e)
        {
            await _receiverEngine.StopAllAsync();
        }

        private static SRTMode ParseSrtMode(ComboBox? cmb)
        {
            string modeStr = (cmb?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Caller";
            if (modeStr.Contains("Listener", StringComparison.OrdinalIgnoreCase)) return SRTMode.Listener;
            if (modeStr.Contains("Rendezvous", StringComparison.OrdinalIgnoreCase)) return SRTMode.Rendezvous;
            return SRTMode.Caller;
        }

        private void ApplyFormInputsToChannel(int index)
        {
            var ch = _receiverEngine.Channels[index];
            switch (index)
            {
                case 0:
                    ch.Config.Host = TxtIpCam1.Text.Trim();
                    if (int.TryParse(TxtPortCam1.Text, out int p1)) ch.Config.Port = p1;
                    ch.Config.Mode = ParseSrtMode(CmbModeCam1);
                    ch.Config.StreamId = TxtStreamIdCam1.Text.Trim();
                    ch.Config.AutoLatency = ChkAutoLatencyCam1.IsChecked == true;
                    if (int.TryParse(TxtLatencyCam1.Text, out int lat1)) ch.Config.LatencyMs = lat1;
                    ch.Config.EncryptionEnabled = ChkDecryptCam1.IsChecked == true;
                    ch.Config.Passphrase = TxtPassphraseCam1.Text;
                    ch.Config.KeyLength = CmbKeyLenCam1.SelectedIndex switch { 1 => 24, 2 => 16, _ => 32 };
                    break;
                case 1:
                    ch.Config.Host = TxtIpCam2.Text.Trim();
                    if (int.TryParse(TxtPortCam2.Text, out int p2)) ch.Config.Port = p2;
                    ch.Config.Mode = ParseSrtMode(CmbModeCam2);
                    ch.Config.StreamId = TxtStreamIdCam2.Text.Trim();
                    ch.Config.AutoLatency = ChkAutoLatencyCam2.IsChecked == true;
                    if (int.TryParse(TxtLatencyCam2.Text, out int lat2)) ch.Config.LatencyMs = lat2;
                    break;
                case 2:
                    ch.Config.Host = TxtIpCam3.Text.Trim();
                    if (int.TryParse(TxtPortCam3.Text, out int p3)) ch.Config.Port = p3;
                    ch.Config.Mode = ParseSrtMode(CmbModeCam3);
                    ch.Config.StreamId = TxtStreamIdCam3.Text.Trim();
                    ch.Config.AutoLatency = ChkAutoLatencyCam3.IsChecked == true;
                    if (int.TryParse(TxtLatencyCam3.Text, out int lat3)) ch.Config.LatencyMs = lat3;
                    break;
                case 3:
                    ch.Config.Host = TxtIpCam4.Text.Trim();
                    if (int.TryParse(TxtPortCam4.Text, out int p4)) ch.Config.Port = p4;
                    ch.Config.Mode = ParseSrtMode(CmbModeCam4);
                    ch.Config.StreamId = TxtStreamIdCam4.Text.Trim();
                    ch.Config.AutoLatency = ChkAutoLatencyCam4.IsChecked == true;
                    if (int.TryParse(TxtLatencyCam4.Text, out int lat4)) ch.Config.LatencyMs = lat4;
                    break;
            }
        }

        #endregion

        #region Vision Switcher & Tally Routing

        private void BtnPgmSelect_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tagStr && int.TryParse(tagStr, out int index))
            {
                if (index != _currentProgramIndex)
                {
                    _currentPreviewIndex = _currentProgramIndex; // Old PGM becomes preview
                    _currentProgramIndex = index;
                    UpdateTallyIndicators();
                    LogEvent("[SWITCHER]", $"Program chuyển góc quay sang: CAM {_currentProgramIndex + 1} (Direct Cut)");
                }
            }
        }

        private void BtnCutTransition_Click(object sender, RoutedEventArgs e)
        {
            // Swap PGM and PVW immediately
            int temp = _currentProgramIndex;
            _currentProgramIndex = _currentPreviewIndex;
            _currentPreviewIndex = temp;
            UpdateTallyIndicators();
            LogEvent("[SWITCHER]", $"CUT: CAM {_currentProgramIndex + 1} lên sóng PROGRAM.");
        }

        private async void BtnDissolveTransition_Click(object sender, RoutedEventArgs e)
        {
            if (_isTransitioning) return;
            _isTransitioning = true;
            BtnDissolveTransition.Background = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));

            LogEvent("[SWITCHER]", $"Bắt đầu chuyển cảnh DISSOLVE (1.0s) từ CAM {_currentProgramIndex + 1} ➔ CAM {_currentPreviewIndex + 1}...");
            await Task.Delay(1000);

            int temp = _currentProgramIndex;
            _currentProgramIndex = _currentPreviewIndex;
            _currentPreviewIndex = temp;
            UpdateTallyIndicators();

            BtnDissolveTransition.Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x2A));
            _isTransitioning = false;
            LogEvent("[SWITCHER]", $"Hoàn tất DISSOLVE: CAM {_currentProgramIndex + 1} đã ở trên sóng PROGRAM.");
        }

        private void UpdateTallyIndicators()
        {
            for (int i = 0; i < 4; i++)
            {
                if (i == _currentProgramIndex)
                {
                    _cellBorders[i].BorderBrush = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)); // Red
                    _cellBorders[i].BorderThickness = new Thickness(2);
                    _tallyBadges[i].Background = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
                    _tallyTexts[i].Text = "PGM";
                    _tallyTexts[i].Foreground = Brushes.White;
                    _pgmButtons[i].Background = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
                    _pgmButtons[i].Foreground = Brushes.White;
                }
                else if (i == _currentPreviewIndex)
                {
                    _cellBorders[i].BorderBrush = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A)); // Green
                    _cellBorders[i].BorderThickness = new Thickness(1.5);
                    _tallyBadges[i].Background = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));
                    _tallyTexts[i].Text = "PVW";
                    _tallyTexts[i].Foreground = Brushes.White;
                    _pgmButtons[i].Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x24));
                    _pgmButtons[i].Foreground = Brushes.LightGreen;
                }
                else
                {
                    _cellBorders[i].BorderBrush = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x35)); // Gray
                    _cellBorders[i].BorderThickness = new Thickness(1);
                    _tallyBadges[i].Background = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x38));
                    _tallyTexts[i].Text = $"CAM {i + 1}";
                    _tallyTexts[i].Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
                    _pgmButtons[i].Background = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x2E));
                    _pgmButtons[i].Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
                }
            }
        }

        #endregion

        #region Audio Monitoring & VU Meters

        private void OnAudioLevelsUpdated(ChannelAudioLevels[] levels)
        {
            Dispatcher.Invoke(() =>
            {
                for (int i = 0; i < 4; i++)
                {
                    _vuBarsL[i].Value = levels[i].LeftPercent;
                    _vuBarsR[i].Value = levels[i].RightPercent;

                    // Color code high audio levels
                    _vuBarsL[i].Foreground = levels[i].LeftPercent > 90 ? Brushes.Red : (levels[i].LeftPercent > 75 ? Brushes.Gold : Brushes.LightGreen);
                    _vuBarsR[i].Foreground = levels[i].RightPercent > 90 ? Brushes.Red : (levels[i].RightPercent > 75 ? Brushes.Gold : Brushes.LightGreen);
                }
            });
        }

        private void OnProgramLevelsUpdated(ChannelAudioLevels pgmLevels)
        {
            Dispatcher.Invoke(() =>
            {
                VuMasterL.Value = pgmLevels.LeftPercent;
                VuMasterR.Value = pgmLevels.RightPercent;
                VuMasterL.Foreground = pgmLevels.LeftPercent > 90 ? Brushes.Red : (pgmLevels.LeftPercent > 75 ? Brushes.Gold : Brushes.LightGreen);
                VuMasterR.Foreground = pgmLevels.RightPercent > 90 ? Brushes.Red : (pgmLevels.RightPercent > 75 ? Brushes.Gold : Brushes.LightGreen);
            });
        }

        private void BtnSoloCam_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tagStr && int.TryParse(tagStr, out int camNum))
            {
                _audioManager.SoloSource = camNum switch
                {
                    1 => SoloAudioSource.Cam1,
                    2 => SoloAudioSource.Cam2,
                    3 => SoloAudioSource.Cam3,
                    4 => SoloAudioSource.Cam4,
                    _ => SoloAudioSource.ProgramMaster
                };
                TxtCurrentSolo.Text = _audioManager.GetSoloLabel(_audioManager.SoloSource);
            }
        }

        private void BtnMuteAll_Click(object sender, RoutedEventArgs e)
        {
            _audioManager.IsMuteAll = !_audioManager.IsMuteAll;
            BtnMuteAll.Content = _audioManager.IsMuteAll ? "UNMUTE" : "MUTE ALL";
            BtnMuteAll.Background = _audioManager.IsMuteAll ? Brushes.Red : new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x38));
        }

        #endregion

        #region Layout & Ingest Mode Handlers

        private void CmbLayoutMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized || _cellBorders == null || _cellBorders.Length < 4) return;

            int selected = CmbLayoutMode.SelectedIndex;
            switch (selected)
            {
                case 0: // Quad-View (2x2)
                    for (int i = 0; i < 4; i++)
                    {
                        _cellBorders[i].Visibility = Visibility.Visible;
                        Grid.SetRow(_cellBorders[i], i / 2);
                        Grid.SetColumn(_cellBorders[i], i % 2);
                        Grid.SetRowSpan(_cellBorders[i], 1);
                        Grid.SetColumnSpan(_cellBorders[i], 1);
                    }
                    break;
                case 1: // 1 PGM + 3 PVW
                    for (int i = 0; i < 4; i++)
                    {
                        _cellBorders[i].Visibility = Visibility.Visible;
                    }
                    Grid.SetRow(_cellBorders[0], 0);
                    Grid.SetColumn(_cellBorders[0], 0);
                    Grid.SetRowSpan(_cellBorders[0], 2);
                    Grid.SetColumnSpan(_cellBorders[0], 1);

                    Grid.SetRow(_cellBorders[1], 0);
                    Grid.SetColumn(_cellBorders[1], 1);
                    Grid.SetRowSpan(_cellBorders[1], 1);

                    Grid.SetRow(_cellBorders[2], 1);
                    Grid.SetColumn(_cellBorders[2], 1);
                    Grid.SetRowSpan(_cellBorders[2], 1);

                    _cellBorders[3].Visibility = Visibility.Collapsed;
                    break;
                case 2: // Single View Cam 1
                case 3: // Single View Cam 2
                case 4: // Single View Cam 3
                case 5: // Single View Cam 4
                    int targetCam = selected - 2;
                    for (int i = 0; i < 4; i++)
                    {
                        if (i == targetCam)
                        {
                            _cellBorders[i].Visibility = Visibility.Visible;
                            Grid.SetRow(_cellBorders[i], 0);
                            Grid.SetColumn(_cellBorders[i], 0);
                            Grid.SetRowSpan(_cellBorders[i], 2);
                            Grid.SetColumnSpan(_cellBorders[i], 2);
                        }
                        else
                        {
                            _cellBorders[i].Visibility = Visibility.Collapsed;
                        }
                    }
                    break;
            }
        }

        private void CmbIngestMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;
            _isSingleStreamMode = CmbIngestMode.SelectedIndex == 1;
            LogEvent("[INGEST]", _isSingleStreamMode 
                ? "Chuyển sang chế độ Single Stream Mode (1 Camera)" 
                : "Chuyển sang chế độ Multi-Camera Mode (4 Cameras)");
        }

        #endregion

        #region NTP Sync & Outputs Controls

        private void ChkMasterSync_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            _syncEngine.MasterSyncEnabled = ChkMasterSync.IsChecked == true;
        }

        private async void BtnQueryNtp_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            _syncEngine.NtpServer = TxtNtpServer.Text.Trim();
            bool ok = await _syncEngine.QueryNtpMasterAsync();
            if (ok && _syncEngine.LastNtpResult != null)
            {
                TxtNtpOffsetResult.Text = _syncEngine.LastNtpResult.GetFormattedOffset();
                TxtNtpOffsetResult.Foreground = Brushes.LightGreen;
            }
            else
            {
                TxtNtpOffsetResult.Text = "NTP Query Failed";
                TxtNtpOffsetResult.Foreground = Brushes.Red;
            }
        }

        private void SliderSyncWindow_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized) return;
            if (TxtTargetSyncWindowVal != null)
            {
                int val = (int)SliderSyncWindow.Value;
                TxtTargetSyncWindowVal.Text = $"{val} ms";
                _syncEngine.TargetSyncWindowMs = val;
            }
        }

        private void ChkShowHudOverlay_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || _hudOverlays == null || _hudOverlays.Length < 4) return;
            bool show = ChkShowHudOverlay.IsChecked == true;
            for (int i = 0; i < 4; i++)
            {
                _hudOverlays[i].Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private async void ChkSdiOutput_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            bool enable = ChkSdiOutput.IsChecked == true;
            await _outputManager.ToggleSdiAsync(enable);
            BadgeOutputSdi.Background = enable ? new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)) : new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x2B));
            ((TextBlock)BadgeOutputSdi.Child).Foreground = enable ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
        }

        private async void ChkNdiOutput_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            bool enable = ChkNdiOutput.IsChecked == true;
            _outputManager.NdiStreamName = TxtNdiName.Text.Trim();
            _outputManager.NdiMultiviewerMode = ChkNdiMultiviewer.IsChecked == true;
            await _outputManager.ToggleNdiAsync(enable);
            BadgeOutputNdi.Background = enable ? new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)) : new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x2B));
            ((TextBlock)BadgeOutputNdi.Child).Foreground = enable ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
        }

        private async void ChkSrtBridge_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            bool enable = ChkSrtBridge.IsChecked == true;
            _outputManager.SrtBridgeHost = TxtBridgeHost.Text.Trim();
            if (int.TryParse(TxtBridgePort.Text, out int bp)) _outputManager.SrtBridgePort = bp;
            await _outputManager.ToggleSrtBridgeAsync(enable);
            BadgeOutputBridge.Background = enable ? new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)) : new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x2B));
            ((TextBlock)BadgeOutputBridge.Child).Foreground = enable ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
        }

        private async void ChkRecording_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            bool enable = ChkRecording.IsChecked == true;
            await _outputManager.ToggleRecordingAsync(enable);
            BadgeOutputRec.Background = enable ? new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)) : new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x2B));
            ((TextBlock)BadgeOutputRec.Child).Foreground = enable ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
        }

        #endregion

        #region Logging & Console Utilities

        private void LogEvent(string tag, string message)
        {
            Dispatcher.InvokeAsync(() =>
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                string line = $"[{timestamp}] {tag} {message}\n";

                _logBuffer.Insert(0, line);
                if (_logBuffer.Length > 50000)
                {
                    _logBuffer.Length = 40000;
                }

                if (TxtLogConsole != null)
                {
                    TxtLogConsole.Text = _logBuffer.ToString();
                    if (ChkAutoScroll.IsChecked == true)
                    {
                        ScrollerLogs?.ScrollToHome();
                    }
                }
            });
        }

        private void BtnClearLogs_Click(object sender, RoutedEventArgs e)
        {
            _logBuffer.Clear();
            if (TxtLogConsole != null) TxtLogConsole.Text = string.Empty;
        }

        private void BtnCopyLogs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(_logBuffer.ToString());
                LogEvent("[INFO]", "Đã sao chép toàn bộ log vào Clipboard.");
            }
            catch { }
        }

        #endregion
    }
}
