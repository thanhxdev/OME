using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using OME_PLAYOUT.Models;

namespace OME_PLAYOUT
{
    public partial class MainWindow : Window
    {
        private readonly GpuMatrixCompositor _compositor = new();
        private readonly BroadcastPlaylistScheduler _scheduler = new();

        private DispatcherTimer? _uiRenderTimer;
        private DispatcherTimer? _telemetryTimer;
        private DispatcherTimer? _fadeTimer;

        private WriteableBitmap? _pgmInBmp;      // Preview (NEXT) WriteableBitmap
        private WriteableBitmap? _masterOutBmp;  // Program (CURRENT) WriteableBitmap
        private WriteableBitmap? _scopeBmp;

        private bool _isLoaded = false;
        private bool _isShuttingDown = false;
        private bool _isPopulatingRouter = false;
        private List<SdiDeviceInfo> _sdiDevices = new();
        private bool _isUpdatingCcuUi = false;

        // IP Router Matrix & NDI Auto Scanners
        private IpRouterAutoScanner? _ipScanner;
        private NdiAutoScanner? _ndiScanner;
        private NdiNativeReceiver? _ndiPlayoutReceiver;

        // Playout Switcher State
        private int _currentProgramSourceIndex = 1; // Live Cam 01
        private int _currentPreviewSourceIndex = 2; // Live Cam 02
        private bool _isFtbActive = false;
        private bool _isTBarAutoTaking = false;

        private DateTime _fadeStartTime;
        private double _fadeDurationMs = 1000;
        private double _fadeStartVal = 0;
        private double _fadeTargetVal = 100;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 1. Initialize Compositor Core Engine
            _compositor.Initialize();
            _compositor.SelectedIngestSource = _currentProgramSourceIndex;
            _compositor.PreviewIngestSource = _currentPreviewSourceIndex;

            // 2. Bind Rundown DataGrid to Automation Scheduler
            GridRundown.ItemsSource = _scheduler.RundownList;
            _scheduler.OnAirSwitched += OnSchedulerOnAirSwitched;

            // Trigger initial source setup from scheduler's first items
            OnSchedulerOnAirSwitched(_scheduler.CurrentItem, _scheduler.NextItem);

            // 3. Setup UI Render Timer (60fps refresh on WPF)
            _uiRenderTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16.6)
            };
            _uiRenderTimer.Tick += OnUiRenderTick;
            _uiRenderTimer.Start();

            // 4. Setup Telemetry Timer (10Hz)
            _telemetryTimer = new DispatcherTimer(DispatcherPriority.Normal)
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _telemetryTimer.Tick += OnTelemetryTick;
            _telemetryTimer.Start();

            // 5. Populate safe default SDI devices list
            PopulateDefaultSdiDevices();

            // 6. Wire up SDI selection events
            if (CmbSdiResolution != null) CmbSdiResolution.SelectionChanged += CmbSdiConfig_SelectionChanged;
            if (CmbSdiFps != null) CmbSdiFps.SelectionChanged += CmbSdiConfig_SelectionChanged;

            // 7. Populate initial Router Crosspoint Gateway
            PopulateRouterCrosspoint();

            // 8. Wire up Emergency Fail-Safe Controls
            if (CmbFailSafeMode != null) CmbFailSafeMode.SelectionChanged += OnFailSafeConfigChanged;
            if (CmbFailSafeBackupPort != null) CmbFailSafeBackupPort.SelectionChanged += OnFailSafeConfigChanged;
            if (CmbFailSafeTimeout != null) CmbFailSafeTimeout.SelectionChanged += OnFailSafeConfigChanged;

            // 9. Populate DVE layout combo boxes
            PopulateDveControls();

            // 10. Sync initial DSK UI buttons
            UpdateDskButtonVisuals();

            // 11. Initialize Auto Scanners for IP Matrix & NDI LAN
            _ipScanner = new IpRouterAutoScanner(_compositor);
            _ndiScanner = new NdiAutoScanner(Dispatcher);

            if (ListIpPreviewSources != null)
            {
                ListIpPreviewSources.ItemsSource = _ipScanner.DiscoveredSources;
                var previewView = System.Windows.Data.CollectionViewSource.GetDefaultView(_ipScanner.DiscoveredSources);
                if (previewView != null)
                {
                    // Chỉ những slot# được gán CAM (PORT) và có tín hiệu online mới được đưa vào Preview IPC
                    previewView.Filter = obj => obj is IpPreviewItem item && item.IsAssigned && item.IsActive;
                }
            }

            InitRouter11Slots();
            if (ListRouter11Slots != null)
            {
                ListRouter11Slots.ItemsSource = Router11Slots;
            }

            if (ListNdiPreviewSources != null) ListNdiPreviewSources.ItemsSource = _ndiScanner.DiscoveredSources;

            _ipScanner.ScanCompleted += () =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    UpdateSummaryBadges();
                    SyncAssignedSlotsToPreview();
                    PopulateRouterCrosspoint();
                });
            };
            _ndiScanner.ScanCompleted += UpdateSummaryBadges;

            _ipScanner.Start();
            _ndiScanner.Start();

            // Đồng bộ danh sách slot đã gán từ Router Playout sang giao diện Preview bên trái
            SyncAssignedSlotsToPreview();

            // Khởi tạo profile CCU mục tiêu mặc định (PGM Master)
            UpdateCcuAndScopeTarget();

            _isLoaded = true;
        }

        #region Automation Scheduler Events

        private void OnSchedulerOnAirSwitched(PlaylistItem? current, PlaylistItem? next)
        {
            if (current != null)
            {
                switch (current.SourceType)
                {
                    case PlaylistItemSourceType.ClipFile:
                        _compositor.PlayClipOnAir(current.FilePath, current.Duration);
                        break;
                    case PlaylistItemSourceType.LiveIngest:
                        _compositor.IsOnAirClip = false;
                        _compositor.OnAirClipReader.Close();
                        _compositor.SelectedIngestSource = current.LivePortIndex;
                        _currentProgramSourceIndex = current.LivePortIndex;
                        break;
                    case PlaylistItemSourceType.ColorBars:
                        _compositor.IsOnAirClip = false;
                        _compositor.OnAirClipReader.Close();
                        _compositor.SelectedIngestSource = 11;
                        _currentProgramSourceIndex = 11;
                        break;
                    case PlaylistItemSourceType.CommercialBreak:
                        _compositor.IsOnAirClip = false;
                        _compositor.OnAirClipReader.Close();
                        _compositor.SelectedIngestSource = 0;
                        break;
                }

                // Apply DSK rules for this event
                _compositor.GraphicsEngine.IsLogoOnAir = current.IsDskLogo;
                _compositor.GraphicsEngine.IsTickerOnAir = current.IsDskTicker;
                _compositor.GraphicsEngine.IsLowerThirdOnAir = current.IsDskLowerThird;
                if (!string.IsNullOrEmpty(current.LowerThirdText))
                {
                    _compositor.GraphicsEngine.UpdateLowerThird(current.LowerThirdText, current.Title);
                }
                UpdateDskButtonVisuals();
            }
            else
            {
                _compositor.IsOnAirClip = false;
                _compositor.OnAirClipReader.Close();
            }

            if (next != null)
            {
                switch (next.SourceType)
                {
                    case PlaylistItemSourceType.ClipFile:
                        _compositor.CueClipNext(next.FilePath, next.Duration);
                        break;
                    case PlaylistItemSourceType.LiveIngest:
                        _compositor.IsPreviewClip = false;
                        _compositor.NextClipReader.Close();
                        _compositor.PreviewIngestSource = next.LivePortIndex;
                        _currentPreviewSourceIndex = next.LivePortIndex;
                        break;
                    case PlaylistItemSourceType.ColorBars:
                        _compositor.IsPreviewClip = false;
                        _compositor.NextClipReader.Close();
                        _compositor.PreviewIngestSource = 11;
                        _currentPreviewSourceIndex = 11;
                        break;
                    default:
                        _compositor.IsPreviewClip = false;
                        _compositor.NextClipReader.Close();
                        break;
                }
            }
            else
            {
                _compositor.IsPreviewClip = false;
                _compositor.NextClipReader.Close();
            }
        }

        #endregion

        #region 60 FPS UI Rendering Loop

        private void OnUiRenderTick(object? sender, EventArgs e)
        {
            if (_isShuttingDown) return;

            // 1. Advance Scheduler Frame Clock
            _scheduler.TickFrame(0.0166);

            // Update round-robin live thumbnail for discovered IP sources
            _ipScanner?.UpdateActiveThumbnailsRoundRobin();

            // 2. Render GPU frames to WPF monitors (Preview NEXT & Program CURRENT)
            _compositor.UpdateWpfMonitors(ref _pgmInBmp, ref _masterOutBmp);

            if (ImgMasterOut != null && ImgMasterOut.Source != _masterOutBmp) ImgMasterOut.Source = _masterOutBmp;
            if (ImgPgmIn != null && ImgPgmIn.Source != _pgmInBmp) ImgPgmIn.Source = _pgmInBmp;

            // 3. Update C-REM Countdown (Big Number & Dynamic Colors)
            if (TxtClipRemaining != null)
            {
                TxtClipRemaining.Text = _scheduler.ClipRemainingTimecode;
                string hexColor = _scheduler.CountdownColor;
                TxtClipRemaining.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor));
            }

            if (TxtClipElapsed != null)
            {
                TxtClipElapsed.Text = _scheduler.ClipElapsedTimecode;
            }

            if (BarOnAirProgress != null)
            {
                BarOnAirProgress.Value = _scheduler.ProgressFraction;
            }

            if (TxtScheduleDelta != null)
            {
                TxtScheduleDelta.Text = _scheduler.ScheduleDeltaText;
            }

            // 4. Update Event Info Headers
            if (TxtCurrentHouseId != null) TxtCurrentHouseId.Text = _scheduler.CurrentHouseId;
            if (TxtCurrentEventTitle != null) TxtCurrentEventTitle.Text = _scheduler.CurrentTitle;
            if (TxtCurrentSourceBadge != null) TxtCurrentSourceBadge.Text = $"[{_scheduler.CurrentItem?.SourceBadge ?? "NONE"}]";

            if (TxtNextHouseId != null) TxtNextHouseId.Text = _scheduler.NextHouseId;
            if (TxtNextEventTitle != null) TxtNextEventTitle.Text = _scheduler.NextTitle;
            if (TxtNextSourceBadge != null) TxtNextSourceBadge.Text = $"[{_scheduler.NextItem?.SourceBadge ?? "NONE"}]";

            // 5. Update Broadcast Scope Display (Only when Side Panel is open and Tab CCU Scopes is selected)
            if (ImgBroadcastScope != null && DrawerContainer?.Visibility == Visibility.Visible && TabItemCcuScopes?.IsSelected == true)
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
                    TxtScopeGamutStatus.Text = m.IsLegalSignal ? "LEGAL" : $"{m.OutOfGamutPercent:F1}% OUT";
                    TxtScopeGamutStatus.Foreground = m.IsLegalSignal ? new SolidColorBrush(Color.FromRgb(0, 230, 118)) : new SolidColorBrush(Color.FromRgb(239, 68, 68));
                }
            }

            // 6. Update EBU R128 Audio Loudness Meters
            var levels = _compositor.CurrentAudioLevels;
            if (BarAudioL != null) BarAudioL.Value = levels.NormalizedPeakL;
            if (BarAudioR != null) BarAudioR.Value = levels.NormalizedPeakR;
            if (BarAudioLufs != null) BarAudioLufs.Value = levels.NormalizedLufs;

            if (TxtMeterL != null) TxtMeterL.Text = $"L: {levels.PeakL_dBFS:F1}";
            if (TxtMeterR != null) TxtMeterR.Text = $"R: {levels.PeakR_dBFS:F1}";
            if (TxtLufsValue != null)
            {
                TxtLufsValue.Text = levels.LufsDisplay;
                TxtLufsValue.Foreground = Math.Abs(levels.MomentaryLufs - (-23.0f)) <= 1.5f ?
                    new SolidColorBrush(Color.FromRgb(0, 229, 255)) :
                    (levels.MomentaryLufs > -20.0f ? new SolidColorBrush(Color.FromRgb(239, 68, 68)) : new SolidColorBrush(Color.FromRgb(148, 163, 184)));
            }
            if (TxtTruePeakValue != null) TxtTruePeakValue.Text = levels.TruePeakDisplay;

            // 7. Fail-safe badge telemetry
            bool isFallback = _compositor.FailSafeEngine.IsFallbackActive;
            if (BadgeFailSafeStatus != null)
            {
                BadgeFailSafeStatus.Visibility = isFallback ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void OnTelemetryTick(object? sender, EventArgs e)
        {
            if (_isShuttingDown) return;

            // Telemetry readings
            if (TxtEngineFps != null) TxtEngineFps.Text = $"{_compositor.EngineFps:F2} FPS";
            if (TxtDropFrames != null) TxtDropFrames.Text = _compositor.DroppedFrames.ToString("N0");
            if (TxtVramUsage != null) TxtVramUsage.Text = $"{_compositor.VramUsageMb:F1} MB";

            // Master Timecode Clock (LTC)
            if (TxtMasterClock != null)
            {
                var now = DateTime.Now;
                int frame = (int)(now.Millisecond * 59.94 / 1000.0);
                TxtMasterClock.Text = $"{now:HH:mm:ss}:{frame:D2}";
            }

            // Router Sync Status
            bool isSync = _compositor.IsRouterSyncActive;
            if (DotRouterSync != null)
                DotRouterSync.Fill = isSync ? new SolidColorBrush(Color.FromRgb(0, 230, 118)) : new SolidColorBrush(Color.FromRgb(239, 68, 68));
            if (TxtRouterSyncStatus != null)
            {
                TxtRouterSyncStatus.Text = isSync ? "IPC: 32 CH (0ms)" : "IPC: SEARCHING";
                TxtRouterSyncStatus.Foreground = isSync ? new SolidColorBrush(Color.FromRgb(0, 230, 118)) : new SolidColorBrush(Color.FromRgb(239, 68, 68));
            }

            // Update IP Matrix & NDI telemetry summary badges
            UpdateSummaryBadges();
        }

        #endregion

        #region MCR Transport & Hotkey Controls (TAKE, HOLD, SKIP, CUE)

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space || e.Key == Key.F12)
            {
                ExecuteMasterTake();
                e.Handled = true;
            }
            else if (e.Key == Key.F11)
            {
                ToggleAutoMode();
                e.Handled = true;
            }
            else if (e.Key == Key.F5)
            {
                ToggleEmergencyBars();
                e.Handled = true;
            }
        }

        private void BtnMasterTake_Click(object sender, RoutedEventArgs e) => ExecuteMasterTake();

        public void ExecuteMasterTake()
        {
            if (_compositor != null)
            {
                _compositor.TransitionProgress = 0.0;
            }

            if (SliderTBar != null && SliderTBar.Value != 0)
            {
                bool prevAutoTaking = _isTBarAutoTaking;
                _isTBarAutoTaking = true;
                SliderTBar.Value = 0;
                if (TxtTBarPercent != null) TxtTBarPercent.Text = "0%";
                _isTBarAutoTaking = prevAutoTaking;
            }

            _scheduler.ExecuteTake();
        }

        private void BtnMasterHold_Click(object sender, RoutedEventArgs e)
        {
            _scheduler.ExecuteHold();
            if (BtnMasterHold != null)
            {
                BtnMasterHold.Content = _scheduler.IsHold ? "▶ RESUME" : "⏸ HOLD";
                BtnMasterHold.Background = _scheduler.IsHold ? new SolidColorBrush(Color.FromRgb(245, 158, 11)) : new SolidColorBrush(Color.FromRgb(30, 41, 59));
                BtnMasterHold.Foreground = _scheduler.IsHold ? Brushes.Black : new SolidColorBrush(Color.FromRgb(245, 158, 11));
            }
        }

        private void BtnMasterSkip_Click(object sender, RoutedEventArgs e)
        {
            _scheduler.ExecuteSkip();
        }

        private void BtnMasterCue_Click(object sender, RoutedEventArgs e)
        {
            if (GridRundown.SelectedIndex >= 0)
            {
                _scheduler.ExecuteCue(GridRundown.SelectedIndex);
            }
        }

        private void BtnAutoModeToggle_Click(object sender, RoutedEventArgs e) => ToggleAutoMode();

        private void ToggleAutoMode()
        {
            _scheduler.IsAutoMode = !_scheduler.IsAutoMode;
            if (TxtAutoBadge != null)
            {
                TxtAutoBadge.Text = _scheduler.AutoModeBadge;
                TxtAutoBadge.Foreground = _scheduler.IsAutoMode ?
                    new SolidColorBrush(Color.FromRgb(52, 211, 153)) :
                    new SolidColorBrush(Color.FromRgb(251, 191, 36));
            }
            if (BtnAutoModeToggle != null)
            {
                BtnAutoModeToggle.Background = _scheduler.IsAutoMode ?
                    new SolidColorBrush(Color.FromArgb(255, 6, 78, 59)) :
                    new SolidColorBrush(Color.FromArgb(255, 120, 53, 15));
            }
        }

        private void BtnEmergencyBars_Click(object sender, RoutedEventArgs e) => ToggleEmergencyBars();

        private void ToggleEmergencyBars()
        {
            if (_compositor.FailSafeEngine.IsFallbackActive)
            {
                _compositor.FailSafeEngine.DismissFallback();
            }
            else
            {
                _compositor.FailSafeEngine.ForceTestFallback = !_compositor.FailSafeEngine.ForceTestFallback;
            }
        }

        private void BtnFtb_Click(object sender, RoutedEventArgs e)
        {
            _isFtbActive = !_isFtbActive;
            _compositor.IsFadeToBlack = _isFtbActive;
            if (BtnMasterFtb != null)
            {
                BtnMasterFtb.Background = _isFtbActive ? new SolidColorBrush(Color.FromRgb(220, 38, 38)) : new SolidColorBrush(Color.FromRgb(30, 41, 59));
                BtnMasterFtb.Foreground = _isFtbActive ? Brushes.White : new SolidColorBrush(Color.FromRgb(239, 68, 68));
            }
        }

        private void BtnCut_Click(object sender, RoutedEventArgs e)
        {
            ExecuteMasterTake();
            if (SliderTBar != null) SliderTBar.Value = 0;
        }

        private void BtnFade_Click(object sender, RoutedEventArgs e)
        {
            ExecuteFade(1000);
        }

        public void ExecuteFade(double durationMs)
        {
            if (_fadeTimer != null && _fadeTimer.IsEnabled) return;

            _fadeDurationMs = durationMs;
            _fadeStartTime = DateTime.UtcNow;
            _fadeStartVal = SliderTBar?.Value ?? 0;
            _fadeTargetVal = _fadeStartVal < 50 ? 100 : 0;

            _fadeTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16.6)
            };
            _fadeTimer.Tick += OnFadeTimerTick;
            _fadeTimer.Start();
        }

        private void OnFadeTimerTick(object? sender, EventArgs e)
        {
            double elapsed = (DateTime.UtcNow - _fadeStartTime).TotalMilliseconds;
            double progress = Math.Clamp(elapsed / _fadeDurationMs, 0.0, 1.0);

            double currentVal = _fadeStartVal + (_fadeTargetVal - _fadeStartVal) * progress;
            if (SliderTBar != null) SliderTBar.Value = currentVal;

            if (progress >= 1.0)
            {
                _fadeTimer?.Stop();
                _fadeTimer = null;
                ExecuteMasterTake();
                if (SliderTBar != null) SliderTBar.Value = 0;
            }
        }

        private void SliderTBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isLoaded || _compositor == null || SliderTBar == null) return;
            if (_isTBarAutoTaking) return;

            double val = SliderTBar.Value;
            _compositor.TransitionProgress = Math.Clamp(val / 100.0, 0.0, 1.0);
            if (TxtTBarPercent != null) TxtTBarPercent.Text = $"{(int)val}%";

            // Auto-take when manually dragged to 100% (not during fade animation)
            if (val >= 100.0 && (_fadeTimer == null || !_fadeTimer.IsEnabled))
            {
                _isTBarAutoTaking = true;
                ExecuteMasterTake();
                _isTBarAutoTaking = false;
            }
        }

        #endregion

        #region DSK Master Overlays & SCTE-35 Trigger

        private void BtnDskLogo_Click(object sender, RoutedEventArgs e)
        {
            _compositor.GraphicsEngine.IsLogoOnAir = !_compositor.GraphicsEngine.IsLogoOnAir;
            UpdateDskButtonVisuals();
        }

        private void BtnDskLowerThird_Click(object sender, RoutedEventArgs e)
        {
            _compositor.GraphicsEngine.IsLowerThirdOnAir = !_compositor.GraphicsEngine.IsLowerThirdOnAir;
            UpdateDskButtonVisuals();
        }

        private void BtnDskTicker_Click(object sender, RoutedEventArgs e)
        {
            _compositor.GraphicsEngine.IsTickerOnAir = !_compositor.GraphicsEngine.IsTickerOnAir;
            UpdateDskButtonVisuals();
        }

        private void BtnDskRating_Click(object sender, RoutedEventArgs e)
        {
            _compositor.GraphicsEngine.IsRatingOnAir = !_compositor.GraphicsEngine.IsRatingOnAir;
            UpdateDskButtonVisuals();
        }

        private void UpdateDskButtonVisuals()
        {
            var ge = _compositor.GraphicsEngine;

            if (BtnDskLogo != null)
            {
                BtnDskLogo.Content = ge.IsLogoOnAir ? "DSK 1: LOGO 🟢" : "DSK 1: LOGO";
                BtnDskLogo.Background = ge.IsLogoOnAir ? new SolidColorBrush(Color.FromRgb(30, 58, 95)) : new SolidColorBrush(Color.FromRgb(17, 28, 48));
                BtnDskLogo.Foreground = ge.IsLogoOnAir ? new SolidColorBrush(Color.FromRgb(56, 189, 248)) : new SolidColorBrush(Color.FromRgb(148, 163, 184));
            }

            if (BtnDskLowerThird != null)
            {
                BtnDskLowerThird.Content = ge.IsLowerThirdOnAir ? "DSK 2: LOWER-3RD 🟢" : "DSK 2: LOWER-3RD";
                BtnDskLowerThird.Background = ge.IsLowerThirdOnAir ? new SolidColorBrush(Color.FromRgb(30, 58, 95)) : new SolidColorBrush(Color.FromRgb(17, 28, 48));
                BtnDskLowerThird.Foreground = ge.IsLowerThirdOnAir ? new SolidColorBrush(Color.FromRgb(245, 158, 11)) : new SolidColorBrush(Color.FromRgb(148, 163, 184));
            }

            if (BtnDskTicker != null)
            {
                BtnDskTicker.Content = ge.IsTickerOnAir ? "DSK 3: TICKER 60F 🟢" : "DSK 3: TICKER 60F";
                BtnDskTicker.Background = ge.IsTickerOnAir ? new SolidColorBrush(Color.FromRgb(30, 58, 95)) : new SolidColorBrush(Color.FromRgb(17, 28, 48));
                BtnDskTicker.Foreground = ge.IsTickerOnAir ? new SolidColorBrush(Color.FromRgb(0, 230, 118)) : new SolidColorBrush(Color.FromRgb(148, 163, 184));
            }

            if (BtnDskRating != null)
            {
                BtnDskRating.Content = ge.IsRatingOnAir ? "DSK 4: RATING 16+ 🟢" : "DSK 4: RATING 16+";
                BtnDskRating.Background = ge.IsRatingOnAir ? new SolidColorBrush(Color.FromRgb(69, 10, 10)) : new SolidColorBrush(Color.FromRgb(17, 28, 48));
                BtnDskRating.Foreground = ge.IsRatingOnAir ? new SolidColorBrush(Color.FromRgb(239, 68, 68)) : new SolidColorBrush(Color.FromRgb(148, 163, 184));
            }
        }

        private void BtnScte35Trigger_Click(object sender, RoutedEventArgs e)
        {
            // Inject SCTE-35 commercial ad splice event into rundown
            var adItem = new PlaylistItem
            {
                HouseId = $"SCTE35-{DateTime.Now:HHmmss}",
                Title = "KHỐI QUẢNG CÁO CHÈN SCTE-35 (SPLICE INSERT 30s)",
                SourceType = PlaylistItemSourceType.CommercialBreak,
                Duration = TimeSpan.FromSeconds(30),
                Status = PlaylistItemStatus.Ready,
                IsDskLogo = false,
                IsDskTicker = false
            };
            _scheduler.RundownList.Add(adItem);
            _scheduler.ExecuteCue(_scheduler.RundownList.Count - 1);
        }

        private void BtnApplyLtText_Click(object sender, RoutedEventArgs e)
        {
            if (TxtEditLtTitle != null && TxtEditLtSubtitle != null)
            {
                _compositor.GraphicsEngine.UpdateLowerThird(TxtEditLtTitle.Text, TxtEditLtSubtitle.Text);
            }
        }

        private void BtnApplyTickerText_Click(object sender, RoutedEventArgs e)
        {
            if (TxtEditTickerText != null)
            {
                _compositor.GraphicsEngine.UpdateTickerText(TxtEditTickerText.Text);
            }
        }

        #endregion

        #region Rundown Grid Interactions & Drag-and-Drop

        private void GridRundown_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (GridRundown.SelectedIndex >= 0)
            {
                _scheduler.ExecuteCue(GridRundown.SelectedIndex);
            }
        }

        private void GridRundown_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private void GridRundown_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                foreach (var file in files)
                {
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext == ".mp4" || ext == ".mov" || ext == ".mxf" || ext == ".avi" || ext == ".mkv" || ext == ".ts")
                    {
                        _scheduler.AddClipItem(file);
                    }
                }
            }
        }

        private void BtnAddClipToRundown_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Title = "Nạp Tệp Tin Video Vào Rundown Playlist",
                Filter = "Video Files (*.mp4;*.mov;*.mxf;*.avi;*.mkv;*.ts)|*.mp4;*.mov;*.mxf;*.avi;*.mkv;*.ts|All Files (*.*)|*.*",
                Multiselect = true
            };
            if (ofd.ShowDialog() == true)
            {
                foreach (var file in ofd.FileNames)
                {
                    _scheduler.AddClipItem(file);
                }
            }
        }

        private void BtnAddLiveCam_Click(object sender, RoutedEventArgs e)
        {
            int camPort = (_scheduler.RundownList.Count % 10) + 1;
            _scheduler.AddLiveItem(camPort, $"TRỰC TIẾP LIVE CAM {camPort:D2}", TimeSpan.FromMinutes(5), $"CAM {camPort:D2} • LIVE BROADCAST");
        }

        private void BtnAddBars_Click(object sender, RoutedEventArgs e)
        {
            _scheduler.AddColorBarsItem("BẢNG MÀU HIỆU CHUẨN SMPTE COLOR BARS", TimeSpan.FromMinutes(2));
        }

        private void BtnAddBreak_Click(object sender, RoutedEventArgs e)
        {
            _scheduler.AddCommercialBreakItem("KHỐI QUẢNG CÁO THƯƠNG MẠI SCTE-35 (60s)", TimeSpan.FromSeconds(60));
        }

        private void BtnMoveUp_Click(object sender, RoutedEventArgs e)
        {
            int idx = GridRundown.SelectedIndex;
            if (idx > 0)
            {
                _scheduler.MoveItemUp(idx);
                GridRundown.SelectedIndex = idx - 1;
            }
        }

        private void BtnMoveDown_Click(object sender, RoutedEventArgs e)
        {
            int idx = GridRundown.SelectedIndex;
            if (idx >= 0 && idx < _scheduler.RundownList.Count - 1)
            {
                _scheduler.MoveItemDown(idx);
                GridRundown.SelectedIndex = idx + 1;
            }
        }

        private void BtnRemoveItem_Click(object sender, RoutedEventArgs e)
        {
            int idx = GridRundown.SelectedIndex;
            if (idx >= 0)
            {
                _scheduler.RemoveItem(idx);
                if (GridRundown.Items.Count > 0)
                {
                    GridRundown.SelectedIndex = Math.Clamp(idx, 0, GridRundown.Items.Count - 1);
                }
            }
        }

        private void BtnClearRundown_Click(object sender, RoutedEventArgs e)
        {
            var res = MessageBox.Show("Bạn có chắc chắn muốn xóa toàn bộ sự kiện trong Rundown Playlist?", "Xác nhận xóa Rundown", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res == MessageBoxResult.Yes)
            {
                _scheduler.Clear();
            }
        }

        private void BtnSavePlaylist_Click(object sender, RoutedEventArgs e)
        {
            var sfd = new SaveFileDialog
            {
                Title = "Lưu Danh Sách Phát Rundown Playlist",
                Filter = "Playout Playlist (*.json)|*.json|All Files (*.*)|*.*",
                FileName = $"Rundown_{DateTime.Now:yyyyMMdd_HHmm}.json"
            };
            if (sfd.ShowDialog() == true)
            {
                try
                {
                    _scheduler.SavePlaylistJson(sfd.FileName);
                    MessageBox.Show($"Đã lưu danh sách phát ({_scheduler.RundownList.Count} mục) thành công!", "Lưu Playlist", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lỗi lưu playlist: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnLoadPlaylist_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Title = "Mở Danh Sách Phát Rundown Playlist",
                Filter = "Playout Playlist (*.json)|*.json|All Files (*.*)|*.*"
            };
            if (ofd.ShowDialog() == true)
            {
                try
                {
                    if (_scheduler.LoadPlaylistJson(ofd.FileName))
                    {
                        MessageBox.Show($"Đã nạp danh sách phát ({_scheduler.RundownList.Count} mục) thành công!", "Nạp Playlist", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show("Không thể đọc tệp playlist hoặc tệp không đúng định dạng.", "Lỗi nạp Playlist", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lỗi mở playlist: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #endregion

        #region Collapsible Side Panels (Left Preview & Right Tools Drawer)

        private double _lastLeftPanelWidth = 390;
        private double _lastRightPanelWidth = 450;

        private void SetLeftPanelVisible(bool visible)
        {
            if (LeftPreviewDrawer == null) return;

            if (visible)
            {
                LeftPreviewDrawer.Visibility = Visibility.Visible;
                if (SplitterLeft != null) SplitterLeft.Visibility = Visibility.Visible;
                if (ColLeftPanel != null)
                {
                    ColLeftPanel.MinWidth = 260;
                    ColLeftPanel.MaxWidth = 680;
                    ColLeftPanel.Width = new GridLength(_lastLeftPanelWidth > 200 ? _lastLeftPanelWidth : 390);
                }
            }
            else
            {
                if (ColLeftPanel != null && ColLeftPanel.ActualWidth > 0)
                {
                    _lastLeftPanelWidth = ColLeftPanel.ActualWidth;
                }
                LeftPreviewDrawer.Visibility = Visibility.Collapsed;
                if (SplitterLeft != null) SplitterLeft.Visibility = Visibility.Collapsed;
                if (ColLeftPanel != null)
                {
                    ColLeftPanel.MinWidth = 0;
                    ColLeftPanel.Width = new GridLength(0);
                }
            }
        }

        private void SetRightPanelVisible(bool visible)
        {
            if (DrawerContainer == null) return;

            if (visible)
            {
                DrawerContainer.Visibility = Visibility.Visible;
                if (SplitterRight != null) SplitterRight.Visibility = Visibility.Visible;
                if (ColRightPanel != null)
                {
                    ColRightPanel.MinWidth = 300;
                    ColRightPanel.MaxWidth = 720;
                    ColRightPanel.Width = new GridLength(_lastRightPanelWidth > 200 ? _lastRightPanelWidth : 450);
                }
            }
            else
            {
                if (ColRightPanel != null && ColRightPanel.ActualWidth > 0)
                {
                    _lastRightPanelWidth = ColRightPanel.ActualWidth;
                }
                DrawerContainer.Visibility = Visibility.Collapsed;
                if (SplitterRight != null) SplitterRight.Visibility = Visibility.Collapsed;
                if (ColRightPanel != null)
                {
                    ColRightPanel.MinWidth = 0;
                    ColRightPanel.Width = new GridLength(0);
                }
            }
        }

        private void BtnToggleLeftPreview_Click(object sender, RoutedEventArgs e)
        {
            bool isCurrentlyVisible = LeftPreviewDrawer != null && LeftPreviewDrawer.Visibility == Visibility.Visible;
            SetLeftPanelVisible(!isCurrentlyVisible);
        }

        private void BtnToggleSideTools_Click(object sender, RoutedEventArgs e)
        {
            bool isCurrentlyVisible = DrawerContainer != null && DrawerContainer.Visibility == Visibility.Visible;
            SetRightPanelVisible(!isCurrentlyVisible);
        }

        private void OpenDrawerWithTab(TabItem? tab, string title)
        {
            SetRightPanelVisible(true);
            if (TxtDrawerTitle != null) TxtDrawerTitle.Text = title;
            if (TabsPlayoutControl != null && tab != null) TabsPlayoutControl.SelectedItem = tab;
        }

        private void BtnCloseDrawer_Click(object sender, RoutedEventArgs e)
        {
            SetRightPanelVisible(false);
        }

        private TabItem? TabItemCcu => TabItemCcuScopes;
        private TabItem? TabItemScopes => TabItemCcuScopes;

        private void BtnDrawerDve_Click(object sender, RoutedEventArgs e) => OpenDrawerWithTab(TabItemDve, "7. DVE MULTI-BOX COMPOSITOR");
        private void BtnDrawerCcu_Click(object sender, RoutedEventArgs e) => OpenDrawerWithTab(TabItemCcuScopes, "3. CCU / LUT & BROADCAST SCOPES");
        private void BtnQuickScopes_Click(object sender, RoutedEventArgs e) => OpenDrawerWithTab(TabItemCcuScopes, "3. CCU / LUT & BROADCAST SCOPES");
        private void BtnDrawerLipSync_Click(object sender, RoutedEventArgs e) => OpenDrawerWithTab(TabItemLipSync, "4. LIP-SYNC DELAY & SYNC");
        private void BtnDrawerFailSafe_Click(object sender, RoutedEventArgs e) => OpenDrawerWithTab(TabItemFailSafe, "5. FAIL-SAFE EMERGENCY");
        private void BtnDrawerBroadcastOut_Click(object sender, RoutedEventArgs e) => OpenDrawerWithTab(TabItemBroadcastOut, "1. PGM OUTPUT (SDI & NDI)");
        private void BtnDrawerRouter_Click(object sender, RoutedEventArgs e) => OpenDrawerWithTab(TabItemRouter, "6. CROSSPOINT ROUTER IP");
        private void BtnDrawerCg_Click(object sender, RoutedEventArgs e) => OpenDrawerWithTab(TabItemCg, "2. BROADCAST GRAPHICS (CG LOGO)");
        private void BtnDrawerPreviewIp_Click(object sender, RoutedEventArgs e)
        {
            SetLeftPanelVisible(true);
            if (TabsLeftPreview != null && TabItemPreviewIp != null) TabsLeftPreview.SelectedItem = TabItemPreviewIp;
        }
        private void BtnDrawerPreviewNdi_Click(object sender, RoutedEventArgs e)
        {
            SetLeftPanelVisible(true);
            if (TabsLeftPreview != null && TabItemPreviewNdi != null) TabsLeftPreview.SelectedItem = TabItemPreviewNdi;
        }

        #region Scalable Layout & Zoom Event Handlers

        private void SliderCardScale_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isLoaded) return;
            double scale = e.NewValue;
            if (ScaleTransformLeftCards != null)
            {
                ScaleTransformLeftCards.ScaleX = scale;
                ScaleTransformLeftCards.ScaleY = scale;
            }
            if (ScaleTransformNdiCards != null)
            {
                ScaleTransformNdiCards.ScaleX = scale;
                ScaleTransformNdiCards.ScaleY = scale;
            }
        }

        private void CmbMasterUiScale_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded || MasterUiScale == null || CmbMasterUiScale == null) return;
            double scale = CmbMasterUiScale.SelectedIndex switch
            {
                0 => 0.90,
                1 => 1.00,
                2 => 1.15,
                3 => 1.25,
                _ => 1.00
            };
            MasterUiScale.ScaleX = scale;
            MasterUiScale.ScaleY = scale;
        }

        private void BtnResetLayout_Click(object sender, RoutedEventArgs e)
        {
            // Reset Left Panel Width
            _lastLeftPanelWidth = 390;
            if (ColLeftPanel != null)
            {
                ColLeftPanel.MinWidth = 260;
                ColLeftPanel.MaxWidth = 680;
                ColLeftPanel.Width = new GridLength(390);
            }
            if (LeftPreviewDrawer != null) LeftPreviewDrawer.Visibility = Visibility.Visible;
            if (SplitterLeft != null) SplitterLeft.Visibility = Visibility.Visible;

            // Reset Right Panel Width
            _lastRightPanelWidth = 450;
            if (ColRightPanel != null)
            {
                ColRightPanel.MinWidth = 300;
                ColRightPanel.MaxWidth = 720;
                ColRightPanel.Width = new GridLength(450);
            }
            if (DrawerContainer != null) DrawerContainer.Visibility = Visibility.Visible;
            if (SplitterRight != null) SplitterRight.Visibility = Visibility.Visible;

            // Reset Top Deck & Rundown Height
            if (RowTopControlDeck != null)
            {
                RowTopControlDeck.Height = new GridLength(1, GridUnitType.Star);
            }
            if (RowRundownPlaylist != null)
            {
                RowRundownPlaylist.Height = new GridLength(260);
            }

            // Reset Card Scale
            if (SliderCardScale != null) SliderCardScale.Value = 1.0;

            // Reset Master UI Scale
            if (CmbMasterUiScale != null) CmbMasterUiScale.SelectedIndex = 1;

            // Reset Playlist Row Scale
            if (CmbPlaylistRowScale != null) CmbPlaylistRowScale.SelectedIndex = 1;
        }

        private void BtnLayoutBalance_Click(object sender, RoutedEventArgs e)
        {
            if (RowTopControlDeck != null) RowTopControlDeck.Height = new GridLength(1, GridUnitType.Star);
            if (RowRundownPlaylist != null) RowRundownPlaylist.Height = new GridLength(260);
        }

        private void BtnLayoutExpandList_Click(object sender, RoutedEventArgs e)
        {
            // Thu nhỏ tối đa PGM / CUE Preview để mở rộng toàn bộ bảng Rundown List
            if (RowTopControlDeck != null) RowTopControlDeck.Height = new GridLength(155);
            if (RowRundownPlaylist != null) RowRundownPlaylist.Height = new GridLength(1, GridUnitType.Star);
        }

        private void BtnLayoutExpandPreview_Click(object sender, RoutedEventArgs e)
        {
            // Mở rộng tối đa PGM / CUE Preview để xem chi tiết video phát sóng
            if (RowTopControlDeck != null) RowTopControlDeck.Height = new GridLength(1, GridUnitType.Star);
            if (RowRundownPlaylist != null) RowRundownPlaylist.Height = new GridLength(130);
        }

        private void CmbPlaylistRowScale_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded || GridRundown == null || CmbPlaylistRowScale == null) return;
            switch (CmbPlaylistRowScale.SelectedIndex)
            {
                case 0:
                    GridRundown.RowHeight = 24;
                    GridRundown.FontSize = 10;
                    break;
                case 1:
                    GridRundown.RowHeight = 30;
                    GridRundown.FontSize = 11;
                    break;
                case 2:
                    GridRundown.RowHeight = 38;
                    GridRundown.FontSize = 12;
                    break;
            }
        }

        #endregion

        #endregion

        #region IP Matrix & NDI LAN Preview Handlers

        public void SyncAssignedSlotsToPreview()
        {
            if (_ipScanner == null) return;

            // 1. Tạo bản đồ Slot -> Port hiện đang được gán từ Router Playout (Port 0..10)
            var slotToPortMap = new Dictionary<int, int>();
            for (int port = 0; port <= 10; port++)
            {
                int slot = _compositor.GetPortRoute(port);
                if (slot >= 0 && slot < GpuMatrixCompositor.TotalMatrixSlots)
                {
                    slotToPortMap[slot] = port;
                }
            }

            // 2. Cập nhật thuộc tính AssignedPlayoutPort cho từng nguồn IP
            foreach (var src in _ipScanner.DiscoveredSources)
            {
                if (slotToPortMap.TryGetValue(src.SlotIndex, out int port))
                {
                    src.AssignedPlayoutPort = port;
                }
                else
                {
                    src.AssignedPlayoutPort = -1;
                }
            }

            // 3. Làm mới filter view nếu đang ở chế độ xem slot đã gán
            if (ListIpPreviewSources != null)
            {
                var view = System.Windows.Data.CollectionViewSource.GetDefaultView(_ipScanner.DiscoveredSources);
                view?.Refresh();
            }
        }

        private void BtnScan32MatrixPorts_Click(object sender, RoutedEventArgs e)
        {
            _ipScanner?.ScanOnce();
            PopulateRouterCrosspoint();
            SyncAssignedSlotsToPreview();
            UpdateSummaryBadges();

            if (TxtRouterMatrixSummary != null)
            {
                int active = _ipScanner?.ActiveCount ?? 0;
                TxtRouterMatrixSummary.Text = $"Đã quét 32 slot VRAM • {active} nguồn Online (0ms)";
            }
        }

        private void BtnRescanChangingIps_Click(object sender, RoutedEventArgs e)
        {
            // Quét lại các IP đang thay đổi nhưng giữ nguyên cấu hình các Slot và Port 0..10 đang hiển thị
            _ipScanner?.ScanOnce();
            PopulateRouterCrosspoint();
            SyncAssignedSlotsToPreview();
            UpdateSummaryBadges();

            if (TxtRouterMatrixSummary != null)
            {
                int active = _ipScanner?.ActiveCount ?? 0;
                TxtRouterMatrixSummary.Text = $"Đã quét IP đang đổi ({active} Online) • Giữ nguyên các Slot hiển thị";
            }
        }

        private async void BtnScanNdiInRouter_Click(object sender, RoutedEventArgs e)
        {
            if (_ndiScanner != null)
            {
                if (TxtRouterMatrixSummary != null)
                {
                    TxtRouterMatrixSummary.Text = "Đang dò tìm luồng NDI LAN...";
                }
                await _ndiScanner.ScanAsync();
                UpdateSummaryBadges();

                if (TxtRouterMatrixSummary != null)
                {
                    TxtRouterMatrixSummary.Text = $"Dò NDI LAN: {_ndiScanner.OnlineCount} luồng sẵn sàng";
                }
            }
        }

        private void BtnSyncToLeftPreview_Click(object sender, RoutedEventArgs e)
        {
            SyncAssignedSlotsToPreview();
            if (TxtRouterMatrixSummary != null)
            {
                TxtRouterMatrixSummary.Text = "Đã đồng bộ toàn bộ slot sang Preview Trái!";
            }
        }

        private void UpdateSummaryBadges()
        {
            if (_isShuttingDown) return;

            int ipActive = _ipScanner?.ActiveCount ?? 0;
            int ipTotal = _ipScanner?.TotalCount ?? 0;
            int ndiOnline = _ndiScanner?.OnlineCount ?? 0;
            int ndiTotal = _ndiScanner?.TotalCount ?? 0;

            if (TxtHeaderPreviewBadge != null)
                TxtHeaderPreviewBadge.Text = $"{ipActive} IP | {ndiOnline} NDI";

            if (TxtLeftPanelBadge != null)
                TxtLeftPanelBadge.Text = $"IP: {ipActive}/{ipTotal} • NDI: {ndiOnline}/{ndiTotal}";

            if (TxtRightPanelBadge != null)
                TxtRightPanelBadge.Text = $"IP: {ipActive}/{ipTotal} • NDI: {ndiOnline}/{ndiTotal}";

            if (TxtIpScanSummary != null)
                TxtIpScanSummary.Text = $"32 CỔNG MATRIX ({ipActive} ONLINE 0ms)";

            if (TxtNdiScanSummary != null)
                TxtNdiScanSummary.Text = $"LAN GIGABIT ({ndiOnline} ONLINE)";
        }

        private void BtnRescanIp_Click(object sender, RoutedEventArgs e)
        {
            _ipScanner?.ScanOnce();
            PopulateRouterCrosspoint();
            SyncAssignedSlotsToPreview();
            UpdateSummaryBadges();
        }

        private void ChkAutoScanIp_Click(object sender, RoutedEventArgs e)
        {
            if (_ipScanner != null && ChkAutoScanIp != null)
            {
                _ipScanner.IsAutoScanEnabled = ChkAutoScanIp.IsChecked == true;
            }
        }

        private void CmbFilterIp_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_ipScanner == null || CmbFilterIp == null) return;
            string filter = CmbFilterIp.SelectedIndex switch
            {
                0 => "ASSIGNED_ACTIVE",
                1 => "ACTIVE",
                2 => "SRT",
                3 => "WEBRTC",
                _ => "ALL"
            };
            _ipScanner.FilterMode = filter;

            if (ListIpPreviewSources != null)
            {
                var view = System.Windows.Data.CollectionViewSource.GetDefaultView(_ipScanner.DiscoveredSources);
                if (view != null)
                {
                    view.Filter = obj =>
                    {
                        if (obj is not IpPreviewItem item) return false;
                        return filter switch
                        {
                            "ASSIGNED_ACTIVE" => item.IsAssigned && item.IsActive,
                            "ACTIVE" => item.IsActive,
                            "SRT" => item.Origin.Contains("SRT", StringComparison.OrdinalIgnoreCase),
                            "WEBRTC" => item.Origin.Contains("WebRTC", StringComparison.OrdinalIgnoreCase),
                            _ => true
                        };
                    };
                }
            }
        }

        private void BtnCueIpItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is IpPreviewItem item)
            {
                _compositor.CueSlotDirect(item.SlotIndex);
                _currentPreviewSourceIndex = 10;

                foreach (var s in _ipScanner?.DiscoveredSources ?? Enumerable.Empty<IpPreviewItem>())
                {
                    s.IsCued = (s == item);
                }

                if (TxtNextHouseId != null) TxtNextHouseId.Text = $"IP-SLOT-{item.SlotIndex:D2}";
                if (TxtNextEventTitle != null) TxtNextEventTitle.Text = item.DisplayName;
                if (TxtNextSourceBadge != null) TxtNextSourceBadge.Text = $"[{item.Origin.ToUpperInvariant()} CUED]";
            }
        }

        private void BtnTakeIpItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is IpPreviewItem item)
            {
                _compositor.TakeSlotDirect(item.SlotIndex);
                _currentProgramSourceIndex = 0;

                foreach (var s in _ipScanner?.DiscoveredSources ?? Enumerable.Empty<IpPreviewItem>())
                {
                    s.IsOnAir = (s == item);
                }

                if (TxtCurrentHouseId != null) TxtCurrentHouseId.Text = $"IP-SLOT-{item.SlotIndex:D2}";
                if (TxtCurrentEventTitle != null) TxtCurrentEventTitle.Text = item.DisplayName;
                if (TxtCurrentSourceBadge != null) TxtCurrentSourceBadge.Text = $"[🔴 {item.Origin.ToUpperInvariant()} ON-AIR]";
            }
        }

        private void BtnAddIpToRundown_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is IpPreviewItem item)
            {
                int port = item.AssignedPlayoutPort >= 0 ? item.AssignedPlayoutPort : 1;
                _scheduler.AddLiveItem(port, $"[IP] {item.DisplayName}", TimeSpan.FromMinutes(5), $"{item.Origin} • {item.ResolutionText}");
                MessageBox.Show($"Đã thêm nguồn IP '{item.DisplayName}' (Slot {item.SlotIndex}) vào Rundown Playlist!", "Thêm vào Rundown", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnAssignIpPort_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is IpPreviewItem item)
            {
                // Gán luân phiên vào Port 1..10
                int targetPort = item.AssignedPlayoutPort >= 1 && item.AssignedPlayoutPort < 10 
                    ? item.AssignedPlayoutPort + 1 
                    : 1;

                _compositor.SetPortRoute(targetPort, item.SlotIndex);
                SyncAssignedSlotsToPreview();
                PopulateRouterCrosspoint();

                MessageBox.Show($"Đã gán thành công {item.DisplayName} (Slot {item.SlotIndex}) vào Playout Port {targetPort}!", "Gán Cổng Playout", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnQuickAssignSlot_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is IpPreviewItem item)
            {
                // Nếu là slot 0 thì ưu tiên gán vào Port 0 (PGM Ingest)
                int targetPort;
                if (item.SlotIndex == 0)
                {
                    targetPort = item.AssignedPlayoutPort == 0 ? 1 : 0;
                }
                else
                {
                    // Luân phiên các cổng ISO CAM 1..10
                    targetPort = item.AssignedPlayoutPort >= 1 && item.AssignedPlayoutPort < 10 
                        ? item.AssignedPlayoutPort + 1 
                        : (item.AssignedPlayoutPort == 10 ? 1 : Math.Clamp(item.SlotIndex, 1, 10));
                }

                _compositor.SetPortRoute(targetPort, item.SlotIndex);
                SyncAssignedSlotsToPreview();
                PopulateRouterCrosspoint();

                if (TxtRouterMatrixSummary != null)
                {
                    TxtRouterMatrixSummary.Text = $"Đã gán {item.DisplayName} (Slot {item.SlotIndex}) ➔ Port {targetPort}!";
                }
            }
        }

        private void BtnToggleAudioMonitor_Click(object sender, RoutedEventArgs e)
        {
            if (_compositor == null) return;
            bool isMuted = !_compositor.AudioMonitor.IsMuted;
            _compositor.AudioMonitor.IsMuted = isMuted;
            if (BtnToggleAudioMonitor != null)
            {
                BtnToggleAudioMonitor.Content = isMuted ? "🔇" : "🔊";
                BtnToggleAudioMonitor.Foreground = isMuted ? Brushes.Red : new SolidColorBrush(Color.FromRgb(0, 229, 255));
            }
        }

        private void SliderMonitorVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_compositor == null || SliderMonitorVolume == null) return;
            _compositor.AudioMonitor.Volume = SliderMonitorVolume.Value;
        }

        private async void BtnRescanNdi_Click(object sender, RoutedEventArgs e)
        {
            if (_ndiScanner != null)
            {
                await _ndiScanner.ScanAsync();
                UpdateSummaryBadges();
            }
        }

        private void ChkAutoScanNdi_Click(object sender, RoutedEventArgs e)
        {
            if (_ndiScanner != null && ChkAutoScanNdi != null)
            {
                _ndiScanner.IsAutoScanEnabled = ChkAutoScanNdi.IsChecked == true;
            }
        }

        private void BtnCueNdiItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is NdiPreviewItem item)
            {
                ConnectNdiToPlayout(item, isTake: false);
                foreach (var s in _ndiScanner?.DiscoveredSources ?? Enumerable.Empty<NdiPreviewItem>())
                {
                    s.IsCued = (s == item);
                }

                if (TxtNextHouseId != null) TxtNextHouseId.Text = "NDI-LAN";
                if (TxtNextEventTitle != null) TxtNextEventTitle.Text = item.StreamName;
                if (TxtNextSourceBadge != null) TxtNextSourceBadge.Text = "[🌐 NDI LAN CUED]";
            }
        }

        private void BtnTakeNdiItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is NdiPreviewItem item)
            {
                ConnectNdiToPlayout(item, isTake: true);
                foreach (var s in _ndiScanner?.DiscoveredSources ?? Enumerable.Empty<NdiPreviewItem>())
                {
                    s.IsOnAir = (s == item);
                }

                if (TxtCurrentHouseId != null) TxtCurrentHouseId.Text = "NDI-LAN";
                if (TxtCurrentEventTitle != null) TxtCurrentEventTitle.Text = item.StreamName;
                if (TxtCurrentSourceBadge != null) TxtCurrentSourceBadge.Text = "[🔴 NDI LAN ON-AIR]";
            }
        }

        private void BtnAddNdiToRundown_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is NdiPreviewItem item)
            {
                _scheduler.AddLiveItem(0, $"[NDI] {item.StreamName}", TimeSpan.FromMinutes(5), $"{item.MachineName} • LAN NDI Stream");
                MessageBox.Show($"Đã thêm luồng NDI '{item.StreamName}' vào Rundown Playlist!", "Thêm vào Rundown", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ConnectNdiToPlayout(NdiPreviewItem item, bool isTake)
        {
            if (_ndiPlayoutReceiver == null || !_ndiPlayoutReceiver.SourceName.Equals(item.StreamName, StringComparison.OrdinalIgnoreCase))
            {
                _ndiPlayoutReceiver?.Stop();
                _ndiPlayoutReceiver?.Dispose();

                _ndiPlayoutReceiver = new NdiNativeReceiver(item.StreamName);
                _ndiPlayoutReceiver.VideoFrameReceived += (bgraData, width, height, stride, fps) =>
                {
                    _compositor.FeedNdiLiveFrame(bgraData, width, height, stride);
                };
                _ndiPlayoutReceiver.Start();
            }

            if (isTake)
            {
                _compositor.IsOnAirClip = false;
                _compositor.SetNdiOnAir(true);
            }
            else
            {
                _compositor.IsPreviewClip = false;
                _compositor.SetNdiCued(true);
            }

            _ndiScanner?.AttachLivePreview(item);
        }

        #endregion

        #region CCU & Color Correction & 3D LUT

        private ColorGradingProfile GetActiveCcuProfile()
        {
            int targetMode = -1;
            if (CmbCcuTargetChannel?.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag != null)
            {
                if (int.TryParse(selectedItem.Tag.ToString(), out int parsedTag))
                {
                    targetMode = parsedTag;
                }
            }

            if (targetMode == -2)
            {
                if (GridRundown?.SelectedItem is PlaylistItem item)
                {
                    if (item.SourceType == PlaylistItemSourceType.LiveIngest)
                    {
                        return _compositor.ColorEngine.GetSlotProfile(item.LivePortIndex);
                    }
                    else
                    {
                        string clipKey = !string.IsNullOrEmpty(item.FilePath) ? item.FilePath : item.Title;
                        return _compositor.ColorEngine.GetClipProfile(clipKey);
                    }
                }
                return _compositor.ColorEngine.MasterProfile;
            }
            else if (targetMode >= 1 && targetMode <= 10)
            {
                return _compositor.ColorEngine.GetSlotProfile(targetMode);
            }
            else
            {
                return _compositor.ColorEngine.MasterProfile;
            }
        }

        private void LoadProfileToUi(ColorGradingProfile profile)
        {
            _isUpdatingCcuUi = true;
            try
            {
                var p = profile.Parameters;
                if (SliderGain != null) SliderGain.Value = p.GainR;
                if (SliderGamma != null) SliderGamma.Value = p.GammaR;
                if (SliderLift != null) SliderLift.Value = p.LiftR;
                if (SliderSaturation != null) SliderSaturation.Value = p.Saturation;

                if (TxtCcuGainVal != null) TxtCcuGainVal.Text = p.GainR.ToString("F2");
                if (TxtCcuGammaVal != null) TxtCcuGammaVal.Text = p.GammaR.ToString("F2");
                if (TxtCcuLiftVal != null) TxtCcuLiftVal.Text = p.LiftR.ToString("F2");
                if (TxtCcuSatVal != null) TxtCcuSatVal.Text = p.Saturation.ToString("F2");

                if (TxtActiveLutName != null)
                {
                    if (profile.IsLutEnabled && (profile.FlatLut != null || profile.LutData != null))
                    {
                        TxtActiveLutName.Text = $"LUT: {profile.LoadedLutName} ({profile.LutSize}³)";
                        TxtActiveLutName.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
                    }
                    else
                    {
                        TxtActiveLutName.Text = "LUT: Bypass";
                        TxtActiveLutName.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
                    }
                }
            }
            finally
            {
                _isUpdatingCcuUi = false;
            }
        }

        private void SliderCcu_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isLoaded || _compositor == null || _isUpdatingCcuUi) return;
            var profile = GetActiveCcuProfile();
            var p = profile.Parameters;
            if (SliderGain != null)
            {
                p.GainR = p.GainG = p.GainB = (float)SliderGain.Value;
                if (TxtCcuGainVal != null) TxtCcuGainVal.Text = SliderGain.Value.ToString("F2");
            }
            if (SliderGamma != null)
            {
                p.GammaR = p.GammaG = p.GammaB = (float)SliderGamma.Value;
                if (TxtCcuGammaVal != null) TxtCcuGammaVal.Text = SliderGamma.Value.ToString("F2");
            }
            if (SliderLift != null)
            {
                p.LiftR = p.LiftG = p.LiftB = (float)SliderLift.Value;
                if (TxtCcuLiftVal != null) TxtCcuLiftVal.Text = SliderLift.Value.ToString("F2");
            }
            if (SliderSaturation != null)
            {
                p.Saturation = (float)SliderSaturation.Value;
                if (TxtCcuSatVal != null) TxtCcuSatVal.Text = SliderSaturation.Value.ToString("F2");
            }
            profile.Invalidate();
        }

        private void BtnResetCcu_Click(object sender, RoutedEventArgs e)
        {
            var profile = GetActiveCcuProfile();
            profile.Parameters.Reset();
            profile.Invalidate();
            LoadProfileToUi(profile);
        }

        private void CmbCcuTargetChannel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded || _compositor == null) return;
            UpdateCcuAndScopeTarget();
        }

        private void BtnLutPreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                var profile = GetActiveCcuProfile();
                var p = profile.Parameters;
                p.Reset();
                switch (tag)
                {
                    case "warm":
                        p.GainR = 1.15f;
                        p.GainB = 0.88f;
                        break;
                    case "teal_orange":
                        p.LiftB = 0.08f;
                        p.GainR = 1.12f;
                        p.Saturation = 1.25f;
                        break;
                    case "rec709":
                        p.Contrast = 1.1f;
                        p.Saturation = 1.05f;
                        break;
                    default:
                        profile.UnloadLut();
                        break;
                }
                profile.Invalidate();
                LoadProfileToUi(profile);
            }
        }

        private void BtnLoadCustomLut_Click(object sender, RoutedEventArgs e)
        {
            var profile = GetActiveCcuProfile();
            var ofd = new OpenFileDialog
            {
                Title = "Chọn Tệp Tin 3D LUT (.cube) Áp Dụng Cho Đối Tượng Đã Chọn",
                Filter = "Cube 3D LUT (*.cube)|*.cube|Tất cả tệp (*.*)|*.*"
            };
            if (ofd.ShowDialog() == true)
            {
                if (profile.LoadCubeFile(ofd.FileName))
                {
                    profile.Invalidate();
                    LoadProfileToUi(profile);
                }
                else
                {
                    MessageBox.Show("Không thể đọc tệp .cube. Vui lòng đảm bảo tệp đúng chuẩn 3D LUT Cube (17^3, 33^3 hoặc 65^3).", "Lỗi nạp 3D LUT", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void BtnClearLut_Click(object sender, RoutedEventArgs e)
        {
            var profile = GetActiveCcuProfile();
            profile.UnloadLut();
            profile.Invalidate();
            LoadProfileToUi(profile);
        }

        #endregion

        #region Broadcast Scopes & Playlist Target Tracking

        private void GridRundown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateCcuAndScopeTarget();
        }

        private void UpdateCcuAndScopeTarget()
        {
            if (!_isLoaded || _compositor == null) return;

            int targetMode = -1; // -1: PGM, -2: Rundown Follow, 1..10: Specific Slot
            if (CmbCcuTargetChannel?.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag != null)
            {
                if (int.TryParse(selectedItem.Tag.ToString(), out int parsedTag))
                {
                    targetMode = parsedTag;
                }
            }

            ColorGradingProfile activeProfile;

            if (targetMode == -2)
            {
                // Theo dòng Playlist Rundown
                if (GridRundown?.SelectedItem is PlaylistItem item)
                {
                    if (item.SourceType == PlaylistItemSourceType.LiveIngest)
                    {
                        if (TxtCcuTargetInfo != null)
                        {
                            TxtCcuTargetInfo.Text = $"🎯 Đang tác động: [{item.SourceBadge}] Slot {item.LivePortIndex:D2} - {item.Title}";
                            TxtCcuTargetInfo.Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248));
                        }
                        _compositor.SelectedPlaylistPort = item.LivePortIndex;
                        _compositor.SelectedClipKey = null;
                        activeProfile = _compositor.ColorEngine.GetSlotProfile(item.LivePortIndex);
                    }
                    else
                    {
                        if (TxtCcuTargetInfo != null)
                        {
                            TxtCcuTargetInfo.Text = $"🎯 Đang tác động: [{item.SourceBadge}] Clip: {item.Title}";
                            TxtCcuTargetInfo.Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248));
                        }
                        _compositor.SelectedPlaylistPort = -2;
                        string clipKey = !string.IsNullOrEmpty(item.FilePath) ? item.FilePath : item.Title;
                        _compositor.SelectedClipKey = clipKey;
                        activeProfile = _compositor.ColorEngine.GetClipProfile(clipKey);
                    }
                }
                else
                {
                    if (TxtCcuTargetInfo != null)
                    {
                        TxtCcuTargetInfo.Text = "🎯 Đang tác động: PGM Master (Chưa chọn dòng Rundown)";
                        TxtCcuTargetInfo.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
                    }
                    _compositor.SelectedPlaylistPort = -1;
                    _compositor.SelectedClipKey = null;
                    activeProfile = _compositor.ColorEngine.MasterProfile;
                }
            }
            else if (targetMode >= 1 && targetMode <= 10)
            {
                // Kênh Ingest / Slot cụ thể
                if (TxtCcuTargetInfo != null)
                {
                    TxtCcuTargetInfo.Text = $"🎯 Đang tác động: Slot {targetMode:D2} (Live Cam / IP Ingest)";
                    TxtCcuTargetInfo.Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36));
                }
                _compositor.SelectedPlaylistPort = targetMode;
                _compositor.SelectedClipKey = null;
                activeProfile = _compositor.ColorEngine.GetSlotProfile(targetMode);
            }
            else
            {
                // PGM Master (ON-AIR Màn hình chính)
                if (TxtCcuTargetInfo != null)
                {
                    TxtCcuTargetInfo.Text = "🎯 Đang tác động: PGM Master (ON-AIR Màn hình chính)";
                    TxtCcuTargetInfo.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
                }
                _compositor.SelectedPlaylistPort = -1;
                _compositor.SelectedClipKey = null;
                activeProfile = _compositor.ColorEngine.MasterProfile;
            }

            LoadProfileToUi(activeProfile);
        }

        private void CmbScopeMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded || _compositor == null || CmbScopeMode == null) return;
            _compositor.ScopesEngine.CurrentScope = CmbScopeMode.SelectedIndex switch
            {
                0 => BroadcastScopeType.WaveformIreb,
                1 => BroadcastScopeType.RgbParade,
                2 => BroadcastScopeType.Vectorscope,
                3 => BroadcastScopeType.Histogram,
                _ => BroadcastScopeType.WaveformIreb
            };
        }

        #endregion

        #region DVE Multi-Box Compositor

        private void PopulateDveControls()
        {
            if (CmbDveBox1Port == null || CmbDveBox2Port == null) return;

            CmbDveBox1Port.Items.Clear();
            CmbDveBox2Port.Items.Clear();

            for (int i = 0; i <= 10; i++)
            {
                string name = i == 0 ? "Port 0: PGM Ingest" : $"Port {i}: CAM {i:D2}";
                CmbDveBox1Port.Items.Add(new ComboBoxItem { Content = name, Tag = i });
                CmbDveBox2Port.Items.Add(new ComboBoxItem { Content = name, Tag = i });
            }

            CmbDveBox1Port.SelectedIndex = 1; // Cam 1
            CmbDveBox2Port.SelectedIndex = 2; // Cam 2
        }

        private void BtnToggleDve_Click(object sender, RoutedEventArgs e)
        {
            bool on = _compositor.DveCompositor.Mode == DveLayoutMode.Single;
            _compositor.DveCompositor.Mode = on ? DveLayoutMode.TwoBoxSide : DveLayoutMode.Single;
            if (BtnToggleDve != null)
            {
                BtnToggleDve.Content = on ? "TẮT DVE" : "BẬT DVE";
                BtnToggleDve.Background = on ? new SolidColorBrush(Color.FromRgb(220, 38, 38)) : new SolidColorBrush(Color.FromRgb(37, 99, 235));
            }
        }

        private void CmbDveLayout_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded || _compositor == null || CmbDveLayout == null) return;
            _compositor.DveCompositor.Mode = CmbDveLayout.SelectedIndex switch
            {
                0 => DveLayoutMode.Single,
                1 => DveLayoutMode.TwoBoxSide,
                2 => DveLayoutMode.TwoBoxSide,
                3 => DveLayoutMode.TwoBoxSide,
                4 => DveLayoutMode.FourBoxQuad,
                _ => DveLayoutMode.Single
            };
        }

        private void CmbDveBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded || _compositor == null) return;
            if (CmbDveBox1Port?.SelectedItem is ComboBoxItem item1 && item1.Tag is int p1)
                _compositor.DveCompositor.Box1Camera = p1;
            if (CmbDveBox2Port?.SelectedItem is ComboBoxItem item2 && item2.Tag is int p2)
                _compositor.DveCompositor.Box2Camera = p2;
        }

        #endregion

        #region SDI & Master Codec & NDI Setup

        private void UpdateSdiDeviceList(List<SdiDeviceInfo> devices)
        {
            _sdiDevices = devices ?? new List<SdiDeviceInfo>();
            if (CmbSdiDevice == null) return;

            CmbSdiDevice.Items.Clear();

            if (_sdiDevices.Count > 0)
            {
                // Tìm thấy card SDI phần cứng trên máy
                foreach (var d in _sdiDevices)
                {
                    CmbSdiDevice.Items.Add(new ComboBoxItem
                    {
                        Content = !string.IsNullOrWhiteSpace(d.DisplayLabel) ? d.DisplayLabel : d.Name,
                        Tag = d
                    });
                }
                CmbSdiDevice.SelectedIndex = 0;

                // Cho phép bật / tắt SDI OUT
                if (BtnToggleSdiOut != null)
                {
                    BtnToggleSdiOut.IsEnabled = true;
                    BtnToggleSdiOut.Content = _compositor.IsSdiMasterOutEnabled ? "Tắt SDI 🔴" : "Bật SDI";
                    BtnToggleSdiOut.Foreground = Brushes.White;
                    BtnToggleSdiOut.Background = _compositor.IsSdiMasterOutEnabled
                        ? new SolidColorBrush(Color.FromRgb(220, 38, 38))
                        : new SolidColorBrush(Color.FromRgb(37, 99, 235));
                    BtnToggleSdiOut.ToolTip = "Bật / Tắt phát sóng SDI Master ra card Blackmagic/DeckLink phần cứng";
                }

                if (BadgeSdiStatus != null && TxtSdiHardwareStatus != null)
                {
                    TxtSdiHardwareStatus.Text = $"SDI: {_sdiDevices.Count} PORT(S)";
                    TxtSdiHardwareStatus.Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248));
                    BadgeSdiStatus.BorderBrush = _compositor.IsSdiMasterOutEnabled
                        ? new SolidColorBrush(Color.FromRgb(0, 230, 118))
                        : new SolidColorBrush(Color.FromRgb(37, 99, 235));
                }
            }
            else
            {
                // Không tìm thấy card Blackmagic / DeckLink trên máy
                var noCardItem = new ComboBoxItem
                {
                    Content = "❌ Không tìm thấy card SDI (DeckLink / Blackmagic)",
                    Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113)),
                    IsSelected = true
                };
                CmbSdiDevice.Items.Add(noCardItem);

                // Disable chế độ bật/tắt nhưng giữ button màu xanh và chữ màu xám theo yêu cầu
                if (BtnToggleSdiOut != null)
                {
                    if (_compositor.IsSdiMasterOutEnabled)
                    {
                        _compositor.ToggleMasterSdi(false, "", "");
                    }
                    BtnToggleSdiOut.IsEnabled = false;
                    BtnToggleSdiOut.Content = "Bật SDI (NONE CARD)";
                    BtnToggleSdiOut.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)); // Chữ màu xám
                    BtnToggleSdiOut.Background = new SolidColorBrush(Color.FromRgb(37, 99, 235));   // Button có màu xanh
                    BtnToggleSdiOut.ToolTip = "Không phát hiện card SDI Blackmagic/DeckLink phần cứng trên máy tính. Chế độ bật/tắt bị vô hiệu hóa.";
                }

                if (BadgeSdiStatus != null && TxtSdiHardwareStatus != null)
                {
                    TxtSdiHardwareStatus.Text = "SDI: NO CARD";
                    TxtSdiHardwareStatus.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
                    BadgeSdiStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(71, 85, 105));
                }
            }
        }

        private async void PopulateDefaultSdiDevices()
        {
            try
            {
                var devices = await SdiHardwareScanner.ScanOutputsAsync();
                UpdateSdiDeviceList(devices);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[SDI INIT] Lỗi quét thiết bị SDI: {ex.Message}");
                UpdateSdiDeviceList(new List<SdiDeviceInfo>());
            }
        }

        private async void BtnScanSdi_Click(object sender, RoutedEventArgs e)
        {
            if (BtnScanSdi != null)
            {
                BtnScanSdi.IsEnabled = false;
                BtnScanSdi.Content = "⏳ Đang quét...";
            }
            try
            {
                var devices = await SdiHardwareScanner.ScanOutputsAsync();
                UpdateSdiDeviceList(devices);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[SDI SCAN] Lỗi quét cổng SDI: {ex.Message}");
                UpdateSdiDeviceList(new List<SdiDeviceInfo>());
            }
            finally
            {
                if (BtnScanSdi != null)
                {
                    BtnScanSdi.IsEnabled = true;
                    BtnScanSdi.Content = "🔍 Scan SDI";
                }
            }
        }

        private void BtnToggleSdiOut_Click(object sender, RoutedEventArgs e)
        {
            if (_sdiDevices == null || _sdiDevices.Count == 0)
            {
                UpdateSdiDeviceList(new List<SdiDeviceInfo>());
                return;
            }

            bool wasRunning = _compositor.IsSdiMasterOutEnabled;
            bool target = !wasRunning;

            string dev = CmbSdiDevice?.Text ?? "";
            string mode = CmbSdiResolution?.Text ?? "1080p";
            double fps = 60.0;
            if (CmbSdiFps?.SelectedItem is ComboBoxItem fpsItem)
            {
                string text = fpsItem.Content?.ToString() ?? "";
                if (text.Contains("30")) fps = 30.0;
                else if (text.Contains("25")) fps = 25.0;
                else if (text.Contains("24")) fps = 24.0;
                else if (text.Contains("50")) fps = 50.0;
                else if (text.Contains("60")) fps = 60.0;
            }

            bool success = _compositor.ToggleMasterSdi(target, dev, mode, fps);
            if (BtnToggleSdiOut != null)
            {
                BtnToggleSdiOut.Content = success && target ? "Tắt SDI 🔴" : "Bật SDI";
                BtnToggleSdiOut.Foreground = Brushes.White;
                BtnToggleSdiOut.Background = success && target
                    ? new SolidColorBrush(Color.FromRgb(220, 38, 38))
                    : new SolidColorBrush(Color.FromRgb(37, 99, 235));
            }
            if (BadgeSdiStatus != null)
            {
                BadgeSdiStatus.BorderBrush = success && target
                    ? new SolidColorBrush(Color.FromRgb(0, 230, 118))
                    : new SolidColorBrush(Color.FromRgb(37, 99, 235));
            }
        }

        private void BtnToggleNdiOut_Click(object sender, RoutedEventArgs e)
        {
            bool wasRunning = _compositor.IsNdiMasterOutEnabled;
            bool target = !wasRunning;
            string streamName = !string.IsNullOrWhiteSpace(TxtNdiStreamName?.Text) ? TxtNdiStreamName.Text.Trim() : "OME Playout Master HD";
            bool success = _compositor.ToggleMasterNdi(target, streamName);
            if (BtnToggleNdiOut != null)
            {
                BtnToggleNdiOut.Content = success && target ? "TẮT NDI OUT 🔴" : "BẬT NDI OUT";
                BtnToggleNdiOut.Background = success && target
                    ? new SolidColorBrush(Color.FromRgb(220, 38, 38))
                    : new SolidColorBrush(Color.FromRgb(37, 99, 235)); // Màu xanh dương #2563EB ở trạng thái Tắt
            }
        }

        private void CmbSdiConfig_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded || _compositor == null) return;
        }

        private void CmbMasterCodec_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded || _compositor == null) return;
        }

        #endregion

        #region Crosspoint Router Gateway (11 Slots Mapping)

        public ObservableCollection<RouterSlotConfigItem> Router11Slots { get; } = new();
        private readonly Dictionary<int, ComboBox> _routerSlotComboBoxes = new();

        private void InitRouter11Slots()
        {
            Router11Slots.Clear();
            for (int i = 0; i <= 10; i++)
            {
                int defaultMatrixSlot = _compositor?.GetPortRoute(i) ?? i;
                var item = new RouterSlotConfigItem
                {
                    SlotIndex = i,
                    SelectedMatrixSlot = defaultMatrixSlot
                };
                Router11Slots.Add(item);
                UpdateSingleRouterSlotStatus(item);
            }
        }

        private void CmbRouterSlotSource_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ComboBox cmb) return;
            int slot = GetRouterSlotIndexFromSender(cmb);
            if (slot < 0 || slot > 10) return;

            _routerSlotComboBoxes[slot] = cmb;
            cmb.Unloaded -= CmbRouterSlotSource_Unloaded;
            cmb.Unloaded += CmbRouterSlotSource_Unloaded;

            PopulateRouterSlotSourceComboBox(cmb, slot);
        }

        private void CmbRouterSlotSource_Unloaded(object sender, RoutedEventArgs e)
        {
            if (sender is ComboBox cmb)
            {
                int slot = GetRouterSlotIndexFromSender(cmb);
                if (slot >= 0) _routerSlotComboBoxes.Remove(slot);
            }
        }

        private static int GetRouterSlotIndexFromSender(ComboBox cmb)
        {
            if (cmb.Tag is int sInt) return sInt;
            if (cmb.DataContext is RouterSlotConfigItem item) return item.SlotIndex;
            return -1;
        }

        private void PopulateRouterSlotSourceComboBox(ComboBox cmb, int slotIndex)
        {
            if (cmb == null || _compositor == null) return;

            _isPopulatingRouter = true;
            try
            {
                int currentMatrixSlot = _compositor.GetPortRoute(slotIndex);

                // Lấy thông tin quét từ compositor để bổ sung tên/độ phân giải/online thực tế
                var discovered = _compositor.GetDiscoveredSources();
                var sourceMap = discovered.ToDictionary(x => x.slot, x => x);

                cmb.Items.Clear();

                // 1. Tùy chọn Chưa gán
                cmb.Items.Add(new ComboBoxItem
                {
                    Content = "⚪ [Trống] Chưa kết nối tín hiệu",
                    Tag = -1,
                    Foreground = Brushes.Gray
                });

                // 2. Nhóm SRT DECODE (1 PGM + 10 CAM)
                // PGM (Matrix Slot 0)
                string srtPgmStatus = "";
                if (sourceMap.TryGetValue(0, out var pgmInfo) && pgmInfo.isActive)
                    srtPgmStatus = $" • {pgmInfo.width}x{pgmInfo.height} (LIVE 🟢)";
                else
                    srtPgmStatus = " (Standby ⚪)";

                cmb.Items.Add(new ComboBoxItem
                {
                    Content = $"🔴 [SRT] PGM Master (Port 0){srtPgmStatus}",
                    Tag = 0,
                    Foreground = new SolidColorBrush(Color.FromRgb(244, 63, 94)),
                    FontWeight = FontWeights.Bold
                });

                // SRT CAM 01..10 (Matrix Slot 1..10)
                for (int c = 1; c <= 10; c++)
                {
                    string camStatus = "";
                    string customName = "";
                    if (sourceMap.TryGetValue(c, out var camInfo))
                    {
                        if (!string.IsNullOrWhiteSpace(camInfo.name) && !camInfo.name.StartsWith("Slot"))
                            customName = $": {camInfo.name}";
                        if (camInfo.isActive)
                            camStatus = $" • {camInfo.width}x{camInfo.height} (LIVE 🟢)";
                        else
                            camStatus = " (Standby ⚪)";
                    }
                    else
                    {
                        camStatus = " (Standby ⚪)";
                    }

                    cmb.Items.Add(new ComboBoxItem
                    {
                        Content = $"🟢 [SRT] CAM {c:D2}{customName}{camStatus}",
                        Tag = c,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF))
                    });
                }

                // 3. Nhóm WEBRTC DECODE (1 PGM + 10 CAM)
                // WebRTC PGM Master (Matrix Slot 21) - Đứng trước WebRTC CAM 01..10
                string webrtcPgmStatus = "";
                if (sourceMap.TryGetValue(21, out var webrtcPgmInfo) && webrtcPgmInfo.isActive)
                    webrtcPgmStatus = $" • {webrtcPgmInfo.width}x{webrtcPgmInfo.height} (LIVE 🟢)";
                else
                    webrtcPgmStatus = " (Standby ⚪)";

                cmb.Items.Add(new ComboBoxItem
                {
                    Content = $"🟣 [WebRTC] PGM Master (Port 21){webrtcPgmStatus}",
                    Tag = 21,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0x84, 0xFC)),
                    FontWeight = FontWeights.Bold
                });

                // WebRTC CAM 01..10 (Matrix Slot 11..20)
                for (int c = 1; c <= 10; c++)
                {
                    int webrtcSlot = 10 + c; // Slot 11..20
                    string camStatus = "";
                    string customName = "";
                    if (sourceMap.TryGetValue(webrtcSlot, out var camInfo))
                    {
                        if (!string.IsNullOrWhiteSpace(camInfo.name) && !camInfo.name.StartsWith("Slot"))
                            customName = $": {camInfo.name}";
                        if (camInfo.isActive)
                            camStatus = $" • {camInfo.width}x{camInfo.height} (LIVE 🟢)";
                        else
                            camStatus = " (Standby ⚪)";
                    }
                    else
                    {
                        camStatus = " (Standby ⚪)";
                    }

                    cmb.Items.Add(new ComboBoxItem
                    {
                        Content = $"🟣 [WebRTC] CAM {c:D2}{customName}{camStatus}",
                        Tag = webrtcSlot,
                        Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0x84, 0xFC))
                    });
                }

                // Chọn item tương ứng với currentMatrixSlot
                int selectedIndex = 0;
                for (int i = 0; i < cmb.Items.Count; i++)
                {
                    if (cmb.Items[i] is ComboBoxItem item && item.Tag is int tagVal && tagVal == currentMatrixSlot)
                    {
                        selectedIndex = i;
                        break;
                    }
                }
                cmb.SelectedIndex = selectedIndex;
            }
            finally
            {
                _isPopulatingRouter = false;
            }
        }

        private void PopulateRouterCrosspoint()
        {
            if (_compositor == null) return;
            foreach (var kvp in _routerSlotComboBoxes.ToList())
            {
                PopulateRouterSlotSourceComboBox(kvp.Value, kvp.Key);
            }
            UpdateAllRouterSlotStatuses();
        }

        private void UpdateSingleRouterSlotStatus(RouterSlotConfigItem item)
        {
            if (item == null || _compositor == null) return;

            int matrixSlot = _compositor.GetPortRoute(item.SlotIndex);
            item.SelectedMatrixSlot = matrixSlot;

            if (matrixSlot < 0)
            {
                item.SelectedSourceTitle = "⚪ [Trống] Chưa gán tín hiệu";
                item.SignalOrigin = "None";
                item.IsSignalActive = false;
                item.ResolutionText = "--";
                return;
            }

            var info = _compositor.GetDiscoveredSources().FirstOrDefault(x => x.slot == matrixSlot);
            bool isActive = info.isActive;
            string srcName = !string.IsNullOrWhiteSpace(info.name) ? info.name : $"Slot {matrixSlot:D2}";
            string origin = matrixSlot == 0 ? "SRT PGM" : (matrixSlot == 21 ? "WebRTC PGM" : (matrixSlot <= 10 ? "SRT Decode" : (matrixSlot <= 20 ? "WebRTC Decode" : "IP Router")));

            item.SignalOrigin = origin;
            item.IsSignalActive = isActive;
            item.ResolutionText = isActive && info.width > 0 ? $"{info.width}x{info.height} @ {info.fps:F0}fps" : "Chờ tín hiệu";

            if (matrixSlot == 0)
                item.SelectedSourceTitle = $"[SRT] PGM Master (Port 0)";
            else if (matrixSlot == 21)
                item.SelectedSourceTitle = $"[WebRTC] PGM Master (Port 21)";
            else if (matrixSlot <= 10)
                item.SelectedSourceTitle = $"[SRT] CAM {matrixSlot:D2} - {srcName}";
            else if (matrixSlot <= 20)
                item.SelectedSourceTitle = $"[WebRTC] CAM {matrixSlot - 10:D2} - {srcName}";
            else
                item.SelectedSourceTitle = $"[IP] Slot {matrixSlot:D2} - {srcName}";
        }

        private void UpdateAllRouterSlotStatuses()
        {
            foreach (var slotItem in Router11Slots)
            {
                UpdateSingleRouterSlotStatus(slotItem);
            }
        }

        private void CmbRouterSlotSource_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isPopulatingRouter || !_isLoaded || _compositor == null) return;
            if (sender is not ComboBox cmb) return;

            int slotIndex = GetRouterSlotIndexFromSender(cmb);
            if (slotIndex < 0 || slotIndex > 10) return;

            if (cmb.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag is int targetMatrixSlot)
            {
                _compositor.SetPortRoute(slotIndex, targetMatrixSlot);

                if (slotIndex >= 0 && slotIndex < Router11Slots.Count)
                {
                    UpdateSingleRouterSlotStatus(Router11Slots[slotIndex]);
                }

                // Cập nhật lại danh sách hiển thị các ComboBox nếu có thay đổi
                PopulateRouterCrosspoint();

                // Đồng bộ trạng thái gán sang Preview IPC bên trái
                SyncAssignedSlotsToPreview();
            }
        }

        #endregion

        #region CG Logo & Overlays Tab

        private void BtnToggleLogo_Click(object sender, RoutedEventArgs e)
        {
            _compositor.GraphicsEngine.IsLogoOnAir = !_compositor.GraphicsEngine.IsLogoOnAir;
            if (BtnToggleLogo != null)
            {
                bool on = _compositor.GraphicsEngine.IsLogoOnAir;
                BtnToggleLogo.Content = on ? "DSK LOGO ON AIR 🟢" : "DSK LOGO OFF";
                BtnToggleLogo.Background = on ? new SolidColorBrush(Color.FromRgb(34, 197, 94)) : new SolidColorBrush(Color.FromRgb(30, 41, 59));
                BtnToggleLogo.Foreground = on ? Brushes.Black : Brushes.White;
            }
            UpdateDskButtonVisuals();
        }

        private void BtnBrowseLogo_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Title = "Chọn Tệp Tin Logo PNG Trong Suốt (Alpha Channel)",
                Filter = "PNG Images (*.png)|*.png|All Files (*.*)|*.*"
            };
            if (ofd.ShowDialog() == true)
            {
                if (_compositor.GraphicsEngine.LoadLogoFromFile(ofd.FileName))
                {
                    int w = _compositor.GraphicsEngine.LogoPixelWidth;
                    int h = _compositor.GraphicsEngine.LogoPixelHeight;
                    string dimStr = (w > 0 && h > 0) ? $" ({w}x{h})" : "";
                    if (TxtLogoFile != null) TxtLogoFile.Text = $"File: {Path.GetFileName(ofd.FileName)}{dimStr}";

                    // Tự động kích hoạt Logo DSK lên sóng PGM sau khi nạp
                    _compositor.GraphicsEngine.IsLogoOnAir = true;
                    if (BtnToggleLogo != null)
                    {
                        BtnToggleLogo.Content = "DSK LOGO ON AIR 🟢";
                        BtnToggleLogo.Background = new SolidColorBrush(Color.FromRgb(34, 197, 94));
                        BtnToggleLogo.Foreground = Brushes.Black;
                    }
                    UpdateDskButtonVisuals();
                }
            }
        }

        private void BtnLogoPos_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string pos)
            {
                double x = 0.88, y = 0.08;
                switch (pos)
                {
                    case "TL": x = 0.04; y = 0.06; break;
                    case "TR": x = 0.88; y = 0.06; break;
                    case "BL": x = 0.04; y = 0.88; break;
                    case "BR": x = 0.88; y = 0.88; break;
                }
                _compositor.GraphicsEngine.LogoX = x;
                _compositor.GraphicsEngine.LogoY = y;
                if (SliderLogoX != null) SliderLogoX.Value = x;
                if (SliderLogoY != null) SliderLogoY.Value = y;
                if (TxtLogoXVal != null) TxtLogoXVal.Text = $"{x * 100:0}%";
                if (TxtLogoYVal != null) TxtLogoYVal.Text = $"{y * 100:0}%";
            }
        }

        private void SliderLogo_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isLoaded || _compositor == null) return;
            if (SliderLogoScale != null) _compositor.GraphicsEngine.LogoScale = SliderLogoScale.Value;
            if (SliderLogoOpacity != null) _compositor.GraphicsEngine.LogoOpacity = SliderLogoOpacity.Value;
        }

        private void SliderLogoPos_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isLoaded || _compositor == null) return;
            if (SliderLogoX != null)
            {
                _compositor.GraphicsEngine.LogoX = SliderLogoX.Value;
                if (TxtLogoXVal != null) TxtLogoXVal.Text = $"{SliderLogoX.Value * 100:0}%";
            }
            if (SliderLogoY != null)
            {
                _compositor.GraphicsEngine.LogoY = SliderLogoY.Value;
                if (TxtLogoYVal != null) TxtLogoYVal.Text = $"{SliderLogoY.Value * 100:0}%";
            }
        }

        #endregion

        #region Fail-Safe Emergency Tab

        private void ChkFailSafeEnabled_Click(object sender, RoutedEventArgs e)
        {
            if (ChkFailSafeEnabled != null)
                _compositor.FailSafeEngine.IsEnabled = ChkFailSafeEnabled.IsChecked == true;
        }

        private void OnFailSafeConfigChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded || _compositor == null) return;
            if (CmbFailSafeMode != null)
            {
                _compositor.FailSafeEngine.FallbackMode = CmbFailSafeMode.SelectedIndex == 1
                    ? FailSafeFallbackMode.BackupIsoPort
                    : FailSafeFallbackMode.SmpteColorBars;
            }
            if (CmbFailSafeBackupPort != null)
            {
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
                    _ => 1.5
                };
                _compositor.FailSafeEngine.LossThresholdSeconds = timeoutSec;
            }
        }

        #endregion

        #region Lip-Sync Delay

        private void SliderVideoDelay_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isLoaded || _compositor == null || SliderVideoDelay == null) return;
            int delayMs = (int)SliderVideoDelay.Value;
            _compositor.DelayEngine.VideoDelayMs = delayMs;
            if (TxtEffectiveDelay != null) TxtEffectiveDelay.Text = $"Độ trễ Video: {delayMs} ms";
            if (TxtFooterDelay != null) TxtFooterDelay.Text = delayMs == 0 ? "0 ms (DIRECT 0ms)" : $"{delayMs} ms (BUFFERED)";
        }

        #endregion

        #region Window Lifecycle

        private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            _isShuttingDown = true;
            _uiRenderTimer?.Stop();
            _telemetryTimer?.Stop();
            _fadeTimer?.Stop();

            _ipScanner?.Dispose();
            _ndiScanner?.Dispose();
            _ndiPlayoutReceiver?.Dispose();

            _compositor.Dispose();
        }

        #endregion
    }
}
