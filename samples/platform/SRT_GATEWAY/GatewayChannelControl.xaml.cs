using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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

            // Ingest
            CmbIngestMode.SelectedIndex = Math.Clamp(_config.SrtModeIndex, 0, 2);
            TxtIngestHost.Text = _config.IngestHost;
            TxtIngestPort.Text = _config.IngestPort.ToString();
            TxtIngestStreamId.Text = _config.StreamId;
            TxtIngestLatency.Text = _config.LatencyMs.ToString();
            TxtIngestPassphrase.Password = _config.Passphrase;

            ChkSmpteIngest.IsChecked = _config.IsSmpte2022_7IngestEnabled;
            PnlSmpteIngestConfig.Visibility = _config.IsSmpte2022_7IngestEnabled ? Visibility.Visible : Visibility.Collapsed;
            BdSmpteBadge.Visibility = _config.IsSmpte2022_7IngestEnabled ? Visibility.Visible : Visibility.Collapsed;
            TxtMemberAPort.Text = _config.MemberAPort.ToString();
            TxtMemberBPort.Text = _config.MemberBPort.ToString();
            SldDiffDelay.Value = _config.DifferentialDelayMs;
            TxtDiffDelayDisplay.Text = $"{_config.DifferentialDelayMs} ms";

            // Egress
            ChkSrtOut.IsChecked = _config.SrtOutEnabled;
            CmbSrtOutMode.SelectedIndex = Math.Clamp(_config.SrtOutModeIndex, 0, 1);
            TxtSrtOutHost.Text = _config.SrtOutHost;
            TxtSrtOutPort.Text = _config.SrtOutPort.ToString();

            ChkWebRtcOut.IsChecked = _config.WebRtcOutEnabled;
            TxtWebRtcPort.Text = _config.WebRtcPort.ToString();
            TxtWebRtcUrl.Text = $"http://127.0.0.1:{_config.WebRtcPort}/webrtc";

            ChkRtmpOut.IsChecked = _config.RtmpOutEnabled;
            TxtRtmpUrl.Text = _config.RtmpUrl;
            TxtRtmpKey.Text = _config.RtmpStreamKey;

            ChkRtspOut.IsChecked = _config.RtspOutEnabled;
            TxtRtspPort.Text = _config.RtspPort.ToString();
            TxtRtspEndpoint.Text = $"rtsp://0.0.0.0:{_config.RtspPort}{_config.RtspPath}";

            ChkLrtOut.IsChecked = _config.LrtOutEnabled;
            TxtLrtPath1.Text = $"{_config.LrtPath1Host}:{_config.LrtPath1Port}";
            TxtLrtPath2.Text = $"{_config.LrtPath2Host}:{_config.LrtPath2Port}";

            ChkHlsOut.IsChecked = _config.HlsOutEnabled;
            ChkDashOut.IsChecked = _config.DashOutEnabled;
            TxtHlsPort.Text = _config.HlsPort.ToString();
            TxtHlsUrl.Text = $"http://127.0.0.1:{_config.HlsPort}/live.m3u8";

            UpdateOsdHeader();
            UpdateEgressChips();
        }

        public void ReadUIToConfig()
        {
            if (!_isInitialized || _config == null || TxtChannelTitle == null || CmbIngestMode == null || TxtIngestHost == null) return;

            _config.ChannelName = TxtChannelTitle.Text.Trim();
            _config.SrtModeIndex = CmbIngestMode.SelectedIndex;
            _config.IngestHost = TxtIngestHost.Text.Trim();
            if (int.TryParse(TxtIngestPort.Text.Trim(), out int port)) _config.IngestPort = port;
            _config.StreamId = TxtIngestStreamId.Text.Trim();
            if (int.TryParse(TxtIngestLatency.Text.Trim(), out int lat)) _config.LatencyMs = lat;
            _config.Passphrase = TxtIngestPassphrase.Password;

            _config.IsSmpte2022_7IngestEnabled = ChkSmpteIngest.IsChecked == true;
            if (int.TryParse(TxtMemberAPort.Text.Trim(), out int mA)) _config.MemberAPort = mA;
            if (int.TryParse(TxtMemberBPort.Text.Trim(), out int mB)) _config.MemberBPort = mB;
            _config.DifferentialDelayMs = (int)SldDiffDelay.Value;

            // Egress
            _config.SrtOutEnabled = ChkSrtOut.IsChecked == true;
            _config.SrtOutModeIndex = CmbSrtOutMode.SelectedIndex;
            _config.SrtOutHost = TxtSrtOutHost.Text.Trim();
            if (int.TryParse(TxtSrtOutPort.Text.Trim(), out int srtPort)) _config.SrtOutPort = srtPort;

            _config.WebRtcOutEnabled = ChkWebRtcOut.IsChecked == true;
            if (int.TryParse(TxtWebRtcPort.Text.Trim(), out int wrtcPort)) _config.WebRtcPort = wrtcPort;

            _config.RtmpOutEnabled = ChkRtmpOut.IsChecked == true;
            _config.RtmpUrl = TxtRtmpUrl.Text.Trim();
            _config.RtmpStreamKey = TxtRtmpKey.Text.Trim();

            _config.RtspOutEnabled = ChkRtspOut.IsChecked == true;
            if (int.TryParse(TxtRtspPort.Text.Trim(), out int rtspPort)) _config.RtspPort = rtspPort;

            _config.LrtOutEnabled = ChkLrtOut.IsChecked == true;

            _config.HlsOutEnabled = ChkHlsOut.IsChecked == true;
            _config.DashOutEnabled = ChkDashOut.IsChecked == true;
            if (int.TryParse(TxtHlsPort.Text.Trim(), out int hlsPort))
            {
                _config.HlsPort = hlsPort;
                _config.DashPort = hlsPort;
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
            int active = 0;
            if (ChkSrtOut.IsChecked == true) active++;
            if (ChkWebRtcOut.IsChecked == true) active++;
            if (ChkRtmpOut.IsChecked == true) active++;
            if (ChkRtspOut.IsChecked == true) active++;
            if (ChkLrtOut.IsChecked == true) active++;
            if (ChkHlsOut.IsChecked == true) active++;
            if (ChkDashOut.IsChecked == true) active++;

            TxtActiveEgressCount.Text = $"EGRESS: {active}/7 ON";
            TxtActiveEgressCount.Foreground = active > 0 ? new SolidColorBrush(Color.FromRgb(0, 230, 118)) : new SolidColorBrush(Color.FromRgb(158, 158, 158));
        }

        public async Task<bool> StartAsync()
        {
            if (_engine != null) return true;

            ReadUIToConfig();
            _config.IsStarted = true;
            BtnChannelStart.IsEnabled = false;
            BtnChannelStop.IsEnabled = true;

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

                // Egress labels
                TxtSrtOutStatus.Text = _engine.SrtOutStatus.IsActive ? $" [LIVE: {_engine.SrtOutStatus.BitrateKbps:F0} kbps]" : " [OFF]";
                TxtWebRtcStatus.Text = _engine.WebRtcStatus.IsActive ? $" [LIVE: {_engine.WebRtcStatus.BitrateKbps:F0} kbps]" : " [OFF]";
                TxtRtmpStatus.Text = _engine.RtmpStatus.IsActive ? $" [LIVE: {_engine.RtmpStatus.BitrateKbps:F0} kbps]" : " [OFF]";
                TxtRtspStatus.Text = _engine.RtspStatus.IsActive ? $" [LIVE: {_engine.RtspStatus.BitrateKbps:F0} kbps]" : " [OFF]";
                TxtLrtStatus.Text = _engine.LrtStatus.IsActive ? $" [LIVE: {_engine.LrtStatus.BitrateKbps:F0} kbps]" : " [OFF]";
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

        private void Passphrase_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            Input_Changed(sender, e);
        }

        private void ChkSmpteIngest_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            bool isSmpte = ChkSmpteIngest.IsChecked == true;
            if (PnlSmpteIngestConfig != null) PnlSmpteIngestConfig.Visibility = isSmpte ? Visibility.Visible : Visibility.Collapsed;
            if (BdSmpteBadge != null) BdSmpteBadge.Visibility = isSmpte ? Visibility.Visible : Visibility.Collapsed;
            Input_Changed(sender, e);
        }

        private void SldDiffDelay_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized) return;
            if (TxtDiffDelayDisplay != null)
            {
                TxtDiffDelayDisplay.Text = $"{(int)SldDiffDelay.Value} ms";
            }
            Input_Changed(sender, e);
        }

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
