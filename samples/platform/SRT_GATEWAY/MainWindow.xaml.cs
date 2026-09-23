using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Media;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using OpenMedia.Platform.Telemetry;

namespace SRT_GATEWAY
{
    public partial class MainWindow : Window
    {
        private GatewaySettings _settings;
        private readonly List<GatewayChannelControl> _channels = new();
        private TelemetryReceiverServer? _monitorServer;
        private readonly DispatcherTimer _clockTimer;
        private readonly DispatcherTimer _footerTimer;
        private bool _isInitialized = false;

        private readonly ConcurrentDictionary<string, Border> _cardControls = new();

        public MainWindow()
        {
            InitializeComponent();
            _settings = AppSettingsManager.LoadSettings();

            _clockTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _clockTimer.Tick += (s, e) =>
            {
                TxtMasterClock.Text = $"{DateTime.UtcNow:HH:mm:ss} UTC";
            };
            _clockTimer.Start();

            _footerTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _footerTimer.Tick += (s, e) => UpdateFooterSummary();
            _footerTimer.Start();

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeLayoutMode();
            InitializeChannels();

            _isInitialized = true;

            // Tự động khởi chạy Monitor server nếu được cấu hình bật
            if (_settings.IsMonitorServerEnabled)
            {
                StartMonitorServer();
            }
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            SaveCurrentSettings();

            foreach (var ch in _channels)
            {
                ch.Dispose();
            }
            _channels.Clear();

            _monitorServer?.Stop();
            _monitorServer?.Dispose();
        }

        #region Channel Multiview Management

        private void InitializeLayoutMode()
        {
            switch (_settings.MultiviewLayoutMode)
            {
                case 1:
                    RbLayout1x1.IsChecked = true;
                    break;
                case 2:
                    RbLayout1x2.IsChecked = true;
                    break;
                case 3:
                    RbLayout2x2.IsChecked = true;
                    break;
                default:
                    RbLayoutAuto.IsChecked = true;
                    break;
            }
        }

        private void InitializeChannels()
        {
            MultiviewGrid.Children.Clear();
            _channels.Clear();

            if (_settings.Channels == null || _settings.Channels.Count == 0)
            {
                _settings.Channels = new List<GatewayChannelConfig>
                {
                    AppSettingsManager.CreateDefaultChannel(1, _settings)
                };
            }

            foreach (var config in _settings.Channels)
            {
                AddChannelControl(config);
            }

            ApplyMultiviewLayout();
        }

        private GatewayChannelControl AddChannelControl(GatewayChannelConfig config)
        {
            var ch = new GatewayChannelControl();
            ch.BindConfig(config);

            ch.RemoveRequested += OnChannelRemoveRequested;
            ch.StateChanged += OnChannelStateChanged;

            _channels.Add(ch);
            return ch;
        }

        public void AddNewChannel()
        {
            int nextId = _channels.Count + 1;
            var newConfig = AppSettingsManager.CreateDefaultChannel(nextId, _settings);

            AddChannelControl(newConfig);
            ApplyMultiviewLayout();
            SaveCurrentSettings();
        }

        public void RemoveChannel(GatewayChannelControl ch)
        {
            if (_channels.Count <= 1)
            {
                MessageBox.Show("Cần giữ lại tối thiểu 1 luồng trong Multiview Gateway.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            ch.RemoveRequested -= OnChannelRemoveRequested;
            ch.StateChanged -= OnChannelStateChanged;
            ch.Dispose();

            _channels.Remove(ch);

            // Cập nhật lại số thứ tự ChannelId cho đồng bộ
            for (int i = 0; i < _channels.Count; i++)
            {
                _channels[i].Config.ChannelId = i + 1;
            }

            ApplyMultiviewLayout();
            SaveCurrentSettings();
        }

        private void OnChannelRemoveRequested(GatewayChannelControl ch)
        {
            RemoveChannel(ch);
        }

        private void OnChannelStateChanged(GatewayChannelControl ch)
        {
            SaveCurrentSettings();
            UpdateFooterSummary();
        }

        private void ApplyMultiviewLayout()
        {
            if (MultiviewGrid == null) return;

            MultiviewGrid.Children.Clear();
            MultiviewGrid.RowDefinitions.Clear();
            MultiviewGrid.ColumnDefinitions.Clear();

            int count = _channels.Count;
            if (count == 0) return;

            int cols = 1;
            int rows = 1;

            if (RbLayout1x1.IsChecked == true)
            {
                cols = 1;
                rows = count;
            }
            else if (RbLayout1x2.IsChecked == true)
            {
                cols = 2;
                rows = Math.Max(1, (count + 1) / 2);
            }
            else if (RbLayout2x2.IsChecked == true)
            {
                cols = 2;
                rows = Math.Max(2, (count + 1) / 2);
            }
            else // Auto Grid
            {
                if (count <= 1)
                {
                    cols = 1;
                    rows = 1;
                }
                else if (count == 2)
                {
                    cols = 2;
                    rows = 1;
                }
                else if (count <= 4)
                {
                    cols = 2;
                    rows = (count + 1) / 2;
                }
                else if (count <= 6)
                {
                    cols = 3;
                    rows = (count + 2) / 3;
                }
                else
                {
                    cols = 4;
                    rows = (count + 3) / 4;
                }
            }

            for (int c = 0; c < cols; c++)
            {
                MultiviewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            for (int r = 0; r < rows; r++)
            {
                MultiviewGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            for (int i = 0; i < count; i++)
            {
                var chControl = _channels[i];
                int r = i / cols;
                int c = i % cols;

                Grid.SetRow(chControl, r);
                Grid.SetColumn(chControl, c);

                MultiviewGrid.Children.Add(chControl);
            }

            TxtMultiviewChannelCount.Text = $"LUỒNG HOẠT ĐỘNG: {count}";
            UpdateFooterSummary();
        }

        private void RbLayout_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            if (RbLayout1x1.IsChecked == true) _settings.MultiviewLayoutMode = 1;
            else if (RbLayout1x2.IsChecked == true) _settings.MultiviewLayoutMode = 2;
            else if (RbLayout2x2.IsChecked == true) _settings.MultiviewLayoutMode = 3;
            else _settings.MultiviewLayoutMode = 0;

            ApplyMultiviewLayout();
            SaveCurrentSettings();
        }

        private void BtnAddChannel_Click(object sender, RoutedEventArgs e)
        {
            AddNewChannel();
        }

        private void BtnRemoveChannel_Click(object sender, RoutedEventArgs e)
        {
            if (_channels.Count > 1)
            {
                RemoveChannel(_channels.Last());
            }
            else
            {
                MessageBox.Show("Cần giữ lại tối thiểu 1 luồng trong Multiview Gateway.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void BtnStartAllChannels_Click(object sender, RoutedEventArgs e)
        {
            BtnMasterStart.IsEnabled = false;
            BtnMasterStop.IsEnabled = true;

            foreach (var ch in _channels)
            {
                if (!ch.IsRunning)
                {
                    await ch.StartAsync();
                }
            }

            UpdateFooterSummary();
        }

        private void BtnStopAllChannels_Click(object sender, RoutedEventArgs e)
        {
            BtnMasterStart.IsEnabled = true;
            BtnMasterStop.IsEnabled = false;

            foreach (var ch in _channels)
            {
                if (ch.IsRunning)
                {
                    ch.Stop();
                }
            }

            UpdateFooterSummary();
        }

        private void UpdateFooterSummary()
        {
            int runningCount = _channels.Count(c => c.IsRunning);
            int totalEgress = 0;
            bool hasSmpte = false;

            foreach (var ch in _channels)
            {
                if (ch.Config.IsSmpte2022_7IngestEnabled) hasSmpte = true;
                if (ch.Config.SrtOutEnabled) totalEgress++;
                if (ch.Config.WebRtcOutEnabled) totalEgress++;
                if (ch.Config.RtmpOutEnabled) totalEgress++;
                if (ch.Config.RtspOutEnabled) totalEgress++;
                if (ch.Config.LrtOutEnabled) totalEgress++;
                if (ch.Config.HlsOutEnabled) totalEgress++;
                if (ch.Config.DashOutEnabled) totalEgress++;
            }

            TxtFooterIngestSummary.Text = runningCount > 0
                ? $"INGEST: {runningCount}/{_channels.Count} streams LIVE"
                : $"INGEST: {_channels.Count} streams STANDBY";

            TxtFooterEgressSummary.Text = $"EGRESS: {totalEgress} active targets";

            TxtFooterSmpteStatus.Text = hasSmpte ? "ARMED (A/B)" : "OFF";
            TxtFooterSmpteStatus.Foreground = hasSmpte ? new SolidColorBrush(Color.FromRgb(0, 229, 255)) : new SolidColorBrush(Color.FromRgb(136, 136, 136));

            if (runningCount > 0)
            {
                LedIngestSignal.Fill = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Green
                TxtHeaderIngestStatus.Text = $"INGEST: {runningCount} LIVE";
                TxtHeaderIngestStatus.Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80));
            }
            else
            {
                LedIngestSignal.Fill = new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Amber
                TxtHeaderIngestStatus.Text = "INGEST: STANDBY";
                TxtHeaderIngestStatus.Foreground = new SolidColorBrush(Color.FromRgb(245, 158, 11));
            }
        }

        #endregion

        #region Settings Save

        private void SaveCurrentSettings()
        {
            try
            {
                _settings.Channels.Clear();
                foreach (var ch in _channels)
                {
                    ch.ReadUIToConfig();
                    _settings.Channels.Add(ch.Config);
                }

                _settings.IsMonitorServerEnabled = ChkEnableMonitorServer.IsChecked == true;
                if (int.TryParse(TxtMonitorPort.Text.Trim(), out int port)) _settings.MonitorPort = port;
                _settings.BeeperAlertEnabled = ChkBeeperAlert.IsChecked == true;

                AppSettingsManager.SaveSettings(_settings);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] Save settings failed: {ex.Message}");
            }
        }

        #endregion

        #region Navigation Switcher

        private void TabBtn_Checked(object sender, RoutedEventArgs e)
        {
            if (PnlMediaGatewayView == null || PnlNocMonitorView == null) return;

            if (TabBtnRouting.IsChecked == true)
            {
                PnlMediaGatewayView.Visibility = Visibility.Visible;
                PnlNocMonitorView.Visibility = Visibility.Collapsed;
                TabBtnRouting.Foreground = Brushes.White;
                TabBtnMonitor.Foreground = new SolidColorBrush(Color.FromRgb(204, 204, 204));
            }
            else
            {
                PnlMediaGatewayView.Visibility = Visibility.Collapsed;
                PnlNocMonitorView.Visibility = Visibility.Visible;
                TabBtnMonitor.Foreground = Brushes.White;
                TabBtnRouting.Foreground = new SolidColorBrush(Color.FromRgb(204, 204, 204));
            }
        }

        #endregion

        #region Master Engine Controls

        private void BtnMasterStart_Click(object sender, RoutedEventArgs e)
        {
            BtnStartAllChannels_Click(sender, e);
        }

        private void BtnMasterStop_Click(object sender, RoutedEventArgs e)
        {
            BtnStopAllChannels_Click(sender, e);
        }

        #endregion

        #region NOC Telemetry Monitor Server Integration

        private void StartMonitorServer()
        {
            _monitorServer?.Stop();
            _monitorServer?.Dispose();

            _monitorServer = new TelemetryReceiverServer();
            _monitorServer.NodeUpdated += OnMonitorNodeUpdated;
            _monitorServer.NodeOffline += OnMonitorNodeOffline;
            _monitorServer.LogEmitted += OnMonitorLogEmitted;
            _monitorServer.AlertTriggered += OnMonitorAlertTriggered;

            int port = int.TryParse(TxtMonitorPort?.Text, out int p) ? p : 8088;
            bool ok = _monitorServer.Start(port);
            if (ok)
            {
                UpdateMonitorClientUrlText(port);
            }
        }

        private void StopMonitorServer()
        {
            _monitorServer?.Stop();
            _monitorServer?.Dispose();
            _monitorServer = null;
        }

        private void ChkEnableMonitorServer_Changed(object sender, RoutedEventArgs e)
        {
            if (ChkEnableMonitorServer.IsChecked == true)
            {
                StartMonitorServer();
            }
            else
            {
                StopMonitorServer();
            }
            if (_isInitialized) SaveCurrentSettings();
        }

        private void TxtMonitorPort_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitialized) return;
            if (int.TryParse(TxtMonitorPort.Text, out int port))
            {
                UpdateMonitorClientUrlText(port);
                SaveCurrentSettings();
            }
        }

        private void UpdateMonitorClientUrlText(int port)
        {
            string localIp = GetLocalIpAddress();
            if (TxtMonitorClientUrl != null)
            {
                TxtMonitorClientUrl.Text = $"http://{localIp}:{port}/api/telemetry";
            }
        }

        private static string GetLocalIpAddress()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                    {
                        return ip.ToString();
                    }
                }
            }
            catch { }
            return "127.0.0.1";
        }

        private void BtnCopyMonitorUrl_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(TxtMonitorClientUrl.Text);
            MessageBox.Show($"Đã copy Endpoint Telemetry:\n{TxtMonitorClientUrl.Text}\n\nNhập URL này vào ô Server URL trên các máy SRT_ENCODE hoặc SRT_DECODE.", "SRT Monitor", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ChkBeeperAlert_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitialized) SaveCurrentSettings();
        }

        private void OnMonitorAlertTriggered(string nodeName, string message)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (ChkBeeperAlert?.IsChecked == true)
                {
                    try { SystemSounds.Beep.Play(); } catch { }
                }
            });
        }

        private void OnMonitorLogEmitted(TelemetryAuditLogItem item)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (GridAuditLogs != null && _monitorServer != null)
                {
                    GridAuditLogs.ItemsSource = _monitorServer.AuditLogs;
                    if (TxtLogCount != null)
                    {
                        TxtLogCount.Text = $"({_monitorServer.AuditLogs.Count} records)";
                    }
                }
            });
        }

        private void OnMonitorNodeOffline(TelemetryNodeState state)
        {
            Dispatcher.BeginInvoke(() =>
            {
                UpdateOrAddNodeCard(state);
            });
        }

        private void OnMonitorNodeUpdated(TelemetryNodeState state)
        {
            Dispatcher.BeginInvoke(() =>
            {
                UpdateOrAddNodeCard(state);
            });
        }

        private void UpdateOrAddNodeCard(TelemetryNodeState state)
        {
            if (!_cardControls.TryGetValue(state.NodeName, out var cardBorder))
            {
                cardBorder = CreateNodeCardControl(state);
                _cardControls[state.NodeName] = cardBorder;
                WrapNodeCards.Children.Add(cardBorder);
            }
            else
            {
                RefreshNodeCardContent(cardBorder, state);
            }

            TxtActiveNodeCount.Text = $"({_cardControls.Count(c => ((TelemetryNodeState)c.Value.Tag).Status != NodeHealthStatus.Offline)} Active / {_cardControls.Count} Total)";
        }

        private Border CreateNodeCardControl(TelemetryNodeState state)
        {
            var card = new Border
            {
                Style = (Style)FindResource("DarkCardBorderStyle"),
                Width = 400,
                Margin = new Thickness(0, 0, 10, 10),
                Tag = state
            };

            RefreshNodeCardContent(card, state);
            return card;
        }

        private void RefreshNodeCardContent(Border card, TelemetryNodeState state)
        {
            card.Tag = state;
            var packet = state.LatestPacket;

            Brush borderBrush = state.Status switch
            {
                NodeHealthStatus.Live => new SolidColorBrush(Color.FromRgb(76, 175, 80)),      // Green
                NodeHealthStatus.Warning => new SolidColorBrush(Color.FromRgb(255, 193, 7)),   // Yellow
                NodeHealthStatus.Critical => new SolidColorBrush(Color.FromRgb(244, 67, 54)),  // Red
                _ => new SolidColorBrush(Color.FromRgb(85, 85, 95))                            // Gray Offline
            };
            card.BorderBrush = borderBrush;
            card.BorderThickness = new Thickness(1.5);

            string statusText = state.Status switch
            {
                NodeHealthStatus.Live => "🟢 LIVE",
                NodeHealthStatus.Warning => "🟡 WARNING",
                NodeHealthStatus.Critical => "🔴 CRITICAL",
                _ => "⚫ OFFLINE"
            };

            var stack = new StackPanel();

            // 1. Header Card: Node Name, Type, Status Badge
            var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titlePanel = new StackPanel { Orientation = Orientation.Horizontal };
            titlePanel.Children.Add(new TextBlock
            {
                Text = state.NodeName,
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            });

            // Type Badge
            var typeBadge = new Border
            {
                Background = state.NodeType.Equals("Encoder", StringComparison.OrdinalIgnoreCase)
                    ? new SolidColorBrush(Color.FromRgb(156, 39, 176))
                    : new SolidColorBrush(Color.FromRgb(0, 150, 136)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 1, 5, 1),
                VerticalAlignment = VerticalAlignment.Center
            };
            typeBadge.Child = new TextBlock
            {
                Text = state.NodeType.ToUpperInvariant(),
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            };
            titlePanel.Children.Add(typeBadge);
            Grid.SetColumn(titlePanel, 0);
            headerGrid.Children.Add(titlePanel);

            // Status Badge
            var statusBlock = new TextBlock
            {
                Text = statusText,
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                Foreground = borderBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(statusBlock, 1);
            headerGrid.Children.Add(statusBlock);
            stack.Children.Add(headerGrid);

            // 2. Metrics Grid: Bitrate, FPS, RTT, Loss
            var metricsGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            for (int i = 0; i < 4; i++) metricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            AddMetricColumn(metricsGrid, 0, "BITRATE", packet.BitrateKbps > 1000 ? $"{(packet.BitrateKbps / 1000.0):F1}M" : $"{(int)packet.BitrateKbps}k", Color.FromRgb(0, 230, 118));
            AddMetricColumn(metricsGrid, 1, "FPS", $"{packet.Fps:F1}", Color.FromRgb(0, 230, 118));
            AddMetricColumn(metricsGrid, 2, "RTT", $"{(int)packet.RttMs}ms", packet.RttMs > 150 ? Color.FromRgb(244, 67, 54) : Color.FromRgb(0, 255, 204));
            AddMetricColumn(metricsGrid, 3, "LOSS", $"{packet.LossPercent:F1}%", packet.LossPercent > 2 ? Color.FromRgb(244, 67, 54) : Color.FromRgb(240, 240, 240));
            stack.Children.Add(metricsGrid);

            // 3. SMPTE 2022-7 Dual-Path Status (if active)
            if (packet.IsSmpte2022_7Active)
            {
                var smpteGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                var smptePanel = new StackPanel { Orientation = Orientation.Horizontal };
                smptePanel.Children.Add(new TextBlock { Text = "SMPTE 2022-7:", FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)), Margin = new Thickness(0, 0, 6, 0) });

                smptePanel.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = packet.PathAConnected ? Brushes.LimeGreen : Brushes.Red, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
                smptePanel.Children.Add(new TextBlock { Text = "Path A", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = packet.PathAConnected ? Brushes.White : Brushes.Red, Margin = new Thickness(0, 0, 8, 0) });

                smptePanel.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = packet.PathBConnected ? Brushes.LimeGreen : Brushes.Red, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
                smptePanel.Children.Add(new TextBlock { Text = "Path B", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = packet.PathBConnected ? Brushes.White : Brushes.Red });

                smpteGrid.Children.Add(smptePanel);

                var uptimeText = new TextBlock
                {
                    Text = $"Uptime: {TimeSpan.FromSeconds(packet.UptimeSeconds):hh\\:mm\\:ss}",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                smpteGrid.Children.Add(uptimeText);
                stack.Children.Add(smpteGrid);
            }

            // 4. Sparkline Canvas (Bitrate & RTT trend)
            var (bitrates, rtts, _) = state.GetHistorySnapshots();
            var sparklineCanvas = RenderSparkline(bitrates, rtts, 376, 42);
            stack.Children.Add(sparklineCanvas);

            card.Child = stack;
        }

        private static void AddMetricColumn(Grid grid, int col, string label, string val, Color valColor)
        {
            var p = new StackPanel();
            p.Children.Add(new TextBlock { Text = label, FontSize = 9.5, Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140)) });
            p.Children.Add(new TextBlock { Text = val, FontSize = 13, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(valColor) });
            Grid.SetColumn(p, col);
            grid.Children.Add(p);
        }

        private static FrameworkElement RenderSparkline(double[] bitrates, double[] rtts, double width, double height)
        {
            var canvas = new Canvas
            {
                Width = width,
                Height = height,
                Background = new SolidColorBrush(Color.FromRgb(18, 18, 22)),
                ClipToBounds = true
            };

            if (bitrates.Length < 2)
            {
                var ph = new TextBlock
                {
                    Text = "Thu thập biểu đồ Sparkline 60s...",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 90))
                };
                Canvas.SetLeft(ph, 10);
                Canvas.SetTop(ph, 12);
                canvas.Children.Add(ph);
                return canvas;
            }

            double maxBitrate = Math.Max(1000.0, bitrates.Max());
            var bitratePoints = new PointCollection();
            double xStep = width / 60.0;

            for (int i = 0; i < bitrates.Length; i++)
            {
                double x = i * xStep;
                double normY = Math.Clamp(bitrates[i] / maxBitrate, 0, 1);
                double y = height - (normY * (height - 6)) - 3;
                bitratePoints.Add(new Point(x, y));
            }

            var polyBitrate = new Polyline
            {
                Points = bitratePoints,
                Stroke = new SolidColorBrush(Color.FromRgb(0, 230, 118)),
                StrokeThickness = 1.5
            };
            canvas.Children.Add(polyBitrate);

            double maxRtt = Math.Max(50.0, rtts.Max());
            var rttPoints = new PointCollection();
            for (int i = 0; i < rtts.Length; i++)
            {
                double x = i * xStep;
                double normY = Math.Clamp(rtts[i] / maxRtt, 0, 1);
                double y = height - (normY * (height - 6)) - 3;
                rttPoints.Add(new Point(x, y));
            }

            var polyRtt = new Polyline
            {
                Points = rttPoints,
                Stroke = new SolidColorBrush(Color.FromArgb(160, 255, 179, 0)),
                StrokeThickness = 1.0
            };
            canvas.Children.Add(polyRtt);

            return canvas;
        }

        private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
        {
            if (_monitorServer == null || _monitorServer.AuditLogs.Count == 0)
            {
                MessageBox.Show("Hiện tại chưa có dữ liệu audit log để xuất.", "Xuất CSV", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var sfd = new SaveFileDialog
            {
                Title = "Xuất nhật ký sự kiện SRT Monitor",
                Filter = "CSV File (*.csv)|*.csv",
                FileName = $"SRT_MONITOR_AUDIT_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    _monitorServer.ExportCsv(sfd.FileName);
                    MessageBox.Show($"Đã xuất thành công file CSV:\n{sfd.FileName}", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lỗi xuất file: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #endregion
    }
}
