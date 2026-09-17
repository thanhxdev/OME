using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using OpenMedia.Platform;
using OpenMedia.Platform.Controls.Wpf;
using OpenMedia.Platform.Models;

namespace WEBRTC_DECODE
{
    public partial class MainWindow : Window
    {
        private const int MaxChannels = 10;

        // ─── Subsystem Engines ──────────────────────────────────────
        private readonly NtpSyncEngine _syncEngine = new();
        private readonly VideoPlayoutAlignmentEngine _playoutAlignmentEngine;
        private readonly MultiStreamRtpReceiver _rtpReceiver;
        private readonly WebRtcSignalingClient _signalingClient = new();
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
        private volatile bool _isShuttingDown = false;
        private bool _isClosed = false;
        private readonly StringBuilder _logBuffer = new();

        // ─── Studio Mic / Talkback Audio Input ──────────────────────
        private readonly AudioInputDevice _studioMicDevice = new();
        private bool _isStudioMicEnabled = false;
        private bool _isTalkbackPressed = false;

        // ─── Intercom Matrix Console State & Opus Codec ────────────
        private string _intercomTarget = "all"; // "all" | "cam-01" | "cam-02" ...
        private bool _isIntercomLatchMode = true; // true = Latch (Toggle), false = Momentary (PTT)
        private CancellationTokenSource? _intercomTalkbackCts;
        private Button[] _intercomTargetButtons = Array.Empty<Button>();
        private readonly IntercomOpusCodec _intercomCodec = new();
        private DispatcherTimer? _intercomIncomingWatchdogTimer;

        // ─── Engine Event Delegates for Safe Unhooking ──────────────
        private Action<string, string>? _logDelegate;
        private Action<int, ReceiverChannelState>? _channelUpdatedDelegate;
        private Action<int, byte[], int, int>? _frameReadyDelegate;
        private Action<int, byte[], int>? _audioPcmReadyDelegate;
        private Action<int>? _audioResetDelegate;
        private Action<int>? _audioAlignDelegate;
        private Action<ChannelAudioLevels[]>? _camLevelsUpdatedDelegate;
        private Action<ChannelAudioLevels>? _programLevelsUpdatedDelegate;
        private Action<ChannelAudioLevels>? _studioMicLevelsDelegate;
        private Action<byte[], int, int>? _programMixedPcmDelegate;

        // ─── Dynamic Metadata State ─────────────────────────────────
        private readonly string[] _channelNames = new string[MaxChannels];
        private readonly string[] _cameraDisplayNames = new string[MaxChannels];
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

        // ─── Studio Audio Mixer Arrays ──────────────────────────────
        private Border[] _mixerStrips = Array.Empty<Border>();
        private Border[] _mixerTallyBadges = Array.Empty<Border>();
        private TextBlock[] _mixerTallyTexts = Array.Empty<TextBlock>();
        private TextBlock[] _mixerNameTexts = Array.Empty<TextBlock>();
        private ProgressBar[] _mixerVuBarsL = Array.Empty<ProgressBar>();
        private ProgressBar[] _mixerVuBarsR = Array.Empty<ProgressBar>();
        private Ellipse[] _mixerClipLedsL = Array.Empty<Ellipse>();
        private Ellipse[] _mixerClipLedsR = Array.Empty<Ellipse>();
        private TextBlock[] _mixerGainTexts = Array.Empty<TextBlock>();
        private Slider[] _mixerFaders = Array.Empty<Slider>();
        private Slider[] _mixerPans = Array.Empty<Slider>();
        private TextBlock[] _mixerPanTexts = Array.Empty<TextBlock>();
        private Button[] _mixerMuteButtons = Array.Empty<Button>();
        private Button[] _mixerSoloButtons = Array.Empty<Button>();

        // TextBlock & HUD array references
        private TextBlock[] _hudRtt = Array.Empty<TextBlock>();
        private TextBlock[] _hudLoss = Array.Empty<TextBlock>();
        private TextBlock[] _hudBitrate = Array.Empty<TextBlock>();
        private TextBlock[] _hudDrift = Array.Empty<TextBlock>();

        private TextBlock[] _diagRtt = Array.Empty<TextBlock>();
        private TextBlock[] _diagLoss = Array.Empty<TextBlock>();
        private TextBlock[] _diagBitrate = Array.Empty<TextBlock>();
        private TextBlock[] _diagFps = Array.Empty<TextBlock>();
        private TextBlock[] _diagJitter = Array.Empty<TextBlock>();
        private TextBlock[] _diagNack = Array.Empty<TextBlock>();
        private TextBlock[] _diagHealth = Array.Empty<TextBlock>();
        private Border[] _brdTelemetryQos = Array.Empty<Border>();
        private ProgressBar[] _pbBuffer = Array.Empty<ProgressBar>();
        private TextBlock[] _txtDriftVal = Array.Empty<TextBlock>();

        // Ingest form inputs
        private TextBox[] _txtNameInputs = Array.Empty<TextBox>();
        private TextBox[] _txtIps = Array.Empty<TextBox>();
        private TextBox[] _txtPorts = Array.Empty<TextBox>();
        private TextBox[] _txtAudioPorts = Array.Empty<TextBox>();
        private TextBox[] _txtCameraIds = Array.Empty<TextBox>();
        private ComboBox[] _cmbPortModes = Array.Empty<ComboBox>();
        private Button[] _btnToggles = Array.Empty<Button>();

        // ─── Hardware & Display Discovery ───────────────────────────
        private List<DisplayMonitorInfo> _monitors = new();
        private List<SdiDeviceInfo> _sdiDevices = new();
        private FullscreenPlayoutWindow? _playoutWindow;

        // ─── ISO Channel Outputs (CAM 1..10) ────────────────────────
        private CheckBox[] _chkIsoSdi = Array.Empty<CheckBox>();
        private ComboBox[] _cmbIsoSdiPort = Array.Empty<ComboBox>();
        private CheckBox[] _chkIsoNdi = Array.Empty<CheckBox>();
        private TextBox[] _txtIsoNdiName = Array.Empty<TextBox>();
        private CheckBox[] _chkIsoSrt = Array.Empty<CheckBox>();
        private TextBox[] _txtIsoSrtHost = Array.Empty<TextBox>();
        private TextBox[] _txtIsoSrtPort = Array.Empty<TextBox>();
        private ComboBox[] _cmbIsoSrtMode = Array.Empty<ComboBox>();
        private ComboBox[] _cmbIsoSrtCodec = Array.Empty<ComboBox>();
        private TextBox[] _txtIsoSrtBitrate = Array.Empty<TextBox>();
        private CheckBox[] _chkIsoRec = Array.Empty<CheckBox>();
        private ComboBox[] _cmbIsoRecFormat = Array.Empty<ComboBox>();
        private ComboBox[] _cmbIsoRes = Array.Empty<ComboBox>();
        private ComboBox[] _cmbIsoCodec = Array.Empty<ComboBox>();
        private ComboBox[] _cmbIsoBitrate = Array.Empty<ComboBox>();
        private ComboBox[] _cmbIsoFps = Array.Empty<ComboBox>();

        // ─── Video Presentation Bitmaps ─────────────────────────────
        private readonly WriteableBitmap?[] _camBitmaps = new WriteableBitmap?[MaxChannels];

        public MainWindow()
        {
            _playoutAlignmentEngine = new VideoPlayoutAlignmentEngine(_syncEngine);
            _playoutAlignmentEngine.FrameReadyForPlayout += OnPlayoutFrameReady;

            _rtpReceiver = new MultiStreamRtpReceiver(_syncEngine);

            // Create strongly-referenced delegates for unhooking
            _logDelegate = LogEvent;
            _channelUpdatedDelegate = OnReceiverChannelUpdated;
            _frameReadyDelegate = OnFrameReady;
            _audioPcmReadyDelegate = (chIdx, pcm, len) =>
            {
                if (_isShuttingDown) return;
                _audioManager.ProcessDecodedPcm(chIdx, pcm, len);
                _outputManager.FeedIsoAudio(chIdx, pcm, len);
            };
            _camLevelsUpdatedDelegate = OnAudioLevelsUpdated;
            _programLevelsUpdatedDelegate = OnProgramLevelsUpdated;
            _studioMicLevelsDelegate = OnStudioMicLevelsUpdated;
            _programMixedPcmDelegate = (pcm, offset, count) =>
            {
                if (_isShuttingDown) return;
                byte[] pcmCopy = new byte[count];
                Buffer.BlockCopy(pcm, offset, pcmCopy, 0, count);
                _outputManager.FeedMasterAudio(pcmCopy, count);
            };

            // Wire log events
            _syncEngine.LogEmitted += _logDelegate;
            _rtpReceiver.LogEmitted += _logDelegate;
            _audioManager.LogEmitted += _logDelegate;
            _outputManager.LogEmitted += _logDelegate;

            // Wire receiver updates
            _audioResetDelegate = (chIdx) =>
            {
                if (_isShuttingDown) return;
                _audioManager.ResetChannelBuffer(chIdx);
            };
            _audioAlignDelegate = (chIdx) =>
            {
                if (_isShuttingDown) return;
                _audioManager.AlignChannelToLive(chIdx, 3840);
            };

            _rtpReceiver.ChannelUpdated += _channelUpdatedDelegate;
            _rtpReceiver.FrameReady += _frameReadyDelegate;
            _rtpReceiver.AudioPcmReady += _audioPcmReadyDelegate;
            _rtpReceiver.AudioResetRequested += _audioResetDelegate;
            _rtpReceiver.AudioAlignmentRequested += _audioAlignDelegate;
            _rtpReceiver.CameraDisplayNameReceived += (chIdx, displayName) =>
            {
                if (_isShuttingDown || Dispatcher.HasShutdownStarted) return;
                Dispatcher.InvokeAsync(() =>
                {
                    if (chIdx >= 0 && chIdx < MaxChannels && !string.IsNullOrWhiteSpace(displayName))
                    {
                        if (_cameraDisplayNames[chIdx] != displayName)
                        {
                            _cameraDisplayNames[chIdx] = displayName;
                            UpdateChannelDisplayMeta(chIdx);
                            LogEvent("[SDES]", $"🏷️ Nhận Camera Display Name qua RTCP SDES cho CAM {chIdx + 1}: \"{displayName}\"");
                        }
                    }
                });
            };
            _rtpReceiver.ChannelSocketsBound += (idx, vPort, aPort, singlePort) =>
            {
                if (_isShuttingDown) return;
                string camId = $"cam-{(idx + 1):D2}";
                _ = _signalingClient.SendDecoderReadyAsync(camId, vPort, aPort, singlePort);
            };
            _audioManager.CamLevelsUpdated += _camLevelsUpdatedDelegate;
            _audioManager.ProgramLevelsUpdated += _programLevelsUpdatedDelegate;
            _audioManager.StudioMicLevelsUpdated += _studioMicLevelsDelegate;
            _audioManager.ProgramMixedPcmAvailable += _programMixedPcmDelegate;

            // Wire Studio Mic raw capture
            _studioMicDevice.DataAvailable += pcm =>
            {
                if (_isShuttingDown || !_isStudioMicEnabled) return;
                _audioManager.FeedStudioMicPcm(pcm, pcm.Length);
            };

            // Wire Intercom incoming audio from Encoders
            _signalingClient.IntercomAudioReceived += OnIntercomAudioReceived;

            InitializeComponent();
            _intercomIncomingWatchdogTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            _intercomIncomingWatchdogTimer.Tick += (s, e) =>
            {
                _intercomIncomingWatchdogTimer.Stop();
                if (!_isShuttingDown && PnlIntercomIncoming != null)
                {
                    PnlIntercomIncoming.Visibility = Visibility.Collapsed;
                }
            };
            _isInitialized = true;

            MasterClockProvider.Instance.SyncStatusChanged += res =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    if (TxtNtpOffsetResult != null)
                    {
                        if (res.Success)
                        {
                            TxtNtpOffsetResult.Text = res.GetFormattedOffset();
                            TxtNtpOffsetResult.Foreground = Brushes.LightGreen;
                        }
                        else
                        {
                            TxtNtpOffsetResult.Text = "NTP Sync Failed";
                            TxtNtpOffsetResult.Foreground = Brushes.Red;
                        }
                    }
                });
            };

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        #region Initialization & Lifecycle

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Initialize default names and ports
                // Initialize default names and ports (matching WEBRTC_ENCODE RTP ports: 10000, 10004, 10008, ...)
                for (int i = 0; i < MaxChannels; i++)
                {
                    _channelNames[i] = $"CAM {i + 1}";
                    _channelPorts[i] = 10000 + (i * 4);
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

                // Intercom Console Target Buttons
                _intercomTargetButtons = new[]
                {
                    BtnIntercomAll, BtnIntercomCam1, BtnIntercomCam2, BtnIntercomCam3,
                    BtnIntercomCam4, BtnIntercomCam5, BtnIntercomCam6, BtnIntercomCam7, BtnIntercomCam8
                };
                _ingestCards = new[] { CardCam1, CardCam2, CardCam3, CardCam4, CardCam5, CardCam6, CardCam7, CardCam8, CardCam9, CardCam10 };
                _driftRows = new[] { RowDriftCam1, RowDriftCam2, RowDriftCam3, RowDriftCam4, RowDriftCam5, RowDriftCam6, RowDriftCam7, RowDriftCam8, RowDriftCam9, RowDriftCam10 };
                _telemetryCards = new[] { CardTelemetryCam1, CardTelemetryCam2, CardTelemetryCam3, CardTelemetryCam4, CardTelemetryCam5, CardTelemetryCam6, CardTelemetryCam7, CardTelemetryCam8, CardTelemetryCam9, CardTelemetryCam10 };
                _outputCards = new[] { CardOutputCam1, CardOutputCam2, CardOutputCam3, CardOutputCam4, CardOutputCam5, CardOutputCam6, CardOutputCam7, CardOutputCam8, CardOutputCam9, CardOutputCam10 };
                _badgeOutputTexts = new[] { TxtBadgeOutputCam1, TxtBadgeOutputCam2, TxtBadgeOutputCam3, TxtBadgeOutputCam4, TxtBadgeOutputCam5, TxtBadgeOutputCam6, TxtBadgeOutputCam7, TxtBadgeOutputCam8, TxtBadgeOutputCam9, TxtBadgeOutputCam10 };
                _txtOutputNdiNames = new[] { TxtOutputNdiNameCam1, TxtOutputNdiNameCam2, TxtOutputNdiNameCam3, TxtOutputNdiNameCam4, TxtOutputNdiNameCam5, TxtOutputNdiNameCam6, TxtOutputNdiNameCam7, TxtOutputNdiNameCam8, TxtOutputNdiNameCam9, TxtOutputNdiNameCam10 };

                // ISO Camera Output Controls
                _chkIsoSdi = new[] { ChkOutputSdiCam1, ChkOutputSdiCam2, ChkOutputSdiCam3, ChkOutputSdiCam4, ChkOutputSdiCam5, ChkOutputSdiCam6, ChkOutputSdiCam7, ChkOutputSdiCam8, ChkOutputSdiCam9, ChkOutputSdiCam10 };
                _cmbIsoSdiPort = new[] { CmbOutputSdiPortCam1, CmbOutputSdiPortCam2, CmbOutputSdiPortCam3, CmbOutputSdiPortCam4, CmbOutputSdiPortCam5, CmbOutputSdiPortCam6, CmbOutputSdiPortCam7, CmbOutputSdiPortCam8, CmbOutputSdiPortCam9, CmbOutputSdiPortCam10 };
                _chkIsoNdi = new[] { ChkOutputNdiCam1, ChkOutputNdiCam2, ChkOutputNdiCam3, ChkOutputNdiCam4, ChkOutputNdiCam5, ChkOutputNdiCam6, ChkOutputNdiCam7, ChkOutputNdiCam8, ChkOutputNdiCam9, ChkOutputNdiCam10 };
                _txtIsoNdiName = new[] { TxtOutputNdiNameCam1, TxtOutputNdiNameCam2, TxtOutputNdiNameCam3, TxtOutputNdiNameCam4, TxtOutputNdiNameCam5, TxtOutputNdiNameCam6, TxtOutputNdiNameCam7, TxtOutputNdiNameCam8, TxtOutputNdiNameCam9, TxtOutputNdiNameCam10 };
                _chkIsoSrt = new[] { ChkOutputSrtCam1, ChkOutputSrtCam2, ChkOutputSrtCam3, ChkOutputSrtCam4, ChkOutputSrtCam5, ChkOutputSrtCam6, ChkOutputSrtCam7, ChkOutputSrtCam8, ChkOutputSrtCam9, ChkOutputSrtCam10 };
                _txtIsoSrtHost = new[] { TxtOutputSrtHostCam1, TxtOutputSrtHostCam2, TxtOutputSrtHostCam3, TxtOutputSrtHostCam4, TxtOutputSrtHostCam5, TxtOutputSrtHostCam6, TxtOutputSrtHostCam7, TxtOutputSrtHostCam8, TxtOutputSrtHostCam9, TxtOutputSrtHostCam10 };
                _txtIsoSrtPort = new[] { TxtOutputSrtPortCam1, TxtOutputSrtPortCam2, TxtOutputSrtPortCam3, TxtOutputSrtPortCam4, TxtOutputSrtPortCam5, TxtOutputSrtPortCam6, TxtOutputSrtPortCam7, TxtOutputSrtPortCam8, TxtOutputSrtPortCam9, TxtOutputSrtPortCam10 };
                _cmbIsoSrtMode = new[] { CmbOutputSrtModeCam1, CmbOutputSrtModeCam2, CmbOutputSrtModeCam3, CmbOutputSrtModeCam4, CmbOutputSrtModeCam5, CmbOutputSrtModeCam6, CmbOutputSrtModeCam7, CmbOutputSrtModeCam8, CmbOutputSrtModeCam9, CmbOutputSrtModeCam10 };
                _cmbIsoSrtCodec = new[] { CmbOutputSrtCodecCam1, CmbOutputSrtCodecCam2, CmbOutputSrtCodecCam3, CmbOutputSrtCodecCam4, CmbOutputSrtCodecCam5, CmbOutputSrtCodecCam6, CmbOutputSrtCodecCam7, CmbOutputSrtCodecCam8, CmbOutputSrtCodecCam9, CmbOutputSrtCodecCam10 };
                _txtIsoSrtBitrate = new[] { TxtOutputSrtBitrateCam1, TxtOutputSrtBitrateCam2, TxtOutputSrtBitrateCam3, TxtOutputSrtBitrateCam4, TxtOutputSrtBitrateCam5, TxtOutputSrtBitrateCam6, TxtOutputSrtBitrateCam7, TxtOutputSrtBitrateCam8, TxtOutputSrtBitrateCam9, TxtOutputSrtBitrateCam10 };
                _chkIsoRec = new[] { ChkOutputRecCam1, ChkOutputRecCam2, ChkOutputRecCam3, ChkOutputRecCam4, ChkOutputRecCam5, ChkOutputRecCam6, ChkOutputRecCam7, ChkOutputRecCam8, ChkOutputRecCam9, ChkOutputRecCam10 };
                _cmbIsoRecFormat = new[] { CmbOutputRecFormatCam1, CmbOutputRecFormatCam2, CmbOutputRecFormatCam3, CmbOutputRecFormatCam4, CmbOutputRecFormatCam5, CmbOutputRecFormatCam6, CmbOutputRecFormatCam7, CmbOutputRecFormatCam8, CmbOutputRecFormatCam9, CmbOutputRecFormatCam10 };
                _cmbIsoRes = new[] { CmbOutputResCam1, CmbOutputResCam2, CmbOutputResCam3, CmbOutputResCam4, CmbOutputResCam5, CmbOutputResCam6, CmbOutputResCam7, CmbOutputResCam8, CmbOutputResCam9, CmbOutputResCam10 };
                _cmbIsoCodec = new[] { CmbOutputCodecCam1, CmbOutputCodecCam2, CmbOutputCodecCam3, CmbOutputCodecCam4, CmbOutputCodecCam5, CmbOutputCodecCam6, CmbOutputCodecCam7, CmbOutputCodecCam8, CmbOutputCodecCam9, CmbOutputCodecCam10 };
                _cmbIsoBitrate = new[] { CmbOutputBitrateCam1, CmbOutputBitrateCam2, CmbOutputBitrateCam3, CmbOutputBitrateCam4, CmbOutputBitrateCam5, CmbOutputBitrateCam6, CmbOutputBitrateCam7, CmbOutputBitrateCam8, CmbOutputBitrateCam9, CmbOutputBitrateCam10 };
                _cmbIsoFps = new[] { CmbOutputFpsCam1, CmbOutputFpsCam2, CmbOutputFpsCam3, CmbOutputFpsCam4, CmbOutputFpsCam5, CmbOutputFpsCam6, CmbOutputFpsCam7, CmbOutputFpsCam8, CmbOutputFpsCam9, CmbOutputFpsCam10 };

                for (int i = 0; i < MaxChannels; i++)
                {
                    int camIdx = i;
                    _chkIsoSdi[i].Tag = camIdx;
                    _chkIsoSdi[i].Checked += ChkIsoSdi_Changed;
                    _chkIsoSdi[i].Unchecked += ChkIsoSdi_Changed;

                    _chkIsoNdi[i].Tag = camIdx;
                    _chkIsoNdi[i].Checked += ChkIsoNdi_Changed;
                    _chkIsoNdi[i].Unchecked += ChkIsoNdi_Changed;

                    _chkIsoSrt[i].Tag = camIdx;
                    _chkIsoSrt[i].Checked += ChkIsoSrt_Changed;
                    _chkIsoSrt[i].Unchecked += ChkIsoSrt_Changed;

                    _chkIsoRec[i].Tag = camIdx;
                    _chkIsoRec[i].Checked += ChkIsoRec_Changed;
                    _chkIsoRec[i].Unchecked += ChkIsoRec_Changed;
                }
                _chkMuteCams = new[] { ChkMuteCam1, ChkMuteCam2, ChkMuteCam3, ChkMuteCam4, ChkMuteCam5, ChkMuteCam6, ChkMuteCam7, ChkMuteCam8, ChkMuteCam9, ChkMuteCam10 };
                _chkVuCams = new[] { ChkVuCam1, ChkVuCam2, ChkVuCam3, ChkVuCam4, ChkVuCam5, ChkVuCam6, ChkVuCam7, ChkVuCam8, ChkVuCam9, ChkVuCam10 };
                _btnSoloCams = new[] { BtnSoloCam1, BtnSoloCam2, BtnSoloCam3, BtnSoloCam4, BtnSoloCam5, BtnSoloCam6, BtnSoloCam7, BtnSoloCam8, BtnSoloCam9, BtnSoloCam10 };
                _videoViews = new[] { VideoViewCam1, VideoViewCam2, VideoViewCam3, VideoViewCam4, VideoViewCam5, VideoViewCam6, VideoViewCam7, VideoViewCam8, VideoViewCam9, VideoViewCam10 };

                // Cache Studio Audio Mixer Elements
                _mixerStrips = new[] { StripMixerCam1, StripMixerCam2, StripMixerCam3, StripMixerCam4, StripMixerCam5, StripMixerCam6, StripMixerCam7, StripMixerCam8, StripMixerCam9, StripMixerCam10 };
                _mixerTallyBadges = new[] { TallyMixerCam1, TallyMixerCam2, TallyMixerCam3, TallyMixerCam4, TallyMixerCam5, TallyMixerCam6, TallyMixerCam7, TallyMixerCam8, TallyMixerCam9, TallyMixerCam10 };
                _mixerTallyTexts = new[] { TxtTallyMixerCam1, TxtTallyMixerCam2, TxtTallyMixerCam3, TxtTallyMixerCam4, TxtTallyMixerCam5, TxtTallyMixerCam6, TxtTallyMixerCam7, TxtTallyMixerCam8, TxtTallyMixerCam9, TxtTallyMixerCam10 };
                _mixerNameTexts = new[] { TxtMixerNameCam1, TxtMixerNameCam2, TxtMixerNameCam3, TxtMixerNameCam4, TxtMixerNameCam5, TxtMixerNameCam6, TxtMixerNameCam7, TxtMixerNameCam8, TxtMixerNameCam9, TxtMixerNameCam10 };
                _mixerVuBarsL = new[] { VuMixerCam1L, VuMixerCam2L, VuMixerCam3L, VuMixerCam4L, VuMixerCam5L, VuMixerCam6L, VuMixerCam7L, VuMixerCam8L, VuMixerCam9L, VuMixerCam10L };
                _mixerVuBarsR = new[] { VuMixerCam1R, VuMixerCam2R, VuMixerCam3R, VuMixerCam4R, VuMixerCam5R, VuMixerCam6R, VuMixerCam7R, VuMixerCam8R, VuMixerCam9R, VuMixerCam10R };
                _mixerClipLedsL = new[] { ClipMixerCam1_L, ClipMixerCam2_L, ClipMixerCam3_L, ClipMixerCam4_L, ClipMixerCam5_L, ClipMixerCam6_L, ClipMixerCam7_L, ClipMixerCam8_L, ClipMixerCam9_L, ClipMixerCam10_L };
                _mixerClipLedsR = new[] { ClipMixerCam1_R, ClipMixerCam2_R, ClipMixerCam3_R, ClipMixerCam4_R, ClipMixerCam5_R, ClipMixerCam6_R, ClipMixerCam7_R, ClipMixerCam8_R, ClipMixerCam9_R, ClipMixerCam10_R };
                _mixerGainTexts = new[] { TxtGainDbCam1, TxtGainDbCam2, TxtGainDbCam3, TxtGainDbCam4, TxtGainDbCam5, TxtGainDbCam6, TxtGainDbCam7, TxtGainDbCam8, TxtGainDbCam9, TxtGainDbCam10 };
                _mixerFaders = new[] { FaderCam1, FaderCam2, FaderCam3, FaderCam4, FaderCam5, FaderCam6, FaderCam7, FaderCam8, FaderCam9, FaderCam10 };
                _mixerPans = new[] { PanCam1, PanCam2, PanCam3, PanCam4, PanCam5, PanCam6, PanCam7, PanCam8, PanCam9, PanCam10 };
                _mixerPanTexts = new[] { TxtPanCam1, TxtPanCam2, TxtPanCam3, TxtPanCam4, TxtPanCam5, TxtPanCam6, TxtPanCam7, TxtPanCam8, TxtPanCam9, TxtPanCam10 };
                _mixerMuteButtons = new[] { BtnMixerMuteCam1, BtnMixerMuteCam2, BtnMixerMuteCam3, BtnMixerMuteCam4, BtnMixerMuteCam5, BtnMixerMuteCam6, BtnMixerMuteCam7, BtnMixerMuteCam8, BtnMixerMuteCam9, BtnMixerMuteCam10 };
                _mixerSoloButtons = new[] { BtnMixerSoloCam1, BtnMixerSoloCam2, BtnMixerSoloCam3, BtnMixerSoloCam4, BtnMixerSoloCam5, BtnMixerSoloCam6, BtnMixerSoloCam7, BtnMixerSoloCam8, BtnMixerSoloCam9, BtnMixerSoloCam10 };

                _audioManager.ChannelGainChanged += (ch, db) => Dispatcher.InvokeAsync(() => OnChannelGainChanged(ch, db));
                _audioManager.ChannelPanChanged += (ch, pan) => Dispatcher.InvokeAsync(() => OnChannelPanChanged(ch, pan));
                _audioManager.ChannelMuteChanged += (ch, muted) => Dispatcher.InvokeAsync(() => OnChannelMuteChanged(ch, muted));

                // Set initial VU meter overlay visibility (default to Enabled / Visible)
                for (int i = 0; i < _overlayAudioVuCams.Length; i++)
                {
                    if (_overlayAudioVuCams[i] != null)
                    {
                        _overlayAudioVuCams[i].Visibility = Visibility.Visible;
                    }
                    if (i < _chkVuCams.Length && _chkVuCams[i] != null)
                    {
                        _chkVuCams[i].IsChecked = true;
                    }
                }

                // Set default audio preview MUTE for all preview screens on layout & master preview monitor
                for (int i = 0; i < MaxChannels; i++)
                {
                    if (i < _chkMuteCams.Length && _chkMuteCams[i] != null)
                    {
                        _chkMuteCams[i].IsChecked = true;
                    }
                    _audioManager.SetChannelMuted(i, true, _channelNames[i]);
                    UpdateMixerMuteUI(i, true);
                }
                _audioManager.IsPreviewMuted = true;
                UpdatePreviewMuteUI();
                UpdateSoloButtonsUI();

                _hudRtt = new[] { HudRttCam1, HudRttCam2, HudRttCam3, HudRttCam4, HudRttCam5, HudRttCam6, HudRttCam7, HudRttCam8, HudRttCam9, HudRttCam10 };
                _hudLoss = new[] { HudLossCam1, HudLossCam2, HudLossCam3, HudLossCam4, HudLossCam5, HudLossCam6, HudLossCam7, HudLossCam8, HudLossCam9, HudLossCam10 };
                _hudBitrate = new[] { HudBitrateCam1, HudBitrateCam2, HudBitrateCam3, HudBitrateCam4, HudBitrateCam5, HudBitrateCam6, HudBitrateCam7, HudBitrateCam8, HudBitrateCam9, HudBitrateCam10 };
                _hudDrift = new[] { HudDriftCam1, HudDriftCam2, HudDriftCam3, HudDriftCam4, HudDriftCam5, HudDriftCam6, HudDriftCam7, HudDriftCam8, HudDriftCam9, HudDriftCam10 };

                _diagRtt = new[] { DiagRttCam1, DiagRttCam2, DiagRttCam3, DiagRttCam4, DiagRttCam5, DiagRttCam6, DiagRttCam7, DiagRttCam8, DiagRttCam9, DiagRttCam10 };
                _diagLoss = new[] { DiagLossCam1, DiagLossCam2, DiagLossCam3, DiagLossCam4, DiagLossCam5, DiagLossCam6, DiagLossCam7, DiagLossCam8, DiagLossCam9, DiagLossCam10 };
                _diagBitrate = new[] { DiagBitrateCam1, DiagBitrateCam2, DiagBitrateCam3, DiagBitrateCam4, DiagBitrateCam5, DiagBitrateCam6, DiagBitrateCam7, DiagBitrateCam8, DiagBitrateCam9, DiagBitrateCam10 };
                _diagFps = new[] { DiagFpsCam1, DiagFpsCam2, DiagFpsCam3, DiagFpsCam4, DiagFpsCam5, DiagFpsCam6, DiagFpsCam7, DiagFpsCam8, DiagFpsCam9, DiagFpsCam10 };
                _diagJitter = new[] { DiagJitterCam1, DiagJitterCam2, DiagJitterCam3, DiagJitterCam4, DiagJitterCam5, DiagJitterCam6, DiagJitterCam7, DiagJitterCam8, DiagJitterCam9, DiagJitterCam10 };
                _diagNack = new[] { DiagNackCam1, DiagNackCam2, DiagNackCam3, DiagNackCam4, DiagNackCam5, DiagNackCam6, DiagNackCam7, DiagNackCam8, DiagNackCam9, DiagNackCam10 };
                _diagHealth = new[] { DiagHealthCam1, DiagHealthCam2, DiagHealthCam3, DiagHealthCam4, DiagHealthCam5, DiagHealthCam6, DiagHealthCam7, DiagHealthCam8, DiagHealthCam9, DiagHealthCam10 };
                _brdTelemetryQos = new[] { BrdTelemetryQosCam1, BrdTelemetryQosCam2, BrdTelemetryQosCam3, BrdTelemetryQosCam4, BrdTelemetryQosCam5, BrdTelemetryQosCam6, BrdTelemetryQosCam7, BrdTelemetryQosCam8, BrdTelemetryQosCam9, BrdTelemetryQosCam10 };
                _pbBuffer = new[] { PbBufferCam1, PbBufferCam2, PbBufferCam3, PbBufferCam4, PbBufferCam5, PbBufferCam6, PbBufferCam7, PbBufferCam8, PbBufferCam9, PbBufferCam10 };
                _txtDriftVal = new[] { TxtDriftValCam1, TxtDriftValCam2, TxtDriftValCam3, TxtDriftValCam4, TxtDriftValCam5, TxtDriftValCam6, TxtDriftValCam7, TxtDriftValCam8, TxtDriftValCam9, TxtDriftValCam10 };

                _txtNameInputs = new[] { TxtNameCam1, TxtNameCam2, TxtNameCam3, TxtNameCam4, TxtNameCam5, TxtNameCam6, TxtNameCam7, TxtNameCam8, TxtNameCam9, TxtNameCam10 };
                _txtCameraIds = new[] { TxtCameraIdCam1, TxtCameraIdCam2, TxtCameraIdCam3, TxtCameraIdCam4, TxtCameraIdCam5, TxtCameraIdCam6, TxtCameraIdCam7, TxtCameraIdCam8, TxtCameraIdCam9, TxtCameraIdCam10 };
                _txtIps = new[] { TxtIpCam1, TxtIpCam2, TxtIpCam3, TxtIpCam4, TxtIpCam5, TxtIpCam6, TxtIpCam7, TxtIpCam8, TxtIpCam9, TxtIpCam10 };
                _txtPorts = new[] { TxtPortCam1, TxtPortCam2, TxtPortCam3, TxtPortCam4, TxtPortCam5, TxtPortCam6, TxtPortCam7, TxtPortCam8, TxtPortCam9, TxtPortCam10 };
                _txtAudioPorts = new[] { TxtAudioPortCam1, TxtAudioPortCam2, TxtAudioPortCam3, TxtAudioPortCam4, TxtAudioPortCam5, TxtAudioPortCam6, TxtAudioPortCam7, TxtAudioPortCam8, TxtAudioPortCam9, TxtAudioPortCam10 };
                _cmbPortModes = new[] { CmbPortModeCam1, CmbPortModeCam2, CmbPortModeCam3, CmbPortModeCam4, CmbPortModeCam5, CmbPortModeCam6, CmbPortModeCam7, CmbPortModeCam8, CmbPortModeCam9, CmbPortModeCam10 };
                _btnToggles = new[] { BtnToggleCam1, BtnToggleCam2, BtnToggleCam3, BtnToggleCam4, BtnToggleCam5, BtnToggleCam6, BtnToggleCam7, BtnToggleCam8, BtnToggleCam9, BtnToggleCam10 };

                // Set Zero-Config read-only display for all Ingest Ports
                for (int i = 0; i < MaxChannels; i++)
                {
                    if (i < _txtPorts.Length && _txtPorts[i] != null)
                    {
                        _txtPorts[i].IsReadOnly = true;
                        _txtPorts[i].Text = "Auto";
                        _txtPorts[i].ToolTip = "Port Ingest: Auto (Đang nghe trên UDP ephemeral port do OS cấp)";
                    }
                    if (i < _txtAudioPorts.Length && _txtAudioPorts[i] != null)
                    {
                        _txtAudioPorts[i].IsReadOnly = true;
                        _txtAudioPorts[i].Text = "Auto";
                        _txtAudioPorts[i].ToolTip = "Port Ingest Audio: Auto";
                    }
                }

                // Lazy-initialize WriteableBitmaps for active channels only (default: 1 channel; saves ~75MB RAM on startup)
                for (int i = 0; i < _activeChannelCount; i++)
                {
                    _camBitmaps[i] = new WriteableBitmap(1920, 1080, 96, 96, PixelFormats.Bgra32, null);
                    _videoViews[i].PresentBitmap(_camBitmaps[i]);
                }
                if (_camBitmaps[0] != null)
                {
                    VideoViewPgm.PresentBitmap(_camBitmaps[0]);
                }

                // Initialize Multiviewer cell titles & metadata for all 10 cameras at launch
                for (int i = 0; i < MaxChannels; i++)
                {
                    UpdateChannelDisplayMeta(i);
                }

                LogEvent("[INFO]", "Ứng dụng OME Broadcast Multi-Camera WebRTC Studio Multiviewer & Playout Decoder & Studio Sync đang khởi chạy...");
                TxtEngineStatus.Text = "Engine: Standalone / Host Mode";
                _signalingClient.CameraStreamPublished += OnCameraStreamPublished;
                _signalingClient.CameraMetaUpdated += OnCameraMetaUpdated;
                _signalingClient.ConnectionStateChanged += OnSignalingConnectionStateChanged;
                string signalingUrl = TxtSignalingUrl?.Text?.Trim() ?? "";
                if (!string.IsNullOrEmpty(signalingUrl))
                {
                    _signalingClient.ServerUrl = signalingUrl;
                }
                _ = _signalingClient.ConnectAsync();

                // Start High-precision Master UTC Clock immediately on startup
                StartMasterClockTimer();

                // Tự động đồng bộ Master NTP nếu được kích hoạt (mặc định khởi chạy là disabled)
                if (ChkMasterSync?.IsChecked == true)
                {
                    string defaultNtp = TxtNtpServer?.Text?.Trim() ?? "time.google.com";
                    if (string.IsNullOrEmpty(defaultNtp)) defaultNtp = "time.google.com";
                    MasterClockProvider.Instance.StartPeriodicSync(defaultNtp, 30);
                    _syncEngine.MasterSyncEnabled = true;
                    _playoutAlignmentEngine.IsEnabled = true;
                }
                else
                {
                    _syncEngine.MasterSyncEnabled = false;
                    _playoutAlignmentEngine.IsEnabled = false;
                    MasterClockProvider.Instance.StopPeriodicSync();
                    if (TxtNtpOffsetResult != null)
                    {
                        TxtNtpOffsetResult.Text = "Disabled (Free-Run)";
                        TxtNtpOffsetResult.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
                    }
                }

                // Initialize OpenMedia Runtime Engine asynchronously in background to prevent UI freeze
                _ = Task.Run(async () =>
                {
                    try
                    {
                        bool runtimeInit = await OpenMediaRuntime.InitializeAsync(new RuntimeOptions { AutoLaunch = false, ConnectionTimeout = 1000 });
                        _ = Dispatcher.InvokeAsync(() =>
                        {
                            if (_isShuttingDown || Dispatcher.HasShutdownStarted) return;
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
                            }
                        });
                    }
                    catch { }
                });

                // Start Telemetry & VU Timers
                StartTelemetryTimer();
                StartVuMeterTimer();

                // Initial Active Streams UI & Layout
                UpdateActiveStreamsUI();
                UpdateTallyIndicators();
                UpdateNdiUiBadges();

                // Khởi tạo placeholder mặc định (Không tự động quét khi khởi chạy theo yêu cầu)
                if (CmbDisplayMonitors != null && CmbDisplayMonitors.Items.Count == 0)
                {
                    CmbDisplayMonitors.Items.Add("[Nhấn 'Scan Devices' để quét màn hình]");
                    CmbDisplayMonitors.SelectedIndex = 0;
                }

                if (CmbSdiDevice != null && CmbSdiDevice.Items.Count == 0)
                {
                    CmbSdiDevice.Items.Add("[Nhấn 'Scan SDI' để quét card phần cứng]");
                    CmbSdiDevice.SelectedIndex = 0;
                }

                for (int i = 0; i < MaxChannels; i++)
                {
                    if (i < _cmbIsoSdiPort.Length && _cmbIsoSdiPort[i] != null && _cmbIsoSdiPort[i].Items.Count == 0)
                    {
                        _cmbIsoSdiPort[i].Items.Add("[Nhấn Scan SDI]");
                        _cmbIsoSdiPort[i].SelectedIndex = 0;
                    }
                }

                if (CmbStudioSpeakerDevice != null && CmbStudioSpeakerDevice.Items.Count == 0)
                {
                    CmbStudioSpeakerDevice.Items.Add("[Mặc định Hệ thống (Default Audio)]");
                    CmbStudioSpeakerDevice.SelectedIndex = 0;
                }

                if (CmbStudioMicDevice != null && CmbStudioMicDevice.Items.Count == 0)
                {
                    CmbStudioMicDevice.Items.Add("[Nhấn nút để quét Micro]");
                    CmbStudioMicDevice.SelectedIndex = 0;
                }

                UpdatePreviewMuteUI();

                LogEvent("[INFO]", "Hệ thống Master Control Room đã sẵn sàng (Tab 1. WebRTC Ingest, 2. Tab Outputs, tối đa 10 luồng).");
            }
            catch (Exception ex)
            {
                LogEvent("[ERROR]", $"Lỗi khởi tạo ứng dụng: {ex.Message}");
            }
        }

        private void UnhookAllEngineEvents()
        {
            try
            {
                _studioMicDevice.Stop();
                _studioMicDevice.Dispose();
            }
            catch { }
            _isShuttingDown = true;

            try
            {
                if (_logDelegate != null)
                {
                    _syncEngine.LogEmitted -= _logDelegate;
                    _rtpReceiver.LogEmitted -= _logDelegate;
                    _audioManager.LogEmitted -= _logDelegate;
                    _outputManager.LogEmitted -= _logDelegate;
                }

                if (_channelUpdatedDelegate != null)
                    _rtpReceiver.ChannelUpdated -= _channelUpdatedDelegate;
                if (_frameReadyDelegate != null)
                    _rtpReceiver.FrameReady -= _frameReadyDelegate;
                if (_audioPcmReadyDelegate != null)
                    _rtpReceiver.AudioPcmReady -= _audioPcmReadyDelegate;
                if (_audioResetDelegate != null)
                    _rtpReceiver.AudioResetRequested -= _audioResetDelegate;
                if (_audioAlignDelegate != null)
                    _rtpReceiver.AudioAlignmentRequested -= _audioAlignDelegate;
                if (_camLevelsUpdatedDelegate != null)
                    _audioManager.CamLevelsUpdated -= _camLevelsUpdatedDelegate;
                if (_programLevelsUpdatedDelegate != null)
                    _audioManager.ProgramLevelsUpdated -= _programLevelsUpdatedDelegate;
                if (_programMixedPcmDelegate != null)
                    _audioManager.ProgramMixedPcmAvailable -= _programMixedPcmDelegate;
                _signalingClient.CameraStreamPublished -= OnCameraStreamPublished;
                _signalingClient.CameraMetaUpdated -= OnCameraMetaUpdated;
                _signalingClient.ConnectionStateChanged -= OnSignalingConnectionStateChanged;
            }
            catch { }
        }

        private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_isClosed) return;

            try
            {
                // 1. Cancel the direct close event and hide window for immediate UX feedback
                e.Cancel = true;
                _isShuttingDown = true;
                Hide();

                // 2. Dừng ngay toàn bộ Timer UI & Intercom
                _masterClockTimer?.Stop();
                _telemetryTimer?.Stop();
                _vuMeterTimer?.Stop();
                StopIntercomTalkback();
                MasterClockProvider.Instance.Dispose();
                _playoutAlignmentEngine.Dispose();

                // 3. Đóng cửa sổ Fullscreen Playout nếu đang mở
                if (_playoutWindow != null)
                {
                    try { _playoutWindow.Close(); } catch { }
                    _playoutWindow = null;
                }

                // 4. Unhook toàn bộ event log & data
                UnhookAllEngineEvents();

                // 5. Cho phép chạy cleanup song song trong background với timeout an toàn
                await Task.Run(async () =>
                {
                    try
                    {
                        var stopTask = Task.Run(async () =>
                        {
                            try
                            {
                                await _rtpReceiver.StopAllAsync().ConfigureAwait(false);
                                _rtpReceiver.Dispose();
                                _outputManager.Dispose();
                                _syncEngine.Dispose();
                                OpenMediaRuntime.Shutdown();
                            }
                            catch { }
                        });

                        await Task.WhenAny(stopTask, Task.Delay(2500)).ConfigureAwait(false);
                    }
                    catch { }
                }).ConfigureAwait(false);
            }
            catch { }
            finally
            {
                _isClosed = true;
                Environment.Exit(0);
            }
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
                _channelPorts[idx] = 10000 + (idx * 4);
            }

            bool isSinglePort = idx < _cmbPortModes.Length && _cmbPortModes[idx]?.SelectedIndex == 1;
            int audioPort = isSinglePort ? _channelPorts[idx] : _channelPorts[idx] + 2;

            if (idx < _txtAudioPorts.Length && _txtAudioPorts[idx] != null)
            {
                _txtAudioPorts[idx].Text = audioPort.ToString();
            }

            // Sync to Engine channel metadata
            _rtpReceiver.Channels[idx].Name = _channelNames[idx];
            _rtpReceiver.Channels[idx].IsSinglePortMode = isSinglePort;
            _rtpReceiver.Channels[idx].VideoPort = _channelPorts[idx];
            _rtpReceiver.Channels[idx].AudioPort = audioPort;

            // Update Multiviewer cell header & fallback title
            string displayLabel = !string.IsNullOrWhiteSpace(_cameraDisplayNames[idx])
                ? _cameraDisplayNames[idx]
                : "WebRTC RTP";

            if (idx < _headerTexts.Length && _headerTexts[idx] != null)
            {
                _headerTexts[idx].Text = $"{_channelNames[idx]} ({displayLabel})";
                _headerTexts[idx].ToolTip = $"Camera ID: cam-{(idx + 1):D2} | Display Name: {displayLabel}";
            }
            if (idx < _fallbackTitles.Length && _fallbackTitles[idx] != null)
            {
                _fallbackTitles[idx].Text = $"{_channelNames[idx]} • {displayLabel}";
            }
            if (idx < _badgeCamTexts.Length && _badgeCamTexts[idx] != null)
            {
                _badgeCamTexts[idx].Text = _channelNames[idx];
            }
            if (idx < _telemetryTitles.Length && _telemetryTitles[idx] != null)
            {
                _telemetryTitles[idx].Text = $"{_channelNames[idx]} (WebRTC Stream)";
            }
            if (idx < _mixerNameTexts.Length && _mixerNameTexts[idx] != null)
            {
                _mixerNameTexts[idx].Text = _channelNames[idx];
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
                LogEvent("[INGEST]", $"➕ Đã thêm khung WebRTC Camera Stream #{_activeChannelCount} ({_channelNames[_activeChannelCount - 1]}). Tổng số luồng: {_activeChannelCount}/10.");
            }
            else
            {
                LogEvent("[WARN]", "Đã đạt giới hạn tối đa 10 luồng WebRTC Camera Stream.");
            }
        }

        private async void BtnRemoveStream_Click(object sender, RoutedEventArgs e)
        {
            if (_activeChannelCount > 1)
            {
                int removeIdx = _activeChannelCount - 1;
                // Stop channel if running
                if (_rtpReceiver.Channels[removeIdx].IsRunning)
                {
                    await _rtpReceiver.StopChannelAsync(removeIdx);
                }

                _activeChannelCount--;
                if (removeIdx < _camBitmaps.Length && _camBitmaps[removeIdx] != null)
                {
                    _camBitmaps[removeIdx] = null;
                    if (removeIdx < _videoViews.Length && _videoViews[removeIdx] != null)
                    {
                        _videoViews[removeIdx].Detach();
                    }
                }
                if (removeIdx < _fallbacks.Length && _fallbacks[removeIdx] != null)
                {
                    _fallbacks[removeIdx].Visibility = Visibility.Visible;
                }

                if (_currentProgramIndex >= _activeChannelCount)
                {
                    _currentProgramIndex = 0;
                    UpdateTallyIndicators();
                }
                UpdateActiveStreamsUI();
                LogEvent("[INGEST]", $"➖ Đã bớt luồng WebRTC Ingest #{removeIdx + 1}. Đã thu hồi bộ nhớ bitmap. Còn lại: {_activeChannelCount}/10 luồng.");
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

                // Sync audio mixer strips with active streams
                if (i < _mixerStrips.Length && _mixerStrips[i] != null)
                {
                    _mixerStrips[i].Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
                }
            }

            // Re-apply Multiviewer layout (View vs PGM+View)
            ApplyMultiviewerLayout();

            if (_playoutWindow != null && _playoutWindow.IsMultiviewer)
            {
                _playoutWindow.UpdateLayoutConfig(_activeChannelCount, _channelNames, _currentProgramIndex, _currentPreviewIndex);
            }
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

            int selected = CmbLayoutMode.SelectedIndex;
            // 0: View (Multi-view 2 cột)
            // 1: View (Multi-view 3 cột)
            // 2: View (Multi-view 4 cột)
            // 3: PGM+View (PGM trên + Multi-view)

            MultiviewerContainer.RowDefinitions.Clear();
            MultiviewerContainer.ColumnDefinitions.Clear();

            int cols = 2;
            bool showPgmTop = false;

            switch (selected)
            {
                case 1:
                    cols = 3;
                    showPgmTop = false;
                    break;
                case 2:
                    cols = 4;
                    showPgmTop = false;
                    break;
                case 3:
                    cols = 2;
                    showPgmTop = true;
                    break;
                case 0:
                default:
                    cols = 2;
                    showPgmTop = false;
                    break;
            }

            for (int c = 0; c < cols; c++)
            {
                MultiviewerContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            int numRows = Math.Max(1, (_activeChannelCount + cols - 1) / cols);
            for (int r = 0; r < numRows; r++)
            {
                MultiviewerContainer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            }

            if (!showPgmTop)
            {
                CellPgmMaster.Visibility = Visibility.Collapsed;
            }
            else
            {
                // PGM trên cùng, dưới là tất cả các cam theo thứ tự cột
                CellPgmMaster.Visibility = Visibility.Visible;
                TxtPgmMasterTitle.Text = $"PROGRAM ({_channelNames[_currentProgramIndex]})";

                // Present live PGM frame on top screen
                if (_camBitmaps[_currentProgramIndex] != null)
                {
                    VideoViewPgm.PresentBitmap(_camBitmaps[_currentProgramIndex]);
                }
                FallbackPgm.Visibility = _fallbacks[_currentProgramIndex].Visibility;
            }

            for (int i = 0; i < MaxChannels; i++)
            {
                if (i < _activeChannelCount)
                {
                    _cellBorders[i].Visibility = Visibility.Visible;
                    int row = i / cols;
                    int col = i % cols;
                    Grid.SetRow(_cellBorders[i], row);
                    Grid.SetColumn(_cellBorders[i], col);
                    Grid.SetRowSpan(_cellBorders[i], 1);
                    Grid.SetColumnSpan(_cellBorders[i], 1);
                }
                else
                {
                    _cellBorders[i].Visibility = Visibility.Collapsed;
                }
            }
        }

        #endregion

        #region Master Clock & Telemetry Timers

        private void StartMasterClockTimer()
        {
            _masterClockTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(40) // ~25 fps update for milliseconds & SMPTE frame sync
            };
            _masterClockTimer.Tick += (s, e) =>
            {
                var now = MasterClockProvider.Instance.CurrentUtcTime;
                TxtMasterUtcClock.Text = now.ToString("HH:mm:ss.fff");
                if (TxtMasterSmpteTime != null)
                {
                    TxtMasterSmpteTime.Text = MasterClockProvider.Instance.GetFormattedSmpteTimecode(25);
                }

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
                    active[i] = i < _activeChannelCount && _rtpReceiver.Channels[i].IsConnected;
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
                var ch = _rtpReceiver.Channels[i];
                var sync = syncSnapshot[i];

                totalBitrateKbps += ch.CurrentBitrateKbps;
                totalBytes += ch.TotalBytesReceived;

                // Update HUD Overlay in Video Cell
                _hudRtt[i].Text = $"RTT: {ch.CurrentRttMs:F0} ms";
                _hudLoss[i].Text = $"Loss: {ch.CurrentPacketLoss:F2}%";
                _hudLoss[i].Foreground = ch.CurrentPacketLoss > 3.0 ? Brushes.Red : Brushes.LightGreen;
                _hudBitrate[i].Text = $"Bitrate: {ch.CurrentBitrateKbps:F0} kbps";
                _hudDrift[i].Text = $"Drift: {sync.GetFormattedDrift()}";
                _hudDrift[i].Foreground = sync.LockState == SyncLockState.Locked ? Brushes.LightGreen : (sync.LockState == SyncLockState.Syncing ? Brushes.Orange : Brushes.Gray);

                // Update Diag Tab & Real-Time Camera Drift Matrix in Tab 4
                if (i < _driftRows.Length && _driftRows[i] != null)
                {
                    _driftRows[i].Visibility = (i < _activeChannelCount) ? Visibility.Visible : Visibility.Collapsed;
                }

                _diagRtt[i].Text = $"⏱️ RTT: {ch.CurrentRttMs:F1} ms";
                _diagLoss[i].Text = $"📉 Loss: {ch.CurrentPacketLoss:F2} %";
                _diagLoss[i].Foreground = ch.CurrentPacketLoss > 3.0 ? Brushes.Red : Brushes.LightGreen;
                _diagBitrate[i].Text = $"🚀 Ingest: {ch.CurrentBitrateKbps:F0} kbps";
                _diagFps[i].Text = $"🎬 FPS: {ch.CurrentFps:F1} fps";
                _diagJitter[i].Text = $"⚡ Jitter: {ch.CurrentJitterMs:F1} ms";
                _diagNack[i].Text = $"🔄 Gaps: {ch.SequenceGaps}";

                string statusDesc = ch.BufferHealthPercent > 85 ? "STABLE" : (ch.BufferHealthPercent > 60 ? "MODERATE" : "DEGRADED");
                _diagHealth[i].Text = $"QoS: {ch.BufferHealthPercent:F0}% ({statusDesc})";
                _brdTelemetryQos[i].Background = ch.BufferHealthPercent > 85
                    ? new SolidColorBrush(Color.FromRgb(0x06, 0x4E, 0x3B))
                    : (ch.BufferHealthPercent > 60
                        ? new SolidColorBrush(Color.FromRgb(0x78, 0x35, 0x0F))
                        : new SolidColorBrush(Color.FromRgb(0x7F, 0x1D, 0x1D)));

                _pbBuffer[i].Value = sync.BufferFillPercent;
                _pbBuffer[i].Foreground = sync.LockState == SyncLockState.Locked ? Brushes.LightGreen : (sync.LockState == SyncLockState.Syncing ? Brushes.Orange : Brushes.Gray);
                
                string dropRptInfo = sync.DroppedFrames > 0 || sync.RepeatedFrames > 0 ? $" • Drop:{sync.DroppedFrames} Rpt:{sync.RepeatedFrames}" : "";
                _txtDriftVal[i].Text = $"Δt: {sync.GetFormattedDrift()} ({sync.LockState}{dropRptInfo})";
                _txtDriftVal[i].Foreground = sync.LockState == SyncLockState.Locked ? Brushes.LightGreen : (sync.LockState == SyncLockState.Syncing ? Brushes.Orange : Brushes.Gray);
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
            if (_isShuttingDown || Dispatcher.HasShutdownStarted) return;

            try
            {
                Dispatcher.InvokeAsync(() =>
                {
                    if (_isShuttingDown || index < 0 || index >= MaxChannels) return;

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

                    // Update dynamic ports on UI if assigned
                    if (index < _txtPorts.Length && _txtPorts[index] != null)
                    {
                        _txtPorts[index].Text = state.IsRunning ? state.VideoPort.ToString() : "Auto";
                        _txtPorts[index].ToolTip = state.IsRunning ? $"Local UDP: {state.VideoPort}" : "Port Ingest: Auto (Zero-Config)";
                    }
                    if (index < _txtAudioPorts.Length && _txtAudioPorts[index] != null)
                    {
                        _txtAudioPorts[index].Text = state.IsRunning ? state.AudioPort.ToString() : "Auto";
                        _txtAudioPorts[index].ToolTip = state.IsRunning ? $"Local UDP: {state.AudioPort}" : "Port Ingest: Auto (Zero-Config)";
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
            catch { }
        }

        private void OnCameraStreamPublished(string cameraId, string cameraName, bool isSinglePort, int videoPort, int audioPort, string codec)
        {
            if (_isShuttingDown || Dispatcher.HasShutdownStarted) return;

            Dispatcher.InvokeAsync(async () =>
            {
                if (_isShuttingDown) return;

                int camIndex = -1;
                var match = System.Text.RegularExpressions.Regex.Match(cameraId, @"\d+");
                if (match.Success && int.TryParse(match.Value, out int num))
                {
                    camIndex = num - 1;
                }

                if (camIndex < 0 || camIndex >= MaxChannels) return;

                if (!string.IsNullOrWhiteSpace(cameraName))
                {
                    _cameraDisplayNames[camIndex] = cameraName;
                }
                UpdateChannelDisplayMeta(camIndex);

                var ch = _rtpReceiver.Channels[camIndex];
                ch.IsSinglePortMode = isSinglePort;

                int prevVideoPort = ch.VideoPort;
                int prevAudioPort = ch.AudioPort;

                if (videoPort > 0)
                {
                    ch.VideoPort = videoPort;
                    ch.AudioPort = audioPort > 0 ? audioPort : (isSinglePort ? videoPort : videoPort + 2);

                    if (camIndex < _txtPorts.Length && _txtPorts[camIndex] != null)
                    {
                        _txtPorts[camIndex].Text = ch.VideoPort.ToString();
                    }
                    if (camIndex < _txtAudioPorts.Length && _txtAudioPorts[camIndex] != null)
                    {
                        _txtAudioPorts[camIndex].Text = ch.AudioPort.ToString();
                    }
                }

                if (camIndex < _cmbPortModes.Length && _cmbPortModes[camIndex] != null)
                {
                    _cmbPortModes[camIndex].SelectedIndex = isSinglePort ? 1 : 0;
                }

                string displayName = !string.IsNullOrWhiteSpace(cameraName) ? $" \"{cameraName}\"" : "";
                LogEvent("[AUTO-DISCOVERY]", $"📡 Phát hiện luồng camera online: {cameraId.ToUpper()}{displayName} (Cổng: Video UDP {ch.VideoPort}, Audio UDP {ch.AudioPort}, {(isSinglePort ? "1-Port BUNDLE" : "2-Ports SPLIT")}, Codec: {codec.ToUpper()})");

                // Start or restart channel if ports changed or not running
                if (ch.IsRunning)
                {
                    if (prevVideoPort != ch.VideoPort || prevAudioPort != ch.AudioPort)
                    {
                        LogEvent("[AUTO-RECONNECT]", $"🔄 Cổng camera thay đổi -> Khởi động lại Ingest cho {ch.Name} trên cổng UDP {ch.VideoPort}...");
                        await _rtpReceiver.StopChannelAsync(camIndex);
                        ApplyFormInputsToChannel(camIndex);
                        await _rtpReceiver.StartChannelAsync(camIndex);
                    }
                }
                else
                {
                    LogEvent("[AUTO-START]", $"🚀 Tự động bật Multiviewer cho {ch.Name} ({cameraId}{displayName}) trên cổng Video UDP {ch.VideoPort}...");
                    ApplyFormInputsToChannel(camIndex);
                    await _rtpReceiver.StartChannelAsync(camIndex);
                }
            });
        }

        private void OnCameraMetaUpdated(string cameraId, string cameraName)
        {
            if (_isShuttingDown || Dispatcher.HasShutdownStarted) return;

            Dispatcher.InvokeAsync(() =>
            {
                if (_isShuttingDown) return;

                int camIndex = -1;
                var match = System.Text.RegularExpressions.Regex.Match(cameraId, @"\d+");
                if (match.Success && int.TryParse(match.Value, out int num))
                {
                    camIndex = num - 1;
                }

                if (camIndex >= 0 && camIndex < MaxChannels)
                {
                    _cameraDisplayNames[camIndex] = cameraName;
                    UpdateChannelDisplayMeta(camIndex);
                    LogEvent("[METADATA]", $"🏷️ Đã cập nhật tên hiển thị camera {cameraId.ToUpper()}: \"{cameraName}\"");
                }
            });
        }

        #region WebRTC Signaling & ICE NAT Traversal Handlers

        private void OnSignalingConnectionStateChanged(bool isConnected)
        {
            if (_isShuttingDown || Dispatcher.HasShutdownStarted) return;

            Dispatcher.Invoke(() =>
            {
                if (EllipseSignaling != null)
                {
                    EllipseSignaling.Fill = new SolidColorBrush(isConnected 
                        ? Color.FromRgb(22, 163, 74) 
                        : Color.FromRgb(220, 38, 38));
                }

                if (TxtSignalingStatusCard != null)
                {
                    TxtSignalingStatusCard.Text = isConnected 
                        ? $"Signaling: Connected ({_signalingClient.SessionId})" 
                        : "Signaling: Disconnected";
                }

                if (BtnToggleSignaling != null)
                {
                    BtnToggleSignaling.Content = isConnected ? "🔌 Disconnect" : "🔗 Connect Signaling";
                }
            });
        }

        private async void BtnToggleSignaling_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_signalingClient.IsConnected)
                {
                    await _signalingClient.DisconnectAsync();
                }
                else
                {
                    string url = TxtSignalingUrl?.Text?.Trim() ?? "";
                    if (string.IsNullOrEmpty(url)) url = "ws://127.0.0.1:3000/ws";
                    _signalingClient.ServerUrl = url;
                    _ = _signalingClient.ConnectAsync();
                }
            }
            catch (Exception ex)
            {
                LogEvent("[SIGNALING]", $"Lỗi thao tác kết nối Signaling: {ex.Message}");
            }
        }

        private void ChkEnableIce_Changed(object sender, RoutedEventArgs e)
        {
            if (PnlIceConfig != null)
            {
                PnlIceConfig.Visibility = (ChkEnableIce.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        #region ICE / STUN / TURN Server Configuration

        public bool IsIceEnabled => ChkEnableIce?.IsChecked == true;
        public string StunServerUrl => TxtStunServer?.Text?.Trim() ?? string.Empty;
        public string TurnServerUrl => TxtTurnServer?.Text?.Trim() ?? string.Empty;
        public string TurnUsername => TxtTurnUsername?.Text?.Trim() ?? string.Empty;
        public string TurnPassword => TxtTurnPassword?.Password ?? string.Empty;

        public IceServerConfig GetIceServers()
        {
            return new IceServerConfig
            {
                Enabled = ChkEnableIce?.IsChecked == true,
                StunUrl = TxtStunServer?.Text?.Trim() ?? string.Empty,
                TurnUrl = TxtTurnServer?.Text?.Trim() ?? string.Empty,
                TurnUsername = TxtTurnUsername?.Text?.Trim() ?? string.Empty,
                TurnPassword = TxtTurnPassword?.Password ?? string.Empty
            };
        }

        #endregion

        #endregion

        private void OnFrameReady(int channelIndex, byte[] frameBytes, int width, int height)
        {
            if (_isShuttingDown || Dispatcher.HasShutdownStarted) return;
            if (channelIndex < 0 || channelIndex >= MaxChannels) return;

            double rttMs = _rtpReceiver.Channels[channelIndex].CurrentRttMs;
            _playoutAlignmentEngine.EnqueueFrame(channelIndex, frameBytes, width, height, rttMs);
        }

        private void OnPlayoutFrameReady(int channelIndex, byte[] frameBytes, int width, int height)
        {
            if (_isShuttingDown || Dispatcher.HasShutdownStarted) return;
            if (channelIndex < 0 || channelIndex >= MaxChannels) return;

            // Feed frame into ISO Output Worker
            _outputManager.FeedIsoVideo(channelIndex, frameBytes, width, height);

            // If this channel is the active Program, feed Master Output Worker
            if (channelIndex == _currentProgramIndex)
            {
                _outputManager.FeedMasterVideo(frameBytes, width, height);
            }

            // Feed Fullscreen Playout Window if open
            if (_playoutWindow != null)
            {
                if (_playoutWindow.IsMultiviewer)
                {
                    _playoutWindow.UpdateChannelFrame(channelIndex, frameBytes, width, height);
                }
                else if (channelIndex == _currentProgramIndex)
                {
                    _playoutWindow.UpdateFrame(frameBytes, width, height);
                }
            }

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

                var ch = _rtpReceiver.Channels[index];
                if (ch.IsRunning)
                {
                    await _rtpReceiver.StopChannelAsync(index);
                }
                else
                {
                    await _rtpReceiver.StartChannelAsync(index);
                }
            }
        }

        private async void BtnConnectAll_Click(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < _activeChannelCount; i++)
            {
                ApplyFormInputsToChannel(i);
            }
            await _rtpReceiver.StartAllAsync(_activeChannelCount);
        }

        private async void BtnDisconnectAll_Click(object sender, RoutedEventArgs e)
        {
            await _rtpReceiver.StopAllAsync(_activeChannelCount);
        }

        private void ApplyFormInputsToChannel(int index)
        {
            if (index < 0 || index >= MaxChannels) return;
            var ch = _rtpReceiver.Channels[index];

            if (index < _txtIps.Length && _txtIps[index] != null)
            {
                string host = _txtIps[index].Text.Trim();
                ch.SfuUrl = host.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? host : $"http://{host}:4000";
            }

            if (index < _txtPorts.Length && _txtPorts[index] != null && int.TryParse(_txtPorts[index].Text, out int port))
            {
                ch.VideoPort = port;
            }
            else
            {
                ch.VideoPort = 10000 + (index * 4);
            }

            bool isSinglePort = index < _cmbPortModes.Length && _cmbPortModes[index]?.SelectedIndex == 1;
            ch.IsSinglePortMode = isSinglePort;
            ch.AudioPort = isSinglePort ? ch.VideoPort : ch.VideoPort + 2;

            // Update Name and UI elements
            UpdateChannelDisplayMeta(index);
        }

        private void CmbPortModeCam_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;
            if (sender is ComboBox cmb && cmb.Tag is string tagStr && int.TryParse(tagStr, out int idx))
            {
                UpdateChannelDisplayMeta(idx);
                bool isSinglePort = cmb.SelectedIndex == 1;
                var ch = _rtpReceiver.Channels[idx];
                string modeDesc = isSinglePort ? "1 Port duy nhất (Muxed BUNDLE)" : "2 Ports riêng biệt (Split Video/Audio)";
                LogEvent("[PORT-MODE]", $"{_channelNames[idx]}: Đã chọn chế độ {modeDesc} (Video UDP {ch.VideoPort}, Audio UDP {ch.AudioPort})");
            }
        }

        private void BtnAllSinglePort_Click(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < MaxChannels; i++)
            {
                if (i < _cmbPortModes.Length && _cmbPortModes[i] != null)
                {
                    _cmbPortModes[i].SelectedIndex = 1;
                }
            }
            LogEvent("[CONFIG]", "⚡ Đã chuyển toàn bộ 10 camera sang chế độ 1 Port (Muxed BUNDLE)");
        }

        private void BtnAllDualPort_Click(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < MaxChannels; i++)
            {
                if (i < _cmbPortModes.Length && _cmbPortModes[i] != null)
                {
                    _cmbPortModes[i].SelectedIndex = 0;
                }
            }
            LogEvent("[CONFIG]", "⚡ Đã chuyển toàn bộ 10 camera sang chế độ 2 Ports (Split A/V)");
        }

        private void ChkEnableMixMinus_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            bool enabled = ChkEnableMixMinus.IsChecked == true;
            _audioManager.EnableMixMinus = enabled;

            if (PnlMixMinusBadge != null && TxtMixMinusBadge != null)
            {
                PnlMixMinusBadge.Background = enabled ? new SolidColorBrush(Color.FromRgb(6, 78, 59)) : new SolidColorBrush(Color.FromRgb(40, 40, 48));
                PnlMixMinusBadge.BorderBrush = enabled ? new SolidColorBrush(Color.FromRgb(5, 150, 105)) : new SolidColorBrush(Color.FromRgb(70, 70, 80));
                TxtMixMinusBadge.Text = enabled ? "WEBRTC 2.0 MIX-MINUS ACTIVE (N-1)" : "MIX-MINUS DISABLED";
                TxtMixMinusBadge.Foreground = enabled ? new SolidColorBrush(Color.FromRgb(52, 211, 153)) : new SolidColorBrush(Color.FromRgb(150, 150, 160));
            }

            LogEvent("[AEC-DSP]", enabled 
                ? "Đã KÍCH HOẠT WebRTC 2.0 Mix-Minus Clean Feed: Loại bỏ hoàn toàn tiếng vọng khi phát trả về hiện trường." 
                : "Đã TẮT Mix-Minus: Âm thanh trả về bao gồm tất cả kênh.");
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

        private void BtnPgmSelect_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tagStr && int.TryParse(tagStr, out int index))
            {
                e.Handled = true;
                SelectPreviewChannel(index);
            }
        }

        private void SelectProgramChannel(int index)
        {
            if (index < 0 || index >= _activeChannelCount) return;
            int prevProgram = _currentProgramIndex;
            _currentProgramIndex = index;
            if (_currentPreviewIndex == index && _activeChannelCount > 1)
            {
                // Nếu kênh được chọn làm PGM đang là PVW, chuyển PVW về kênh PGM trước đó
                _currentPreviewIndex = prevProgram;
            }
            _audioManager.CurrentProgramIndex = index;
            UpdateTallyIndicators();
            LogEvent("[SWITCHER]", $"Đã chọn {_channelNames[index]} làm tín hiệu PROGRAM (On-Air).");

            if (TxtIntercomTargetBadge != null && _intercomTarget == "all")
            {
                TxtIntercomTargetBadge.Text = "TARGET: ALL CAMERAS (PARTYLINE)";
            }

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

        private void SelectPreviewChannel(int index)
        {
            if (index < 0 || index >= _activeChannelCount) return;
            if (index == _currentProgramIndex)
            {
                LogEvent("[SWITCHER]", $"{_channelNames[index]} đang phát PROGRAM (On-Air), không thể chọn làm PREVIEW đồng thời.");
                return;
            }
            _currentPreviewIndex = index;
            UpdateTallyIndicators();
            LogEvent("[SWITCHER]", $"Đã chọn {_channelNames[index]} làm tín hiệu PREVIEW (Standby - Chuẩn bị chuyển cảnh).");

            _playoutWindow?.SetProgramChannel(_currentProgramIndex, _currentPreviewIndex);
            _outputManager?.Compositor?.UpdateConfig(_activeChannelCount, _channelNames, _currentProgramIndex, _currentPreviewIndex);

            // Broadcast Tally to Encoders via WebRTC Signaling Server
            for (int ch = 0; ch < MaxChannels; ch++)
            {
                string camId = $"cam-{ch + 1:D2}";
                string tallyState = (ch == _currentProgramIndex) ? "on-air" : (ch == _currentPreviewIndex ? "preview" : "off");
                _ = _signalingClient.SendTallyUpdateAsync(camId, tallyState);
            }
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
                    _pgmButtons[i].BorderBrush = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
                    _pgmButtons[i].BorderThickness = new Thickness(1.5);
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
                    _pgmButtons[i].Background = new SolidColorBrush(Color.FromRgb(0x15, 0x80, 0x3D));
                    _pgmButtons[i].BorderBrush = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
                    _pgmButtons[i].BorderThickness = new Thickness(1.5);
                    _pgmButtons[i].Foreground = Brushes.White;
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
                    _pgmButtons[i].BorderBrush = new SolidColorBrush(Color.FromRgb(0x3F, 0x3F, 0x46));
                    _pgmButtons[i].BorderThickness = new Thickness(1);
                    _pgmButtons[i].Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
                }

                // Sync to Audio Mixer Console Strip
                if (i < _mixerTallyBadges.Length && _mixerTallyBadges[i] != null)
                {
                    if (i == _currentProgramIndex)
                    {
                        _mixerTallyBadges[i].Background = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
                        _mixerTallyTexts[i].Text = "PGM";
                        _mixerTallyTexts[i].Foreground = Brushes.White;
                        if (i < _mixerStrips.Length && _mixerStrips[i] != null)
                            _mixerStrips[i].BorderBrush = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
                    }
                    else if (!_audioManager.IsChannelMuted(i))
                    {
                        _mixerTallyBadges[i].Background = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));
                        _mixerTallyTexts[i].Text = "ON-AIR";
                        _mixerTallyTexts[i].Foreground = Brushes.White;
                        if (i < _mixerStrips.Length && _mixerStrips[i] != null)
                            _mixerStrips[i].BorderBrush = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));
                    }
                    else
                    {
                        _mixerTallyBadges[i].Background = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x38));
                        _mixerTallyTexts[i].Text = _channelNames[i];
                        _mixerTallyTexts[i].Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
                        if (i < _mixerStrips.Length && _mixerStrips[i] != null)
                            _mixerStrips[i].BorderBrush = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x36));
                    }
                }
            }

            _playoutWindow?.SetProgramChannel(_currentProgramIndex, _currentPreviewIndex);
                        _outputManager?.Compositor?.UpdateConfig(_activeChannelCount, _channelNames, _currentProgramIndex, _currentPreviewIndex);

            // Broadcast Tally to Encoders via WebRTC Signaling Server
            for (int ch = 0; ch < MaxChannels; ch++)
            {
                string camId = $"cam-{ch + 1:D2}";
                string tallyState = (ch == _currentProgramIndex) ? "on-air" : (ch == _currentPreviewIndex ? "preview" : "off");
                _ = _signalingClient.SendTallyUpdateAsync(camId, tallyState);
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
            if (_isShuttingDown || Dispatcher.HasShutdownStarted) return;

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

                        // Sync to Studio Audio Mixer Strips
                        if (i < _mixerVuBarsL.Length && _mixerVuBarsL[i] != null)
                        {
                            _mixerVuBarsL[i].Value = leftDb;
                            _mixerVuBarsR[i].Value = rightDb;
                            _mixerVuBarsL[i].Foreground = GetVuMeterColorBrush(leftDb, clipL);
                            _mixerVuBarsR[i].Foreground = GetVuMeterColorBrush(rightDb, clipR);
                            if (_mixerClipLedsL[i] != null) _mixerClipLedsL[i].Fill = clipL ? _brushLedClip : _brushLedOff;
                            if (_mixerClipLedsR[i] != null) _mixerClipLedsR[i].Fill = clipR ? _brushLedClip : _brushLedOff;
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

                        if (i < _mixerVuBarsL.Length && _mixerVuBarsL[i] != null)
                        {
                            _mixerVuBarsL[i].Value = -60;
                            _mixerVuBarsR[i].Value = -60;
                            if (_mixerClipLedsL[i] != null) _mixerClipLedsL[i].Fill = _brushLedOff;
                            if (_mixerClipLedsR[i] != null) _mixerClipLedsR[i].Fill = _brushLedOff;
                        }
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

                double maxPk = AudioMeterService.MIN_DBFS;
                for (int i = 0; i < count; i++)
                {
                    if (pgm.PeakDb[i] > maxPk) maxPk = pgm.PeakDb[i];
                }

                if (TxtMasterPeakDb != null)
                {
                    TxtMasterPeakDb.Text = (maxPk > -55.0) ? $"{maxPk:F1} dB" : "-∞ dB";
                    TxtMasterPeakDb.Foreground = pgm.IsClipping ? Brushes.Red : new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
                }

                // Update Studio Audio Mixer Console Master Strip
                if (VuMixerMasterL != null)
                {
                    VuMixerMasterL.Value = Math.Clamp(pgm.LeftDb, -60.0, 0.0);
                    VuMixerMasterL.Foreground = GetVuMeterColorBrush(pgm.LeftDb, pgm.IsChannelClipping[0]);
                }
                if (VuMixerMasterR != null)
                {
                    VuMixerMasterR.Value = Math.Clamp(pgm.RightDb, -60.0, 0.0);
                    VuMixerMasterR.Foreground = GetVuMeterColorBrush(pgm.RightDb, pgm.IsChannelClipping[1]);
                }
                if (TxtMixerMasterPeak != null)
                {
                    TxtMixerMasterPeak.Text = (maxPk > -55.0) ? $"{maxPk:F1} dB" : "-∞ dB";
                    TxtMixerMasterPeak.Foreground = pgm.IsClipping ? Brushes.Red : new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
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

        private void OnStudioMicLevelsUpdated(ChannelAudioLevels mic)
        {
            if (_isShuttingDown || Dispatcher.HasShutdownStarted) return;
            if (!_isStudioMicEnabled) return;

            Dispatcher.InvokeAsync(() =>
            {
                if (VuStudioMicL != null)
                {
                    VuStudioMicL.Value = Math.Clamp(mic.LeftDb, -60.0, 0.0);
                    VuStudioMicL.Foreground = GetVuMeterColorBrush(mic.LeftDb, mic.IsChannelClipping[0]);
                }
                if (VuStudioMicR != null)
                {
                    VuStudioMicR.Value = Math.Clamp(mic.RightDb, -60.0, 0.0);
                    VuStudioMicR.Foreground = GetVuMeterColorBrush(mic.RightDb, mic.IsChannelClipping[1]);
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
            if (_btnSoloCams != null && _btnSoloCams.Length > 0)
            {
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

            if (_mixerSoloButtons != null && _mixerSoloButtons.Length > 0)
            {
                for (int i = 0; i < _mixerSoloButtons.Length; i++)
                {
                    if (_mixerSoloButtons[i] == null) continue;
                    int camNum = i + 1;
                    bool isThisSolo = (_audioManager.SoloSource == (SoloAudioSource)camNum);

                    if (isThisSolo)
                    {
                        _mixerSoloButtons[i].Background = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
                        _mixerSoloButtons[i].Foreground = Brushes.Black;
                    }
                    else
                    {
                        _mixerSoloButtons[i].Background = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x34));
                        _mixerSoloButtons[i].Foreground = Brushes.White;
                    }
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
            string text = _audioManager.IsMuteAll ? "UNMUTE" : "MUTE ALL";
            Brush bg = _audioManager.IsMuteAll ? Brushes.Red : new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x38));

            if (BtnMixerMuteAll != null)
            {
                BtnMixerMuteAll.Content = text;
                BtnMixerMuteAll.Background = bg;
            }
        }

        private void BtnMutePreview_Click(object sender, RoutedEventArgs e)
        {
            _audioManager.IsPreviewMuted = !_audioManager.IsPreviewMuted;
            UpdatePreviewMuteUI();
        }

        private void UpdatePreviewMuteUI()
        {
            bool isMuted = _audioManager.IsPreviewMuted;
            if (BtnMutePreview != null)
            {
                BtnMutePreview.Content = isMuted ? "🔇 PREVIEW MUTED" : "🔊 PREVIEW";
                BtnMutePreview.Background = isMuted ? new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)) : new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x30));
                BtnMutePreview.Foreground = isMuted ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0xCC));
                BtnMutePreview.BorderBrush = isMuted ? new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)) : new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x48));
                BtnMutePreview.ToolTip = isMuted 
                    ? "Đang MUTE loa kiểm âm tại chỗ (âm thanh trong luồng Stream/NDI vẫn phát 100%)" 
                    : "Mute âm thanh loa kiểm âm tại chỗ (âm thanh trong luồng Stream/NDI vẫn phát bình thường)";
            }
        }

        private void SliderLipSync_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtLipSyncVal == null || _audioManager == null || _playoutAlignmentEngine == null) return;
            int delayMs = (int)Math.Round(e.NewValue / 5.0) * 5; // Snap to 5ms steps

            if (delayMs > 0)
            {
                TxtLipSyncVal.Text = $"+{delayMs} ms";
                TxtLipSyncVal.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0xCC)); // Cyan = Âm thanh trễ lại
                _audioManager.LipSyncDelayMs = delayMs;
                _playoutAlignmentEngine.VideoLipSyncDelayMs = 0;
            }
            else if (delayMs < 0)
            {
                TxtLipSyncVal.Text = $"{delayMs} ms";
                TxtLipSyncVal.Foreground = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24)); // Vàng cam = Hình ảnh trễ lại
                _audioManager.LipSyncDelayMs = 0;
                _playoutAlignmentEngine.VideoLipSyncDelayMs = -delayMs;
            }
            else
            {
                TxtLipSyncVal.Text = "0 ms";
                TxtLipSyncVal.Foreground = Brushes.White;
                _audioManager.LipSyncDelayMs = 0;
                _playoutAlignmentEngine.VideoLipSyncDelayMs = 0;
            }
        }

        private void SliderLipSync_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (SliderLipSync != null)
            {
                SliderLipSync.Value = 0;
            }
        }

        private void BtnResetLipSync_Click(object sender, RoutedEventArgs e)
        {
            if (SliderLipSync != null)
            {
                SliderLipSync.Value = 0;
            }
        }

        private void ChkMuteCam_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.Tag is string tagStr && int.TryParse(tagStr, out int camIdx))
            {
                bool isMuted = cb.IsChecked == true;
                _audioManager.SetChannelMuted(camIdx, isMuted, _channelNames[camIdx]);
                UpdateMixerMuteUI(camIdx, isMuted);
                UpdateTallyIndicators();
            }
        }

        private void UpdateMixerMuteUI(int camIdx, bool isMuted)
        {
            if (camIdx >= 0 && camIdx < _mixerMuteButtons.Length && _mixerMuteButtons[camIdx] != null)
            {
                _mixerMuteButtons[camIdx].Background = isMuted 
                    ? new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)) 
                    : new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x34));
                _mixerMuteButtons[camIdx].Foreground = Brushes.White;
            }
        }

        private void BtnMixerMute_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tagStr && int.TryParse(tagStr, out int camIdx))
            {
                bool newMute = !_audioManager.IsChannelMuted(camIdx);
                _audioManager.SetChannelMuted(camIdx, newMute, _channelNames[camIdx]);
                if (camIdx < _chkMuteCams.Length && _chkMuteCams[camIdx] != null)
                {
                    _chkMuteCams[camIdx].IsChecked = newMute;
                }
                UpdateMixerMuteUI(camIdx, newMute);
                UpdateTallyIndicators();
            }
        }

        private void BtnMixerSolo_Click(object sender, RoutedEventArgs e)
        {
            BtnSoloCam_Click(sender, e);
        }

        private void MixerFader_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized) return;
            if (sender is Slider slider && slider.Tag is string tagStr && int.TryParse(tagStr, out int camIdx))
            {
                double val = Math.Round(slider.Value, 1);
                _audioManager.SetChannelGain(camIdx, val);
                if (camIdx < _mixerGainTexts.Length && _mixerGainTexts[camIdx] != null)
                {
                    _mixerGainTexts[camIdx].Text = (val <= -58.0) ? "-∞ dB" : $"{val:+#0.0;-#0.0;0.0} dB";
                    _mixerGainTexts[camIdx].Foreground = (val > 0.0)
                        ? new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B))
                        : new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0xCC));
                }
            }
        }

        private void MixerFader_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is Slider slider)
            {
                slider.Value = 0.0; // Reset to 0.0 dB
            }
        }

        private void MixerPan_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized) return;
            if (sender is Slider slider && slider.Tag is string tagStr && int.TryParse(tagStr, out int camIdx))
            {
                double val = Math.Round(slider.Value, 2);
                _audioManager.SetChannelPan(camIdx, val);
                if (camIdx < _mixerPanTexts.Length && _mixerPanTexts[camIdx] != null)
                {
                    if (Math.Abs(val) < 0.05) _mixerPanTexts[camIdx].Text = "C";
                    else if (val < 0) _mixerPanTexts[camIdx].Text = $"L{Math.Abs(val * 100):0}";
                    else _mixerPanTexts[camIdx].Text = $"R{val * 100:0}";
                }
            }
        }

        private void MixerPan_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is Slider slider)
            {
                slider.Value = 0.0; // Reset to Center
            }
        }

        private void MixerMasterFader_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized) return;
            if (sender is Slider slider)
            {
                _audioManager.MonitorVolumePercent = slider.Value;
                if (TxtGainDbMaster != null)
                {
                    TxtGainDbMaster.Text = $"{slider.Value:0} %";
                }
            }
        }

        private void MixerMasterFader_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is Slider slider)
            {
                slider.Value = 80.0; // Reset to 80% default
            }
        }

        private void BtnMixerResetAll_Click(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < MaxChannels; i++)
            {
                if (i < _mixerFaders.Length && _mixerFaders[i] != null)
                {
                    _mixerFaders[i].Value = 0.0;
                }
                if (i < _mixerPans.Length && _mixerPans[i] != null)
                {
                    _mixerPans[i].Value = 0.0;
                }
            }
            if (FaderMaster != null) FaderMaster.Value = 80.0;
            LogEvent("[MIXER]", "Đã khôi phục toàn bộ Fader về 0.0 dB (Unity Gain) và Pan về Center.");
        }

        private void BtnMixerMuteInactive_Click(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < MaxChannels; i++)
            {
                var ch = _rtpReceiver.Channels[i];
                if (!ch.IsRunning)
                {
                    _audioManager.SetChannelMuted(i, true, _channelNames[i]);
                    if (i < _chkMuteCams.Length && _chkMuteCams[i] != null)
                    {
                        _chkMuteCams[i].IsChecked = true;
                    }
                    UpdateMixerMuteUI(i, true);
                }
            }
            UpdateTallyIndicators();
            LogEvent("[MIXER]", "Đã Mute các kênh SRT không chạy.");
        }

        #region Studio Mic / Director Talkback Controls

        private void ScanStudioMicDevices()
        {
            try
            {
                CmbStudioMicDevice.Items.Clear();
                var devices = AudioInputDevice.GetInputDevices();
                if (devices.Count > 0)
                {
                    foreach (var dev in devices)
                    {
                        CmbStudioMicDevice.Items.Add($"[{dev.Id}] {dev.Name}");
                    }
                    CmbStudioMicDevice.SelectedIndex = 0;
                }
                else
                {
                    CmbStudioMicDevice.Items.Add("Không tìm thấy Micro Studio");
                    CmbStudioMicDevice.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                LogEvent("[WARN]", $"Lỗi quét thiết bị Micro Studio: {ex.Message}");
            }
        }

        private void BtnRefreshStudioMic_Click(object sender, RoutedEventArgs e)
        {
            ScanStudioMicDevices();
            LogEvent("[TALKBACK]", "Đã quét lại danh sách thiết bị Micro Studio.");
        }

        private void CmbStudioMicDevice_DropDownOpened(object sender, EventArgs e)
        {
            // Không tự động quét khi mở danh sách, chỉ quét khi bấm nút Refresh
        }

        private void ScanStudioSpeakerDevices(bool preserveSelection = true)
        {
            try
            {
                int currentSelectedId = -2;
                if (preserveSelection && CmbStudioSpeakerDevice.SelectedItem is string curItem && curItem.StartsWith("["))
                {
                    int closeIdx = curItem.IndexOf(']');
                    if (closeIdx > 1 && int.TryParse(curItem.Substring(1, closeIdx - 1), out int parsedId))
                    {
                        currentSelectedId = parsedId;
                    }
                }

                CmbStudioSpeakerDevice.Items.Clear();
                CmbStudioSpeakerDevice.Items.Add("[-1] Mặc định Hệ thống (Default Audio)");
                var devices = AudioOutputDevice.GetOutputDevices();
                int selectIdx = 0;
                for (int i = 0; i < devices.Count; i++)
                {
                    var dev = devices[i];
                    CmbStudioSpeakerDevice.Items.Add($"[{dev.Id}] {dev.Name}");
                    if (dev.Id == currentSelectedId)
                    {
                        selectIdx = i + 1;
                    }
                }
                CmbStudioSpeakerDevice.SelectedIndex = selectIdx;
            }
            catch (Exception ex)
            {
                LogEvent("[WARN]", $"Lỗi quét thiết bị Loa/Tai nghe: {ex.Message}");
            }
        }

        private void BtnRefreshStudioSpeaker_Click(object sender, RoutedEventArgs e)
        {
            ScanStudioSpeakerDevices(preserveSelection: false);
            LogEvent("[AUDIO]", "🔄 Đã quét lại danh sách thiết bị Loa / Tai nghe phát.");
        }

        private void CmbStudioSpeakerDevice_DropDownOpened(object sender, EventArgs e)
        {
            // Không tự động quét khi mở danh sách, chỉ quét khi bấm nút Refresh
        }

        private void CmbStudioSpeakerDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized || CmbStudioSpeakerDevice.SelectedItem == null) return;
            try
            {
                string itemText = CmbStudioSpeakerDevice.SelectedItem.ToString() ?? "";
                if (itemText.StartsWith("["))
                {
                    int closeIdx = itemText.IndexOf(']');
                    if (closeIdx > 1 && int.TryParse(itemText.Substring(1, closeIdx - 1), out int devId))
                    {
                        bool ok = _audioManager.SetOutputDevice(devId);
                        LogEvent("[AUDIO]", ok 
                            ? $"🔊 Đã chuyển lối phát âm thanh: {itemText}" 
                            : $"⚠️ Không thể chuyển lối phát âm thanh {itemText}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogEvent("[WARN]", $"Lỗi áp dụng thiết bị phát âm thanh: {ex.Message}");
            }
        }

        private void ChkEnableStudioMic_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            _isStudioMicEnabled = ChkEnableStudioMic.IsChecked == true;
            _audioManager.EnableStudioMic = _isStudioMicEnabled;

            CmbStudioMicDevice.IsEnabled = _isStudioMicEnabled;
            SldStudioMicGain.IsEnabled = _isStudioMicEnabled;
            ChkStudioMicToProgram.IsEnabled = _isStudioMicEnabled;

            if (PnlStudioMicBadge != null && TxtStudioMicBadge != null)
            {
                PnlStudioMicBadge.Background = _isStudioMicEnabled ? new SolidColorBrush(Color.FromRgb(6, 78, 59)) : new SolidColorBrush(Color.FromRgb(38, 38, 38));
                PnlStudioMicBadge.BorderBrush = _isStudioMicEnabled ? new SolidColorBrush(Color.FromRgb(5, 150, 105)) : new SolidColorBrush(Color.FromRgb(82, 82, 82));
                TxtStudioMicBadge.Text = _isStudioMicEnabled ? "ARMED" : "DISABLED";
                TxtStudioMicBadge.Foreground = _isStudioMicEnabled ? new SolidColorBrush(Color.FromRgb(52, 211, 153)) : new SolidColorBrush(Color.FromRgb(158, 158, 158));
            }

            // Sync Channel Strip (MIC DIR) Enabled / Visual state
            if (StripMixerDirector != null)
            {
                StripMixerDirector.IsEnabled = _isStudioMicEnabled;
                StripMixerDirector.Opacity = _isStudioMicEnabled ? 1.0 : 0.38;
                StripMixerDirector.BorderBrush = _isStudioMicEnabled 
                    ? new SolidColorBrush(Color.FromRgb(2, 132, 199)) 
                    : new SolidColorBrush(Color.FromRgb(44, 44, 54));
            }
            if (TallyMixerDirector != null)
            {
                TallyMixerDirector.Background = _isStudioMicEnabled 
                    ? new SolidColorBrush(Color.FromRgb(2, 132, 199)) 
                    : new SolidColorBrush(Color.FromRgb(51, 51, 56));
            }
            if (TxtTallyMixerDirector != null)
            {
                TxtTallyMixerDirector.Foreground = _isStudioMicEnabled 
                    ? Brushes.White 
                    : new SolidColorBrush(Color.FromRgb(136, 136, 136));
            }
            if (TxtMixerNameDirector != null)
            {
                TxtMixerNameDirector.Foreground = _isStudioMicEnabled 
                    ? new SolidColorBrush(Color.FromRgb(56, 189, 248)) 
                    : new SolidColorBrush(Color.FromRgb(119, 119, 119));
            }
            if (BtnTalkback != null)
            {
                BtnTalkback.IsEnabled = _isStudioMicEnabled;
            }

            if (_isStudioMicEnabled)
            {
                int devId = CmbStudioMicDevice.SelectedIndex >= 0 ? CmbStudioMicDevice.SelectedIndex : 0;
                bool ok = _studioMicDevice.Start(devId);
                LogEvent("[TALKBACK]", ok 
                    ? $"🎙️ Đã SẴN SÀNG Micro Đạo diễn (Thiết bị ID: {devId}). Nhấn nút TALKBACK để nói chuyện với hiện trường." 
                    : $"⚠️ Không thể mở thiết bị Micro ID {devId}.");
            }
            else
            {
                _studioMicDevice.Stop();
                _audioManager.IsTalkbackActive = false;
                _isTalkbackPressed = false;
                StopIntercomTalkback();
                UpdateMasterTalkUI(false);
                if (VuStudioMicL != null) VuStudioMicL.Value = -60;
                if (VuStudioMicR != null) VuStudioMicR.Value = -60;
                LogEvent("[TALKBACK]", "🔇 Đã TẮT Micro Đạo diễn Studio.");
            }
        }

        private void CmbStudioMicDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized || !_isStudioMicEnabled) return;
            int devId = CmbStudioMicDevice.SelectedIndex >= 0 ? CmbStudioMicDevice.SelectedIndex : 0;
            _studioMicDevice.Stop();
            _studioMicDevice.Start(devId);
            LogEvent("[TALKBACK]", $"Micro Studio chuyển sang thiết bị ID {devId}.");
        }

        private void SldStudioMicGain_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized) return;
            _audioManager.StudioMicGainDb = e.NewValue;
            if (TxtStudioMicGainVal != null)
            {
                TxtStudioMicGainVal.Text = (e.NewValue <= -58.0) ? "-∞ dB" : $"{e.NewValue:+#0.0;-#0.0;0.0} dB";
            }
        }

        private void ChkStudioMicToProgram_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            bool toPgm = ChkStudioMicToProgram.IsChecked == true;
            _audioManager.StudioMicRouteToProgram = toPgm;
            LogEvent("[TALKBACK]", toPgm 
                ? "⚠️ Đã chèn tiếng Micro Đạo diễn vào sóng Program (PGM Broadcast Master)!" 
                : "Đã tách Micro Đạo diễn ra khỏi PGM (chỉ đi đường Talkback riêng).");
        }

        // ─── Intercom & Talkback Controls ──────────────────────────

        private void OnIntercomAudioReceived(string from, byte[] audioData)
        {
            if (_isShuttingDown || audioData == null || audioData.Length == 0) return;

            // Tự động nhận diện Opus frame (< 1000 bytes) hoặc legacy uncompressed PCM
            byte[] pcmToFeed;
            if (audioData.Length < 1000)
            {
                pcmToFeed = _intercomCodec.DecodeToStereoPcm(audioData, 0, audioData.Length);
            }
            else
            {
                pcmToFeed = audioData;
            }

            if (pcmToFeed.Length > 0)
            {
                // Feed to AudioMonitoringManager for studio speaker/headphone monitoring
                _audioManager.FeedIntercomIncomingPcm(pcmToFeed, pcmToFeed.Length);
            }

            // Update UI with debounce watchdog to prevent badge flickering
            Dispatcher.InvokeAsync(() =>
            {
                if (PnlIntercomIncoming != null && TxtIntercomIncomingSpeaker != null)
                {
                    PnlIntercomIncoming.Visibility = Visibility.Visible;
                    TxtIntercomIncomingSpeaker.Text = $"{from.ToUpper()} IS TALKING...";
                }
                _intercomIncomingWatchdogTimer?.Stop();
                _intercomIncomingWatchdogTimer?.Start();
            });
        }

        private long _intercomMouseDownTick = 0;
        private bool _wasIntercomActiveOnMouseDown = false;

        private void BtnTalkback_Click(object sender, RoutedEventArgs e)
        {
            SetIntercomTalkActive(!_isTalkbackPressed);
        }

        private void BtnIntercomMasterTalk_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isStudioMicEnabled)
            {
                LogEvent("[WARN]", "Vui lòng tích chọn 'Bật Micro Đạo Diễn' trước khi kích hoạt Talkback/Intercom.");
                return;
            }
            _intercomMouseDownTick = Environment.TickCount64;
            _wasIntercomActiveOnMouseDown = _isTalkbackPressed;
            SetIntercomTalkActive(true);
            e.Handled = true;
        }

        private void BtnIntercomMasterTalk_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (Mouse.Captured != null)
            {
                Mouse.Capture(null);
            }

            if (!_isStudioMicEnabled) return;

            long elapsed = Environment.TickCount64 - _intercomMouseDownTick;
            if (!_isIntercomLatchMode || elapsed > 350)
            {
                // Chế độ Momentary PTT hoặc người dùng đè giữ lâu (>350ms): Nhả chuột là ngắt nói
                SetIntercomTalkActive(false);
            }
            else
            {
                // Chế độ Latch (Toggle) hoặc người dùng click nhả nhanh (<350ms): Chế độ Click để Hold (Giữ bật nói liên tục)
                if (_wasIntercomActiveOnMouseDown)
                {
                    SetIntercomTalkActive(false);
                }
                else
                {
                    SetIntercomTalkActive(true);
                }
            }
            e.Handled = true;
        }

        private void BtnIntercomMasterTalk_MouseLeave(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _isTalkbackPressed && (!_isIntercomLatchMode || (Environment.TickCount64 - _intercomMouseDownTick > 350)))
            {
                SetIntercomTalkActive(false);
            }
            if (Mouse.Captured != null)
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
                    SetIntercomTalkActive(true);
                }
                e.Handled = true; // Luôn chặn lặp phím Space để WPF không kích hoạt hover/click kẹt chuột
            }
        }

        private void MainWindow_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space && !(FocusManager.GetFocusedElement(this) is TextBox))
            {
                SetIntercomTalkActive(false);
                e.Handled = true;
                if (Mouse.Captured != null)
                {
                    Mouse.Capture(null);
                }
            }
        }

        private void SetIntercomTalkActive(bool active)
        {
            if (!active && Mouse.Captured != null)
            {
                Mouse.Capture(null);
            }

            if (active && !_isStudioMicEnabled)
            {
                LogEvent("[WARN]", "Vui lòng tích chọn 'Bật Micro Đạo Diễn' trước khi kích hoạt Talkback/Intercom.");
                return;
            }

            if (_isTalkbackPressed == active) return;
            _isTalkbackPressed = active;
            _audioManager.IsTalkbackActive = active;

            UpdateMasterTalkUI(active);

            // Phát tín hiệu Toggle Enable / Disable rõ ràng tới Encoder
            if (_intercomTarget == "all")
            {
                // Khi chọn ALL: Gửi broadcast tới toàn bộ các camera (Partyline)
                _ = _signalingClient.SendIntercomStateAsync("all", active, "Director");
            }
            else
            {
                // Khi chọn C1, C2...: CHỈ gửi tới riêng luồng đã chọn (ISO Direct)
                _ = _signalingClient.SendIntercomStateAsync(_intercomTarget, active, "Director");
            }

            if (active)
            {
                StartIntercomTalkback();
                string targetDesc = _intercomTarget == "all"
                    ? "TOÀN BỘ CAMERA (PARTYLINE)"
                    : _intercomTarget.ToUpper();
                LogEvent("[INTERCOM]", $"📢 [ON-AIR TALK] Đạo diễn đang đàm thoại tới {targetDesc}!");
            }
            else
            {
                StopIntercomTalkback();
                LogEvent("[INTERCOM]", "🔇 [OFF-AIR TALK] Đã ngắt đường đàm thoại đạo diễn.");
            }
        }

        private void StartIntercomTalkback()
        {
            if (_intercomTalkbackCts != null) return;
            _intercomTalkbackCts = new CancellationTokenSource();
            var token = _intercomTalkbackCts.Token;

            Task.Run(async () =>
            {
                var sw = Stopwatch.StartNew();
                long packetCount = 0;
                const double msPerFrame = 20.0;

                while (!token.IsCancellationRequested && _isTalkbackPressed)
                {
                    try
                    {
                        string currentTarget = _intercomTarget;
                        if (currentTarget == "all")
                        {
                            // Khi chọn ALL: Truyền broadcast tới toàn bộ các luồng qua kênh "all"
                            byte[] pcm = _audioManager.GenerateMixMinusPcm(-1, 960);
                            if (pcm != null && pcm.Length > 0 && _signalingClient.IsConnected)
                            {
                                byte[] opusPacket = _intercomCodec.EncodePcm(pcm, 0, pcm.Length);
                                if (opusPacket.Length > 0)
                                {
                                    await _signalingClient.SendIntercomAudioAsync("all", opusPacket, "opus", token).ConfigureAwait(false);
                                }
                            }
                        }
                        else
                        {
                            // Khi chọn luồng đến qua button C1, C2, C3...: CHỈ truyền đến riêng luồng đó (ISO Direct)
                            int targetIndex = GetCameraIndexFromTarget(currentTarget);
                            byte[] pcm = _audioManager.GenerateMixMinusPcm(targetIndex, 960);
                            if (pcm != null && pcm.Length > 0 && _signalingClient.IsConnected)
                            {
                                byte[] opusPacket = _intercomCodec.EncodePcm(pcm, 0, pcm.Length);
                                if (opusPacket.Length > 0)
                                {
                                    await _signalingClient.SendIntercomAudioAsync(currentTarget, opusPacket, "opus", token).ConfigureAwait(false);
                                }
                            }
                        }

                        packetCount++;
                        double targetMs = packetCount * msPerFrame;
                        double elapsedMs = sw.Elapsed.TotalMilliseconds;
                        int sleepMs = (int)(targetMs - elapsedMs);
                        if (sleepMs > 0)
                        {
                            await Task.Delay(sleepMs, token).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Intercom] Talkback transmit error: {ex.Message}");
                        break;
                    }
                }
            }, token);
        }

        private void StopIntercomTalkback()
        {
            _intercomTalkbackCts?.Cancel();
            _intercomTalkbackCts?.Dispose();
            _intercomTalkbackCts = null;
        }

        private int GetCameraIndexFromTarget(string target)
        {
            if (string.IsNullOrEmpty(target) || target == "all") return -1;
            if (target.StartsWith("cam-", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(target.Substring(4), out int camNum) && camNum >= 1 && camNum <= MaxChannels)
            {
                return camNum - 1;
            }
            return -1;
        }

        private void UpdateMasterTalkUI(bool active)
        {
            if (BtnTalkback != null)
            {
                BtnTalkback.Background = active ? new SolidColorBrush(Color.FromRgb(220, 38, 38)) : new SolidColorBrush(Color.FromRgb(31, 41, 55));
                BtnTalkback.Foreground = active ? Brushes.White : new SolidColorBrush(Color.FromRgb(107, 114, 128));
                BtnTalkback.BorderBrush = active ? new SolidColorBrush(Color.FromRgb(248, 113, 113)) : new SolidColorBrush(Color.FromRgb(55, 65, 81));
                BtnTalkback.Content = active ? "LIVE" : "TALK";
            }

            if (BtnIntercomMasterTalk != null && TxtIntercomMasterTalkLabel != null)
            {
                if (active)
                {
                    BtnIntercomMasterTalk.Background = new SolidColorBrush(Color.FromRgb(220, 38, 38)); // Red
                    BtnIntercomMasterTalk.BorderBrush = new SolidColorBrush(Color.FromRgb(248, 113, 113));
                    TxtIntercomMasterTalkLabel.Text = "🔴 ON-AIR (TALKING)";
                    TxtIntercomMasterTalkLabel.Foreground = Brushes.White;
                }
                else
                {
                    BtnIntercomMasterTalk.Background = new SolidColorBrush(Color.FromRgb(31, 41, 55));
                    BtnIntercomMasterTalk.BorderBrush = new SolidColorBrush(Color.FromRgb(55, 65, 81));
                    TxtIntercomMasterTalkLabel.Text = "🎙️ TALK TO AIR";
                    TxtIntercomMasterTalkLabel.Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175));
                }
            }
        }

        private void BtnIntercomTarget_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            string target = btn.Tag?.ToString() ?? "all";
            string oldTarget = _intercomTarget;
            _intercomTarget = target;

            // Nếu đang đàm thoại (ON-AIR TALK) mà chuyển đổi đích (Target C1/C2/All)
            if (_isTalkbackPressed && oldTarget != target)
            {
                // Tắt trạng thái ở luồng cũ
                _ = _signalingClient.SendIntercomStateAsync(oldTarget, false, "Director");

                // Bật trạng thái ở luồng mới
                _ = _signalingClient.SendIntercomStateAsync(target, true, "Director");
            }

            foreach (var b in _intercomTargetButtons)
            {
                if (b == null) continue;
                bool isSelected = (b.Tag?.ToString() == target);
                if (isSelected)
                {
                    b.Background = new SolidColorBrush(Color.FromRgb(30, 58, 95));
                    b.Foreground = new SolidColorBrush(Color.FromRgb(96, 165, 250));
                    b.BorderBrush = new SolidColorBrush(Color.FromRgb(59, 130, 246));
                    b.BorderThickness = new Thickness(2);
                }
                else
                {
                    b.Background = new SolidColorBrush(Color.FromRgb(31, 41, 55));
                    b.Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175));
                    b.BorderBrush = new SolidColorBrush(Color.FromRgb(55, 65, 81));
                    b.BorderThickness = new Thickness(1.5);
                }
            }

            if (TxtIntercomTargetBadge != null)
            {
                TxtIntercomTargetBadge.Text = target == "all"
                    ? "TARGET: ALL CAMERAS (PARTYLINE)"
                    : $"TARGET: {target.ToUpper()} (ISO DIRECT)";
            }
        }

        private void SldIntercomVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized) return;
            double vol = e.NewValue / 100.0;
            _audioManager.IntercomReceiveVolume = vol;
            if (TxtIntercomVolumeVal != null)
            {
                TxtIntercomVolumeVal.Text = $"{(int)e.NewValue}%";
            }
        }

        private void RbIntercomMode_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            _isIntercomLatchMode = (RbIntercomLatch?.IsChecked == true);
            if (TxtIntercomMasterTalkSub != null)
            {
                TxtIntercomMasterTalkSub.Text = _isIntercomLatchMode ? "CLICK TO TOGGLE TALK" : "HOLD TO TALK (PTT)";
            }
        }

        #endregion

        private void OnChannelGainChanged(int ch, double db)
        {
            if (ch >= 0 && ch < _mixerFaders.Length && _mixerFaders[ch] != null)
            {
                if (Math.Abs(_mixerFaders[ch].Value - db) > 0.1)
                {
                    _mixerFaders[ch].Value = db;
                }
            }
        }

        private void OnChannelPanChanged(int ch, double pan)
        {
            if (ch >= 0 && ch < _mixerPans.Length && _mixerPans[ch] != null)
            {
                if (Math.Abs(_mixerPans[ch].Value - pan) > 0.05)
                {
                    _mixerPans[ch].Value = pan;
                }
            }
        }

        private void OnChannelMuteChanged(int ch, bool muted)
        {
            if (ch >= 0 && ch < _chkMuteCams.Length && _chkMuteCams[ch] != null)
            {
                _chkMuteCams[ch].IsChecked = muted;
            }
            UpdateMixerMuteUI(ch, muted);
            UpdateTallyIndicators();
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
            bool enable = ChkMasterSync.IsChecked == true;
            _syncEngine.MasterSyncEnabled = enable;
            _playoutAlignmentEngine.IsEnabled = enable;

            if (enable)
            {
                string host = TxtNtpServer?.Text?.Trim() ?? "time.google.com";
                if (string.IsNullOrEmpty(host)) host = "time.google.com";
                MasterClockProvider.Instance.StartPeriodicSync(host, 30);
                LogEvent("[SYNC]", $"🕒 KÍCH HOẠT Broadcast Frame Synchronization: Các camera được đồng bộ theo cửa sổ trễ {_syncEngine.TargetSyncWindowMs} ms.");
            }
            else
            {
                MasterClockProvider.Instance.StopPeriodicSync();
                if (TxtNtpOffsetResult != null)
                {
                    TxtNtpOffsetResult.Text = "Disabled (Free-Run)";
                    TxtNtpOffsetResult.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
                }
                LogEvent("[SYNC]", "ĐÃ TẮT Broadcast Frame Synchronization (Chuyển sang chế độ Free-Run Passthrough).");
            }
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
                _playoutAlignmentEngine.TargetSyncWindowMs = val;
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

        private void UpdateMasterVideoCodecFromUI()
        {
            if (CmbMasterRes?.SelectedItem != null)
                _outputManager.MasterVideoCodec.Resolution = GetComboBoxText(CmbMasterRes);
            if (CmbMasterCodec?.SelectedItem != null)
                _outputManager.MasterVideoCodec.Codec = GetComboBoxText(CmbMasterCodec);
            if (CmbMasterBitrate?.SelectedItem != null)
                _outputManager.MasterVideoCodec.Bitrate = GetComboBoxText(CmbMasterBitrate);
            if (CmbMasterFps?.SelectedItem != null)
                _outputManager.MasterVideoCodec.Fps = GetComboBoxText(CmbMasterFps);

            if (CmbSdiDevice?.SelectedItem != null)
            {
                string raw = CmbSdiDevice.SelectedItem.ToString() ?? "";
                if (raw.StartsWith("📡 [SDI HW] ")) raw = raw.Substring("📡 [SDI HW] ".Length);
                else if (raw.StartsWith("📡 [PORT] ")) raw = raw.Substring("📡 [PORT] ".Length);
                _outputManager.SdiDevice = raw;
            }
        }

        private async void ChkSdiOutput_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            bool enable = ChkSdiOutput.IsChecked == true;
            UpdateMasterVideoCodecFromUI();
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
            UpdateNdiUiBadges();
            BadgeOutputNdi.Background = enable ? new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)) : new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x2B));
            ((TextBlock)BadgeOutputNdi.Child).Foreground = enable ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
        }

        private void ChkNdiMultiviewer_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || _outputManager == null) return;
            bool isMv = ChkNdiMultiviewer.IsChecked == true;
            _outputManager.SetNdiMultiviewerMode(isMv);
            UpdateNdiUiBadges();
            LogEvent("[NDI]", isMv 
                ? "NDI Routing: Chuyển sang phát MULTIVIEWER GRID (Tự động 2x2 / 3x3 kèm Tally OSD)." 
                : "NDI Routing: Chuyển sang phát MASTER PROGRAM (PGM sạch độc lập).");
        }

        private void TxtNdiName_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitialized || _outputManager == null || TxtNdiName == null) return;
            _outputManager.NdiStreamName = TxtNdiName.Text.Trim();
        }

        private void UpdateNdiUiBadges()
        {
            if (BadgeNdiStatusPill != null && TxtNdiStatusPill != null)
            {
                bool isTx = ChkNDIOutput?.IsChecked == true;
                BadgeNdiStatusPill.Background = isTx 
                    ? new SolidColorBrush(Color.FromRgb(0x06, 0x5F, 0x46)) 
                    : new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x2B));
                TxtNdiStatusPill.Text = isTx ? "ON-AIR (TX)" : "OFFLINE";
                TxtNdiStatusPill.Foreground = isTx 
                    ? new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99)) 
                    : new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
            }

            if (BadgeNdiSourceType != null && TxtNdiSourceType != null)
            {
                bool isMultiviewer = ChkNdiMultiviewer?.IsChecked == true;
                if (isMultiviewer)
                {
                    BadgeNdiSourceType.Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x3A, 0x47));
                    BadgeNdiSourceType.BorderBrush = new SolidColorBrush(Color.FromRgb(0x06, 0xB6, 0xD4));
                    TxtNdiSourceType.Text = "MULTIVIEWER GRID";
                    TxtNdiSourceType.Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0xD3, 0xEE));
                }
                else
                {
                    BadgeNdiSourceType.Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x1B, 0x1B));
                    BadgeNdiSourceType.BorderBrush = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
                    TxtNdiSourceType.Text = "MASTER PGM";
                    TxtNdiSourceType.Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
                }
            }

            if (TxtNdiGridHint != null)
            {
                bool isMultiviewer = ChkNdiMultiviewer?.IsChecked == true;
                TxtNdiGridHint.Foreground = isMultiviewer 
                    ? new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)) 
                    : new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
            }
        }

        private async void ChkSrtBridge_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            bool enable = ChkSrtBridge.IsChecked == true;
            UpdateMasterVideoCodecFromUI();
            _outputManager.SrtBridgeHost = TxtBridgeHost.Text.Trim();
            if (int.TryParse(TxtBridgePort.Text, out int bp)) _outputManager.SrtBridgePort = bp;
            _outputManager.SrtBridgeMode = CmbBridgeMode.SelectedIndex == 1 ? SRTMode.Listener : SRTMode.Caller;
            await _outputManager.ToggleSrtBridgeAsync(enable);
            BadgeOutputBridge.Background = enable ? new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)) : new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x2B));
            ((TextBlock)BadgeOutputBridge.Child).Foreground = enable ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
        }

        private void CmbBridgeMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized || TxtBridgeHost == null || TxtBridgePort == null || TxtBridgeHint == null) return;

            bool isListener = CmbBridgeMode.SelectedIndex == 1;
            if (isListener)
            {
                TxtBridgeHost.IsEnabled = false;
                TxtBridgeHost.Opacity = 0.5;
                string portStr = string.IsNullOrWhiteSpace(TxtBridgePort.Text) ? "9100" : TxtBridgePort.Text.Trim();
                string localIp = GetLocalIpAddress();
                TxtBridgeHint.Text = $"💡 Listener Mode: Mở port tại máy này. Phía thu mở xem qua URL: srt://{localIp}:{portStr}?mode=caller";
                TxtBridgeHint.Foreground = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8));
            }
            else
            {
                TxtBridgeHost.IsEnabled = true;
                TxtBridgeHost.Opacity = 1.0;
                TxtBridgeHint.Text = "Đẩy luồng tới địa chỉ đích. Phía nhận phải bật Listener.";
                TxtBridgeHint.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0xCC));
            }
        }

        private void TxtBridgePort_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitialized || CmbBridgeMode == null || TxtBridgeHint == null) return;
            if (CmbBridgeMode.SelectedIndex == 1)
            {
                string portStr = string.IsNullOrWhiteSpace(TxtBridgePort.Text) ? "9100" : TxtBridgePort.Text.Trim();
                string localIp = GetLocalIpAddress();
                TxtBridgeHint.Text = $"💡 Listener Mode: Mở port tại máy này. Phía thu mở xem qua URL: srt://{localIp}:{portStr}?mode=caller";
            }
        }

        private static string GetLocalIpAddress()
        {
            try
            {
                using var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Dgram, 0);
                socket.Connect("8.8.8.8", 65530);
                if (socket.LocalEndPoint is System.Net.IPEndPoint endPoint)
                {
                    return endPoint.Address.ToString();
                }
            }
            catch { }
            return "127.0.0.1";
        }

        private async void ChkRecording_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            bool enable = ChkRecording.IsChecked == true;
            UpdateMasterVideoCodecFromUI();
            await _outputManager.ToggleRecordingAsync(enable);
            BadgeOutputRec.Background = enable ? new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)) : new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x2B));
            ((TextBlock)BadgeOutputRec.Child).Foreground = enable ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
        }

        #endregion

        #region ISO Output Controls (CAM 1..10)

        private async void ChkIsoSdi_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || sender is not CheckBox cb) return;
            int camIdx = cb.Tag is int idx ? idx : (cb.Tag is string s && int.TryParse(s, out int parsed) ? parsed : -1);
            if (camIdx < 0 || camIdx >= MaxChannels) return;

            bool enable = cb.IsChecked == true;
            if (enable && camIdx < _cmbIsoSdiPort.Length && _cmbIsoSdiPort[camIdx]?.SelectedItem != null)
            {
                _outputManager.ReceiverOutputs[camIdx].SdiPort = _cmbIsoSdiPort[camIdx].SelectedItem.ToString() ?? "";
            }
            await _outputManager.ToggleIsoSdiAsync(camIdx, enable);
        }

        private async void ChkIsoNdi_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || sender is not CheckBox cb) return;
            int camIdx = cb.Tag is int idx ? idx : (cb.Tag is string s && int.TryParse(s, out int parsed) ? parsed : -1);
            if (camIdx < 0 || camIdx >= MaxChannels) return;

            bool enable = cb.IsChecked == true;
            if (enable && camIdx < _txtIsoNdiName.Length && !string.IsNullOrWhiteSpace(_txtIsoNdiName[camIdx]?.Text))
            {
                _outputManager.ReceiverOutputs[camIdx].NdiName = _txtIsoNdiName[camIdx].Text.Trim();
            }
            await _outputManager.ToggleIsoNdiAsync(camIdx, enable);
        }

        private async void ChkIsoSrt_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || sender is not CheckBox cb) return;
            int camIdx = cb.Tag is int idx ? idx : (cb.Tag is string s && int.TryParse(s, out int parsed) ? parsed : -1);
            if (camIdx < 0 || camIdx >= MaxChannels) return;

            bool enable = cb.IsChecked == true;
            if (enable)
            {
                var cfg = _outputManager.ReceiverOutputs[camIdx];
                if (camIdx < _txtIsoSrtHost.Length && !string.IsNullOrWhiteSpace(_txtIsoSrtHost[camIdx]?.Text))
                    cfg.SrtBridgeHost = _txtIsoSrtHost[camIdx].Text.Trim();
                if (camIdx < _txtIsoSrtPort.Length && int.TryParse(_txtIsoSrtPort[camIdx]?.Text, out int port))
                    cfg.SrtBridgePort = port;
                if (camIdx < _cmbIsoSrtMode.Length && _cmbIsoSrtMode[camIdx] != null)
                    cfg.SrtBridgeMode = _cmbIsoSrtMode[camIdx].SelectedIndex == 1 ? SRTMode.Listener : SRTMode.Caller;
                if (camIdx < _cmbIsoSrtCodec.Length && _cmbIsoSrtCodec[camIdx]?.SelectedItem != null)
                    cfg.SrtBridgeCodec = GetComboBoxText(_cmbIsoSrtCodec[camIdx]);
                if (camIdx < _txtIsoSrtBitrate.Length && !string.IsNullOrWhiteSpace(_txtIsoSrtBitrate[camIdx]?.Text))
                    cfg.SrtBridgeBitrate = _txtIsoSrtBitrate[camIdx].Text.Trim();
            }
            await _outputManager.ToggleIsoSrtBridgeAsync(camIdx, enable);
        }

        private async void ChkIsoRec_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || sender is not CheckBox cb) return;
            int camIdx = cb.Tag is int idx ? idx : (cb.Tag is string s && int.TryParse(s, out int parsed) ? parsed : -1);
            if (camIdx < 0 || camIdx >= MaxChannels) return;

            bool enable = cb.IsChecked == true;
            if (enable)
            {
                var cfg = _outputManager.ReceiverOutputs[camIdx];
                if (camIdx < _cmbIsoRecFormat.Length && _cmbIsoRecFormat[camIdx]?.SelectedItem != null)
                    cfg.RecFormat = GetComboBoxText(_cmbIsoRecFormat[camIdx]);
                if (camIdx < _cmbIsoRes.Length && _cmbIsoRes[camIdx]?.SelectedItem != null)
                    cfg.Resolution = GetComboBoxText(_cmbIsoRes[camIdx]);
                if (camIdx < _cmbIsoCodec.Length && _cmbIsoCodec[camIdx]?.SelectedItem != null)
                    cfg.Codec = GetComboBoxText(_cmbIsoCodec[camIdx]);
                if (camIdx < _cmbIsoBitrate.Length && _cmbIsoBitrate[camIdx]?.SelectedItem != null)
                    cfg.Bitrate = GetComboBoxText(_cmbIsoBitrate[camIdx]);
                if (camIdx < _cmbIsoFps.Length && _cmbIsoFps[camIdx]?.SelectedItem != null)
                    cfg.Fps = GetComboBoxText(_cmbIsoFps[camIdx]);
            }
            await _outputManager.ToggleIsoRecordingAsync(camIdx, enable);
        }

        private static string GetComboBoxText(ComboBox cmb)
        {
            if (cmb.SelectedItem is ComboBoxItem item)
                return item.Content?.ToString() ?? string.Empty;
            return cmb.SelectedItem?.ToString() ?? cmb.Text ?? string.Empty;
        }

        #endregion

        #region Hardware Discovery & Fullscreen Playout

        private async Task RefreshDevicesAsync()
        {
            try
            {
                LogEvent("[INFO]", "Đang quét màn hình hiển thị và card SDI phần cứng...");

                // 1. Display Monitors
                _monitors = await DisplayMonitorScanner.ScanMonitorsAsync();
                CmbDisplayMonitors.Items.Clear();
                foreach (var m in _monitors)
                {
                    CmbDisplayMonitors.Items.Add(m.DisplayLabel);
                }
                if (CmbDisplayMonitors.Items.Count > 1)
                {
                    // Prefer secondary monitor for playout if available
                    CmbDisplayMonitors.SelectedIndex = 1;
                }
                else if (CmbDisplayMonitors.Items.Count > 0)
                {
                    CmbDisplayMonitors.SelectedIndex = 0;
                }

                // 2. SDI Hardware Devices
                _sdiDevices = await SdiHardwareScanner.ScanDevicesAsync();
                CmbSdiDevice.Items.Clear();
                foreach (var dev in _sdiDevices)
                {
                    CmbSdiDevice.Items.Add(dev.DisplayLabel);
                }
                if (CmbSdiDevice.Items.Count > 0)
                {
                    CmbSdiDevice.SelectedIndex = 0;
                    _outputManager.SdiDevice = _sdiDevices[0].Name;
                }

                // 3. Populate each ISO Cam SDI Port dropdown
                for (int i = 0; i < MaxChannels; i++)
                {
                    if (i < _cmbIsoSdiPort.Length && _cmbIsoSdiPort[i] != null)
                    {
                        var cmb = _cmbIsoSdiPort[i];
                        cmb.Items.Clear();
                        foreach (var dev in _sdiDevices)
                        {
                            cmb.Items.Add(dev.Name);
                        }
                        if (cmb.Items.Count > i)
                        {
                            cmb.SelectedIndex = i;
                        }
                        else if (cmb.Items.Count > 0)
                        {
                            cmb.SelectedIndex = 0;
                        }
                    }
                }

                LogEvent("[SUCCESS]", $"Đã phát hiện {_monitors.Count} màn hình hiển thị và {_sdiDevices.Count} thiết bị SDI / Broadcast.");
            }
            catch (Exception ex)
            {
                LogEvent("[ERROR]", $"Lỗi quét thiết bị: {ex.Message}");
            }
        }

        private async void BtnRefreshDevices_Click(object sender, RoutedEventArgs e)
        {
            await RefreshDevicesAsync();
        }

        private async void BtnScanSdi_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LogEvent("[INFO]", "Đang quét các thiết bị và cổng SDI phần cứng (DeckLink / AJA / WDM)...");
                _sdiDevices = await SdiHardwareScanner.ScanDevicesAsync();

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
                        _outputManager.SdiDevice = _sdiDevices[0].Name;
                    }
                }

                for (int i = 0; i < MaxChannels; i++)
                {
                    if (i < _cmbIsoSdiPort.Length && _cmbIsoSdiPort[i] != null)
                    {
                        var cmb = _cmbIsoSdiPort[i];
                        cmb.Items.Clear();
                        foreach (var dev in _sdiDevices)
                        {
                            cmb.Items.Add(dev.Name);
                        }
                        if (cmb.Items.Count > i)
                        {
                            cmb.SelectedIndex = i;
                        }
                        else if (cmb.Items.Count > 0)
                        {
                            cmb.SelectedIndex = 0;
                        }
                    }
                }

                LogEvent("[SUCCESS]", $"✅ Đã tìm thấy {_sdiDevices.Count} cổng xuất SDI khả dụng trên hệ thống.");
            }
            catch (Exception ex)
            {
                LogEvent("[ERROR]", $"Lỗi quét cổng SDI: {ex.Message}");
            }
        }

        private void BtnLaunchFullscreenPlayout_Click(object sender, RoutedEventArgs e)
        {
            if (_playoutWindow != null)
            {
                try
                {
                    _playoutWindow.Close();
                }
                catch { }
                _playoutWindow = null;
                BtnLaunchFullscreenPlayout.Content = "🖥️ Khởi chạy Fullscreen Playout (Màn hình ngoài)";
                BtnLaunchFullscreenPlayout.Background = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC));
                LogEvent("[INFO]", "Đã dừng Fullscreen Playout trên màn hình ngoài.");
                return;
            }

            if (_monitors.Count == 0)
            {
                LogEvent("[WARN]", "Không tìm thấy màn hình hiển thị để khởi chạy Playout.");
                return;
            }

            int selectedMonitorIdx = CmbDisplayMonitors.SelectedIndex;
            if (selectedMonitorIdx < 0 || selectedMonitorIdx >= _monitors.Count)
            {
                selectedMonitorIdx = 0;
            }

            var monitor = _monitors[selectedMonitorIdx];
            bool isMultiviewer = CmbPlayoutSource.SelectedIndex == 1;
            string sourceTitle = isMultiviewer ? "MULTIVIEWER GRID" : "MASTER PROGRAM (PGM)";

            try
            {
                _playoutWindow = new FullscreenPlayoutWindow(
                    monitor,
                    sourceTitle,
                    isMultiviewer,
                    _activeChannelCount,
                    _channelNames,
                    _currentProgramIndex,
                    _currentPreviewIndex);

                _playoutWindow.WindowClosed += () =>
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        _playoutWindow = null;
                        BtnLaunchFullscreenPlayout.Content = "🖥️ Khởi chạy Fullscreen Playout (Màn hình ngoài)";
                        BtnLaunchFullscreenPlayout.Background = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC));
                        LogEvent("[INFO]", "Cửa sổ Fullscreen Playout đã đóng.");
                    });
                };

                _playoutWindow.Show();

                // Seed existing bitmaps if already available
                if (isMultiviewer)
                {
                    for (int i = 0; i < _activeChannelCount; i++)
                    {
                        var bmp = _camBitmaps[i];
                        if (bmp != null && _fallbacks[i].Visibility == Visibility.Collapsed)
                        {
                            int w = bmp.PixelWidth;
                            int h = bmp.PixelHeight;
                            int stride = w * 4;
                            byte[] pixelBytes = new byte[stride * h];
                            bmp.CopyPixels(pixelBytes, stride, 0);
                            _playoutWindow.UpdateChannelFrame(i, pixelBytes, w, h);
                        }
                    }
                }
                else
                {
                    var pgmBmp = _camBitmaps[_currentProgramIndex];
                    if (pgmBmp != null && _fallbacks[_currentProgramIndex].Visibility == Visibility.Collapsed)
                    {
                        int w = pgmBmp.PixelWidth;
                        int h = pgmBmp.PixelHeight;
                        int stride = w * 4;
                        byte[] pixelBytes = new byte[stride * h];
                        pgmBmp.CopyPixels(pixelBytes, stride, 0);
                        _playoutWindow.UpdateFrame(pixelBytes, w, h);
                    }
                }

                BtnLaunchFullscreenPlayout.Content = "⏹️ Đóng Fullscreen Playout (ESC)";
                BtnLaunchFullscreenPlayout.Background = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
                LogEvent("[INFO]", $"Đã mở Fullscreen Playout ({sourceTitle}) trên {monitor.DisplayLabel}.");
            }
            catch (Exception ex)
            {
                LogEvent("[ERROR]", $"Lỗi mở Fullscreen Playout: {ex.Message}");
            }
        }

        private void CmbPlayoutSource_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized || _playoutWindow == null) return;
            bool isMultiviewer = CmbPlayoutSource.SelectedIndex == 1;
            _playoutWindow.SetSourceMode(isMultiviewer, _activeChannelCount, _channelNames, _currentProgramIndex, _currentPreviewIndex);
        }

        #endregion

        private void LogEvent(string tag, string message)
        {
            if (_isShuttingDown) return;

            try
            {
                if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;

                Dispatcher.InvokeAsync(() =>
                {
                    if (_isShuttingDown) return;

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
            catch { }
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

        #region IP Video Matrix Router NDI Event Handlers

        private async void ChkNdiPgmMaster_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox chk)
            {
                bool enable = chk.IsChecked == true;
                await _outputManager.ToggleNdiAsync(enable);
                LogEvent("[MATRIX-NDI]", $"NDI PGM Master (OME PGM MASTER): {(enable ? "ON AIR 🟢" : "STOPPED ⚪")}");
            }
        }

        private async void ChkNdiIso_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox chk && chk.Tag is string tagStr && int.TryParse(tagStr, out int camIndex))
            {
                bool enable = chk.IsChecked == true;
                await _outputManager.ToggleIsoNdiAsync(camIndex, enable);
                LogEvent("[MATRIX-NDI]", $"NDI ISO Cam {camIndex + 1} (OME ISO CAM {camIndex + 1:D2}): {(enable ? "ACTIVE 🟢" : "OFF ⚪")}");
            }
        }

        private async void BtnEnableAllNdi_Click(object sender, RoutedEventArgs e)
        {
            ChkNdiPgmMaster.IsChecked = true;
            await _outputManager.ToggleNdiAsync(true);

            for (int i = 0; i < 10; i++)
            {
                if (FindName($"ChkNdiIso{i + 1}") is CheckBox chk)
                {
                    chk.IsChecked = true;
                }
                await _outputManager.ToggleIsoNdiAsync(i, true);
            }
            LogEvent("[MATRIX-NDI]", "✅ Đã bật tất cả 11 luồng NDI Matrix (1 PGM + 10 ISO).");
        }

        private async void BtnDisableAllNdi_Click(object sender, RoutedEventArgs e)
        {
            ChkNdiPgmMaster.IsChecked = false;
            await _outputManager.ToggleNdiAsync(false);

            for (int i = 0; i < 10; i++)
            {
                if (FindName($"ChkNdiIso{i + 1}") is CheckBox chk)
                {
                    chk.IsChecked = false;
                }
                await _outputManager.ToggleIsoNdiAsync(i, false);
            }
            LogEvent("[MATRIX-NDI]", "⚪ Đã tắt tất cả các luồng NDI Matrix để tiết kiệm băng thông.");
        }

        private async void BtnToggleMatrixOutput_Click(object sender, RoutedEventArgs e)
        {
            BtnToggleMatrixOutput.IsEnabled = false;
            try
            {
                if (!_outputManager.IsMatrixIpcActive)
                {
                    bool ok = await Task.Run(() => _outputManager.StartMatrixIpc(1920, 1080));
                    if (ok)
                    {
                        BtnToggleMatrixOutput.Content = "⏹ Stop Matrix Output";
                        BtnToggleMatrixOutput.Background = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                        BrdMatrixIpcStatus.Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x28, 0x1E));
                        BrdMatrixIpcStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
                        TxtMatrixIpcStatus.Text = "D3D11 VRAM IPC 0ms: ACTIVE";
                        TxtMatrixIpcStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
                        BrdPort0Status.Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x2D, 0x1F));
                        BrdPort0Status.BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
                        TxtPort0Status.Text = "ONLINE (0ms)";
                        TxtPort0Status.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
                        LogEvent("[MATRIX-ROUTER]", "🚀 Đã kích hoạt 11 cổng D3D11 Shared Texture IPC để kết nối với OME_PLAYOUT.");
                    }
                    else
                    {
                        string err = _outputManager.MatrixPublisher?.LastError ?? "Lỗi không xác định";
                        LogEvent("[WARN]", $"Không thể mở Direct3D11 Matrix Router: {err}");
                    }
                }
                else
                {
                    await Task.Run(() => _outputManager.StopMatrixIpc());
                    BtnToggleMatrixOutput.Content = "▶ Start Matrix Output";
                    BtnToggleMatrixOutput.Background = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC));
                    BrdMatrixIpcStatus.Background = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x2B));
                    BrdMatrixIpcStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
                    TxtMatrixIpcStatus.Text = "STANDBY (Nhấn để bật)";
                    TxtMatrixIpcStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
                    BrdPort0Status.Background = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x2B));
                    BrdPort0Status.BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
                    TxtPort0Status.Text = "STANDBY";
                    TxtPort0Status.Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
                    LogEvent("[MATRIX-ROUTER]", "⏹ Đã dừng D3D11 Matrix Router liên tiến trình.");
                }
            }
            finally
            {
                BtnToggleMatrixOutput.IsEnabled = true;
            }
        }

        #endregion
    }
}
