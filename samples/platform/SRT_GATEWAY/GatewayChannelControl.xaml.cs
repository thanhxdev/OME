using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SRT_GATEWAY
{
    public partial class GatewayChannelControl : UserControl, IDisposable
    {
        private GatewayChannelConfig _config = new();
        private GatewayPipelineEngine? _engine;
        private readonly DispatcherTimer _videoRenderTimer;
        private readonly Random _rnd = new();
        private WriteableBitmap? _videoBmp;
        private int _testPatternTick = 0;
        private bool _isInitialized = false;
        private bool _isDisposed = false;
        private readonly Dictionary<string, TextBlock> _egressStatusLabels = new();

        public GatewayChannelConfig Config => _config;
        public GatewayPipelineEngine? Engine => _engine;
        public bool IsRunning => _engine?.IsIngestConnected == true || _config.IsStarted;

        public event Action<GatewayChannelControl>? RemoveRequested;
        public event Action<GatewayChannelControl>? StateChanged;
        public event Action<string, string>? LogEmitted;

        public GatewayChannelControl()
        {
            InitializeComponent();

            _videoRenderTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(40) // ~25-30 fps test-pattern / frame renderer
            };
            _videoRenderTimer.Tick += OnVideoRenderTick;

            Loaded += (s, e) =>
            {
                InitVideoBitmap();
                _videoRenderTimer.Start();
            };
            Unloaded += (s, e) =>
            {
                _videoRenderTimer.Stop();
            };
        }

        public void BindConfig(GatewayChannelConfig config)
        {
            _isInitialized = false;
            _config = config ?? throw new ArgumentNullException(nameof(config));
            ApplyConfigToUI();
            _isInitialized = true;
        }

        private void ApplyConfigToUI()
        {
            TxtChannelBadge.Text = $"CH {_config.ChannelId}";
            TxtChannelTitle.Text = _config.ChannelName;
            if (TxtBadgeIngest != null) TxtBadgeIngest.Text = $"CH {_config.ChannelId}";
            if (TxtTitleIngest != null) TxtTitleIngest.Text = $"SRT Receiver Ingest #{_config.ChannelId}";
            if (BtnToggleIngest != null)
            {
                BtnToggleIngest.Content = _config.IsStarted ? $"Stop CH {_config.ChannelId}" : $"Start CH {_config.ChannelId}";
                BtnToggleIngest.Background = _config.IsStarted 
                    ? new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)) 
                    : new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC));
            }

            // Ingest
            if (TxtIngestName != null) TxtIngestName.Text = _config.ChannelName;
            CmbIngestMode.SelectedIndex = Math.Clamp(_config.SrtModeIndex, 0, 2);
            TxtIngestHost.Text = _config.IngestHost;
            TxtIngestPort.Text = _config.IngestPort.ToString();
            TxtIngestStreamId.Text = _config.StreamId;
            TxtIngestLatency.Text = _config.LatencyMs.ToString();
            if (ChkAutoLatencyIngest != null) ChkAutoLatencyIngest.IsChecked = _config.AutoLatencyEnabled;
            if (ChkDecryptIngest != null) ChkDecryptIngest.IsChecked = _config.EncryptionEnabled;
            if (TxtIngestPassphrase != null) TxtIngestPassphrase.Text = _config.Passphrase;
            if (CmbKeyLenIngest != null) CmbKeyLenIngest.SelectedIndex = Math.Clamp(_config.KeyLengthIndex, 0, 2);

            if (ToggleSmpteIngest != null) ToggleSmpteIngest.IsChecked = _config.IsSmpte2022_7IngestEnabled;
            if (PnlSingleIngest != null) PnlSingleIngest.Visibility = _config.IsSmpte2022_7IngestEnabled ? Visibility.Collapsed : Visibility.Visible;
            if (PnlGroupIngest != null) PnlGroupIngest.Visibility = _config.IsSmpte2022_7IngestEnabled ? Visibility.Visible : Visibility.Collapsed;
            if (BdSmpteBadge != null) BdSmpteBadge.Visibility = _config.IsSmpte2022_7IngestEnabled ? Visibility.Visible : Visibility.Collapsed;

            // Single NIC
            NetworkInterfaceScanner.PopulateNicComboBox(CmbNicIngest, _config.SourceNicIp);

            // Group Members
            if (TxtDiffDelayIngest != null) TxtDiffDelayIngest.Text = _config.DifferentialDelayMs.ToString();
            if (TxtMemberAHost != null) TxtMemberAHost.Text = _config.MemberAHost;
            if (TxtMemberAPort != null) TxtMemberAPort.Text = _config.MemberAPort.ToString();
            NetworkInterfaceScanner.PopulateNicComboBox(CmbMemberANic, _config.MemberANicIp);

            if (TxtMemberBHost != null) TxtMemberBHost.Text = _config.MemberBHost;
            if (TxtMemberBPort != null) TxtMemberBPort.Text = _config.MemberBPort.ToString();
            NetworkInterfaceScanner.PopulateNicComboBox(CmbMemberBNic, _config.MemberBNicIp);

            if (TxtNewMemberName != null) TxtNewMemberName.Text = _config.NewMemberName;
            if (TxtNewMemberHost != null) TxtNewMemberHost.Text = _config.NewMemberHost;
            if (TxtNewMemberPort != null) TxtNewMemberPort.Text = _config.NewMemberPort.ToString();
            NetworkInterfaceScanner.PopulateNicComboBox(CmbNewMemberNic, _config.NewMemberNicIp);

            // Restore dynamic member cards
            if (StackDynamicMembers != null)
            {
                StackDynamicMembers.Children.Clear();
                if (_config.ExtraGroupMembers != null)
                {
                    foreach (var m in _config.ExtraGroupMembers)
                    {
                        AddDynamicMemberCardToUI(m);
                    }
                }
            }

            // Egress: Render Dynamic Streams
            RenderAllEgressCards();

            UpdateOsdHeader();
            UpdateEgressChips();
        }

        public void ReadUIToConfig()
        {
            if (!_isInitialized || _config == null || CmbIngestMode == null || TxtIngestHost == null) return;

            string chName = TxtIngestName?.Text?.Trim() ?? TxtChannelTitle?.Text?.Trim() ?? _config.ChannelName;
            _config.ChannelName = chName;
            _config.SrtModeIndex = CmbIngestMode.SelectedIndex;
            _config.IngestHost = TxtIngestHost.Text.Trim();
            if (int.TryParse(TxtIngestPort.Text.Trim(), out int port)) _config.IngestPort = port;
            if (TxtIngestStreamId != null) _config.StreamId = TxtIngestStreamId.Text.Trim();
            if (int.TryParse(TxtIngestLatency?.Text.Trim(), out int lat)) _config.LatencyMs = lat;
            if (ChkAutoLatencyIngest != null) _config.AutoLatencyEnabled = ChkAutoLatencyIngest.IsChecked == true;
            if (ChkDecryptIngest != null) _config.EncryptionEnabled = ChkDecryptIngest.IsChecked == true;
            if (TxtIngestPassphrase != null) _config.Passphrase = TxtIngestPassphrase.Text;
            if (CmbKeyLenIngest != null) _config.KeyLengthIndex = CmbKeyLenIngest.SelectedIndex;

            if (ToggleSmpteIngest != null) _config.IsSmpte2022_7IngestEnabled = ToggleSmpteIngest.IsChecked == true;
            if (CmbNicIngest?.SelectedValue is string nicIp) _config.SourceNicIp = nicIp;

            if (int.TryParse(TxtDiffDelayIngest?.Text.Trim(), out int diff)) _config.DifferentialDelayMs = diff;
            if (TxtMemberAHost != null) _config.MemberAHost = TxtMemberAHost.Text.Trim();
            if (int.TryParse(TxtMemberAPort?.Text.Trim(), out int mA)) _config.MemberAPort = mA;
            if (CmbMemberANic?.SelectedValue is string nicA) _config.MemberANicIp = nicA;

            if (TxtMemberBHost != null) _config.MemberBHost = TxtMemberBHost.Text.Trim();
            if (int.TryParse(TxtMemberBPort?.Text.Trim(), out int mB)) _config.MemberBPort = mB;
            if (CmbMemberBNic?.SelectedValue is string nicB) _config.MemberBNicIp = nicB;

            if (TxtNewMemberName != null) _config.NewMemberName = TxtNewMemberName.Text.Trim();
            if (TxtNewMemberHost != null) _config.NewMemberHost = TxtNewMemberHost.Text.Trim();
            if (int.TryParse(TxtNewMemberPort?.Text.Trim(), out int nPort)) _config.NewMemberPort = nPort;
            if (CmbNewMemberNic?.SelectedValue is string nicNew) _config.NewMemberNicIp = nicNew;

            // Sync legacy properties from first matching egress streams if present
            if (_config.EgressStreams != null)
            {
                var srt = _config.EgressStreams.FirstOrDefault(s => s.Protocol == "SRT");
                if (srt != null)
                {
                    _config.SrtOutEnabled = srt.IsEnabled;
                    _config.SrtOutModeIndex = srt.SrtModeIndex;
                    _config.SrtOutHost = srt.SrtHost;
                    _config.SrtOutPort = srt.SrtPort;
                    _config.SrtOutStreamId = srt.SrtStreamId;
                    _config.SrtOutPassphrase = srt.Passphrase;
                    _config.SrtOutSmpte2022_7 = srt.IsSmpte2022_7Enabled;
                    _config.SrtOutMemberBPort = srt.MemberBPort;
                }

                var wrtc = _config.EgressStreams.FirstOrDefault(s => s.Protocol == "WebRTC");
                if (wrtc != null)
                {
                    _config.WebRtcOutEnabled = wrtc.IsEnabled;
                    _config.WebRtcPort = wrtc.WebRtcPort;
                }

                var rtmp = _config.EgressStreams.FirstOrDefault(s => s.Protocol == "RTMP");
                if (rtmp != null)
                {
                    _config.RtmpOutEnabled = rtmp.IsEnabled;
                    _config.RtmpUrl = rtmp.RtmpUrl;
                    _config.RtmpStreamKey = rtmp.RtmpStreamKey;
                }

                var rtsp = _config.EgressStreams.FirstOrDefault(s => s.Protocol == "RTSP");
                if (rtsp != null)
                {
                    _config.RtspOutEnabled = rtsp.IsEnabled;
                    _config.RtspPort = rtsp.RtspPort;
                    _config.RtspPath = rtsp.RtspPath;
                }

                var lrt = _config.EgressStreams.FirstOrDefault(s => s.Protocol == "LRT");
                if (lrt != null)
                {
                    _config.LrtOutEnabled = lrt.IsEnabled;
                    _config.LrtPath1Host = lrt.LrtPath1Host;
                    _config.LrtPath1Port = lrt.LrtPath1Port;
                    _config.LrtPath2Host = lrt.LrtPath2Host;
                    _config.LrtPath2Port = lrt.LrtPath2Port;
                }

                var hls = _config.EgressStreams.FirstOrDefault(s => s.Protocol == "HLS");
                if (hls != null)
                {
                    _config.HlsOutEnabled = hls.IsEnabled;
                    _config.HlsPort = hls.HlsPort;
                    _config.HlsSegmentDurationSec = hls.HlsSegmentDurationSec;
                }

                var dash = _config.EgressStreams.FirstOrDefault(s => s.Protocol == "DASH");
                if (dash != null)
                {
                    _config.DashOutEnabled = dash.IsEnabled;
                    _config.DashPort = dash.DashPort;
                }
            }
        }

        private void UpdateOsdHeader()
        {
            string modeStr = CmbIngestMode.SelectedIndex switch
            {
                1 => "CALLER",
                2 => "RENDEZVOUS",
                _ => "LISTENER"
            };

            OsdStreamId.Text = $"SRT INGEST: {_config.StreamId}";
            OsdPortMode.Text = $" | {modeStr} :{_config.IngestPort}";
            if (_config.IsSmpte2022_7IngestEnabled)
            {
                OsdPortMode.Text += $" [SMPTE A:{_config.MemberAPort} B:{_config.MemberBPort}]";
            }
        }

        private void UpdateEgressChips()
        {
            if (_config?.EgressStreams == null) return;
            int total = _config.EgressStreams.Count;
            int active = _config.EgressStreams.Count(s => s.IsEnabled);

            if (TxtActiveEgressCount != null)
            {
                TxtActiveEgressCount.Text = $"{active}/{total} BẬT";
                TxtActiveEgressCount.Foreground = active > 0 
                    ? new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76)) 
                    : new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));
            }
            if (TxtHudEgressCount != null)
            {
                TxtHudEgressCount.Text = $"EGRESS: {active}/{total} ON";
                TxtHudEgressCount.Foreground = active > 0
                    ? new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76))
                    : new SolidColorBrush(Color.FromRgb(0xFF, 0xB7, 0x4D));
            }
        }

        public async Task<bool> StartAsync()
        {
            if (_engine != null) return true;

            ReadUIToConfig();
            _config.IsStarted = true;
            BtnChannelStart.IsEnabled = false;
            BtnChannelStop.IsEnabled = true;
            if (BtnToggleIngest != null)
            {
                BtnToggleIngest.Content = $"Stop CH {_config.ChannelId}";
                BtnToggleIngest.Background = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
            }

            LedChannelStatus.Fill = new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Amber
            TxtChannelStatus.Text = "CONNECTING...";
            TxtChannelStatus.Foreground = new SolidColorBrush(Color.FromRgb(245, 158, 11));

            AppendLog("[CHANNEL]", $"🚀 Khởi động luồng Gateway {_config.ChannelName} (Port Ingest: {_config.IngestPort})...");

            _engine = new GatewayPipelineEngine(_config);
            _engine.LogEmitted += (tag, msg) => AppendLog(tag, msg);
            _engine.StatsUpdated += OnEngineStatsUpdated;

            bool ok = await _engine.StartAsync().ConfigureAwait(true);
            if (ok)
            {
                LedChannelStatus.Fill = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Green
                TxtChannelStatus.Text = "LIVE";
                TxtChannelStatus.Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80));
                PnlStandbyWatermark.Visibility = Visibility.Collapsed;
            }
            else
            {
                LedChannelStatus.Fill = new SolidColorBrush(Color.FromRgb(244, 67, 54)); // Red
                TxtChannelStatus.Text = "ERROR";
                TxtChannelStatus.Foreground = new SolidColorBrush(Color.FromRgb(244, 67, 54));
                if (BtnToggleIngest != null)
                {
                    BtnToggleIngest.Content = $"Start CH {_config.ChannelId}";
                    BtnToggleIngest.Background = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC));
                }
            }

            StateChanged?.Invoke(this);
            return ok;
        }

        public void Stop()
        {
            _config.IsStarted = false;
            if (_engine != null)
            {
                AppendLog("[CHANNEL]", $"⏹ Dừng luồng Gateway {_config.ChannelName}.");
                _engine.Stop();
                _engine.Dispose();
                _engine = null;
            }

            BtnChannelStart.IsEnabled = true;
            BtnChannelStop.IsEnabled = false;
            if (BtnToggleIngest != null)
            {
                BtnToggleIngest.Content = $"Start CH {_config.ChannelId}";
                BtnToggleIngest.Background = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC));
            }

            LedChannelStatus.Fill = new SolidColorBrush(Color.FromRgb(136, 136, 136));
            TxtChannelStatus.Text = "STANDBY";
            TxtChannelStatus.Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 170));
            PnlStandbyWatermark.Visibility = Visibility.Visible;

            TxtHudBitrate.Text = "0.00 Mbps";
            TxtHudFps.Text = "0.0";
            TxtHudRtt.Text = "0 ms";
            TxtHudLoss.Text = "0.00 %";
            VuMeterL.Value = 0;
            VuMeterR.Value = 0;

            StateChanged?.Invoke(this);
        }

        private void OnEngineStatsUpdated()
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (_engine == null) return;

                double mbps = (_engine.IngestBitrateKbps / 1000.0);
                TxtHudBitrate.Text = $"{mbps:F2} Mbps";
                TxtHudFps.Text = $"{_engine.IngestFps:F1}";
                TxtHudRtt.Text = $"{_engine.IngestRttMs:F0} ms";
                TxtHudLoss.Text = $"{_engine.IngestLossPercent:F2} %";

                // Egress status labels for dynamic streams
                if (_config?.EgressStreams != null)
                {
                    foreach (var st in _config.EgressStreams)
                    {
                        if (_egressStatusLabels.TryGetValue(st.Id, out var lbl))
                        {
                            if (!st.IsEnabled)
                            {
                                lbl.Text = " [OFF]";
                                lbl.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
                            }
                            else if (_engine.DynamicStreamStatuses.TryGetValue(st.Id, out var epStatus))
                            {
                                lbl.Text = epStatus.IsActive ? $" [LIVE: {epStatus.BitrateKbps:F0} kbps]" : $" [{epStatus.StatusMessage}]";
                                lbl.Foreground = epStatus.IsActive 
                                    ? new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76)) 
                                    : new SolidColorBrush(Color.FromRgb(0xFA, 0xCC, 0x15));
                            }
                            else
                            {
                                lbl.Text = " [ONLINE]";
                                lbl.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
                            }
                        }
                    }
                }
            });
        }

        private void AppendLog(string tag, string message)
        {
            Dispatcher.InvokeAsync(() =>
            {
                string line = $"[{DateTime.Now:HH:mm:ss}] {tag} {message}";
                if (TxtLogConsole.Text.Length > 20000)
                {
                    TxtLogConsole.Text = TxtLogConsole.Text.Substring(TxtLogConsole.Text.Length - 10000);
                }
                TxtLogConsole.AppendText(line + Environment.NewLine);
                TxtLogConsole.ScrollToEnd();
            });
            LogEmitted?.Invoke(tag, message);
        }

        #region Video Rendering (Broadcast SMPTE Test Pattern)

        private void InitVideoBitmap()
        {
            if (_videoBmp == null)
            {
                // 320x180 16:9 compact buffer for crisp preview
                _videoBmp = new WriteableBitmap(320, 180, 96, 96, PixelFormats.Bgr32, null);
                ImgVideoPreview.Source = _videoBmp;
            }
        }

        private void OnVideoRenderTick(object? sender, EventArgs e)
        {
            if (_videoBmp == null || _isDisposed) return;

            _testPatternTick++;

            // Fake audio vu peak motion when running
            if (_engine != null && _engine.IsIngestConnected)
            {
                double vuValL = 40 + _rnd.Next(0, 50);
                double vuValR = 38 + _rnd.Next(0, 50);
                VuMeterL.Value = vuValL;
                VuMeterR.Value = vuValR;
            }
            else
            {
                VuMeterL.Value = 0;
                VuMeterR.Value = 0;
            }

            RenderTestPattern();
        }

        private unsafe void RenderTestPattern()
        {
            if (_videoBmp == null) return;

            int width = _videoBmp.PixelWidth;
            int height = _videoBmp.PixelHeight;
            int stride = _videoBmp.BackBufferStride;

            _videoBmp.Lock();
            try
            {
                byte* pBuffer = (byte*)_videoBmp.BackBuffer.ToPointer();

                // SMPTE 75% Bars colors: White, Yellow, Cyan, Green, Magenta, Red, Blue
                uint[] smpteColors = new uint[]
                {
                    0xFFC0C0C0, // White
                    0xFFC0C000, // Yellow
                    0xFF00C0C0, // Cyan
                    0xFF00C000, // Green
                    0xFFC000C0, // Magenta
                    0xFFC00000, // Red
                    0xFF0000C0  // Blue
                };

                int barWidth = width / 7;
                bool isRunning = _engine != null;

                for (int y = 0; y < height; y++)
                {
                    uint* pRow = (uint*)(pBuffer + y * stride);
                    for (int x = 0; x < width; x++)
                    {
                        if (!isRunning)
                        {
                            // Dimmed testbars in standby
                            int barIdx = Math.Min(x / barWidth, 6);
                            uint color = smpteColors[barIdx];
                            byte r = (byte)(((color >> 16) & 0xFF) / 5);
                            byte g = (byte)(((color >> 8) & 0xFF) / 5);
                            byte b = (byte)((color & 0xFF) / 5);
                            pRow[x] = (uint)((0xFF << 24) | (r << 16) | (g << 8) | b);
                        }
                        else
                        {
                            // Full vivid broadcast colorbars with moving tick
                            int barIdx = Math.Min(x / barWidth, 6);
                            uint color = smpteColors[barIdx];

                            // Moving tick line on bottom
                            if (y > height - 25 && y < height - 5)
                            {
                                int tickPos = (_testPatternTick * 3) % width;
                                if (Math.Abs(x - tickPos) < 4)
                                {
                                    color = 0xFFFFFFFF; // White tick
                                }
                            }

                            pRow[x] = color;
                        }
                    }
                }

                _videoBmp.AddDirtyRect(new Int32Rect(0, 0, width, height));
            }
            finally
            {
                _videoBmp.Unlock();
            }
        }

        #endregion

        #region Event Handlers

        private void BtnChannelStart_Click(object sender, RoutedEventArgs e)
        {
            _ = StartAsync();
        }

        private void BtnChannelStop_Click(object sender, RoutedEventArgs e)
        {
            Stop();
        }

        private void BtnRemoveChannel_Click(object sender, RoutedEventArgs e)
        {
            RemoveRequested?.Invoke(this);
        }

        private void Input_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            // Sync name if editing either TxtIngestName or TxtChannelTitle
            if (sender == TxtIngestName && TxtChannelTitle != null)
            {
                TxtChannelTitle.Text = TxtIngestName.Text;
            }
            else if (sender == TxtChannelTitle && TxtIngestName != null)
            {
                TxtIngestName.Text = TxtChannelTitle.Text;
            }

            ReadUIToConfig();
            UpdateOsdHeader();
            UpdateEgressChips();
            StateChanged?.Invoke(this);
        }

        private void CmbIngestMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;
            Input_Changed(sender, e);
        }

        private void BtnToggleIngest_Click(object sender, RoutedEventArgs e)
        {
            if (IsRunning)
            {
                Stop();
            }
            else
            {
                _ = StartAsync();
            }
        }

        private void ToggleSmpteIngest_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            bool isGroup = ToggleSmpteIngest.IsChecked == true;
            if (PnlSingleIngest != null) PnlSingleIngest.Visibility = isGroup ? Visibility.Collapsed : Visibility.Visible;
            if (PnlGroupIngest != null) PnlGroupIngest.Visibility = isGroup ? Visibility.Visible : Visibility.Collapsed;
            if (BdSmpteBadge != null) BdSmpteBadge.Visibility = isGroup ? Visibility.Visible : Visibility.Collapsed;

            AppendLog("[SMPTE_2022-7]", $"Chuyển sang chế độ {(isGroup ? "SMPTE 2022-7 Hitless Redundancy (Group Socket)" : "Single Socket SRT tiêu chuẩn")}.");
            Input_Changed(sender, e);
        }

        private void BtnScanNic_Click(object sender, RoutedEventArgs e)
        {
            var nics = NetworkInterfaceScanner.GetAvailableNetworkInterfaces();
            NetworkInterfaceScanner.PopulateNicComboBox(CmbNicIngest, _config.SourceNicIp, nics);
            NetworkInterfaceScanner.PopulateNicComboBox(CmbMemberANic, _config.MemberANicIp, nics);
            NetworkInterfaceScanner.PopulateNicComboBox(CmbMemberBNic, _config.MemberBNicIp, nics);
            NetworkInterfaceScanner.PopulateNicComboBox(CmbNewMemberNic, _config.NewMemberNicIp, nics);
            AppendLog("[NIC_SCAN]", $"Đã quét và cập nhật lại danh sách {nics.Count} Card mạng (NIC).");
        }

        private async void BtnAddGroupMember_Click(object sender, RoutedEventArgs e)
        {
            string name = TxtNewMemberName?.Text?.Trim() ?? "Path C (5G)";
            string host = TxtNewMemberHost?.Text?.Trim() ?? "0.0.0.0";
            if (!int.TryParse(TxtNewMemberPort?.Text, out int port) || port <= 0 || port > 65535)
            {
                MessageBox.Show("Vui lòng nhập cổng UDP hợp lệ (1-65535).", "SRT Group Socket", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string nicIp = (CmbNewMemberNic?.SelectedValue as string) ?? string.Empty;
            if (nicIp == NetworkInterfaceScanner.DefaultAnyIp) nicIp = string.Empty;

            var newMember = new OpenMedia.Platform.Models.SRTGroupMemberConfig
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                Host = host,
                Port = port,
                LocalInterfaceIp = nicIp,
                Weight = 10,
                IsEnabled = true
            };

            _config.ExtraGroupMembers.Add(newMember);
            AddDynamicMemberCardToUI(newMember);

            bool attached = false;
            if (_engine != null && _engine.IsIngestConnected)
            {
                attached = await _engine.AddMemberSocketAsync(newMember);
            }

            AppendLog("[GROUP_HOTPLUG]", $"⚡ {(attached ? "Đã gắn thành công" : "Đã lưu cấu hình")} member socket '{name}' ({host}:{port} - NIC: {(string.IsNullOrEmpty(nicIp) ? "Auto" : nicIp)}) vào luồng Ingest!");
            StateChanged?.Invoke(this);
        }

        private void AddDynamicMemberCardToUI(OpenMedia.Platform.Models.SRTGroupMemberConfig member)
        {
            if (StackDynamicMembers == null) return;

            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x24, 0x2E)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x02, 0x84, 0xC7)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 0, 0, 4),
                Tag = member.Id
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var infoPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            infoPanel.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 7,
                Height = 7,
                Fill = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)),
                Margin = new Thickness(0, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center
            });

            string nicLabel = string.IsNullOrEmpty(member.LocalInterfaceIp) || member.LocalInterfaceIp == "0.0.0.0" 
                ? "Auto NIC" : member.LocalInterfaceIp;
            infoPanel.Children.Add(new TextBlock
            {
                Text = $"[{member.Name}] {member.Host}:{member.Port} ({nicLabel})",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xBA, 0xE6, 0xFD)),
                VerticalAlignment = VerticalAlignment.Center
            });

            var btnDelete = new Button
            {
                Content = "🗑️ Xoá Member",
                Background = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 9.5,
                Padding = new Thickness(6, 2, 6, 2),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "Gỡ bỏ Member Socket này an toàn ngay cả khi đang chạy live (Zero-Disruption)"
            };

            btnDelete.Click += async (s, e) =>
            {
                await RemoveDynamicMemberAsync(member.Id, border);
            };

            Grid.SetColumn(infoPanel, 0);
            Grid.SetColumn(btnDelete, 1);
            grid.Children.Add(infoPanel);
            grid.Children.Add(btnDelete);
            border.Child = grid;

            StackDynamicMembers.Children.Add(border);
        }

        private async Task RemoveDynamicMemberAsync(string memberId, Border cardBorder)
        {
            if (cardBorder != null && StackDynamicMembers != null)
            {
                StackDynamicMembers.Children.Remove(cardBorder);
            }

            _config.ExtraGroupMembers.RemoveAll(m => m.Id == memberId);

            if (_engine != null)
            {
                await _engine.RemoveMemberSocketAsync(memberId);
            }

            AppendLog("[GROUP_HOTPLUG]", $"Đã gỡ bỏ member socket ID {memberId} khỏi cấu hình.");
            StateChanged?.Invoke(this);
        }

        #endregion

        #region Dynamic Multi-Protocol Egress Fan-Out Cards

        private void BtnAddEgressStream_Click(object sender, RoutedEventArgs e)
        {
            if (_config?.EgressStreams == null) return;

            string protocol = "SRT";
            if (CmbNewEgressProtocol?.SelectedItem is ComboBoxItem item && item.Content is string content)
            {
                protocol = content.Trim();
            }
            else if (!string.IsNullOrEmpty(CmbNewEgressProtocol?.Text))
            {
                protocol = CmbNewEgressProtocol.Text.Trim();
            }

            int count = _config.EgressStreams.Count(s => s.Protocol.Equals(protocol, StringComparison.OrdinalIgnoreCase)) + 1;
            string streamName = $"{protocol} Out #{count}";

            var newStream = new GatewayEgressStreamConfig
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = streamName,
                Protocol = protocol,
                IsEnabled = false
            };

            switch (protocol.ToUpperInvariant())
            {
                case "SRT":
                    newStream.SrtModeIndex = 0; // Caller
                    newStream.SrtHost = "127.0.0.1";
                    newStream.SrtPort = 9000 + _config.EgressStreams.Count * 2;
                    newStream.SrtStreamId = $"live/{_config.ChannelId}/srt_out{count}";
                    newStream.LatencyMs = 120;
                    newStream.AutoLatencyEnabled = false;
                    newStream.EncryptionEnabled = false;
                    newStream.Passphrase = "";
                    newStream.KeyLengthIndex = 2; // 256-bit
                    newStream.IsSmpte2022_7Enabled = false;
                    newStream.DifferentialDelayMs = 50;
                    newStream.MemberBHost = "127.0.0.1";
                    newStream.MemberBPort = newStream.SrtPort + 1;
                    break;

                case "WEBRTC":
                    newStream.CameraId = $"cam-0{Math.Min(count, 9)}";
                    newStream.DisplayName = $"Camera {_config.ChannelId}-{count}";
                    newStream.SessionRoom = $"room-{_config.ChannelId}";
                    newStream.SignalingUrl = "ws://127.0.0.1:3000/ws";
                    newStream.SfuHost = "127.0.0.1";
                    newStream.WebRtcPort = 8443 + _config.EgressStreams.Count;
                    newStream.PortModeIndex = 0; // 2 Ports (Split A/V)
                    newStream.AntiEchoGuard = true;
                    newStream.VideoCodec = "H.264 / AVC (Payload Type 96)";
                    newStream.TargetBitrateKbps = 8000;
                    newStream.EnableIce = false;
                    newStream.StunServer = "stun:stun.l.google.com:19302";
                    newStream.TurnServer = "turn:your-server.com:3478";
                    break;

                case "RTMP":
                    newStream.RtmpUrl = "rtmp://127.0.0.1/live";
                    newStream.RtmpStreamKey = $"ch_{_config.ChannelId}_out{count}";
                    break;

                case "RTSP":
                    newStream.RtspPort = 8554 + _config.EgressStreams.Count;
                    newStream.RtspPath = $"/live/ch{_config.ChannelId}_out{count}";
                    break;

                case "LRT":
                    newStream.LrtPath1Host = "192.168.1.100";
                    newStream.LrtPath1Port = 5000 + _config.EgressStreams.Count * 2;
                    newStream.LrtPath2Host = "192.168.2.100";
                    newStream.LrtPath2Port = newStream.LrtPath1Port + 1;
                    break;

                case "HLS":
                    newStream.HlsPort = 8080 + _config.EgressStreams.Count;
                    newStream.HlsSegmentDurationSec = 4;
                    break;

                case "DASH":
                    newStream.DashPort = 8081 + _config.EgressStreams.Count;
                    break;
            }

            _config.EgressStreams.Add(newStream);
            AddEgressCardToUI(newStream);
            UpdateEgressChips();
            AppendLog("[EGRESS]", $"➕ Đã thêm luồng Out '{newStream.Name}' [{newStream.Protocol}] vào danh sách Fan-out.");
            StateChanged?.Invoke(this);
        }

        private void RenderAllEgressCards()
        {
            if (StackEgressList == null) return;
            StackEgressList.Children.Clear();
            _egressStatusLabels.Clear();

            if (_config?.EgressStreams != null)
            {
                foreach (var stream in _config.EgressStreams)
                {
                    AddEgressCardToUI(stream);
                }
            }
            UpdateEgressChips();
        }

        private void AddEgressCardToUI(GatewayEgressStreamConfig stream)
        {
            if (StackEgressList == null) return;

            var (badgeColor, borderColor) = GetProtocolColors(stream.Protocol);

            var cardBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x18, 0x1C, 0x24)),
                BorderBrush = new SolidColorBrush(borderColor),
                BorderThickness = new Thickness(1.2),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 10),
                Tag = stream.Id
            };

            var mainStack = new StackPanel();

            // Header Row
            var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Left header elements: Toggle ONLINE Button, Badge, Name
            var leftStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            var btnToggleOnline = new Button
            {
                FontWeight = FontWeights.Bold,
                FontSize = 10,
                Padding = new Thickness(7, 2, 7, 2),
                Height = 24,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = Cursors.Hand
            };

            void UpdateToggleOnlineAppearance()
            {
                if (stream.IsEnabled)
                {
                    btnToggleOnline.Content = "🟢 ONLINE";
                    btnToggleOnline.Background = new SolidColorBrush(Color.FromRgb(0x15, 0x80, 0x3D)); // Emerald / Green 700
                    btnToggleOnline.Foreground = Brushes.White;
                    btnToggleOnline.BorderBrush = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)); // Green 500
                    btnToggleOnline.BorderThickness = new Thickness(1);
                    btnToggleOnline.ToolTip = "Luồng đang BẬT (Click để chuyển sang OFFLINE)";
                }
                else
                {
                    btnToggleOnline.Content = "⚪ OFFLINE";
                    btnToggleOnline.Background = new SolidColorBrush(Color.FromRgb(0x2E, 0x32, 0x3D)); // Dark Slate
                    btnToggleOnline.Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)); // Slate 400
                    btnToggleOnline.BorderBrush = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)); // Slate 600
                    btnToggleOnline.BorderThickness = new Thickness(1);
                    btnToggleOnline.ToolTip = "Luồng đang TẮT (Click để chuyển sang ONLINE)";
                }
            }

            UpdateToggleOnlineAppearance();

            btnToggleOnline.Click += (s, e) =>
            {
                stream.IsEnabled = !stream.IsEnabled;
                UpdateToggleOnlineAppearance();
                if (_egressStatusLabels.TryGetValue(stream.Id, out var lbl))
                {
                    lbl.Text = stream.IsEnabled ? " [ONLINE]" : " [OFF]";
                    lbl.Foreground = stream.IsEnabled 
                        ? new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76)) 
                        : new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
                }
                UpdateEgressChips();
                AppendLog("[EGRESS]", $"Luồng '{stream.Name}' [{stream.Protocol}] chuyển sang {(stream.IsEnabled ? "ONLINE" : "OFFLINE")}.");
                StateChanged?.Invoke(this);
            };
            leftStack.Children.Add(btnToggleOnline);

            var badgeBorder = new Border
            {
                Background = new SolidColorBrush(badgeColor),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            badgeBorder.Child = new TextBlock
            {
                Text = stream.Protocol.ToUpperInvariant(),
                FontWeight = FontWeights.Bold,
                FontSize = 10,
                Foreground = Brushes.White
            };
            leftStack.Children.Add(badgeBorder);

            var txtName = new TextBox
            {
                Text = stream.Name,
                FontWeight = FontWeights.SemiBold,
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF)),
                Background = new SolidColorBrush(Color.FromRgb(0x22, 0x25, 0x2D)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x3B, 0x42, 0x52)),
                BorderThickness = new Thickness(1),
                Height = 26,
                Width = 140,
                Padding = new Thickness(5, 2, 5, 2),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            txtName.TextChanged += (s, e) => { stream.Name = txtName.Text.Trim(); StateChanged?.Invoke(this); };
            leftStack.Children.Add(txtName);

            Grid.SetColumn(leftStack, 0);
            headerGrid.Children.Add(leftStack);

            // Status Label
            var statusLabel = new TextBlock
            {
                Text = stream.IsEnabled ? " [ONLINE]" : " [OFF]",
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = stream.IsEnabled ? new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76)) : new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
            _egressStatusLabels[stream.Id] = statusLabel;
            Grid.SetColumn(statusLabel, 1);
            headerGrid.Children.Add(statusLabel);

            // Remove Button
            var btnDelete = new Button
            {
                Content = "➖ Xoá Luồng",
                Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x80)),
                FontWeight = FontWeights.Bold,
                FontSize = 10.5,
                Padding = new Thickness(8, 4, 8, 4),
                Cursor = Cursors.Hand
            };
            btnDelete.Click += (s, e) =>
            {
                _config.EgressStreams.Remove(stream);
                StackEgressList.Children.Remove(cardBorder);
                _egressStatusLabels.Remove(stream.Id);
                UpdateEgressChips();
                AppendLog("[EGRESS]", $"🗑️ Đã xoá luồng Out '{stream.Name}' [{stream.Protocol}].");
                StateChanged?.Invoke(this);
            };
            Grid.SetColumn(btnDelete, 2);
            headerGrid.Children.Add(btnDelete);

            mainStack.Children.Add(headerGrid);

            // Divider
            mainStack.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x31, 0x3D)), Margin = new Thickness(0, 0, 0, 8) });

            // Protocol Body Panel
            FrameworkElement bodyElement = stream.Protocol.ToUpperInvariant() switch
            {
                "SRT" => BuildSrtCardBody(stream),
                "WEBRTC" => BuildWebRtcCardBody(stream),
                "RTMP" => BuildRtmpCardBody(stream),
                "RTSP" => BuildRtspCardBody(stream),
                "LRT" => BuildLrtCardBody(stream),
                "HLS" => BuildHlsCardBody(stream),
                "DASH" => BuildDashCardBody(stream),
                _ => BuildSrtCardBody(stream)
            };
            mainStack.Children.Add(bodyElement);

            cardBorder.Child = mainStack;
            StackEgressList.Children.Add(cardBorder);
        }

        private static (Color badge, Color border) GetProtocolColors(string protocol)
        {
            return protocol.ToUpperInvariant() switch
            {
                "SRT" => (Color.FromRgb(0x00, 0x7A, 0xCC), Color.FromRgb(0x00, 0x98, 0xFF)),
                "WEBRTC" => (Color.FromRgb(0x05, 0x96, 0x69), Color.FromRgb(0x10, 0xB9, 0x81)),
                "RTMP" => (Color.FromRgb(0xD9, 0x77, 0x06), Color.FromRgb(0xF5, 0x9E, 0x0B)),
                "RTSP" => (Color.FromRgb(0x7C, 0x3A, 0xED), Color.FromRgb(0x8B, 0x5C, 0xF6)),
                "LRT" => (Color.FromRgb(0xDB, 0x27, 0x77), Color.FromRgb(0xEC, 0x48, 0x99)),
                "HLS" => (Color.FromRgb(0x08, 0x91, 0xB2), Color.FromRgb(0x06, 0xB6, 0xD4)),
                "DASH" => (Color.FromRgb(0x25, 0x63, 0xEB), Color.FromRgb(0x3B, 0x82, 0xF6)),
                _ => (Color.FromRgb(0x4B, 0x55, 0x63), Color.FromRgb(0x6B, 0x72, 0x80))
            };
        }

        #region Protocol Body Builders

        /// <summary>
        /// Giao diện cấu hình SRT Out học chuẩn từ Tab 1. SRT Config của App SRT_ENCODE
        /// </summary>
        private FrameworkElement BuildSrtCardBody(GatewayEgressStreamConfig stream)
        {
            var stack = new StackPanel();

            // 1. SRT PROTOCOL, LATENCY & AES SECURITY
            var card1 = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x14, 0x18, 0x20)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 8)
            };
            var s1 = new StackPanel();
            s1.Children.Add(new TextBlock
            {
                Text = "1. SRT PROTOCOL, LATENCY & AES SECURITY",
                FontWeight = FontWeights.Bold,
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xD2, 0xFF)),
                Margin = new Thickness(0, 0, 0, 6)
            });

            // Row: Mode + Stream ID
            var gMode = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            gMode.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            gMode.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var pMode = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
            pMode.Children.Add(CreateHeaderLabel("SRT Connection Mode:"));
            var cmbMode = CreateDarkComboBox();
            cmbMode.Items.Add(new ComboBoxItem { Content = "Caller (Connect to Studio)" });
            cmbMode.Items.Add(new ComboBoxItem { Content = "Listener (Wait for Incoming)" });
            cmbMode.Items.Add(new ComboBoxItem { Content = "Rendezvous (P2P Firewall Traversal)" });
            cmbMode.SelectedIndex = Math.Clamp(stream.SrtModeIndex, 0, 2);
            cmbMode.SelectionChanged += (s, e) => { stream.SrtModeIndex = cmbMode.SelectedIndex; StateChanged?.Invoke(this); };
            pMode.Children.Add(cmbMode);
            Grid.SetColumn(pMode, 0);
            gMode.Children.Add(pMode);

            var pStreamId = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };
            pStreamId.Children.Add(CreateHeaderLabel("SRT Stream ID (Optional for Caller Routing):"));
            var txtStreamId = CreateDarkTextBox(stream.SrtStreamId, v => { stream.SrtStreamId = v; StateChanged?.Invoke(this); });
            pStreamId.Children.Add(txtStreamId);
            Grid.SetColumn(pStreamId, 1);
            gMode.Children.Add(pStreamId);
            s1.Children.Add(gMode);

            // Divider
            s1.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2E, 0x3A)), Margin = new Thickness(0, 2, 0, 6) });

            // Latency Row
            var chkAutoLat = new CheckBox
            {
                Content = "Auto Latency (3 x RTT)",
                IsChecked = stream.AutoLatencyEnabled,
                Foreground = Brushes.LightGray,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 4),
                Cursor = Cursors.Hand
            };
            chkAutoLat.Checked += (s, e) => { stream.AutoLatencyEnabled = true; StateChanged?.Invoke(this); };
            chkAutoLat.Unchecked += (s, e) => { stream.AutoLatencyEnabled = false; StateChanged?.Invoke(this); };
            s1.Children.Add(chkAutoLat);

            var gLat = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            gLat.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            gLat.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var pLat = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
            pLat.Children.Add(CreateHeaderLabel("Configured Latency (ms):"));
            var txtLat = CreateDarkTextBox(stream.LatencyMs.ToString(), v =>
            {
                if (int.TryParse(v.Trim(), out int val)) stream.LatencyMs = val;
                StateChanged?.Invoke(this);
            });
            pLat.Children.Add(txtLat);
            Grid.SetColumn(pLat, 0);
            gLat.Children.Add(pLat);

            var pAutoLat = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };
            pAutoLat.Children.Add(CreateHeaderLabel("Calculated Auto Latency:"));
            var bdCalc = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x22, 0x2B)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x3B, 0x42, 0x52)),
                BorderThickness = new Thickness(1),
                Height = 28,
                Padding = new Thickness(8, 4, 8, 4)
            };
            bdCalc.Child = new TextBlock
            {
                Text = $"{stream.LatencyMs} ms (Auto 3 x RTT)",
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76)),
                VerticalAlignment = VerticalAlignment.Center
            };
            pAutoLat.Children.Add(bdCalc);
            Grid.SetColumn(pAutoLat, 1);
            gLat.Children.Add(pAutoLat);
            s1.Children.Add(gLat);

            // Divider
            s1.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2E, 0x3A)), Margin = new Thickness(0, 2, 0, 6) });

            // Encryption Section
            var pnlEnc = new StackPanel { Visibility = stream.EncryptionEnabled ? Visibility.Visible : Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) };
            var chkEnc = new CheckBox
            {
                Content = "Enable Encryption (AES)",
                IsChecked = stream.EncryptionEnabled,
                Foreground = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)),
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 4),
                Cursor = Cursors.Hand
            };
            chkEnc.Checked += (s, e) =>
            {
                stream.EncryptionEnabled = true;
                pnlEnc.Visibility = Visibility.Visible;
                StateChanged?.Invoke(this);
            };
            chkEnc.Unchecked += (s, e) =>
            {
                stream.EncryptionEnabled = false;
                pnlEnc.Visibility = Visibility.Collapsed;
                StateChanged?.Invoke(this);
            };
            s1.Children.Add(chkEnc);

            var gEnc = new Grid();
            gEnc.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            gEnc.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var pPass = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
            pPass.Children.Add(CreateHeaderLabel("Passphrase (10 - 79 ký tự):"));
            var txtPass = CreateDarkTextBox(stream.Passphrase, v => { stream.Passphrase = v; StateChanged?.Invoke(this); });
            pPass.Children.Add(txtPass);
            Grid.SetColumn(pPass, 0);
            gEnc.Children.Add(pPass);

            var pKey = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };
            pKey.Children.Add(CreateHeaderLabel("Key Length:"));
            var cmbKey = CreateDarkComboBox();
            cmbKey.Items.Add(new ComboBoxItem { Content = "128-bit / 16 bytes" });
            cmbKey.Items.Add(new ComboBoxItem { Content = "192-bit / 24 bytes" });
            cmbKey.Items.Add(new ComboBoxItem { Content = "256-bit / 32 bytes" });
            cmbKey.SelectedIndex = Math.Clamp(stream.KeyLengthIndex, 0, 2);
            cmbKey.SelectionChanged += (s, e) => { stream.KeyLengthIndex = cmbKey.SelectedIndex; StateChanged?.Invoke(this); };
            pKey.Children.Add(cmbKey);
            Grid.SetColumn(pKey, 1);
            gEnc.Children.Add(pKey);
            pnlEnc.Children.Add(gEnc);
            s1.Children.Add(pnlEnc);

            card1.Child = s1;
            stack.Children.Add(card1);

            // 2. GROUP SOCKET & NETWORK ENDPOINT (SMPTE 2022-7)
            var card2 = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x14, 0x18, 0x20)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 4)
            };
            var s2 = new StackPanel();

            var gHeader2 = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            gHeader2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            gHeader2.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var t2 = new TextBlock
            {
                Text = "2. GROUP SOCKET & NETWORK ENDPOINT",
                FontWeight = FontWeights.Bold,
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(t2, 0);
            gHeader2.Children.Add(t2);

            var pnlSingle = new StackPanel { Visibility = stream.IsSmpte2022_7Enabled ? Visibility.Collapsed : Visibility.Visible };
            var pnlGroup = new StackPanel { Visibility = stream.IsSmpte2022_7Enabled ? Visibility.Visible : Visibility.Collapsed };

            var chkSmpte = new CheckBox
            {
                Content = stream.IsSmpte2022_7Enabled ? "🛡️ SMPTE 2022-7: BẬT" : "⚪ SMPTE 2022-7: TẮT",
                IsChecked = stream.IsSmpte2022_7Enabled,
                FontWeight = FontWeights.SemiBold,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0xB2, 0xEB, 0xF2)),
                Cursor = Cursors.Hand
            };
            chkSmpte.Checked += (s, e) =>
            {
                stream.IsSmpte2022_7Enabled = true;
                chkSmpte.Content = "🛡️ SMPTE 2022-7: BẬT";
                pnlSingle.Visibility = Visibility.Collapsed;
                pnlGroup.Visibility = Visibility.Visible;
                StateChanged?.Invoke(this);
            };
            chkSmpte.Unchecked += (s, e) =>
            {
                stream.IsSmpte2022_7Enabled = false;
                chkSmpte.Content = "⚪ SMPTE 2022-7: TẮT";
                pnlSingle.Visibility = Visibility.Visible;
                pnlGroup.Visibility = Visibility.Collapsed;
                StateChanged?.Invoke(this);
            };
            Grid.SetColumn(chkSmpte, 1);
            gHeader2.Children.Add(chkSmpte);
            s2.Children.Add(gHeader2);

            // Single Link Panel
            var bdSingle = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x16, 0x1E, 0x28)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 4)
            };
            var sSingle = new StackPanel();
            sSingle.Children.Add(new TextBlock
            {
                Text = "🌐 CHẾ ĐỘ TRUYỀN ĐƠN (SINGLE LINK)",
                FontWeight = FontWeights.Bold,
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xD2, 0xFF)),
                Margin = new Thickness(0, 0, 0, 6)
            });

            var gSingleHost = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            gSingleHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            gSingleHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var pHost = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
            pHost.Children.Add(CreateHeaderLabel("Remote IP Address / Host:"));
            var txtHost = CreateDarkTextBox(stream.SrtHost, v => { stream.SrtHost = v; StateChanged?.Invoke(this); });
            pHost.Children.Add(txtHost);
            Grid.SetColumn(pHost, 0);
            gSingleHost.Children.Add(pHost);

            var pPort = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };
            pPort.Children.Add(CreateHeaderLabel("Port (UDP):"));
            var txtPort = CreateDarkTextBox(stream.SrtPort.ToString(), v =>
            {
                if (int.TryParse(v.Trim(), out int p)) stream.SrtPort = p;
                StateChanged?.Invoke(this);
            });
            pPort.Children.Add(txtPort);
            Grid.SetColumn(pPort, 1);
            gSingleHost.Children.Add(pPort);
            sSingle.Children.Add(gSingleHost);

            // NIC binding
            var pNic = new StackPanel();
            pNic.Children.Add(CreateHeaderLabel("Card Mạng Nguồn (Source NIC Binding):"));
            var gNic = new Grid();
            gNic.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            gNic.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var cmbNic = CreateDarkComboBox();
            NetworkInterfaceScanner.PopulateNicComboBox(cmbNic, stream.SourceNicIp);
            cmbNic.SelectionChanged += (s, e) =>
            {
                if (cmbNic.SelectedValue is string ip) { stream.SourceNicIp = ip; StateChanged?.Invoke(this); }
            };
            Grid.SetColumn(cmbNic, 0);
            gNic.Children.Add(cmbNic);

            var btnScanNic = new Button
            {
                Content = "🔄 Quét NIC",
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2E, 0x3A)),
                Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0xCC)),
                FontSize = 10.5,
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(6, 0, 0, 0),
                Cursor = Cursors.Hand
            };
            btnScanNic.Click += (s, e) => NetworkInterfaceScanner.PopulateNicComboBox(cmbNic, stream.SourceNicIp);
            Grid.SetColumn(btnScanNic, 1);
            gNic.Children.Add(btnScanNic);
            pNic.Children.Add(gNic);
            sSingle.Children.Add(pNic);

            bdSingle.Child = sSingle;
            pnlSingle.Children.Add(bdSingle);
            s2.Children.Add(pnlSingle);

            // Group Redundancy Panel (SMPTE 2022-7)
            var gSmpteTop = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            gSmpteTop.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            gSmpteTop.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var pGrpMech = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
            pGrpMech.Children.Add(CreateHeaderLabel("Cơ Chế Group Socket:"));
            var cmbGrpMech = CreateDarkComboBox();
            cmbGrpMech.Items.Add(new ComboBoxItem { Content = "Broadcast (SMPTE 2022-7 Redundancy)", IsSelected = true });
            cmbGrpMech.Items.Add(new ComboBoxItem { Content = "Backup (Active / Standby Failover)" });
            pGrpMech.Children.Add(cmbGrpMech);
            Grid.SetColumn(pGrpMech, 0);
            gSmpteTop.Children.Add(pGrpMech);

            var pDelay = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };
            pDelay.Children.Add(CreateHeaderLabel("Bù Trễ Vi Sai (ms):"));
            var txtDelay = CreateDarkTextBox(stream.DifferentialDelayMs.ToString(), v =>
            {
                if (int.TryParse(v.Trim(), out int d)) stream.DifferentialDelayMs = d;
                StateChanged?.Invoke(this);
            });
            pDelay.Children.Add(txtDelay);
            Grid.SetColumn(pDelay, 1);
            gSmpteTop.Children.Add(pDelay);
            pnlGroup.Children.Add(gSmpteTop);

            // Member A
            var bdMemA = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x22, 0x28)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 6)
            };
            var sMemA = new StackPanel();
            sMemA.Children.Add(new TextBlock
            {
                Text = "MEMBER #1: PATH A (PRIMARY LINK)",
                FontWeight = FontWeights.Bold,
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76)),
                Margin = new Thickness(0, 0, 0, 4)
            });
            var gMemA = new Grid();
            gMemA.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
            gMemA.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star) });
            gMemA.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
            var txtMemAHost = CreateDarkTextBox(stream.SrtHost, v => { stream.SrtHost = v; StateChanged?.Invoke(this); });
            var txtMemAPort = CreateDarkTextBox(stream.SrtPort.ToString(), v => { if (int.TryParse(v.Trim(), out int p)) stream.SrtPort = p; StateChanged?.Invoke(this); });
            var cmbMemANic = CreateDarkComboBox();
            NetworkInterfaceScanner.PopulateNicComboBox(cmbMemANic, stream.MemberANicIp);
            cmbMemANic.SelectionChanged += (s, e) => { if (cmbMemANic.SelectedValue is string ip) { stream.MemberANicIp = ip; StateChanged?.Invoke(this); } };
            Grid.SetColumn(txtMemAHost, 0); txtMemAHost.Margin = new Thickness(0, 0, 4, 0);
            Grid.SetColumn(txtMemAPort, 1); txtMemAPort.Margin = new Thickness(4, 0, 4, 0);
            Grid.SetColumn(cmbMemANic, 2); cmbMemANic.Margin = new Thickness(4, 0, 0, 0);
            gMemA.Children.Add(txtMemAHost);
            gMemA.Children.Add(txtMemAPort);
            gMemA.Children.Add(cmbMemANic);
            sMemA.Children.Add(gMemA);
            bdMemA.Child = sMemA;
            pnlGroup.Children.Add(bdMemA);

            // Member B
            var bdMemB = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x22, 0x1E, 0x28)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x9C, 0x27, 0xB0)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 4)
            };
            var sMemB = new StackPanel();
            sMemB.Children.Add(new TextBlock
            {
                Text = "MEMBER #2: PATH B (SECONDARY REDUNDANCY / 4G/5G)",
                FontWeight = FontWeights.Bold,
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0xBA, 0x68, 0xC8)),
                Margin = new Thickness(0, 0, 0, 4)
            });
            var gMemB = new Grid();
            gMemB.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
            gMemB.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star) });
            gMemB.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
            var txtMemBHost = CreateDarkTextBox(stream.MemberBHost, v => { stream.MemberBHost = v; StateChanged?.Invoke(this); });
            var txtMemBPort = CreateDarkTextBox(stream.MemberBPort.ToString(), v => { if (int.TryParse(v.Trim(), out int p)) stream.MemberBPort = p; StateChanged?.Invoke(this); });
            var cmbMemBNic = CreateDarkComboBox();
            NetworkInterfaceScanner.PopulateNicComboBox(cmbMemBNic, stream.MemberBNicIp);
            cmbMemBNic.SelectionChanged += (s, e) => { if (cmbMemBNic.SelectedValue is string ip) { stream.MemberBNicIp = ip; StateChanged?.Invoke(this); } };
            Grid.SetColumn(txtMemBHost, 0); txtMemBHost.Margin = new Thickness(0, 0, 4, 0);
            Grid.SetColumn(txtMemBPort, 1); txtMemBPort.Margin = new Thickness(4, 0, 4, 0);
            Grid.SetColumn(cmbMemBNic, 2); cmbMemBNic.Margin = new Thickness(4, 0, 0, 0);
            gMemB.Children.Add(txtMemBHost);
            gMemB.Children.Add(txtMemBPort);
            gMemB.Children.Add(cmbMemBNic);
            sMemB.Children.Add(gMemB);
            bdMemB.Child = sMemB;
            pnlGroup.Children.Add(bdMemB);

            s2.Children.Add(pnlGroup);
            card2.Child = s2;
            stack.Children.Add(card2);

            return stack;
        }

        /// <summary>
        /// Giao diện cấu hình WebRTC Out học chuẩn từ Tab 1. WebRTC Transmit của App WEBRTC_ENCODE
        /// </summary>
        private FrameworkElement BuildWebRtcCardBody(GatewayEgressStreamConfig stream)
        {
            var stack = new StackPanel();

            // 1. CAMERA IDENTITY & BROADCAST SESSION
            var bdCam = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x14, 0x18, 0x20)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 8)
            };
            var sCam = new StackPanel();
            sCam.Children.Add(new TextBlock
            {
                Text = "CAMERA IDENTITY & BROADCAST SESSION",
                FontWeight = FontWeights.Bold,
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)),
                Margin = new Thickness(0, 0, 0, 6)
            });
            var gCam = new Grid();
            gCam.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            gCam.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            gCam.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var pCamId = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
            pCamId.Children.Add(CreateHeaderLabel("Assigned Camera ID:"));
            var cmbCamId = CreateDarkComboBox();
            for (int i = 1; i <= 10; i++)
            {
                string id = $"cam-{i:D2}";
                cmbCamId.Items.Add(new ComboBoxItem { Content = id, IsSelected = (stream.CameraId == id) });
            }
            cmbCamId.SelectionChanged += (s, e) =>
            {
                if (cmbCamId.SelectedItem is ComboBoxItem item && item.Content is string val)
                {
                    stream.CameraId = val;
                    StateChanged?.Invoke(this);
                }
            };
            pCamId.Children.Add(cmbCamId);
            Grid.SetColumn(pCamId, 0);
            gCam.Children.Add(pCamId);

            var pDisp = new StackPanel { Margin = new Thickness(6, 0, 6, 0) };
            pDisp.Children.Add(CreateHeaderLabel("Camera Display Name:"));
            var txtDisp = CreateDarkTextBox(stream.DisplayName, v => { stream.DisplayName = v; StateChanged?.Invoke(this); });
            pDisp.Children.Add(txtDisp);
            Grid.SetColumn(pDisp, 1);
            gCam.Children.Add(pDisp);

            var pRoom = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };
            pRoom.Children.Add(CreateHeaderLabel("Session / Room Name:"));
            var txtRoom = CreateDarkTextBox(stream.SessionRoom, v => { stream.SessionRoom = v; StateChanged?.Invoke(this); });
            pRoom.Children.Add(txtRoom);
            Grid.SetColumn(pRoom, 2);
            gCam.Children.Add(pRoom);
            sCam.Children.Add(gCam);
            bdCam.Child = sCam;
            stack.Children.Add(bdCam);

            // 2. SIGNALING SERVER (WEBSOCKET)
            var bdSig = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x14, 0x18, 0x20)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2D, 0x31, 0x3D)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 8)
            };
            var sSig = new StackPanel();
            sSig.Children.Add(new TextBlock
            {
                Text = "SIGNALING SERVER (WEBSOCKET)",
                FontWeight = FontWeights.Bold,
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)),
                Margin = new Thickness(0, 0, 0, 6)
            });
            var pSigUrl = new StackPanel();
            pSigUrl.Children.Add(CreateHeaderLabel("Signaling Server URL:"));
            var txtSigUrl = CreateDarkTextBox(stream.SignalingUrl, v => { stream.SignalingUrl = v; StateChanged?.Invoke(this); });
            pSigUrl.Children.Add(txtSigUrl);
            sSig.Children.Add(pSigUrl);
            bdSig.Child = sSig;
            stack.Children.Add(bdSig);

            // 3. ICE / NAT TRAVERSAL (OPTIONAL)
            var bdIce = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x14, 0x18, 0x20)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 8)
            };
            var sIce = new StackPanel();
            var gIceHead = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            gIceHead.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            gIceHead.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var tIce = new TextBlock
            {
                Text = "ICE / NAT TRAVERSAL (STUN/TURN)",
                FontWeight = FontWeights.Bold,
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(tIce, 0);
            gIceHead.Children.Add(tIce);

            var pnlIceFields = new StackPanel { Visibility = stream.EnableIce ? Visibility.Visible : Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) };
            var chkIce = new CheckBox
            {
                Content = "Bật ICE NAT Traversal",
                IsChecked = stream.EnableIce,
                FontWeight = FontWeights.SemiBold,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0xFC, 0xD3, 0x4D)),
                Cursor = Cursors.Hand
            };
            chkIce.Checked += (s, e) => { stream.EnableIce = true; pnlIceFields.Visibility = Visibility.Visible; StateChanged?.Invoke(this); };
            chkIce.Unchecked += (s, e) => { stream.EnableIce = false; pnlIceFields.Visibility = Visibility.Collapsed; StateChanged?.Invoke(this); };
            Grid.SetColumn(chkIce, 1);
            gIceHead.Children.Add(chkIce);
            sIce.Children.Add(gIceHead);

            // STUN / TURN inputs
            var gStunTurn = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            gStunTurn.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            gStunTurn.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var pStun = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
            pStun.Children.Add(CreateHeaderLabel("STUN Server:"));
            var txtStun = CreateDarkTextBox(stream.StunServer, v => { stream.StunServer = v; StateChanged?.Invoke(this); });
            pStun.Children.Add(txtStun);
            Grid.SetColumn(pStun, 0);
            gStunTurn.Children.Add(pStun);

            var pTurn = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };
            pTurn.Children.Add(CreateHeaderLabel("TURN Server:"));
            var txtTurn = CreateDarkTextBox(stream.TurnServer, v => { stream.TurnServer = v; StateChanged?.Invoke(this); });
            pTurn.Children.Add(txtTurn);
            Grid.SetColumn(pTurn, 1);
            gStunTurn.Children.Add(pTurn);
            pnlIceFields.Children.Add(gStunTurn);

            var gCreds = new Grid();
            gCreds.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            gCreds.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var pUser = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
            pUser.Children.Add(CreateHeaderLabel("TURN Username:"));
            var txtUser = CreateDarkTextBox(stream.TurnUsername, v => { stream.TurnUsername = v; StateChanged?.Invoke(this); });
            pUser.Children.Add(txtUser);
            Grid.SetColumn(pUser, 0);
            gCreds.Children.Add(pUser);

            var pPassW = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };
            pPassW.Children.Add(CreateHeaderLabel("TURN Password:"));
            var txtPassW = CreateDarkTextBox(stream.TurnPassword, v => { stream.TurnPassword = v; StateChanged?.Invoke(this); });
            pPassW.Children.Add(txtPassW);
            Grid.SetColumn(pPassW, 1);
            gCreds.Children.Add(pPassW);
            pnlIceFields.Children.Add(gCreds);

            sIce.Children.Add(pnlIceFields);
            bdIce.Child = sIce;
            stack.Children.Add(bdIce);

            // 4. SFU MEDIA SERVER INGRESS (UDP RTP)
            var bdSfu = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x14, 0x18, 0x20)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 8)
            };
            var sSfu = new StackPanel();
            sSfu.Children.Add(new TextBlock
            {
                Text = "SFU MEDIA SERVER INGRESS (UDP RTP)",
                FontWeight = FontWeights.Bold,
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)),
                Margin = new Thickness(0, 0, 0, 6)
            });

            var gSfu = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            gSfu.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
            gSfu.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            gSfu.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star) });

            var pSfuHost = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
            pSfuHost.Children.Add(CreateHeaderLabel("SFU Host IP / FQDN:"));
            var txtSfuHost = CreateDarkTextBox(stream.SfuHost, v => { stream.SfuHost = v; StateChanged?.Invoke(this); });
            pSfuHost.Children.Add(txtSfuHost);
            Grid.SetColumn(pSfuHost, 0);
            gSfu.Children.Add(pSfuHost);

            var pModeRtp = new StackPanel { Margin = new Thickness(4, 0, 4, 0) };
            pModeRtp.Children.Add(CreateHeaderLabel("RTP Port Mode:"));
            var cmbPortMode = CreateDarkComboBox();
            cmbPortMode.Items.Add(new ComboBoxItem { Content = "2 Ports (Split A/V)" });
            cmbPortMode.Items.Add(new ComboBoxItem { Content = "1 Port (Muxed BUNDLE)" });
            cmbPortMode.SelectedIndex = Math.Clamp(stream.PortModeIndex, 0, 1);
            cmbPortMode.SelectionChanged += (s, e) => { stream.PortModeIndex = cmbPortMode.SelectedIndex; StateChanged?.Invoke(this); };
            pModeRtp.Children.Add(cmbPortMode);
            Grid.SetColumn(pModeRtp, 1);
            gSfu.Children.Add(pModeRtp);

            var pWrtPort = new StackPanel { Margin = new Thickness(4, 0, 0, 0) };
            pWrtPort.Children.Add(CreateHeaderLabel("Port Ingress:"));
            var txtWrtPort = CreateDarkTextBox(stream.WebRtcPort.ToString(), v =>
            {
                if (int.TryParse(v.Trim(), out int p)) stream.WebRtcPort = p;
                StateChanged?.Invoke(this);
            });
            pWrtPort.Children.Add(txtWrtPort);
            Grid.SetColumn(pWrtPort, 2);
            gSfu.Children.Add(pWrtPort);
            sSfu.Children.Add(gSfu);

            var chkEcho = new CheckBox
            {
                Content = "Bật WebRTC 2.0 Echo Guard (Chống vòng lặp phản hồi âm thanh / Loopback)",
                IsChecked = stream.AntiEchoGuard,
                Foreground = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)),
                FontWeight = FontWeights.SemiBold,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0),
                Cursor = Cursors.Hand
            };
            chkEcho.Checked += (s, e) => { stream.AntiEchoGuard = true; StateChanged?.Invoke(this); };
            chkEcho.Unchecked += (s, e) => { stream.AntiEchoGuard = false; StateChanged?.Invoke(this); };
            sSfu.Children.Add(chkEcho);
            bdSfu.Child = sSfu;
            stack.Children.Add(bdSfu);

            // 5. VIDEO CODEC & HARDWARE ACCELERATION
            var bdCodec = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x14, 0x18, 0x20)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 4)
            };
            var sCodec = new StackPanel();
            sCodec.Children.Add(new TextBlock
            {
                Text = "VIDEO CODEC & BITRATE",
                FontWeight = FontWeights.Bold,
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)),
                Margin = new Thickness(0, 0, 0, 6)
            });

            var gCodec = new Grid();
            gCodec.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
            gCodec.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var pCodecChoice = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
            pCodecChoice.Children.Add(CreateHeaderLabel("WebRTC Video Codec:"));
            var cmbCodec = CreateDarkComboBox();
            cmbCodec.Items.Add(new ComboBoxItem { Content = "H.264 / AVC (Payload Type 96)" });
            cmbCodec.Items.Add(new ComboBoxItem { Content = "H.265 / HEVC (Payload Type 97)" });
            cmbCodec.Items.Add(new ComboBoxItem { Content = "⚡ Passthrough (Copy Source NALs)" });
            cmbCodec.SelectedIndex = stream.VideoCodec.Contains("265") ? 1 : (stream.VideoCodec.Contains("Passthrough") ? 2 : 0);
            cmbCodec.SelectionChanged += (s, e) =>
            {
                if (cmbCodec.SelectedItem is ComboBoxItem item && item.Content is string val)
                {
                    stream.VideoCodec = val;
                    StateChanged?.Invoke(this);
                }
            };
            pCodecChoice.Children.Add(cmbCodec);
            Grid.SetColumn(pCodecChoice, 0);
            gCodec.Children.Add(pCodecChoice);

            var pBitrate = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };
            pBitrate.Children.Add(CreateHeaderLabel("Target Bitrate (kbps):"));
            var txtBitrate = CreateDarkTextBox(stream.TargetBitrateKbps.ToString(), v =>
            {
                if (int.TryParse(v.Trim(), out int b)) stream.TargetBitrateKbps = b;
                StateChanged?.Invoke(this);
            });
            pBitrate.Children.Add(txtBitrate);
            Grid.SetColumn(pBitrate, 1);
            gCodec.Children.Add(pBitrate);
            sCodec.Children.Add(gCodec);
            bdCodec.Child = sCodec;
            stack.Children.Add(bdCodec);

            return stack;
        }

        private FrameworkElement BuildRtmpCardBody(GatewayEgressStreamConfig stream)
        {
            var stack = new StackPanel();
            var g = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.8, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });

            var pUrl = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
            pUrl.Children.Add(CreateHeaderLabel("RTMP Server URL:"));
            var txtUrl = CreateDarkTextBox(stream.RtmpUrl, v => { stream.RtmpUrl = v; StateChanged?.Invoke(this); });
            pUrl.Children.Add(txtUrl);
            Grid.SetColumn(pUrl, 0);
            g.Children.Add(pUrl);

            var pKey = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };
            pKey.Children.Add(CreateHeaderLabel("Stream Key:"));
            var txtKey = CreateDarkTextBox(stream.RtmpStreamKey, v => { stream.RtmpStreamKey = v; StateChanged?.Invoke(this); });
            pKey.Children.Add(txtKey);
            Grid.SetColumn(pKey, 1);
            g.Children.Add(pKey);

            stack.Children.Add(g);
            return stack;
        }

        private FrameworkElement BuildRtspCardBody(GatewayEgressStreamConfig stream)
        {
            var stack = new StackPanel();
            var g = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });

            var pPort = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
            pPort.Children.Add(CreateHeaderLabel("RTSP Port:"));
            var txtPort = CreateDarkTextBox(stream.RtspPort.ToString(), v =>
            {
                if (int.TryParse(v.Trim(), out int p)) stream.RtspPort = p;
                StateChanged?.Invoke(this);
            });
            pPort.Children.Add(txtPort);
            Grid.SetColumn(pPort, 0);
            g.Children.Add(pPort);

            var pPath = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };
            pPath.Children.Add(CreateHeaderLabel("Mount Path:"));
            var txtPath = CreateDarkTextBox(stream.RtspPath, v => { stream.RtspPath = v; StateChanged?.Invoke(this); });
            pPath.Children.Add(txtPath);
            Grid.SetColumn(pPath, 1);
            g.Children.Add(pPath);

            stack.Children.Add(g);
            return stack;
        }

        private FrameworkElement BuildLrtCardBody(GatewayEgressStreamConfig stream)
        {
            var stack = new StackPanel();
            var g = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Path 1
            var p1 = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
            p1.Children.Add(CreateHeaderLabel("Bonding Path #1 (IP:Port):"));
            var g1 = new Grid();
            g1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            g1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var txtH1 = CreateDarkTextBox(stream.LrtPath1Host, v => { stream.LrtPath1Host = v; StateChanged?.Invoke(this); });
            var txtP1 = CreateDarkTextBox(stream.LrtPath1Port.ToString(), v => { if (int.TryParse(v.Trim(), out int p)) stream.LrtPath1Port = p; StateChanged?.Invoke(this); });
            Grid.SetColumn(txtH1, 0); txtH1.Margin = new Thickness(0, 0, 4, 0);
            Grid.SetColumn(txtP1, 1);
            g1.Children.Add(txtH1);
            g1.Children.Add(txtP1);
            p1.Children.Add(g1);
            Grid.SetColumn(p1, 0);
            g.Children.Add(p1);

            // Path 2
            var p2 = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };
            p2.Children.Add(CreateHeaderLabel("Bonding Path #2 (IP:Port):"));
            var g2 = new Grid();
            g2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            g2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var txtH2 = CreateDarkTextBox(stream.LrtPath2Host, v => { stream.LrtPath2Host = v; StateChanged?.Invoke(this); });
            var txtP2 = CreateDarkTextBox(stream.LrtPath2Port.ToString(), v => { if (int.TryParse(v.Trim(), out int p)) stream.LrtPath2Port = p; StateChanged?.Invoke(this); });
            Grid.SetColumn(txtH2, 0); txtH2.Margin = new Thickness(0, 0, 4, 0);
            Grid.SetColumn(txtP2, 1);
            g2.Children.Add(txtH2);
            g2.Children.Add(txtP2);
            p2.Children.Add(g2);
            Grid.SetColumn(p2, 1);
            g.Children.Add(p2);

            stack.Children.Add(g);
            return stack;
        }

        private FrameworkElement BuildHlsCardBody(GatewayEgressStreamConfig stream)
        {
            var stack = new StackPanel();
            var g = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var pPort = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
            pPort.Children.Add(CreateHeaderLabel("HTTP Streaming Port:"));
            var txtPort = CreateDarkTextBox(stream.HlsPort.ToString(), v =>
            {
                if (int.TryParse(v.Trim(), out int p)) stream.HlsPort = p;
                StateChanged?.Invoke(this);
            });
            pPort.Children.Add(txtPort);
            Grid.SetColumn(pPort, 0);
            g.Children.Add(pPort);

            var pDur = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };
            pDur.Children.Add(CreateHeaderLabel("Segment Duration (Giây):"));
            var txtDur = CreateDarkTextBox(stream.HlsSegmentDurationSec.ToString(), v =>
            {
                if (int.TryParse(v.Trim(), out int d)) stream.HlsSegmentDurationSec = d;
                StateChanged?.Invoke(this);
            });
            pDur.Children.Add(txtDur);
            Grid.SetColumn(pDur, 1);
            g.Children.Add(pDur);

            stack.Children.Add(g);
            return stack;
        }

        private FrameworkElement BuildDashCardBody(GatewayEgressStreamConfig stream)
        {
            var stack = new StackPanel();
            var pPort = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
            pPort.Children.Add(CreateHeaderLabel("HTTP Streaming Port:"));
            var txtPort = CreateDarkTextBox(stream.DashPort.ToString(), v =>
            {
                if (int.TryParse(v.Trim(), out int p)) stream.DashPort = p;
                StateChanged?.Invoke(this);
            });
            pPort.Children.Add(txtPort);
            stack.Children.Add(pPort);
            return stack;
        }

        #endregion

        #region UI Creation Helpers

        private static TextBox CreateDarkTextBox(string text, Action<string> onTextChanged)
        {
            var tb = new TextBox
            {
                Text = text,
                Background = new SolidColorBrush(Color.FromRgb(0x22, 0x25, 0x2D)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x3B, 0x42, 0x52)),
                BorderThickness = new Thickness(1),
                Height = 28,
                Padding = new Thickness(6, 2, 6, 2),
                VerticalContentAlignment = VerticalAlignment.Center,
                FontSize = 11.5
            };
            tb.TextChanged += (s, e) => onTextChanged(tb.Text);
            return tb;
        }

        private static ComboBox CreateDarkComboBox()
        {
            return new ComboBox
            {
                Background = new SolidColorBrush(Color.FromRgb(0x22, 0x25, 0x2D)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x3B, 0x42, 0x52)),
                BorderThickness = new Thickness(1),
                Height = 28,
                FontSize = 11.5
            };
        }

        private static TextBlock CreateHeaderLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontWeight = FontWeights.SemiBold,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E)),
                Margin = new Thickness(0, 0, 0, 3)
            };
        }

        #endregion

        #endregion

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _videoRenderTimer.Stop();
            Stop();
        }
    }
}
