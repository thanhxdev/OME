using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace OME_PLAYOUT
{
    public partial class MainWindow : Window
    {
        private readonly GpuMatrixCompositor _compositor = new();
        private DispatcherTimer? _uiRenderTimer;
        private DispatcherTimer? _telemetryTimer;

        private WriteableBitmap? _pgmInBmp;
        private WriteableBitmap? _masterOutBmp;
        private WriteableBitmap? _scopeBmp;

        private bool _isLoaded = false;
        private bool _isShuttingDown = false;
        private bool _testFallbackForced = false;
        private bool _isPopulatingRouter = false;
        private long _lastRenderedFrameIndex = -1;
        private List<SdiDeviceInfo> _sdiDevices = new();

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Initialize Compositor Core Engine
            _compositor.Initialize();

            // Setup UI Render Timer (Targeting 60fps refresh on WPF)
            _uiRenderTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16.6)
            };
            _uiRenderTimer.Tick += OnUiRenderTick;
            _uiRenderTimer.Start();

            // Setup Telemetry Timer (10Hz)
            _telemetryTimer = new DispatcherTimer(DispatcherPriority.Normal)
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _telemetryTimer.Tick += OnTelemetryTick;
            _telemetryTimer.Start();

            // Populate safe default SDI devices list without risky unmanaged COM scan on startup
            PopulateDefaultSdiDevices();

            // Wire up Master codec and SDI selection events safely after load
            if (CmbMasterRes != null) CmbMasterRes.SelectionChanged += CmbMasterCodec_SelectionChanged;
            if (CmbMasterCodec != null) CmbMasterCodec.SelectionChanged += CmbMasterCodec_SelectionChanged;
            if (CmbMasterBitrate != null) CmbMasterBitrate.SelectionChanged += CmbMasterCodec_SelectionChanged;
            if (CmbMasterFps != null) CmbMasterFps.SelectionChanged += CmbMasterCodec_SelectionChanged;
            if (CmbSdiResolution != null) CmbSdiResolution.SelectionChanged += CmbSdiConfig_SelectionChanged;
            if (CmbSdiFps != null) CmbSdiFps.SelectionChanged += CmbSdiConfig_SelectionChanged;

            // Wire up Crosspoint Router Gateway combo boxes
            foreach (var cmb in GetRouteComboBoxes())
            {
                if (cmb != null) cmb.SelectionChanged += OnRouteComboBox_SelectionChanged;
            }

            // Populate initial discovered sources in IP Matrix Router
            PopulateRouterCrosspoint();

            // Wire up Emergency Fail-Safe Controls
            if (CmbFailSafeMode != null) CmbFailSafeMode.SelectionChanged += OnFailSafeConfigChanged;
            if (CmbFailSafeBackupPort != null) CmbFailSafeBackupPort.SelectionChanged += OnFailSafeConfigChanged;
            if (CmbFailSafeTimeout != null) CmbFailSafeTimeout.SelectionChanged += OnFailSafeConfigChanged;

            _isLoaded = true;
        }

        #region 60 FPS UI Rendering Loop

        private void OnUiRenderTick(object? sender, EventArgs e)
        {
            if (_isShuttingDown) return;

            long currentFrame = _compositor.ProcessedFrames;
            if (currentFrame != _lastRenderedFrameIndex || _compositor.FailSafeEngine.IsFallbackActive)
            {
                _lastRenderedFrameIndex = currentFrame;

                // Render GPU frames to WPF monitor WriteableBitmaps (PGM In & Master Out)
                _compositor.UpdateWpfMonitors(ref _pgmInBmp, ref _masterOutBmp);

                if (ImgPgmIn.Source != _pgmInBmp) ImgPgmIn.Source = _pgmInBmp;
                if (ImgMasterOut.Source != _masterOutBmp) ImgMasterOut.Source = _masterOutBmp;
            }

            // Update Broadcast Scope Display (Thread-Safe WPF UI update)
            if (ImgBroadcastScope != null)
            {
                _compositor.ScopesEngine.UpdateWpfScopeBitmap(ref _scopeBmp);
                if (ImgBroadcastScope.Source != _scopeBmp)
                {
                    ImgBroadcastScope.Source = _scopeBmp;
                }

                var m = _compositor.ScopesEngine.Metrics;
                if (TxtScopeMaxIre != null)
                {
                    TxtScopeMaxIre.Text = $"{m.MaxIre:F1} IRE";
                    TxtScopeMaxIre.Foreground = m.MaxIre > 100 ? new SolidColorBrush(Color.FromRgb(239, 68, 68)) : new SolidColorBrush(Color.FromRgb(0, 230, 118));
                }

                if (TxtScopeMinIre != null)
                {
                    TxtScopeMinIre.Text = $"{m.MinIre:F1} IRE";
                    TxtScopeMinIre.Foreground = m.MinIre < 0 ? new SolidColorBrush(Color.FromRgb(239, 68, 68)) : new SolidColorBrush(Color.FromRgb(0, 230, 118));
                }

                if (TxtScopeGamutStatus != null)
                {
                    TxtScopeGamutStatus.Text = m.IsLegalSignal ? "LEGAL (BT.709)" : $"{m.OutOfGamutPercent:F1}% OUT";
                    TxtScopeGamutStatus.Foreground = m.IsLegalSignal ? new SolidColorBrush(Color.FromRgb(0, 230, 118)) : new SolidColorBrush(Color.FromRgb(239, 68, 68));
                }

                if (TxtScopeCct != null) TxtScopeCct.Text = $"{m.EstimatedColorTempK}K Daylight";
                if (TxtScopeTint != null) TxtScopeTint.Text = m.DominantTint;
            }

            // Update Audio Meters
            var levels = _compositor.CurrentAudioLevels;
            BarAudioL.Value = levels.NormalizedPeakL;
            BarAudioR.Value = levels.NormalizedPeakR;

            TxtMeterL.Text = $"L: {levels.PeakL_dBFS:F1} dBFS";
            TxtMeterR.Text = $"R: {levels.PeakR_dBFS:F1} dBFS";

            BadgeClip.Background = levels.IsClipL || levels.IsClipR ?
                new SolidColorBrush(Color.FromRgb(239, 68, 68)) :
                new SolidColorBrush(Color.FromArgb(80, 59, 18, 25));

            // Fail-safe badge & PGM IN (Router Feed) monitor telemetry
            bool isFallback = _compositor.FailSafeEngine.IsFallbackActive;
            bool isSignalRestored = _compositor.FailSafeEngine.IsSignalRestored;
            bool isPgmAlive = _compositor.FailSafeEngine.IsPgmSignalAlive;

            BadgeFailSafeStatus.Visibility = isFallback ? Visibility.Visible : Visibility.Collapsed;
            if (isFallback && TxtFailSafeBadge != null)
            {
                if (isSignalRestored)
                {
                    TxtFailSafeBadge.Text = "🚨 CỨU SÓNG ĐANG BẬT | PGM IN ĐÃ CÓ LẠI (BẤM PORT 0 ĐỂ CHUYỂN SÓNG)";
                    BadgeFailSafeStatus.Background = new SolidColorBrush(Color.FromArgb(230, 45, 36, 12));
                    BadgeFailSafeStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11));
                    TxtFailSafeBadge.Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36));
                }
                else
                {
                    string modeStr = _compositor.FailSafeEngine.FallbackMode == FailSafeFallbackMode.BackupIsoPort
                        ? $"CAM DỰ PHÒNG {_compositor.FailSafeEngine.BackupIsoPort}"
                        : "SMPTE BARS";
                    TxtFailSafeBadge.Text = $"⚠️ FAIL-SAFE ACTIVE: {modeStr}";
                    BadgeFailSafeStatus.Background = new SolidColorBrush(Color.FromRgb(42, 27, 27));
                    BadgeFailSafeStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                    TxtFailSafeBadge.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                }
            }

            // Bottom Monitor (PGM IN ROUTER FEED) header status
            if (TxtPgmInStatus != null && DotPgmInStatus != null)
            {
                if (isFallback)
                {
                    if (isSignalRestored)
                    {
                        DotPgmInStatus.Fill = new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Amber
                        TxtPgmInStatus.Text = "PGM IN (ROUTER FEED) - SÓNG ĐÃ HỒI PHỤC (SẴN SÀNG CHUYỂN)";
                        TxtPgmInStatus.Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36));
                    }
                    else
                    {
                        DotPgmInStatus.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red
                        TxtPgmInStatus.Text = "PGM IN (ROUTER FEED) - MẤT TÍN HIỆU (NO SIGNAL)";
                        TxtPgmInStatus.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
                    }
                }
                else
                {
                    DotPgmInStatus.Fill = isPgmAlive ?
                        new SolidColorBrush(Color.FromRgb(0, 230, 118)) : // Green
                        new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red
                    TxtPgmInStatus.Text = isPgmAlive ? "PGM IN (ROUTER FEED) - LIVE" : "PGM IN (ROUTER FEED) - NO SIGNAL";
                    TxtPgmInStatus.Foreground = isPgmAlive ?
                        new SolidColorBrush(Color.FromRgb(56, 189, 248)) :
                        new SolidColorBrush(Color.FromRgb(248, 113, 113));
                }
            }

        }

        private void OnTelemetryTick(object? sender, EventArgs e)
        {
            if (_isShuttingDown) return;

            // Engine FPS & Dropped frames
            TxtEngineFps.Text = $"{_compositor.EngineFps:F2} FPS";
            TxtDropFrames.Text = _compositor.DroppedFrames.ToString("N0");

            // Router Sync Status & Individual CAM Feeds Telemetry
            bool isSync = _compositor.IsRouterSyncActive;
            DotRouterSync.Fill = isSync ? new SolidColorBrush(Color.FromRgb(0, 230, 118)) : new SolidColorBrush(Color.FromRgb(239, 68, 68));
            TxtRouterSyncStatus.Text = isSync ? "MATRIX ROUTER: SYNCED (0ms)" : "MATRIX ROUTER: SEARCHING...";
            TxtRouterSyncStatus.Foreground = isSync ? new SolidColorBrush(Color.FromRgb(0, 230, 118)) : new SolidColorBrush(Color.FromRgb(239, 68, 68));
            BadgeRouterSync.BorderBrush = isSync ? new SolidColorBrush(Color.FromRgb(0, 230, 118)) : new SolidColorBrush(Color.FromRgb(239, 68, 68));

            // Update Port 0 (PGM Master In) status & dynamic Fail-Safe prompt
            if (BtnSelectPgm != null)
            {
                bool isFallback = _compositor.FailSafeEngine.IsFallbackActive;
                bool isSignalRestored = _compositor.FailSafeEngine.IsSignalRestored;

                if (isFallback && isSignalRestored)
                {
                    string alertText = "🟢 PORT 0: PGM ĐÃ CÓ SÓNG LẠI (CLICK ĐỂ PHÁT SÓNG)";
                    if (!Equals(BtnSelectPgm.Content, alertText))
                        BtnSelectPgm.Content = alertText;

                    BtnSelectPgm.Background = new SolidColorBrush(Color.FromRgb(16, 185, 129)); // Emerald green highlight
                    BtnSelectPgm.Foreground = Brushes.White;
                }
                else
                {
                    var pgmSt = _compositor.GetChannelStatus(0);
                    string targetContent = pgmSt.isActive
                        ? $"🔴 PORT 0: PROGRAM MASTER ({(string.IsNullOrEmpty(pgmSt.sourceName) ? "LIVE" : pgmSt.sourceName)} {pgmSt.width}x{pgmSt.height}) 🟢"
                        : "🔴 PORT 0: PROGRAM MASTER (Bàn cắt chuyển cảnh)";

                    if (!Equals(BtnSelectPgm.Content, targetContent))
                        BtnSelectPgm.Content = targetContent;

                    if (_compositor.SelectedIngestSource == 0)
                    {
                        BtnSelectPgm.Background = new SolidColorBrush(Color.FromRgb(37, 99, 235));
                        BtnSelectPgm.Foreground = Brushes.White;
                    }
                    else
                    {
                        BtnSelectPgm.Background = new SolidColorBrush(Color.FromRgb(30, 41, 59));
                        BtnSelectPgm.Foreground = pgmSt.isActive
                            ? new SolidColorBrush(Color.FromRgb(0, 230, 118))
                            : new SolidColorBrush(Color.FromRgb(248, 113, 113));
                    }
                }
            }

            // Update each IP Channel button with live stream status
            Button[] isoBtns = { BtnIso1, BtnIso2, BtnIso3, BtnIso4, BtnIso5, BtnIso6, BtnIso7, BtnIso8, BtnIso9, BtnIso10 };
            for (int i = 0; i < isoBtns.Length; i++)
            {
                int port = i + 1;
                var st = _compositor.GetChannelStatus(port);
                if (st.isActive)
                {
                    isoBtns[i].Content = $"IP {port}: {st.sourceName} 🟢";
                    if (_compositor.SelectedIngestSource != port)
                    {
                        isoBtns[i].Foreground = new SolidColorBrush(Color.FromRgb(0, 230, 118));
                    }
                }
                else
                {
                    isoBtns[i].Content = $"IP {port} (Port {port}) ⚪";
                    if (_compositor.SelectedIngestSource != port)
                    {
                        isoBtns[i].Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248));
                    }
                }
            }

            // SDI Status in Status Bar
            if (TxtStatusBarSdi != null)
            {
                bool sdiActive = _compositor.IsSdiMasterOutEnabled;
                TxtStatusBarSdi.Text = sdiActive ? $"ON AIR ({_compositor.SdiWorker.FramesSent:N0} frames)" : "STANDBY";
                TxtStatusBarSdi.Foreground = sdiActive ? new SolidColorBrush(Color.FromRgb(0, 230, 118)) : new SolidColorBrush(Color.FromRgb(148, 163, 184));
            }

            // Clock Timecode
            TxtMasterClock.Text = DateTime.Now.ToString("HH:mm:ss:ff");
        }

        #endregion

        #region Tab 1: Ingest Matrix Events

        private void BtnSelectPgm_Click(object sender, RoutedEventArgs e)
        {
            _compositor.FailSafeEngine.DismissFallback();
            _testFallbackForced = false;
            if (BtnTestFailSafe != null)
            {
                BtnTestFailSafe.Content = "🚨 BẬT THỬ NGHIỆM CỨU SÓNG KHẨN CẤP";
                BtnTestFailSafe.Background = new SolidColorBrush(Color.FromRgb(127, 29, 29));
            }

            _compositor.SelectedIngestSource = 0;
            HighlightIngestButton(0);
        }

        private void BtnSelectIso_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tagStr && int.TryParse(tagStr, out int port))
            {
                _compositor.FailSafeEngine.DismissFallback();
                _testFallbackForced = false;
                if (BtnTestFailSafe != null)
                {
                    BtnTestFailSafe.Content = "🚨 BẬT THỬ NGHIỆM CỨU SÓNG KHẨN CẤP";
                    BtnTestFailSafe.Background = new SolidColorBrush(Color.FromRgb(127, 29, 29));
                }

                _compositor.SelectedIngestSource = port;
                HighlightIngestButton(port);
            }
        }

        private void HighlightIngestButton(int selectedPort)
        {
            BtnSelectPgm.Background = selectedPort == 0 ? new SolidColorBrush(Color.FromRgb(37, 99, 235)) : new SolidColorBrush(Color.FromRgb(30, 41, 59));
            Button[] isoBtns = { BtnIso1, BtnIso2, BtnIso3, BtnIso4, BtnIso5, BtnIso6, BtnIso7, BtnIso8, BtnIso9, BtnIso10 };

            for (int i = 0; i < isoBtns.Length; i++)
            {
                int port = i + 1;
                isoBtns[i].Background = selectedPort == port ? new SolidColorBrush(Color.FromRgb(37, 99, 235)) : new SolidColorBrush(Color.FromRgb(30, 41, 59));
            }
        }

        #region Crosspoint Router Gateway Events

        private ComboBox[] GetRouteComboBoxes() => new[]
        {
            CmbRoutePort0, CmbRoutePort1, CmbRoutePort2, CmbRoutePort3, CmbRoutePort4,
            CmbRoutePort5, CmbRoutePort6, CmbRoutePort7, CmbRoutePort8, CmbRoutePort9, CmbRoutePort10
        };

        private void PopulateRouterCrosspoint()
        {
            if (_compositor == null) return;
            _isPopulatingRouter = true;
            try
            {
                var discoveredList = _compositor.GetDiscoveredSources();
                var discovered = new Dictionary<int, (int slot, string name, int width, int height, double fps, bool isActive)>();
                foreach (var s in discoveredList)
                {
                    discovered[s.slot] = s;
                }

                var cmbs = GetRouteComboBoxes();

                for (int port = 0; port < cmbs.Length; port++)
                {
                    var cmb = cmbs[port];
                    if (cmb == null) continue;

                    int currentSlot = _compositor.GetPortRoute(port);
                    cmb.Items.Clear();

                    for (int slot = 0; slot < GpuMatrixCompositor.TotalMatrixSlots; slot++)
                    {
                        string label;
                        bool isActive = false;

                        if (discovered.TryGetValue(slot, out var info) && info.isActive)
                        {
                            isActive = true;
                            label = $"[🟢 Online] Slot {slot:D2}: {info.name} ({info.width}x{info.height} @ {info.fps:F1}fps)";
                        }
                        else
                        {
                            string defaultLabel = slot == 0 ? "Default PGM Master" : (slot <= 10 ? $"Direct IP {slot}" : $"Slot {slot:D2}");
                            label = $"[⚪ Offline] Slot {slot:D2}: {defaultLabel}";
                        }

                        var item = new ComboBoxItem
                        {
                            Content = label,
                            Tag = slot,
                            Foreground = isActive
                                ? new SolidColorBrush(Color.FromRgb(0, 230, 118))
                                : new SolidColorBrush(Color.FromRgb(148, 163, 184))
                        };
                        cmb.Items.Add(item);
                    }

                    if (currentSlot >= 0 && currentSlot < cmb.Items.Count)
                    {
                        cmb.SelectedIndex = currentSlot;
                    }
                    else
                    {
                        cmb.SelectedIndex = (port < cmb.Items.Count) ? port : 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[PopulateRouterCrosspoint] Error: {ex.Message}");
            }
            finally
            {
                _isPopulatingRouter = false;
            }
        }

        private void BtnRefreshRouterSources_Click(object sender, RoutedEventArgs e)
        {
            PopulateRouterCrosspoint();
        }

        private void OnRouteComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isPopulatingRouter || !_isLoaded || _compositor == null) return;

            if (sender is ComboBox cmb && cmb.Tag is string tagStr && int.TryParse(tagStr, out int port))
            {
                if (cmb.SelectedItem is ComboBoxItem cbi && cbi.Tag is int slot)
                {
                    _compositor.SetPortRoute(port, slot);
                    Trace.WriteLine($"[CrosspointRouter] Patched Playout Port {port} -> IP Matrix Slot {slot}");
                }
            }
        }

        #endregion

        #endregion

        #region Tab 2: CG (Character Generator / Logo) Events

        // Logo Layer Events
        private void BtnToggleLogo_Click(object sender, RoutedEventArgs e)
        {
            _compositor.GraphicsEngine.IsLogoOnAir = !_compositor.GraphicsEngine.IsLogoOnAir;
            BtnToggleLogo.Content = _compositor.GraphicsEngine.IsLogoOnAir ? "DSK LOGO ON AIR 🟢" : "LOGO OFF ⚪";
            BtnToggleLogo.Background = _compositor.GraphicsEngine.IsLogoOnAir ?
                new SolidColorBrush(Color.FromRgb(34, 197, 94)) :
                new SolidColorBrush(Color.FromRgb(30, 41, 59));
        }

        private void BtnBrowseLogo_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Filter = "PNG Image (*.png)|*.png|All Image Files|*.png;*.jpg;*.jpeg;*.bmp"
            };
            if (ofd.ShowDialog() == true)
            {
                if (_compositor.GraphicsEngine.LoadLogoFromFile(ofd.FileName))
                {
                    TxtLogoFile.Text = $"File: {_compositor.GraphicsEngine.LoadedLogoPath}";
                }
            }
        }

        private void BtnLogoPos_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                double x = 0.88, y = 0.08;
                switch (tag)
                {
                    case "TR": x = 0.88; y = 0.08; break;
                    case "TL": x = 0.08; y = 0.08; break;
                    case "BR": x = 0.88; y = 0.88; break;
                    case "BL": x = 0.08; y = 0.88; break;
                }

                if (SliderLogoX != null) SliderLogoX.Value = x;
                if (SliderLogoY != null) SliderLogoY.Value = y;
                if (_compositor != null)
                {
                    _compositor.GraphicsEngine.LogoX = x;
                    _compositor.GraphicsEngine.LogoY = y;
                }

                Button[] posBtns = { BtnLogoPosTr, BtnLogoPosTl, BtnLogoPosBr, BtnLogoPosBl };
                foreach (var b in posBtns)
                {
                    if (b == null) continue;
                    b.Background = (b == btn) ?
                        new SolidColorBrush(Color.FromRgb(37, 99, 235)) :
                        new SolidColorBrush(Color.FromRgb(30, 41, 59));
                    b.Foreground = (b == btn) ?
                        new SolidColorBrush(Colors.White) :
                        new SolidColorBrush(Color.FromRgb(226, 232, 240));
                }
            }
        }

        private void SliderLogoPos_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_compositor != null)
            {
                if (SliderLogoX != null) _compositor.GraphicsEngine.LogoX = SliderLogoX.Value;
                if (SliderLogoY != null) _compositor.GraphicsEngine.LogoY = SliderLogoY.Value;
            }
        }

        private void SliderLogo_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_compositor != null)
            {
                if (SliderLogoScale != null) _compositor.GraphicsEngine.LogoScale = SliderLogoScale.Value;
                if (SliderLogoOpacity != null) _compositor.GraphicsEngine.LogoOpacity = SliderLogoOpacity.Value;
            }
        }

        #endregion

        #region Tab 3: CCU & Broadcast Scopes Events

        private void BtnSelectScope_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tagStr && int.TryParse(tagStr, out int scopeIdx))
            {
                _compositor.ScopesEngine.CurrentScope = (BroadcastScopeType)scopeIdx;

                Button[] scopeBtns = { BtnScopeWaveform, BtnScopeParade, BtnScopeVectorscope, BtnScopeHistogram, BtnScopeCie };
                for (int i = 0; i < scopeBtns.Length; i++)
                {
                    if (scopeBtns[i] == null) continue;
                    scopeBtns[i].Background = (i == scopeIdx) ?
                        new SolidColorBrush(Color.FromRgb(37, 99, 235)) :
                        new SolidColorBrush(Color.FromRgb(30, 41, 59));
                    scopeBtns[i].Foreground = (i == scopeIdx) ?
                        new SolidColorBrush(Colors.White) :
                        new SolidColorBrush(Color.FromRgb(226, 232, 240));
                }
            }
        }

        private void ColorSliders_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_compositor == null) return;
            var p = _compositor.ColorEngine.Parameters;

            if (SliderLift != null) p.LiftR = p.LiftG = p.LiftB = (float)SliderLift.Value;
            if (SliderGamma != null) p.GammaR = p.GammaG = p.GammaB = (float)SliderGamma.Value;
            if (SliderGain != null) p.GainR = p.GainG = p.GainB = (float)SliderGain.Value;
            if (SliderBrightness != null) p.Brightness = (float)SliderBrightness.Value;
            if (SliderContrast != null) p.Contrast = (float)SliderContrast.Value;
            if (SliderSaturation != null) p.Saturation = (float)SliderSaturation.Value;

            _compositor.ColorEngine.Invalidate();
        }

        private void BtnResetColor_Click(object sender, RoutedEventArgs e)
        {
            if (_compositor == null) return;
            _compositor.ColorEngine.Parameters.Reset();

            if (SliderLift != null) SliderLift.Value = 0.0;
            if (SliderGamma != null) SliderGamma.Value = 1.0;
            if (SliderGain != null) SliderGain.Value = 1.0;
            if (SliderBrightness != null) SliderBrightness.Value = 0.0;
            if (SliderContrast != null) SliderContrast.Value = 1.0;
            if (SliderSaturation != null) SliderSaturation.Value = 1.0;

            _compositor.ColorEngine.Invalidate();
        }

        private void BtnLoadLut_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Filter = "DaVinci / Adobe 3D LUT (*.cube)|*.cube|All Files|*.*"
            };
            if (ofd.ShowDialog() == true)
            {
                if (_compositor.ColorEngine.LoadCubeFile(ofd.FileName))
                {
                    TxtLutName.Text = $"LUT: {_compositor.ColorEngine.LoadedLutName} ({_compositor.ColorEngine.LutSize}³)";
                }
            }
        }

        private void BtnUnloadLut_Click(object sender, RoutedEventArgs e)
        {
            _compositor.ColorEngine.UnloadLut();
            TxtLutName.Text = "LUT: Chưa nạp file .cube";
        }

        #endregion

        #region Tab 4: Lip-Sync Delay Events

        private void SliderVideoDelay_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_compositor == null || SliderVideoDelay == null) return;
            double ms = SliderVideoDelay.Value;
            _compositor.DelayEngine.VideoDelayMs = ms;

            int frames = (int)Math.Round(ms * 59.94 / 1000.0);
            TxtVideoDelayMs.Text = $"{ms:F0} ms ({frames} frames @ 59.94 fps)";

            if (ChkLinkAv?.IsChecked == true && SliderAudioDelay != null)
            {
                SliderAudioDelay.Value = ms;
            }
        }

        private void SliderAudioDelay_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_compositor == null || SliderAudioDelay == null) return;
            double ms = SliderAudioDelay.Value;
            _compositor.DelayEngine.AudioDelayMs = ms;
            TxtAudioDelayMs.Text = $"{ms:F0} ms";
        }

        private void ChkLinkAv_Click(object sender, RoutedEventArgs e)
        {
            if (ChkLinkAv.IsChecked == true && SliderVideoDelay != null && SliderAudioDelay != null)
            {
                SliderAudioDelay.Value = SliderVideoDelay.Value;
            }
        }

        private void BtnResetDelay_Click(object sender, RoutedEventArgs e)
        {
            if (SliderVideoDelay != null) SliderVideoDelay.Value = 0;
            if (SliderAudioDelay != null) SliderAudioDelay.Value = 0;
            _compositor?.DelayEngine.Reset();
        }

        #endregion

        #region Tab 5: Broadcast Out & Fail-Safe Events

        private void BtnToggleNdiMaster_Click(object sender, RoutedEventArgs e)
        {
            bool nextState = !_compositor.IsNdiMasterOutEnabled;
            _compositor.ToggleMasterNdi(nextState);

            BtnToggleNdiMaster.Content = nextState ? "TẮT NDI OUT 🔴" : "BẬT NDI OUT ⚪";
            BtnToggleNdiMaster.Background = nextState ?
                new SolidColorBrush(Color.FromRgb(239, 68, 68)) :
                new SolidColorBrush(Color.FromRgb(37, 99, 235));
        }

        #region SDI Output & Codec Selection Events

        private void PopulateDefaultSdiDevices()
        {
            try
            {
                _sdiDevices = SdiHardwareScanner.GetDefaultDevices();
                if (CmbSdiDevice != null)
                {
                    CmbSdiDevice.Items.Clear();
                    foreach (var dev in _sdiDevices)
                    {
                        CmbSdiDevice.Items.Add(dev.DisplayLabel);
                    }
                    if (CmbSdiDevice.Items.Count > 0)
                    {
                        CmbSdiDevice.SelectedIndex = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[PopulateDefaultSdiDevices] Error: {ex.Message}");
            }
        }

        private async Task RefreshSdiDevicesAsync()
        {
            try
            {
                var scanned = await SdiHardwareScanner.ScanDevicesAsync();
                if (scanned != null && scanned.Count > 0)
                {
                    _sdiDevices = scanned;
                }
                else if (_sdiDevices.Count == 0)
                {
                    _sdiDevices = SdiHardwareScanner.GetDefaultDevices();
                }

                if (CmbSdiDevice != null)
                {
                    CmbSdiDevice.Items.Clear();
                    foreach (var dev in _sdiDevices)
                    {
                        CmbSdiDevice.Items.Add(dev.DisplayLabel);
                    }
                    if (CmbSdiDevice.Items.Count > 0)
                    {
                        CmbSdiDevice.SelectedIndex = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[RefreshSdiDevicesAsync] Error: {ex.Message}");
            }
        }

        private async void BtnScanSdi_Click(object sender, RoutedEventArgs e)
        {
            if (BtnScanSdi != null) BtnScanSdi.IsEnabled = false;
            await RefreshSdiDevicesAsync();
            if (BtnScanSdi != null) BtnScanSdi.IsEnabled = true;
        }

        private void CmbSdiConfig_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded || _compositor == null) return;

            // Dynamically synchronize if currently on air
            if (_compositor.IsSdiMasterOutEnabled)
            {
                string device = CmbSdiDevice?.SelectedItem?.ToString() ?? "DeckLink";
                string res = GetComboBoxText(CmbSdiResolution);
                string fpsStr = GetComboBoxText(CmbSdiFps);
                string mode = $"{res} ({fpsStr})";
                double fps = MasterVideoCodecConfig.ParseFpsString(fpsStr);

                _compositor.ToggleMasterSdi(true, device, mode, fps);
            }
        }

        private void BtnToggleSdiMaster_Click(object sender, RoutedEventArgs e)
        {
            bool nextState = !_compositor.IsSdiMasterOutEnabled;
            if (nextState)
            {
                string device = CmbSdiDevice?.SelectedItem?.ToString() ?? "DeckLink";
                string sdiRes = GetComboBoxText(CmbSdiResolution);
                string sdiFps = GetComboBoxText(CmbSdiFps);
                string presetMode = (CmbSdiMode?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";

                string mode = !string.IsNullOrWhiteSpace(presetMode) ? presetMode : $"{sdiRes} ({sdiFps})";
                double fps = MasterVideoCodecConfig.ParseFpsString(sdiFps);

                bool ok = _compositor.ToggleMasterSdi(true, device, mode, fps);
                UpdateSdiUiState(ok);
            }
            else
            {
                _compositor.ToggleMasterSdi(false, string.Empty, string.Empty);
                UpdateSdiUiState(false);
            }
        }

        private void UpdateSdiUiState(bool isActive)
        {
            if (BtnToggleSdiMaster != null)
            {
                BtnToggleSdiMaster.Content = isActive ? "TẮT SDI OUT 🔴" : "BẬT SDI OUT ⚪";
                BtnToggleSdiMaster.Background = isActive ?
                    new SolidColorBrush(Color.FromRgb(239, 68, 68)) :
                    new SolidColorBrush(Color.FromRgb(37, 99, 235));
            }

            if (BadgeSdiStatus != null && TxtSdiStatusBadge != null)
            {
                BadgeSdiStatus.Background = isActive ?
                    new SolidColorBrush(Color.FromRgb(6, 95, 70)) :
                    new SolidColorBrush(Color.FromRgb(30, 41, 59));
                BadgeSdiStatus.BorderBrush = isActive ?
                    new SolidColorBrush(Color.FromRgb(0, 230, 118)) :
                    new SolidColorBrush(Color.FromRgb(100, 116, 139));
                TxtSdiStatusBadge.Text = isActive ? "ON AIR (SDI TX)" : "STANDBY";
                TxtSdiStatusBadge.Foreground = isActive ?
                    new SolidColorBrush(Color.FromRgb(0, 230, 118)) :
                    new SolidColorBrush(Color.FromRgb(148, 163, 184));
            }

            if (TxtStatusBarSdi != null)
            {
                TxtStatusBarSdi.Text = isActive ? "ON AIR (TX)" : "STANDBY";
                TxtStatusBarSdi.Foreground = isActive ?
                    new SolidColorBrush(Color.FromRgb(0, 230, 118)) :
                    new SolidColorBrush(Color.FromRgb(148, 163, 184));
            }
        }

        private void CmbMasterCodec_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded || _compositor == null) return;

            if (CmbMasterRes?.SelectedItem != null)
                _compositor.MasterCodecConfig.Resolution = GetComboBoxText(CmbMasterRes);

            if (CmbMasterCodec?.SelectedItem != null)
            {
                string codec = GetComboBoxText(CmbMasterCodec);
                _compositor.MasterCodecConfig.Codec = codec;

                if (TxtStatusBarCodec != null)
                {
                    if (codec.Contains("UYVY")) TxtStatusBarCodec.Text = "UYVY 4:2:2";
                    else if (codec.Contains("H.264") || codec.Contains("AVC")) TxtStatusBarCodec.Text = "H.264 (NVENC)";
                    else if (codec.Contains("H.265") || codec.Contains("HEVC")) TxtStatusBarCodec.Text = "H.265 (NVENC)";
                    else if (codec.Contains("ProRes")) TxtStatusBarCodec.Text = "ProRes 422";
                    else if (codec.Contains("DNx")) TxtStatusBarCodec.Text = "DNxHR";
                    else TxtStatusBarCodec.Text = codec;
                }
            }

            if (CmbMasterBitrate?.SelectedItem != null)
                _compositor.MasterCodecConfig.Bitrate = GetComboBoxText(CmbMasterBitrate);

            if (CmbMasterFps?.SelectedItem != null)
                _compositor.MasterCodecConfig.Fps = GetComboBoxText(CmbMasterFps);
        }

        private static string GetComboBoxText(ComboBox? cb)
        {
            if (cb == null || cb.SelectedItem == null) return "";
            if (cb.SelectedItem is ComboBoxItem cbi) return cbi.Content?.ToString() ?? "";
            return cb.SelectedItem?.ToString() ?? "";
        }

        #endregion

        private void ChkFailSafeEnabled_Click(object sender, RoutedEventArgs e)
        {
            if (_compositor != null && ChkFailSafeEnabled != null)
            {
                _compositor.FailSafeEngine.IsEnabled = ChkFailSafeEnabled.IsChecked == true;
            }
        }

        private void BtnTestFailSafe_Click(object sender, RoutedEventArgs e)
        {
            _testFallbackForced = !_testFallbackForced;
            _compositor.FailSafeEngine.ForceTestFallback = _testFallbackForced;

            BtnTestFailSafe.Content = _testFallbackForced ? "🛑 DỪNG TEST FALLBACK" : "🚨 BẬT THỬ NGHIỆM CỨU SÓNG KHẨN CẤP";
            BtnTestFailSafe.Background = _testFallbackForced ?
                new SolidColorBrush(Color.FromRgb(220, 38, 38)) :
                new SolidColorBrush(Color.FromRgb(127, 29, 29));

            if (!_testFallbackForced)
            {
                _compositor.FailSafeEngine.DismissFallback();
            }
        }

        private void OnFailSafeConfigChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded || _compositor == null) return;

            if (CmbFailSafeMode != null)
            {
                _compositor.FailSafeEngine.FallbackMode = (FailSafeFallbackMode)CmbFailSafeMode.SelectedIndex;
            }

            if (CmbFailSafeBackupPort != null)
            {
                // Index 0 is Cam 1 (Port 1) ... Index 9 is Cam 10 (Port 10)
                _compositor.FailSafeEngine.BackupIsoPort = CmbFailSafeBackupPort.SelectedIndex + 1;
            }

            if (CmbFailSafeTimeout != null)
            {
                double timeoutSec = CmbFailSafeTimeout.SelectedIndex switch
                {
                    0 => 0.5,
                    1 => 1.0,
                    2 => 1.5,
                    3 => 2.0,
                    4 => 3.0,
                    5 => 5.0,
                    _ => 1.5
                };
                _compositor.FailSafeEngine.LossThresholdSeconds = timeoutSec;
            }
        }

        #endregion

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _isShuttingDown = true;
            _uiRenderTimer?.Stop();
            _telemetryTimer?.Stop();
            _compositor.Dispose();
        }
    }
}
