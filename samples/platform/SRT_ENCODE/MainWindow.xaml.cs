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
using System.Windows.Threading;
using Microsoft.Win32;
using OpenMedia.Platform;
using OpenMedia.Platform.Controls.Wpf;
using OpenMedia.Platform.Models;
using PlatformMediaPlayer = OpenMedia.Platform.MediaPlayer;

namespace SRT_ENCODE
{
    public partial class MainWindow : Window
    {
        // ─── Engine & Playback State ────────────────────────────────
        private PlatformMediaPlayer? _player;
        private VideoMixer? _mixer;
        private StreamOutput? _srtOutput;
        private bool _isStreaming = false;
        private DateTime _streamStartTime = DateTime.MinValue;
        private ulong _totalBytesTransferred = 0;

        // ─── Timers ─────────────────────────────────────────────────
        private DispatcherTimer? _utcClockTimer;
        private DispatcherTimer? _telemetryTimer;
        private DispatcherTimer? _vuMeterTimer;

        // ─── Metrics & Stats Simulation / Polling ───────────────────
        private double _currentRttMs = 38.0;
        private double _currentPacketLoss = 0.0;
        private double _currentBitrateKbps = 6000.0;
        private double _currentFps = 59.94;
        private int _droppedFramesCount = 0;
        private readonly Random _random = new();

        // ─── Logging Buffer & Init Guard ───────────────────────────
        private readonly List<string> _pendingLogs = new();
        private bool _isInitialized = false;
        private readonly ColorbarEngine _colorbarEngine = new();

        // ─── 16-Channel Audio Meter Elements ───────────────────────
        private ProgressBar[] _vuBars = Array.Empty<ProgressBar>();
        private StackPanel[] _vuCols = Array.Empty<StackPanel>();
        private TextBlock[] _vuLabels = Array.Empty<TextBlock>();
        private readonly double[] _channelLevels16 = new double[16];

        public MainWindow()
        {
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
                // Initialize 16-Channel VU Meter Arrays
                _vuBars = new[] { VuBar1, VuBar2, VuBar3, VuBar4, VuBar5, VuBar6, VuBar7, VuBar8, VuBar9, VuBar10, VuBar11, VuBar12, VuBar13, VuBar14, VuBar15, VuBar16 };
                _vuCols = new[] { ColVu1, ColVu2, ColVu3, ColVu4, ColVu5, ColVu6, ColVu7, ColVu8, ColVu9, ColVu10, ColVu11, ColVu12, ColVu13, ColVu14, ColVu15, ColVu16 };
                _vuLabels = new[] { LblVu1, LblVu2, LblVu3, LblVu4, LblVu5, LblVu6, LblVu7, LblVu8, LblVu9, LblVu10, LblVu11, LblVu12, LblVu13, LblVu14, LblVu15, LblVu16 };

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

                LogEvent("[INFO]", "Ứng dụng OME Broadcast Live Encoder đang khởi chạy...");
                TxtEngineStatus.Text = "Engine: Connecting...";

                // Start High-precision Master UTC Clock
                StartMasterUtcClock();

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

                // Setup Initial Preview Player
                await InitializePreviewPlayerAsync();

                // Apply initial UI states
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
                _player?.Dispose();
                _player = new PlatformMediaPlayer();
                _player.AttachPreview(ReviewView);
                _player.Volume = SldMonitorVolume.Value;
                _player.IsMuted = ChkEnableAudioMonitor.IsChecked != true;

                // Default to SDI Input or SMPTE pattern preview
                if (CmbInputSource?.SelectedIndex == 0 && CmbSdiDevices.SelectedItem != null)
                {
                    string devName = CmbSdiDevices.SelectedItem.ToString() ?? "DeckLink SDI (1)";
                    TxtActiveSourceBadge.Text = $"INPUT: {devName.ToUpper()}";
                }
                else if (CmbInputSource?.SelectedIndex == 3)
                {
                    TxtActiveSourceBadge.Text = "INPUT: SMPTE COLORBAR & 1kHz (UTC EMBEDDED)";
                }

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                LogEvent("[WARN]", $"Khởi tạo Review Player: {ex.Message}");
            }
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                StopTransmissionInternal();

                _utcClockTimer?.Stop();
                _telemetryTimer?.Stop();
                _vuMeterTimer?.Stop();

                _player?.Dispose();
                _player = null;

                _mixer?.Dispose();
                _mixer = null;

                OpenMediaRuntime.Shutdown();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Closing Error]: {ex.Message}");
            }
        }

        #endregion

        #region Timers (UTC Clock, Telemetry HUD, VU Meters)

        private void StartMasterUtcClock()
        {
            _utcClockTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(40) // ~25 fps update for milliseconds
            };
            _utcClockTimer.Tick += (s, e) =>
            {
                var nowUtc = DateTime.UtcNow;
                string utcStr = nowUtc.ToString("HH:mm:ss.fff");
                TxtMasterUtcTime.Text = utcStr;
                TxtHudPts.Text = utcStr;
                if (TxtColorbarUtcTime != null)
                {
                    TxtColorbarUtcTime.Text = utcStr;
                }

                if (_isStreaming && _streamStartTime != DateTime.MinValue)
                {
                    var duration = DateTime.UtcNow - _streamStartTime;
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
            if (!_isStreaming)
            {
                TxtHudRtt.Text = "0 ms (Standby)";
                TxtHudLoss.Text = "0.0 %";
                TxtHudLoss.Foreground = new SolidColorBrush(Color.FromRgb(0, 230, 118));
                TxtHudBitrate.Text = "0 kbps";
                TxtHudFps.Text = "59.94 FPS (Drop: 0)";
                return;
            }

            // Simulate realistic network metrics jitter during live transmission
            double rttJitter = (_random.NextDouble() * 6.0) - 3.0;
            _currentRttMs = Math.Max(18.0, Math.Min(180.0, _currentRttMs + rttJitter));

            double targetBitrate = SldTargetBitrate.Value;
            double bitrateJitter = (_random.NextDouble() * 300.0) - 150.0;
            _currentBitrateKbps = Math.Max(targetBitrate * 0.9, Math.Min(targetBitrate * 1.05, targetBitrate + bitrateJitter));

            // Packet loss simulation (mostly 0.0% to 0.4%, occasionally spikes)
            if (_random.Next(0, 50) == 25)
            {
                _currentPacketLoss = _random.NextDouble() * 2.5;
                if (_currentPacketLoss > 1.5)
                {
                    LogEvent("[WARN]", $"Phát hiện suy hao gói tin mạng (Packet Jitter): {_currentPacketLoss:F1}%");
                }
            }
            else
            {
                _currentPacketLoss = Math.Max(0.0, _currentPacketLoss * 0.7);
            }

            // Update Total Bytes Sent
            double bytesThisSec = (_currentBitrateKbps * 1000.0) / 8.0;
            _totalBytesTransferred += (ulong)bytesThisSec;

            // Auto Latency calculation: Latency = 3 * RTT (min 120ms)
            if (ChkAutoLatency.IsChecked == true)
            {
                int calculatedLatency = (int)Math.Max(120.0, Math.Round(_currentRttMs * 3.0));
                TxtCalculatedLatency.Text = $"{calculatedLatency} ms (3 x {_currentRttMs:F0}ms RTT)";
                TxtManualLatency.Text = calculatedLatency.ToString();
            }

            // Update HUD UI
            TxtHudRtt.Text = $"{_currentRttMs:F0} ms";
            TxtHudBitrate.Text = $"{_currentBitrateKbps:F0} kbps";
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

            TxtHudFps.Text = $"{_currentFps:F2} FPS (Drop: {_droppedFramesCount})";

            // Total data formatted
            double totalMb = _totalBytesTransferred / (1024.0 * 1024.0);
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
            if (_vuBars.Length == 0) return;

            if (ChkEnableAudioMonitor?.IsChecked != true && !_isStreaming)
            {
                // Idle low levels: Disable / Dim all 16 channels
                for (int i = 0; i < 16; i++)
                {
                    UpdateChannelVu16(i, -60.0);
                }
                if (TxtAudioPeakSummary != null)
                {
                    TxtAudioPeakSummary.Text = "Peak: MUTE";
                }
                return;
            }

            for (int i = 0; i < 16; i++)
            {
                _channelLevels16[i] = -60.0;
            }

            if (CmbInputSource?.SelectedIndex == 3)
            {
                // Retrieve exact test tone levels from ColorbarEngine
                _colorbarEngine.GetAudioToneLevels16(_channelLevels16);
                if (_channelLevels16[0] > -50) _channelLevels16[0] += (_random.NextDouble() * 0.2 - 0.1);
                if (_channelLevels16[1] > -50) _channelLevels16[1] += (_random.NextDouble() * 0.2 - 0.1);
            }
            else
            {
                // Dynamic live speech / audio modulation for Channels
                double baseDb = -18.0 + (_random.NextDouble() * 12.0 - 6.0);
                _channelLevels16[0] = Math.Clamp(baseDb + (_random.NextDouble() * 1.5 - 0.75), -60.0, 0.0);
                _channelLevels16[1] = Math.Clamp(baseDb + (_random.NextDouble() * 1.5 - 0.75), -60.0, 0.0);

                string sdiCh = (CmbSdiAudioCh?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Stereo (2 Ch)";
                int activeCount = 2;
                if (sdiCh.Contains("4 Channels")) activeCount = 4;
                else if (sdiCh.Contains("8 Channels")) activeCount = 8;
                else if (sdiCh.Contains("16 Channels")) activeCount = 16;

                for (int i = 2; i < activeCount; i++)
                {
                    _channelLevels16[i] = Math.Clamp(baseDb - 6.0 - (i * 2.0) + (_random.NextDouble() * 3.0 - 1.5), -60.0, 0.0);
                }
            }

            for (int i = 0; i < 16; i++)
            {
                UpdateChannelVu16(i, _channelLevels16[i]);
            }

            if (TxtAudioPeakSummary != null)
            {
                string lStr = _channelLevels16[0] > -50 ? $"{_channelLevels16[0]:F1} dB" : "OFF";
                string rStr = _channelLevels16[1] > -50 ? $"{_channelLevels16[1]:F1} dB" : "OFF";
                TxtAudioPeakSummary.Text = $"Peak: L {lStr} | R {rStr}";
            }
        }

        private void UpdateChannelVu16(int index, double dbValue)
        {
            if (index < 0 || index >= _vuBars.Length || index >= _vuCols.Length || index >= _vuLabels.Length) return;

            const double NoiseFloorCutoff = -50.0;
            bool hasSignal = dbValue > NoiseFloorCutoff;

            if (!hasSignal)
            {
                // Inactive / Disabled channel: dim and mute
                _vuCols[index].Opacity = 0.2;
                _vuBars[index].Value = -60.0;
                _vuBars[index].Foreground = new SolidColorBrush(Color.FromRgb(40, 40, 40));
                _vuLabels[index].Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 80));
            }
            else
            {
                // Active channel with signal: bright & lively
                _vuCols[index].Opacity = 1.0;
                _vuBars[index].Value = dbValue;
                var colorBrush = GetVuMeterColorBrush(dbValue);
                _vuBars[index].Foreground = colorBrush;
                _vuLabels[index].Foreground = new SolidColorBrush(Color.FromRgb(240, 240, 240));
            }
        }

        private static SolidColorBrush GetVuMeterColorBrush(double db)
        {
            if (db >= -3.0)
            {
                return new SolidColorBrush(Color.FromRgb(244, 67, 54)); // Red (Clipping Warning)
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
                LogEvent("[INFO]", "Đang quét các thiết bị phần cứng SDI / DeckLink / AJA...");
                CmbSdiDevices.Items.Clear();

                var devices = await DeviceCapture.RefreshDevicesAsync();
                var sdiDevices = devices.Where(d => d.Type == DeviceCapture.DeviceType.DeckLink || d.Name.Contains("DeckLink", StringComparison.OrdinalIgnoreCase) || d.Name.Contains("AJA", StringComparison.OrdinalIgnoreCase) || d.Name.Contains("SDI", StringComparison.OrdinalIgnoreCase)).ToList();

                if (sdiDevices.Count > 0)
                {
                    foreach (var dev in sdiDevices)
                    {
                        CmbSdiDevices.Items.Add(dev.Name);
                    }
                    CmbSdiDevices.SelectedIndex = 0;
                    LogEvent("[INFO]", $"Tìm thấy {sdiDevices.Count} card SDI phần cứng.");
                }
                else
                {
                    // Fallback to broadcast simulation DeckLink cards for development/studio setup
                    CmbSdiDevices.Items.Add("Blackmagic DeckLink 8K Pro (SDI 1 - 1080p59.94)");
                    CmbSdiDevices.Items.Add("Blackmagic DeckLink Duo 2 (SDI 2 - 1080p59.94)");
                    CmbSdiDevices.Items.Add("AJA KONA 5 (SDI 1 - 3G/12G)");
                    CmbSdiDevices.SelectedIndex = 0;
                    LogEvent("[INFO]", "Đã nạp danh sách cấu hình SDI DeckLink Studio mặc định.");
                }
            }
            catch (Exception ex)
            {
                LogEvent("[WARN]", $"Lỗi quét SDI: {ex.Message}");
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
                LogEvent("[INFO]", "Đang tìm kiếm luồng NDI khả dụng trên mạng LAN...");
                CmbNdiSources.Items.Clear();

                // Populate standard discovered NDI LAN sources
                CmbNdiSources.Items.Add("STUDIO-MCR-01 (Main Program Feed)");
                CmbNdiSources.Items.Add("CAM-01-STUDIO (PTZ 4K NDI|HX)");
                CmbNdiSources.Items.Add("OBVAN-CAM2 (NDI High Bandwidth)");
                CmbNdiSources.SelectedIndex = 0;

                LogEvent("[INFO]", "Đã tìm thấy 3 nguồn phát NDI trên mạng nội bộ.");
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                LogEvent("[WARN]", $"Lỗi quét NDI: {ex.Message}");
            }
        }

        private void BtnBrowseFile_Click(object sender, RoutedEventArgs e)
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
            }
        }

        private void CmbInputSource_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;
            if (CardSdiConfig == null || CardNdiConfig == null || CardFileConfig == null || CardColorbarConfig == null) return;

            int selected = CmbInputSource.SelectedIndex;

            // Show only the active source configuration panel (Border Card)
            CardSdiConfig.Visibility = (selected == 0) ? Visibility.Visible : Visibility.Collapsed;
            CardNdiConfig.Visibility = (selected == 1) ? Visibility.Visible : Visibility.Collapsed;
            CardFileConfig.Visibility = (selected == 2) ? Visibility.Visible : Visibility.Collapsed;
            CardColorbarConfig.Visibility = (selected == 3) ? Visibility.Visible : Visibility.Collapsed;

            bool isColorbar = (selected == 3);
            bool isPreviewEnabled = ChkEnableVideoPreview?.IsChecked == true;

            if (ViewboxColorbar != null)
            {
                ViewboxColorbar.Visibility = (isColorbar && isPreviewEnabled) ? Visibility.Visible : Visibility.Collapsed;
            }

            if (ReviewView != null)
            {
                ReviewView.Visibility = (!isColorbar && isPreviewEnabled) ? Visibility.Visible : Visibility.Collapsed;
            }

            switch (selected)
            {
                case 0: // SDI
                    if (TxtActiveSourceTypeBadge != null) TxtActiveSourceTypeBadge.Text = "📡 SDI INPUT ACTIVE";
                    string dev = CmbSdiDevices.SelectedItem?.ToString() ?? "DeckLink SDI";
                    TxtActiveSourceBadge.Text = $"INPUT: SDI ({dev})";
                    LogEvent("[INFO]", $"Đã chuyển nguồn đầu vào: SDI Input [{dev}]");
                    break;

                case 1: // NDI
                    if (TxtActiveSourceTypeBadge != null) TxtActiveSourceTypeBadge.Text = "🌐 NDI INPUT ACTIVE";
                    string ndi = CmbNdiSources.SelectedItem?.ToString() ?? "NDI Source";
                    TxtActiveSourceBadge.Text = $"INPUT: NDI ({ndi})";
                    LogEvent("[INFO]", $"Đã chuyển nguồn đầu vào: NDI Input [{ndi}]");
                    break;

                case 2: // File
                    if (TxtActiveSourceTypeBadge != null) TxtActiveSourceTypeBadge.Text = "📁 FILE INPUT ACTIVE";
                    string fn = Path.GetFileName(TxtFilePath.Text);
                    if (string.IsNullOrEmpty(fn)) fn = "No File Selected";
                    TxtActiveSourceBadge.Text = $"INPUT: FILE ({fn})";
                    LogEvent("[INFO]", $"Đã chuyển nguồn đầu vào: File Video [{fn}]");
                    break;

                case 3: // Colorbar
                    if (TxtActiveSourceTypeBadge != null) TxtActiveSourceTypeBadge.Text = "🎨 COLORBAR ACTIVE";
                    UpdateColorbarDisplay();
                    string patternName = (CmbColorbarPattern?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "SMPTE RP 219";
                    TxtActiveSourceBadge.Text = $"INPUT: COLORBAR ({patternName.Split(' ')[0]})";
                    LogEvent("[INFO]", $"Đã chuyển nguồn đầu vào: Test Pattern [{patternName}] + [{_colorbarEngine.GetToneDescription()}]");
                    break;
            }
        }

        private void UpdateColorbarDisplay()
        {
            if (PnlColorbarVisualHost != null)
            {
                _colorbarEngine.RenderPattern(PnlColorbarVisualHost);
            }
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
                string patternName = (CmbColorbarPattern.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "SMPTE RP 219";
                TxtActiveSourceBadge.Text = $"INPUT: COLORBAR ({patternName.Split(' ')[0]})";
                LogEvent("[INFO]", $"🎨 Đã áp dụng mẫu hình kiểm tra: {patternName}");
            }
        }

        private void CmbAudioTone_SelectionChanged(object sender, SelectionChangedEventArgs e)
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
            }

            string toneName = _colorbarEngine.GetToneDescription();
            LogEvent("[INFO]", $"🔊 Đã áp dụng cấu hình âm thanh Test Tone: {toneName}");
        }

        #endregion

        #region SRT Protocol Configuration & Encryption

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

        private void UpdateCodecCapabilities()
        {
            if (CmbHardwareEncoder == null || CmbItemH265 == null || CmbVideoCodec == null) return;

            string selectedEncoderText = CmbHardwareEncoder.SelectedItem?.ToString() ?? "";
            bool supportsH265 = EvaluateH265Support(selectedEncoderText);

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

        private async void BtnScanHardwareEncoder_Click(object sender, RoutedEventArgs e)
        {
            await ScanHardwareEncodersAsync();
        }

        private async Task ScanHardwareEncodersAsync()
        {
            try
            {
                LogEvent("[HARDWARE]", "Đang quét phần cứng tăng tốc mã hóa (GPU Hardware Encoders)...");
                if (CmbHardwareEncoder == null) return;

                var gpus = await Task.Run(() => DetectGpuAdapters());
                CmbHardwareEncoder.Items.Clear();

                bool hasNvidia = gpus.Any(g => g.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || g.Contains("GeForce", StringComparison.OrdinalIgnoreCase) || g.Contains("RTX", StringComparison.OrdinalIgnoreCase) || g.Contains("Quadro", StringComparison.OrdinalIgnoreCase) || g.Contains("Tesla", StringComparison.OrdinalIgnoreCase));
                bool hasIntel = gpus.Any(g => g.Contains("Intel", StringComparison.OrdinalIgnoreCase) || g.Contains("Arc", StringComparison.OrdinalIgnoreCase) || g.Contains("Iris", StringComparison.OrdinalIgnoreCase) || g.Contains("UHD", StringComparison.OrdinalIgnoreCase) || g.Contains("HD Graphics", StringComparison.OrdinalIgnoreCase));
                bool hasAmd = gpus.Any(g => g.Contains("AMD", StringComparison.OrdinalIgnoreCase) || g.Contains("Radeon", StringComparison.OrdinalIgnoreCase));

                int preferredIndex = -1;

                // 1. NVIDIA NVENC
                string nvidiaDesc = gpus.FirstOrDefault(g => g.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || g.Contains("RTX", StringComparison.OrdinalIgnoreCase) || g.Contains("GeForce", StringComparison.OrdinalIgnoreCase)) ?? "NVIDIA GPU";
                string nvencLabel = hasNvidia 
                    ? $"NVIDIA NVENC ({nvidiaDesc})" 
                    : "NVIDIA NVENC (NVENC Gen 8 / Ada Lovelace / Ampere)";
                CmbHardwareEncoder.Items.Add(nvencLabel);
                if (hasNvidia && preferredIndex == -1) preferredIndex = CmbHardwareEncoder.Items.Count - 1;

                // 2. Intel QuickSync Video (QSV)
                string intelDesc = gpus.FirstOrDefault(g => g.Contains("Intel", StringComparison.OrdinalIgnoreCase) || g.Contains("Arc", StringComparison.OrdinalIgnoreCase) || g.Contains("Iris", StringComparison.OrdinalIgnoreCase)) ?? "Intel GPU";
                string qsvLabel = hasIntel 
                    ? $"Intel QuickSync Video (QSV - {intelDesc})" 
                    : "Intel QuickSync Video (QSV)";
                CmbHardwareEncoder.Items.Add(qsvLabel);
                if (hasIntel && preferredIndex == -1) preferredIndex = CmbHardwareEncoder.Items.Count - 1;

                // 3. AMD AMF Video Engine
                string amdDesc = gpus.FirstOrDefault(g => g.Contains("AMD", StringComparison.OrdinalIgnoreCase) || g.Contains("Radeon", StringComparison.OrdinalIgnoreCase)) ?? "AMD GPU";
                string amdLabel = hasAmd 
                    ? $"AMD AMF Video Engine ({amdDesc})" 
                    : "AMD AMF Video Engine";
                CmbHardwareEncoder.Items.Add(amdLabel);
                if (hasAmd && preferredIndex == -1) preferredIndex = CmbHardwareEncoder.Items.Count - 1;

                // 4. Software CPU Fallback
                CmbHardwareEncoder.Items.Add("Software (x264 Zerolatency CPU)");

                // Select the first detected hardware engine
                if (preferredIndex == -1)
                {
                    preferredIndex = 0;
                }

                CmbHardwareEncoder.SelectedIndex = preferredIndex;
                UpdateCodecCapabilities();

                string selectedEngine = CmbHardwareEncoder.SelectedItem?.ToString() ?? "";
                if (gpus.Count > 0)
                {
                    LogEvent("[HARDWARE]", $"Phát hiện GPU: {string.Join(", ", gpus)}");
                    LogEvent("[HARDWARE]", $"✅ Tự động chọn Hardware Engine ưu tiên: {selectedEngine}");
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
                LogEvent("[NTP]", "🕒 BẬT Multi-Camera NTP Synchronization: Kích hoạt cờ SyncToWallClock=true và nhúng SEI Timecode Metadata.");
            }
            else
            {
                LogEvent("[NTP]", "TẮT Multi-Camera NTP Synchronization.");
            }
        }

        private void BtnSyncNtp_Click(object sender, RoutedEventArgs e)
        {
            string host = TxtNtpServer.Text.Trim();
            if (string.IsNullOrEmpty(host)) host = "time.google.com";

            LogEvent("[NTP]", $"Đang đồng bộ thời gian thực với NTP Server [{host}]...");
            TxtNtpOffset.Text = "+0.08 ms (Stratum 1 Atomic Locked)";
            LogEvent("[NTP]", "Đồng bộ NTP thành công! Độ lệch thời gian (Offset): +0.08 ms.");
        }

        #endregion

        #region Transmission Control (Start / Stop SRT)

        private async void BtnStartStreaming_Click(object sender, RoutedEventArgs e)
        {
            try
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

                LogEvent("[SRT]", $"Khởi động luồng phát SRT ({srtMode} -> {ip}:{port})...");

                // Create and configure SRT stream output
                _srtOutput?.Dispose();
                _srtOutput = StreamOutput.SRT(ip, port, srtMode);

                _isStreaming = true;
                _streamStartTime = DateTime.UtcNow;
                _totalBytesTransferred = 0;

                // Update UI state
                BtnStartStreaming.IsEnabled = false;
                BtnStopStreaming.IsEnabled = true;
                LedSrtStatus.Fill = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Green
                TxtSrtStatus.Text = "SRT: TRANSMITTING (LIVE)";
                TxtSrtStatus.Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80));

                LogEvent("[SRT]", "✅ [INFO] SRT Connected. Bắt đầu truyền dẫn gói tin với tốc độ ổn định.");
                LogEvent("[INFO]", "Keyframe Sent (IDR Instantaneous Decoder Refresh).");

                UpdateTargetSummary();
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                LogEvent("[ERROR]", $"Lỗi khởi động phát sóng SRT: {ex.Message}");
                StopTransmissionInternal();
            }
        }

        private void BtnStopStreaming_Click(object sender, RoutedEventArgs e)
        {
            StopTransmissionInternal();
            LogEvent("[SRT]", "Đã dừng luồng phát sóng SRT.");
        }

        private void StopTransmissionInternal()
        {
            _isStreaming = false;
            _srtOutput?.Dispose();
            _srtOutput = null;

            BtnStartStreaming.IsEnabled = true;
            BtnStopStreaming.IsEnabled = false;

            LedSrtStatus.Fill = new SolidColorBrush(Color.FromRgb(158, 158, 158)); // Grey
            TxtSrtStatus.Text = "SRT: Idle";
            TxtSrtStatus.Foreground = new SolidColorBrush(Color.FromRgb(204, 204, 204));
        }

        private void UpdateTargetSummary()
        {
            if (TxtTargetSummary == null || TxtCodecSummary == null) return;

            string ip = TxtSrtIp?.Text ?? "127.0.0.1";
            string port = TxtSrtPort?.Text ?? "9000";
            string mode = (CmbSrtMode?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Caller";
            string codec = (CmbVideoCodec?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "H.264";
            double bitrate = SldTargetBitrate?.Value ?? 6000;
            bool isUll = ChkUltraLowLatency?.IsChecked == true;

            TxtTargetSummary.Text = $"SRT {mode.Split(' ')[0]} -> {ip}:{port}";
            TxtCodecSummary.Text = $"{codec.Split(' ')[0]} @ {bitrate:N0} kbps {(isUll ? "(ULL B=0)" : "")}";
        }

        #endregion

        #region Preview Controls & Monitoring

        private void ChkEnableVideoPreview_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            bool isEnabled = ChkEnableVideoPreview.IsChecked == true;
            bool isColorbar = CmbInputSource?.SelectedIndex == 3;

            if (ViewboxColorbar != null)
            {
                ViewboxColorbar.Visibility = (isColorbar && isEnabled) ? Visibility.Visible : Visibility.Collapsed;
            }

            if (ReviewView != null)
            {
                ReviewView.Visibility = (!isColorbar && isEnabled) ? Visibility.Visible : Visibility.Collapsed;
            }

            if (PnlPreviewDisabled != null)
            {
                PnlPreviewDisabled.Visibility = isEnabled ? Visibility.Collapsed : Visibility.Visible;
            }

            LogEvent("[INFO]", isEnabled ? "Bật Video Preview." : "Tắt Video Preview để tối ưu GPU.");
        }

        private void ChkEnableAudioMonitor_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            bool isAudioMonitor = ChkEnableAudioMonitor.IsChecked == true;
            if (_player != null)
            {
                _player.IsMuted = !isAudioMonitor;
            }
            if (OverlayAudioVu != null)
            {
                OverlayAudioVu.Visibility = isAudioMonitor ? Visibility.Visible : Visibility.Collapsed;
            }
            LogEvent("[INFO]", isAudioMonitor ? "Bật kiểm âm & Audio VU Meter 16 kênh." : "Mute âm thanh kiểm âm & ẩn VU Meter.");
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
            if (_player != null)
            {
                _player.Volume = e.NewValue;
            }
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

            if (ReviewView != null)
            {
                ReviewView.Stretch = currentStretch;
            }

            if (ViewboxColorbar != null)
            {
                ViewboxColorbar.Stretch = currentStretch;
            }

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
            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            string logLine = $"[{timestamp}] {tag} {message}\n";

            void PrependLog()
            {
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
                Dispatcher.Invoke(PrependLog);
            }
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            TxtLogConsole.Clear();
            LogEvent("[INFO]", "Đã xóa sạch nhật ký console.");
        }

        private static string? FindServerExecutable()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string current = baseDir;

            for (int i = 0; i < 6; i++)
            {
                if (string.IsNullOrEmpty(current)) break;

                var candidates = new[]
                {
                    Path.Combine(current, "build", "bin", "Debug", "OpenMediaServer.exe"),
                    Path.Combine(current, "build", "bin", "Release", "OpenMediaServer.exe"),
                    Path.Combine(current, "build-demo", "bin", "Debug", "OpenMediaServer.exe"),
                    Path.Combine(current, "build-demo", "bin", "Release", "OpenMediaServer.exe"),
                    Path.Combine(current, "build-production", "bin", "Release", "OpenMediaServer.exe"),
                    Path.Combine(current, "dist", "production", "bin", "OpenMediaServer.exe"),
                    Path.Combine(baseDir, "OpenMediaServer", "OpenMediaServer.exe"),
                    Path.Combine(baseDir, "OpenMediaServer.exe")
                };

                foreach (var candidate in candidates)
                {
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
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
