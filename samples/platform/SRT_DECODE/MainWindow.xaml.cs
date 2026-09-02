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
        private const int MaxChannels = 10;

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
        private int _activeChannelCount = 1; // Default 1 channel active; '+' button adds up to 10
        private int _currentProgramIndex = 0; // 0..9
        private int _currentPreviewIndex = 1; // 0..9
        private bool _isTransitioning = false;
        private bool _isInitialized = false;
        private readonly StringBuilder _logBuffer = new();

        // ─── Dynamic Metadata State ─────────────────────────────────
        private readonly string[] _channelNames = new string[MaxChannels];
        private readonly int[] _channelPorts = new int[MaxChannels];

        // ─── Control Reference Arrays ───────────────────────────────
        private Border[] _cellBorders = Array.Empty<Border>();
        private Border[] _tallyBadges = Array.Empty<Border>();
        private TextBlock[] _tallyTexts = Array.Empty<TextBlock>();
        private TextBlock[] _headerTexts = Array.Empty<TextBlock>();
        private TextBlock[] _fallbackTitles = Array.Empty<TextBlock>();
        private TextBlock[] _badgeCamTexts = Array.Empty<TextBlock>();
        private TextBlock[] _driftLabels = Array.Empty<TextBlock>();
        private TextBlock[] _telemetryTitles = Array.Empty<TextBlock>();

        private Ellipse[] _ledIndicators = Array.Empty<Ellipse>();
        private Button[] _pgmButtons = Array.Empty<Button>();
        private Border[] _hudOverlays = Array.Empty<Border>();
        private Border[] _fallbacks = Array.Empty<Border>();
        private TextBlock[] _statusTexts = Array.Empty<TextBlock>();
        private ProgressBar[] _vuBarsL = Array.Empty<ProgressBar>();
        private ProgressBar[] _vuBarsR = Array.Empty<ProgressBar>();
        private Border[] _overlayAudioVuCams = Array.Empty<Border>();
        private Ellipse[] _clipLedsCamL = Array.Empty<Ellipse>();
        private Ellipse[] _clipLedsCamR = Array.Empty<Ellipse>();
        private TextBlock[] _txtAudioPeakCams = Array.Empty<TextBlock>();
        private static readonly SolidColorBrush _brushLedOff = new(Color.FromRgb(0x2A, 0x2A, 0x2E));
        private static readonly SolidColorBrush _brushLedClip = new(Color.FromRgb(0xFF, 0x00, 0x33));
        private ProgressBar[] _vuMasterBars = Array.Empty<ProgressBar>();
        private Border[] _ingestCards = Array.Empty<Border>();
        private Border[] _driftRows = Array.Empty<Border>();
        private Border[] _telemetryCards = Array.Empty<Border>();
        private Border[] _outputCards = Array.Empty<Border>();
        private TextBlock[] _badgeOutputTexts = Array.Empty<TextBlock>();
        private TextBox[] _txtOutputNdiNames = Array.Empty<TextBox>();
        private CheckBox[] _chkMuteCams = Array.Empty<CheckBox>();
        private CheckBox[] _chkVuCams = Array.Empty<CheckBox>();
        private Button[] _btnSoloCams = Array.Empty<Button>();
        private OpenMediaVideoView[] _videoViews = Array.Empty<OpenMediaVideoView>();

        // TextBlock & HUD array references
        private TextBlock[] _hudRtt = Array.Empty<TextBlock>();
        private TextBlock[] _hudLoss = Array.Empty<TextBlock>();
        private TextBlock[] _hudBitrate = Array.Empty<TextBlock>();
        private TextBlock[] _hudDrift = Array.Empty<TextBlock>();

        private TextBlock[] _diagRtt = Array.Empty<TextBlock>();
        private TextBlock[] _diagLoss = Array.Empty<TextBlock>();
        private TextBlock[] _diagBitrate = Array.Empty<TextBlock>();
        private TextBlock[] _diagHealth = Array.Empty<TextBlock>();
        private ProgressBar[] _pbBuffer = Array.Empty<ProgressBar>();
        private TextBlock[] _txtDriftVal = Array.Empty<TextBlock>();

        // Ingest form inputs
        private TextBox[] _txtNameInputs = Array.Empty<TextBox>();
        private TextBox[] _txtIps = Array.Empty<TextBox>();
        private TextBox[] _txtPorts = Array.Empty<TextBox>();
        private ComboBox[] _cmbModes = Array.Empty<ComboBox>();
        private TextBox[] _txtStreamIds = Array.Empty<TextBox>();
        private TextBox[] _txtLatencies = Array.Empty<TextBox>();
        private CheckBox[] _chkAutoLatencies = Array.Empty<CheckBox>();
        private Button[] _btnToggles = Array.Empty<Button>();

        // ─── Video Presentation Bitmaps ─────────────────────────────
        private readonly WriteableBitmap?[] _camBitmaps = new WriteableBitmap?[MaxChannels];

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
            _receiverEngine.AudioPcmReady += (chIdx, pcm, len) => _audioManager.ProcessDecodedPcm(chIdx, pcm, len);
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
                // Initialize default names and ports
                for (int i = 0; i < MaxChannels; i++)
                {
                    _channelNames[i] = $"CAM {i + 1}";
                    _channelPorts[i] = 9000 + i;
                }

                // Cache UI Element references for fast indexing
                _cellBorders = new[] { CellCam1, CellCam2, CellCam3, CellCam4, CellCam5, CellCam6, CellCam7, CellCam8, CellCam9, CellCam10 };
                _tallyBadges = new[] { TallyCam1, TallyCam2, TallyCam3, TallyCam4, TallyCam5, TallyCam6, TallyCam7, TallyCam8, TallyCam9, TallyCam10 };
                _tallyTexts = new[]
                {
                    (TextBlock)TallyCam1.Child, (TextBlock)TallyCam2.Child, (TextBlock)TallyCam3.Child, (TextBlock)TallyCam4.Child, (TextBlock)TallyCam5.Child,
                    (TextBlock)TallyCam6.Child, (TextBlock)TallyCam7.Child, (TextBlock)TallyCam8.Child, (TextBlock)TallyCam9.Child, (TextBlock)TallyCam10.Child
                };
                _headerTexts = new[] { TxtHeaderCam1, TxtHeaderCam2, TxtHeaderCam3, TxtHeaderCam4, TxtHeaderCam5, TxtHeaderCam6, TxtHeaderCam7, TxtHeaderCam8, TxtHeaderCam9, TxtHeaderCam10 };
                _fallbackTitles = new[] { TxtFallbackTitleCam1, TxtFallbackTitleCam2, TxtFallbackTitleCam3, TxtFallbackTitleCam4, TxtFallbackTitleCam5, TxtFallbackTitleCam6, TxtFallbackTitleCam7, TxtFallbackTitleCam8, TxtFallbackTitleCam9, TxtFallbackTitleCam10 };
                _badgeCamTexts = new[] { TxtBadgeCam1, TxtBadgeCam2, TxtBadgeCam3, TxtBadgeCam4, TxtBadgeCam5, TxtBadgeCam6, TxtBadgeCam7, TxtBadgeCam8, TxtBadgeCam9, TxtBadgeCam10 };
                _driftLabels = new[] { TxtDriftCamLabel1, TxtDriftCamLabel2, TxtDriftCamLabel3, TxtDriftCamLabel4, TxtDriftCamLabel5, TxtDriftCamLabel6, TxtDriftCamLabel7, TxtDriftCamLabel8, TxtDriftCamLabel9, TxtDriftCamLabel10 };
                _telemetryTitles = new[] { TxtTelemetryCamTitle1, TxtTelemetryCamTitle2, TxtTelemetryCamTitle3, TxtTelemetryCamTitle4, TxtTelemetryCamTitle5, TxtTelemetryCamTitle6, TxtTelemetryCamTitle7, TxtTelemetryCamTitle8, TxtTelemetryCamTitle9, TxtTelemetryCamTitle10 };

                _ledIndicators = new[] { LedCam1, LedCam2, LedCam3, LedCam4, LedCam5, LedCam6, LedCam7, LedCam8, LedCam9, LedCam10 };
                _pgmButtons = new[] { BtnPgmCam1, BtnPgmCam2, BtnPgmCam3, BtnPgmCam4, BtnPgmCam5, BtnPgmCam6, BtnPgmCam7, BtnPgmCam8, BtnPgmCam9, BtnPgmCam10 };
                _hudOverlays = new[] { HudOverlayCam1, HudOverlayCam2, HudOverlayCam3, HudOverlayCam4, HudOverlayCam5, HudOverlayCam6, HudOverlayCam7, HudOverlayCam8, HudOverlayCam9, HudOverlayCam10 };
                _fallbacks = new[] { FallbackCam1, FallbackCam2, FallbackCam3, FallbackCam4, FallbackCam5, FallbackCam6, FallbackCam7, FallbackCam8, FallbackCam9, FallbackCam10 };
                _statusTexts = new[] { TxtStatusCam1, TxtStatusCam2, TxtStatusCam3, TxtStatusCam4, TxtStatusCam5, TxtStatusCam6, TxtStatusCam7, TxtStatusCam8, TxtStatusCam9, TxtStatusCam10 };
                _vuBarsL = new[] { VuCam1L, VuCam2L, VuCam3L, VuCam4L, VuCam5L, VuCam6L, VuCam7L, VuCam8L, VuCam9L, VuCam10L };
                _vuBarsR = new[] { VuCam1R, VuCam2R, VuCam3R, VuCam4R, VuCam5R, VuCam6R, VuCam7R, VuCam8R, VuCam9R, VuCam10R };
                _overlayAudioVuCams = new[] { OverlayAudioVuCam1, OverlayAudioVuCam2, OverlayAudioVuCam3, OverlayAudioVuCam4, OverlayAudioVuCam5, OverlayAudioVuCam6, OverlayAudioVuCam7, OverlayAudioVuCam8, OverlayAudioVuCam9, OverlayAudioVuCam10 };
                _clipLedsCamL = new[] { ClipLedCam1_L, ClipLedCam2_L, ClipLedCam3_L, ClipLedCam4_L, ClipLedCam5_L, ClipLedCam6_L, ClipLedCam7_L, ClipLedCam8_L, ClipLedCam9_L, ClipLedCam10_L };
                _clipLedsCamR = new[] { ClipLedCam1_R, ClipLedCam2_R, ClipLedCam3_R, ClipLedCam4_R, ClipLedCam5_R, ClipLedCam6_R, ClipLedCam7_R, ClipLedCam8_R, ClipLedCam9_R, ClipLedCam10_R };
                _txtAudioPeakCams = new[] { TxtAudioPeakCam1, TxtAudioPeakCam2, TxtAudioPeakCam3, TxtAudioPeakCam4, TxtAudioPeakCam5, TxtAudioPeakCam6, TxtAudioPeakCam7, TxtAudioPeakCam8, TxtAudioPeakCam9, TxtAudioPeakCam10 };
                _vuMasterBars = new[] { VuMasterL, VuMasterR, VuMaster3, VuMaster4, VuMaster5, VuMaster6, VuMaster7, VuMaster8, VuMaster9, VuMaster10, VuMaster11, VuMaster12, VuMaster13, VuMaster14, VuMaster15, VuMaster16 };
                UpdateMasterVuVisibility();
                _ingestCards = new[] { CardCam1, CardCam2, CardCam3, CardCam4, CardCam5, CardCam6, CardCam7, CardCam8, CardCam9, CardCam10 };
                _driftRows = new[] { RowDriftCam1, RowDriftCam2, RowDriftCam3, RowDriftCam4, RowDriftCam5, RowDriftCam6, RowDriftCam7, RowDriftCam8, RowDriftCam9, RowDriftCam10 };
                _telemetryCards = new[] { CardTelemetryCam1, CardTelemetryCam2, CardTelemetryCam3, CardTelemetryCam4, CardTelemetryCam5, CardTelemetryCam6, CardTelemetryCam7, CardTelemetryCam8, CardTelemetryCam9, CardTelemetryCam10 };
                _outputCards = new[] { CardOutputCam1, CardOutputCam2, CardOutputCam3, CardOutputCam4, CardOutputCam5, CardOutputCam6, CardOutputCam7, CardOutputCam8, CardOutputCam9, CardOutputCam10 };
                _badgeOutputTexts = new[] { TxtBadgeOutputCam1, TxtBadgeOutputCam2, TxtBadgeOutputCam3, TxtBadgeOutputCam4, TxtBadgeOutputCam5, TxtBadgeOutputCam6, TxtBadgeOutputCam7, TxtBadgeOutputCam8, TxtBadgeOutputCam9, TxtBadgeOutputCam10 };
                _txtOutputNdiNames = new[] { TxtOutputNdiNameCam1, TxtOutputNdiNameCam2, TxtOutputNdiNameCam3, TxtOutputNdiNameCam4, TxtOutputNdiNameCam5, TxtOutputNdiNameCam6, TxtOutputNdiNameCam7, TxtOutputNdiNameCam8, TxtOutputNdiNameCam9, TxtOutputNdiNameCam10 };
                _chkMuteCams = new[] { ChkMuteCam1, ChkMuteCam2, ChkMuteCam3, ChkMuteCam4, ChkMuteCam5, ChkMuteCam6, ChkMuteCam7, ChkMuteCam8, ChkMuteCam9, ChkMuteCam10 };
                _chkVuCams = new[] { ChkVuCam1, ChkVuCam2, ChkVuCam3, ChkVuCam4, ChkVuCam5, ChkVuCam6, ChkVuCam7, ChkVuCam8, ChkVuCam9, ChkVuCam10 };
                _btnSoloCams = new[] { BtnSoloCam1, BtnSoloCam2, BtnSoloCam3, BtnSoloCam4, BtnSoloCam5, BtnSoloCam6, BtnSoloCam7, BtnSoloCam8, BtnSoloCam9, BtnSoloCam10 };
                _videoViews = new[] { VideoViewCam1, VideoViewCam2, VideoViewCam3, VideoViewCam4, VideoViewCam5, VideoViewCam6, VideoViewCam7, VideoViewCam8, VideoViewCam9, VideoViewCam10 };

                // Set initial VU meter overlay visibility (all default to Collapsed until VU checkbox is toggled)
                for (int i = 0; i < _overlayAudioVuCams.Length; i++)
                {
                    if (_overlayAudioVuCams[i] != null)
                    {
                        _overlayAudioVuCams[i].Visibility = Visibility.Collapsed;
                    }
                }

                // Set default audio preview MUTE for all preview screens on layout
                for (int i = 0; i < MaxChannels; i++)
                {
                    if (i < _chkMuteCams.Length && _chkMuteCams[i] != null)
                    {
                        _chkMuteCams[i].IsChecked = true;
                    }
                    _audioManager.SetChannelMuted(i, true, _channelNames[i]);
                }
                UpdateSoloButtonsUI();

                _hudRtt = new[] { HudRttCam1, HudRttCam2, HudRttCam3, HudRttCam4, HudRttCam5, HudRttCam6, HudRttCam7, HudRttCam8, HudRttCam9, HudRttCam10 };
                _hudLoss = new[] { HudLossCam1, HudLossCam2, HudLossCam3, HudLossCam4, HudLossCam5, HudLossCam6, HudLossCam7, HudLossCam8, HudLossCam9, HudLossCam10 };
                _hudBitrate = new[] { HudBitrateCam1, HudBitrateCam2, HudBitrateCam3, HudBitrateCam4, HudBitrateCam5, HudBitrateCam6, HudBitrateCam7, HudBitrateCam8, HudBitrateCam9, HudBitrateCam10 };
                _hudDrift = new[] { HudDriftCam1, HudDriftCam2, HudDriftCam3, HudDriftCam4, HudDriftCam5, HudDriftCam6, HudDriftCam7, HudDriftCam8, HudDriftCam9, HudDriftCam10 };

                _diagRtt = new[] { DiagRttCam1, DiagRttCam2, DiagRttCam3, DiagRttCam4, DiagRttCam5, DiagRttCam6, DiagRttCam7, DiagRttCam8, DiagRttCam9, DiagRttCam10 };
                _diagLoss = new[] { DiagLossCam1, DiagLossCam2, DiagLossCam3, DiagLossCam4, DiagLossCam5, DiagLossCam6, DiagLossCam7, DiagLossCam8, DiagLossCam9, DiagLossCam10 };
                _diagBitrate = new[] { DiagBitrateCam1, DiagBitrateCam2, DiagBitrateCam3, DiagBitrateCam4, DiagBitrateCam5, DiagBitrateCam6, DiagBitrateCam7, DiagBitrateCam8, DiagBitrateCam9, DiagBitrateCam10 };
                _diagHealth = new[] { DiagHealthCam1, DiagHealthCam2, DiagHealthCam3, DiagHealthCam4, DiagHealthCam5, DiagHealthCam6, DiagHealthCam7, DiagHealthCam8, DiagHealthCam9, DiagHealthCam10 };
                _pbBuffer = new[] { PbBufferCam1, PbBufferCam2, PbBufferCam3, PbBufferCam4, PbBufferCam5, PbBufferCam6, PbBufferCam7, PbBufferCam8, PbBufferCam9, PbBufferCam10 };
                _txtDriftVal = new[] { TxtDriftValCam1, TxtDriftValCam2, TxtDriftValCam3, TxtDriftValCam4, TxtDriftValCam5, TxtDriftValCam6, TxtDriftValCam7, TxtDriftValCam8, TxtDriftValCam9, TxtDriftValCam10 };

                _txtNameInputs = new[] { TxtNameCam1, TxtNameCam2, TxtNameCam3, TxtNameCam4, TxtNameCam5, TxtNameCam6, TxtNameCam7, TxtNameCam8, TxtNameCam9, TxtNameCam10 };
                _txtIps = new[] { TxtIpCam1, TxtIpCam2, TxtIpCam3, TxtIpCam4, TxtIpCam5, TxtIpCam6, TxtIpCam7, TxtIpCam8, TxtIpCam9, TxtIpCam10 };
                _txtPorts = new[] { TxtPortCam1, TxtPortCam2, TxtPortCam3, TxtPortCam4, TxtPortCam5, TxtPortCam6, TxtPortCam7, TxtPortCam8, TxtPortCam9, TxtPortCam10 };
                _cmbModes = new[] { CmbModeCam1, CmbModeCam2, CmbModeCam3, CmbModeCam4, CmbModeCam5, CmbModeCam6, CmbModeCam7, CmbModeCam8, CmbModeCam9, CmbModeCam10 };
                _txtStreamIds = new[] { TxtStreamIdCam1, TxtStreamIdCam2, TxtStreamIdCam3, TxtStreamIdCam4, TxtStreamIdCam5, TxtStreamIdCam6, TxtStreamIdCam7, TxtStreamIdCam8, TxtStreamIdCam9, TxtStreamIdCam10 };
                _txtLatencies = new[] { TxtLatencyCam1, TxtLatencyCam2, TxtLatencyCam3, TxtLatencyCam4, TxtLatencyCam5, TxtLatencyCam6, TxtLatencyCam7, TxtLatencyCam8, TxtLatencyCam9, TxtLatencyCam10 };
                _chkAutoLatencies = new[] { ChkAutoLatencyCam1, ChkAutoLatencyCam2, ChkAutoLatencyCam3, ChkAutoLatencyCam4, ChkAutoLatencyCam5, ChkAutoLatencyCam6, ChkAutoLatencyCam7, ChkAutoLatencyCam8, ChkAutoLatencyCam9, ChkAutoLatencyCam10 };
                _btnToggles = new[] { BtnToggleCam1, BtnToggleCam2, BtnToggleCam3, BtnToggleCam4, BtnToggleCam5, BtnToggleCam6, BtnToggleCam7, BtnToggleCam8, BtnToggleCam9, BtnToggleCam10 };

                for (int i = 0; i < MaxChannels; i++)
                {
                    if (i < _chkAutoLatencies.Length && _chkAutoLatencies[i] != null)
                    {
                        _chkAutoLatencies[i].IsChecked = false;
                    }
                    _receiverEngine.Channels[i].Config.AutoLatency = false;
                }

                // Initialize WriteableBitmaps for video rendering surfaces
                for (int i = 0; i < MaxChannels; i++)
                {
                    _camBitmaps[i] = new WriteableBitmap(1920, 1080, 96, 96, PixelFormats.Bgra32, null);
                    _videoViews[i].PresentBitmap(_camBitmaps[i]);
                }
                VideoViewPgm.PresentBitmap(_camBitmaps[0]);

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

                // Initial Active Streams UI & Layout
                UpdateActiveStreamsUI();
                UpdateTallyIndicators();
                LogEvent("[INFO]", "Hệ thống Master Control Room đã sẵn sàng (Tab 1. SRT Ingest, 2. Tab Outputs, tối đa 10 luồng).");
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

        #region Dynamic Stream Names & Ports Binding

        private void TxtNameOrPort_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitialized) return;

            if (sender is TextBox tb && tb.Tag is string tagStr && int.TryParse(tagStr, out int idx))
            {
                UpdateChannelDisplayMeta(idx);
            }
        }

        private void UpdateChannelDisplayMeta(int idx)
        {
            if (idx < 0 || idx >= MaxChannels) return;

            // Get Name
            string name = _txtNameInputs[idx]?.Text.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(name)) name = $"CAM {idx + 1}";
            _channelNames[idx] = name;

            // Get Port
            if (int.TryParse(_txtPorts[idx]?.Text, out int port))
            {
                _channelPorts[idx] = port;
            }
            else
            {
                _channelPorts[idx] = 9000 + idx;
            }

            // Sync to Engine channel metadata
            _receiverEngine.Channels[idx].Name = _channelNames[idx];

            // Update Multiviewer cell header & fallback title
            if (idx < _headerTexts.Length && _headerTexts[idx] != null)
            {
                _headerTexts[idx].Text = $"{_channelNames[idx]} (PORT {_channelPorts[idx]})";
            }
            if (idx < _fallbackTitles.Length && _fallbackTitles[idx] != null)
            {
                _fallbackTitles[idx].Text = $"{_channelNames[idx]} • SRT RECEIVER";
            }
            if (idx < _badgeCamTexts.Length && _badgeCamTexts[idx] != null)
            {
                _badgeCamTexts[idx].Text = _channelNames[idx];
            }
            if (idx < _badgeOutputTexts.Length && _badgeOutputTexts[idx] != null)
            {
                _badgeOutputTexts[idx].Text = $"{_channelNames[idx]} - ISO CHANNEL OUTPUT ROUTING";
            }
            if (idx < _txtOutputNdiNames.Length && _txtOutputNdiNames[idx] != null)
            {
                string safeName = _channelNames[idx].Replace(" ", "_").ToUpperInvariant();
                _txtOutputNdiNames[idx].Text = $"OME_{safeName}_ISO";
            }
            if (idx < _pgmButtons.Length && _pgmButtons[idx] != null)
            {
                _pgmButtons[idx].Content = _channelNames[idx];
            }
            if (idx < _driftLabels.Length && _driftLabels[idx] != null)
            {
                _driftLabels[idx].Text = $"{_channelNames[idx]}:";
            }
            if (idx < _telemetryTitles.Length && _telemetryTitles[idx] != null)
            {
                _telemetryTitles[idx].Text = $"{_channelNames[idx]} (PORT {_channelPorts[idx]})";
            }

            // Update PGM Top screen title if this camera is currently on-air
            if (idx == _currentProgramIndex && TxtPgmMasterTitle != null)
            {
                TxtPgmMasterTitle.Text = $"PROGRAM ({_channelNames[idx]})";
            }
        }

        #endregion

        #region Dynamic Streams Management (+ Button / Ingest Cards)

        private void BtnAddStream_Click(object sender, RoutedEventArgs e)
        {
            if (_activeChannelCount < MaxChannels)
            {
                _activeChannelCount++;
                UpdateActiveStreamsUI();
                LogEvent("[INGEST]", $"➕ Đã thêm khung SRT Receiver Ingest #{_activeChannelCount} ({_channelNames[_activeChannelCount - 1]}). Tổng số luồng: {_activeChannelCount}/10.");
            }
            else
            {
                LogEvent("[WARN]", "Đã đạt giới hạn tối đa 10 luồng SRT Receiver Ingest.");
            }
        }

        private async void BtnRemoveStream_Click(object sender, RoutedEventArgs e)
        {
            if (_activeChannelCount > 1)
            {
                int removeIdx = _activeChannelCount - 1;
                // Stop channel if running
                if (_receiverEngine.Channels[removeIdx].IsRunning)
                {
                    await _receiverEngine.StopChannelAsync(removeIdx);
                }

                _activeChannelCount--;
                if (_currentProgramIndex >= _activeChannelCount)
                {
                    _currentProgramIndex = 0;
                    UpdateTallyIndicators();
                }
                UpdateActiveStreamsUI();
                LogEvent("[INGEST]", $"➖ Đã bớt luồng SRT Ingest #{removeIdx + 1}. Còn lại: {_activeChannelCount}/10 luồng.");
            }
        }

        private void UpdateActiveStreamsUI()
        {
            if (!_isInitialized) return;

            // Update badge text
            TxtStreamCountBadge.Text = $"Active: {_activeChannelCount}/{MaxChannels}";

            // Enable/disable add button
            BtnAddStream.IsEnabled = _activeChannelCount < MaxChannels;
            BtnRemoveStream.IsEnabled = _activeChannelCount > 1;

            // Toggle Ingest Cards, Output Cards, PGM buttons, Telemetry cards & Drift rows
            for (int i = 0; i < MaxChannels; i++)
            {
                bool isActive = i < _activeChannelCount;
                _ingestCards[i].Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
                if (i < _outputCards.Length && _outputCards[i] != null)
                {
                    _outputCards[i].Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
                }
                _pgmButtons[i].Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
                _driftRows[i].Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
                _telemetryCards[i].Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
            }

            // Re-apply Multiviewer layout (View vs PGM+View)
            ApplyMultiviewerLayout();
        }

        #endregion

        #region Layout Handling (Always 2 Columns Multi-view & PGM Top View)

        private void CmbLayoutMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;
            ApplyMultiviewerLayout();
        }

        private void ApplyMultiviewerLayout()
        {
            if (!_isInitialized || MultiviewerContainer == null || _cellBorders.Length < MaxChannels) return;

            int selected = CmbLayoutMode.SelectedIndex; // 0: View (Multi-view 2 cột), 1: PGM+View (PGM trên + Multi-view)

            MultiviewerContainer.RowDefinitions.Clear();
            MultiviewerContainer.ColumnDefinitions.Clear();

            // Always strictly 2 columns for Multi-view
            MultiviewerContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            MultiviewerContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            int numRows = Math.Max(1, (_activeChannelCount + 1) / 2);
            for (int r = 0; r < numRows; r++)
            {
                MultiviewerContainer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            }

            if (selected == 0)
            {
                // ─── OPTION 1: "View" (Multi-view 2 cột, không có màn hình mở rộng toàn khung) ────────
                CellPgmMaster.Visibility = Visibility.Collapsed;

                for (int i = 0; i < MaxChannels; i++)
                {
                    if (i < _activeChannelCount)
                    {
                        _cellBorders[i].Visibility = Visibility.Visible;
                        int row = i / 2;
                        int col = i % 2;
                        Grid.SetRow(_cellBorders[i], row);
                        Grid.SetColumn(_cellBorders[i], col);
                        Grid.SetRowSpan(_cellBorders[i], 1);
                        Grid.SetColumnSpan(_cellBorders[i], 1); // Strictly 1 column, never expand to 2
                    }
                    else
                    {
                        _cellBorders[i].Visibility = Visibility.Collapsed;
                    }
                }
            }
            else
            {
                // ─── OPTION 2: "PGM+View" (PGM trên cùng, dưới là tất cả các cam theo thứ tự 2 cột) ───
                CellPgmMaster.Visibility = Visibility.Visible;
                TxtPgmMasterTitle.Text = $"PROGRAM ({_channelNames[_currentProgramIndex]})";

                // Present live PGM frame on top screen
                if (_camBitmaps[_currentProgramIndex] != null)
                {
                    VideoViewPgm.PresentBitmap(_camBitmaps[_currentProgramIndex]);
                }
                FallbackPgm.Visibility = _fallbacks[_currentProgramIndex].Visibility;

                for (int i = 0; i < MaxChannels; i++)
                {
                    if (i < _activeChannelCount)
                    {
                        _cellBorders[i].Visibility = Visibility.Visible;
                        int row = i / 2;
                        int col = i % 2;
                        Grid.SetRow(_cellBorders[i], row);
                        Grid.SetColumn(_cellBorders[i], col);
                        Grid.SetRowSpan(_cellBorders[i], 1);
                        Grid.SetColumnSpan(_cellBorders[i], 1); // Strictly 1 column, view all cameras in order
                    }
                    else
                    {
                        _cellBorders[i].Visibility = Visibility.Collapsed;
                    }
                }
            }
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
                bool[] active = new bool[MaxChannels];
                for (int i = 0; i < MaxChannels; i++)
                {
                    active[i] = i < _activeChannelCount && _receiverEngine.Channels[i].IsConnected;
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

            for (int i = 0; i < _activeChannelCount && i < MaxChannels; i++)
            {
                var ch = _receiverEngine.Channels[i];
                var sync = syncSnapshot[i];

                totalBitrateKbps += ch.CurrentBitrateKbps;
                totalBytes += ch.TotalBytesReceived;

                // Update HUD Overlay in Video Cell
                _hudRtt[i].Text = $"RTT: {ch.CurrentRttMs:F0} ms";
                _hudLoss[i].Text = $"Loss: {ch.CurrentPacketLoss:F2}%";
                _hudLoss[i].Foreground = ch.CurrentPacketLoss > 3.0 ? Brushes.Red : Brushes.LightGreen;
                _hudBitrate[i].Text = $"Bitrate: {ch.CurrentBitrateKbps:F0} kbps";
                _hudDrift[i].Text = $"Drift: {sync.GetFormattedDrift()}";

                // Update Diag Tab
                _diagRtt[i].Text = $"⏱️ RTT: {ch.CurrentRttMs:F1} ms";
                _diagLoss[i].Text = $"📉 Loss: {ch.CurrentPacketLoss:F2} %";
                _diagLoss[i].Foreground = ch.CurrentPacketLoss > 3.0 ? Brushes.Red : Brushes.LightGreen;
                _diagBitrate[i].Text = $"🚀 Ingest: {ch.CurrentBitrateKbps:F0} kbps";
                _diagHealth[i].Text = $"⏳ Health: {ch.BufferHealthPercent:F0}% ({(ch.BufferHealthPercent > 80 ? "Stable" : "Jittering")})";
                _pbBuffer[i].Value = ch.BufferHealthPercent;
                _txtDriftVal[i].Text = $"Δt: {sync.GetFormattedDrift()} ({sync.LockState})";
                _txtDriftVal[i].Foreground = sync.LockState == SyncLockState.Locked ? Brushes.LightGreen : Brushes.Orange;
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
                if (index < 0 || index >= MaxChannels) return;

                // Update Status text and LED
                _statusTexts[index].Text = state.StatusMessage;
                _ledIndicators[index].Fill = state.IsConnected 
                    ? new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)) 
                    : new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));

                // Show fallback placeholder if disconnected
                if (!state.IsConnected)
                {
                    _fallbacks[index].Visibility = Visibility.Visible;
                    if (index == _currentProgramIndex)
                    {
                        FallbackPgm.Visibility = Visibility.Visible;
                    }
                }

                // Update toggle button text in config tab
                if (index < _btnToggles.Length && _btnToggles[index] != null)
                {
                    _btnToggles[index].Content = state.IsRunning ? $"Stop {_channelNames[index]}" : $"Start {_channelNames[index]}";
                    _btnToggles[index].Background = state.IsRunning 
                        ? new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)) 
                        : new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC));
                }
            });
        }

        private void OnFrameReady(int channelIndex, byte[] frameBytes, int width, int height)
        {
            if (channelIndex < 0 || channelIndex >= MaxChannels) return;

            Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    var bmp = _camBitmaps[channelIndex];
                    if (bmp == null || bmp.PixelWidth != width || bmp.PixelHeight != height)
                    {
                        bmp = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
                        _camBitmaps[channelIndex] = bmp;
                        _videoViews[channelIndex].PresentBitmap(bmp);
                    }

                    int stride = width * 4;
                    bmp.WritePixels(new Int32Rect(0, 0, width, height), frameBytes, stride, 0);

                    // Ensure video is visible and fallback placeholder is hidden
                    if (_fallbacks[channelIndex].Visibility != Visibility.Collapsed)
                    {
                        _fallbacks[channelIndex].Visibility = Visibility.Collapsed;
                    }

                    // If this channel is the active Program on PGM+View top screen, present it
                    if (channelIndex == _currentProgramIndex)
                    {
                        if (FallbackPgm.Visibility != Visibility.Collapsed)
                        {
                            FallbackPgm.Visibility = Visibility.Collapsed;
                        }
                        VideoViewPgm.PresentBitmap(bmp);
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
            for (int i = 0; i < _activeChannelCount; i++)
            {
                ApplyFormInputsToChannel(i);
            }
            await _receiverEngine.StartAllAsync(_activeChannelCount);
        }

        private async void BtnDisconnectAll_Click(object sender, RoutedEventArgs e)
        {
            await _receiverEngine.StopAllAsync(_activeChannelCount);
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
            if (index < 0 || index >= MaxChannels) return;
            var ch = _receiverEngine.Channels[index];

            ch.Config.Host = _txtIps[index].Text.Trim();
            if (int.TryParse(_txtPorts[index].Text, out int port)) ch.Config.Port = port;
            ch.Config.Mode = ParseSrtMode(_cmbModes[index]);
            ch.Config.StreamId = _txtStreamIds[index].Text.Trim();
            ch.Config.AutoLatency = _chkAutoLatencies[index].IsChecked == true;
            if (int.TryParse(_txtLatencies[index].Text, out int lat)) ch.Config.LatencyMs = lat;

            // Update Name
            UpdateChannelDisplayMeta(index);

            // Cam 1 has decryption controls
            if (index == 0)
            {
                ch.Config.EncryptionEnabled = ChkDecryptCam1.IsChecked == true;
                ch.Config.Passphrase = TxtPassphraseCam1.Text;
            }
        }

        #endregion

        #region Vision Switcher & Program Selection

        private void BtnPgmSelect_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tagStr && int.TryParse(tagStr, out int index))
            {
                SelectProgramChannel(index);
            }
        }

        private void SelectProgramChannel(int index)
        {
            if (index < 0 || index >= _activeChannelCount) return;
            _currentProgramIndex = index;
            _audioManager.CurrentProgramIndex = index;
            UpdateTallyIndicators();
            LogEvent("[SWITCHER]", $"Đã chọn {_channelNames[index]} làm tín hiệu PROGRAM (On-Air).");

            // Update PGM Top screen
            if (TxtPgmMasterTitle != null)
            {
                TxtPgmMasterTitle.Text = $"PROGRAM ({_channelNames[index]})";
            }
            if (_camBitmaps[index] != null)
            {
                VideoViewPgm.PresentBitmap(_camBitmaps[index]);
            }
            FallbackPgm.Visibility = _fallbacks[index].Visibility;
        }

        private void UpdateTallyIndicators()
        {
            for (int i = 0; i < MaxChannels; i++)
            {
                if (i >= _activeChannelCount) continue;

                if (i == _currentProgramIndex)
                {
                    // PGM (Red On-Air)
                    _cellBorders[i].BorderBrush = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
                    _cellBorders[i].BorderThickness = new Thickness(2.5);
                    _tallyBadges[i].Background = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
                    _tallyTexts[i].Text = "PGM";
                    _tallyTexts[i].Foreground = Brushes.White;
                    _pgmButtons[i].Background = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
                    _pgmButtons[i].Foreground = Brushes.White;
                }
                else if (i == _currentPreviewIndex)
                {
                    // PVW (Green Standby)
                    _cellBorders[i].BorderBrush = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));
                    _cellBorders[i].BorderThickness = new Thickness(1.5);
                    _tallyBadges[i].Background = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));
                    _tallyTexts[i].Text = "PVW";
                    _tallyTexts[i].Foreground = Brushes.White;
                    _pgmButtons[i].Background = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x2E));
                    _pgmButtons[i].Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
                }
                else
                {
                    // Inactive Tally (Neutral Dark)
                    _cellBorders[i].BorderBrush = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x35));
                    _cellBorders[i].BorderThickness = new Thickness(1);
                    _tallyBadges[i].Background = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x38));
                    _tallyTexts[i].Text = _channelNames[i];
                    _tallyTexts[i].Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
                    _pgmButtons[i].Background = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x2E));
                    _pgmButtons[i].Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
                }
            }
        }

        private async void BtnCutTransition_Click(object sender, RoutedEventArgs e)
        {
            if (_isTransitioning) return;
            _isTransitioning = true;
            LogEvent("[SWITCHER]", $"Thực hiện CUT Transition: {_channelNames[_currentProgramIndex]} ➔ {_channelNames[_currentPreviewIndex]}");

            int temp = _currentProgramIndex;
            _currentProgramIndex = _currentPreviewIndex;
            _currentPreviewIndex = temp;

            UpdateTallyIndicators();
            SelectProgramChannel(_currentProgramIndex);

            await Task.Delay(50);
            _isTransitioning = false;
        }

        private async void BtnDissolveTransition_Click(object sender, RoutedEventArgs e)
        {
            if (_isTransitioning) return;
            _isTransitioning = true;
            LogEvent("[SWITCHER]", $"Thực hiện DISSOLVE (1.0s) Transition: {_channelNames[_currentProgramIndex]} ➔ {_channelNames[_currentPreviewIndex]}...");

            int steps = 20;
            for (int s = 0; s <= steps; s++)
            {
                await Task.Delay(50);
            }

            int temp = _currentProgramIndex;
            _currentProgramIndex = _currentPreviewIndex;
            _currentPreviewIndex = temp;

            UpdateTallyIndicators();
            SelectProgramChannel(_currentProgramIndex);

            _isTransitioning = false;
            LogEvent("[SWITCHER]", "Dissolve hoàn tất.");
        }

        #endregion

        #region Audio Monitoring & VU Meter Handlers

        private void OnAudioLevelsUpdated(ChannelAudioLevels[] levels)
        {
            Dispatcher.InvokeAsync(() =>
            {
                for (int i = 0; i < MaxChannels; i++)
                {
                    if (i < levels.Length && i < _activeChannelCount)
                    {
                        double leftDb = Math.Clamp(levels[i].LeftDb, -60.0, 0.0);
                        double rightDb = Math.Clamp(levels[i].RightDb, -60.0, 0.0);

                        _vuBarsL[i].Value = leftDb;
                        _vuBarsR[i].Value = rightDb;

                        bool clipL = levels[i].IsChannelClipping[0];
                        bool clipR = levels[i].IsChannelClipping[1];

                        _vuBarsL[i].Foreground = GetVuMeterColorBrush(leftDb, clipL);
                        _vuBarsR[i].Foreground = GetVuMeterColorBrush(rightDb, clipR);

                        if (i < _clipLedsCamL.Length && _clipLedsCamL[i] != null)
                            _clipLedsCamL[i].Fill = clipL ? _brushLedClip : _brushLedOff;

                        if (i < _clipLedsCamR.Length && _clipLedsCamR[i] != null)
                            _clipLedsCamR[i].Fill = clipR ? _brushLedClip : _brushLedOff;

                        if (i < _txtAudioPeakCams.Length && _txtAudioPeakCams[i] != null)
                        {
                            double maxDb = Math.Max(levels[i].LeftDb, levels[i].RightDb);
                            _txtAudioPeakCams[i].Text = (maxDb > -55.0) ? $"{maxDb:F1}" : "-∞";
                        }
                    }
                    else
                    {
                        _vuBarsL[i].Value = -60;
                        _vuBarsR[i].Value = -60;

                        if (i < _clipLedsCamL.Length && _clipLedsCamL[i] != null)
                            _clipLedsCamL[i].Fill = _brushLedOff;

                        if (i < _clipLedsCamR.Length && _clipLedsCamR[i] != null)
                            _clipLedsCamR[i].Fill = _brushLedOff;

                        if (i < _txtAudioPeakCams.Length && _txtAudioPeakCams[i] != null)
                            _txtAudioPeakCams[i].Text = "-∞";
                    }
                }
            }, DispatcherPriority.Render);
        }

        private static SolidColorBrush GetVuMeterColorBrush(double db, bool isClip = false)
        {
            if (isClip || db >= -1.0)
            {
                return new SolidColorBrush(Color.FromRgb(244, 67, 54)); // Red (Clip / Peak Alert)
            }
            if (db >= -18.0)
            {
                return new SolidColorBrush(Color.FromRgb(255, 193, 7)); // Yellow (Standard Broadcast Program Range)
            }
            return new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Green (Normal range)
        }

        private void OnProgramLevelsUpdated(ChannelAudioLevels pgm)
        {
            Dispatcher.InvokeAsync(() =>
            {
                int count = _audioManager.ConfiguredChannelCount;
                for (int i = 0; i < _vuMasterBars.Length; i++)
                {
                    if (i < count)
                    {
                        _vuMasterBars[i].Value = pgm.PeakPercent[i];
                        _vuMasterBars[i].Foreground = pgm.IsChannelClipping[i] ? Brushes.Red : new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
                    }
                    else
                    {
                        _vuMasterBars[i].Value = 0;
                    }
                }

                if (TxtMasterPeakDb != null)
                {
                    double maxPk = AudioMeterService.MIN_DBFS;
                    for (int i = 0; i < count; i++)
                    {
                        if (pgm.PeakDb[i] > maxPk) maxPk = pgm.PeakDb[i];
                    }
                    TxtMasterPeakDb.Text = (maxPk > -55.0) ? $"{maxPk:F1} dB" : "-∞ dB";
                    TxtMasterPeakDb.Foreground = pgm.IsClipping ? Brushes.Red : new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
                }

                // Also update top PGM screen VU meters
                if (VuPgmTopL != null)
                {
                    VuPgmTopL.Value = pgm.PeakPercent[0];
                    VuPgmTopL.Foreground = pgm.IsChannelClipping[0] ? Brushes.Red : new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
                }
                if (VuPgmTopR != null)
                {
                    VuPgmTopR.Value = pgm.PeakPercent[1];
                    VuPgmTopR.Foreground = pgm.IsChannelClipping[1] ? Brushes.Red : new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
                }
            }, DispatcherPriority.Render);
        }

        private void CmbAudioChannels_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized || CmbAudioChannels == null) return;
            _audioManager.ChannelConfig = CmbAudioChannels.SelectedIndex switch
            {
                0 => AudioChannelConfiguration.Stereo2Ch,
                1 => AudioChannelConfiguration.Channels4Ch,
                2 => AudioChannelConfiguration.Surround51_6Ch,
                3 => AudioChannelConfiguration.Surround71_8Ch,
                4 => AudioChannelConfiguration.SdiEmbedded16Ch,
                _ => AudioChannelConfiguration.Stereo2Ch
            };

            UpdateMasterVuVisibility();
        }

        private void UpdateMasterVuVisibility()
        {
            int chCount = _audioManager.ConfiguredChannelCount;
            for (int i = 0; i < _vuMasterBars.Length; i++)
            {
                _vuMasterBars[i].Visibility = (i < chCount) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void UpdateSoloButtonsUI()
        {
            if (_btnSoloCams == null || _btnSoloCams.Length == 0) return;

            for (int i = 0; i < _btnSoloCams.Length; i++)
            {
                if (_btnSoloCams[i] == null) continue;
                int camNum = i + 1;
                bool isThisSolo = (_audioManager.SoloSource == (SoloAudioSource)camNum);

                if (isThisSolo)
                {
                    // Đổi màu nổi bật cho nút SOLO được chọn (Vàng Amber #F59E0B với chữ đậm đen)
                    _btnSoloCams[i].Background = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
                    _btnSoloCams[i].Foreground = Brushes.Black;
                    _btnSoloCams[i].FontWeight = FontWeights.Bold;
                }
                else
                {
                    // Màu mặc định cho nút SOLO không chọn
                    _btnSoloCams[i].Background = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x33));
                    _btnSoloCams[i].Foreground = Brushes.White;
                    _btnSoloCams[i].FontWeight = FontWeights.Normal;
                }
            }
        }

        private void BtnSoloCam_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tagStr && int.TryParse(tagStr, out int camNum))
            {
                var targetSolo = camNum switch
                {
                    1 => SoloAudioSource.Cam1,
                    2 => SoloAudioSource.Cam2,
                    3 => SoloAudioSource.Cam3,
                    4 => SoloAudioSource.Cam4,
                    5 => SoloAudioSource.Cam5,
                    6 => SoloAudioSource.Cam6,
                    7 => SoloAudioSource.Cam7,
                    8 => SoloAudioSource.Cam8,
                    9 => SoloAudioSource.Cam9,
                    10 => SoloAudioSource.Cam10,
                    _ => SoloAudioSource.ProgramMaster
                };

                // Nhấn lại nút SOLO đang bật sẽ tắt SOLO và quay về PGM MASTER
                if (_audioManager.SoloSource == targetSolo)
                {
                    _audioManager.SoloSource = SoloAudioSource.ProgramMaster;
                }
                else
                {
                    _audioManager.SoloSource = targetSolo;
                }

                TxtCurrentSolo.Text = _audioManager.GetSoloLabel(_audioManager.SoloSource);
                UpdateSoloButtonsUI();
            }
        }

        private void BtnMuteAll_Click(object sender, RoutedEventArgs e)
        {
            _audioManager.IsMuteAll = !_audioManager.IsMuteAll;
            BtnMuteAll.Content = _audioManager.IsMuteAll ? "UNMUTE" : "MUTE ALL";
            BtnMuteAll.Background = _audioManager.IsMuteAll ? Brushes.Red : new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x38));
        }

        private void ChkMuteCam_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.Tag is string tagStr && int.TryParse(tagStr, out int camIdx))
            {
                bool isMuted = cb.IsChecked == true;
                _audioManager.SetChannelMuted(camIdx, isMuted, _channelNames[camIdx]);
            }
        }

        private void ChkVuCam_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.Tag is string tagStr && int.TryParse(tagStr, out int camIdx))
            {
                bool isVisible = cb.IsChecked == true;
                if (camIdx >= 0 && camIdx < _overlayAudioVuCams.Length && _overlayAudioVuCams[camIdx] != null)
                {
                    _overlayAudioVuCams[camIdx].Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
                }
            }
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
            if (!_isInitialized || _hudOverlays == null) return;
            bool show = ChkShowHudOverlay.IsChecked == true;
            for (int i = 0; i < MaxChannels; i++)
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
            bool enable = ChkNDIOutput.IsChecked == true;
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
