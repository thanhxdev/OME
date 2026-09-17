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

namespace SRT_DECODE
{
    public partial class MainWindow : Window
    {
        private const int MaxChannels = 10;

        // ─── Subsystem Engines ──────────────────────────────────────
        private readonly NtpSyncEngine _syncEngine = new();
        private readonly VideoPlayoutAlignmentEngine _playoutAlignmentEngine;
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
        private volatile bool _isShuttingDown = false;
        private bool _isClosed = false;
        private readonly StringBuilder _logBuffer = new();

        // ─── Engine Event Delegates for Safe Unhooking ──────────────
        private Action<string, string>? _logDelegate;
        private Action<int, ReceiverChannelState>? _channelUpdatedDelegate;
        private Action<int, byte[], int, int>? _frameReadyDelegate;
        private Action<int, byte[], int, int, long, long>? _frameReadyWithPtsDelegate;
        private Action<int, byte[], int>? _audioPcmReadyDelegate;
        private Action<ChannelAudioLevels[]>? _camLevelsUpdatedDelegate;
        private Action<ChannelAudioLevels>? _programLevelsUpdatedDelegate;
        private Action<byte[], int, int>? _programMixedPcmDelegate;

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
        private TextBlock[] _txtResFps = Array.Empty<TextBlock>();

        private TextBlock[] _diagRtt = Array.Empty<TextBlock>();
        private TextBlock[] _diagLoss = Array.Empty<TextBlock>();
        private TextBlock[] _diagBitrate = Array.Empty<TextBlock>();
        private TextBlock[] _diagHealth = Array.Empty<TextBlock>();
        private TextBlock[] _diagBandwidth = Array.Empty<TextBlock>();
        private TextBlock[] _diagRexmit = Array.Empty<TextBlock>();
        private TextBlock[] _diagDrop = Array.Empty<TextBlock>();
        private TextBlock[] _diagUptime = Array.Empty<TextBlock>();
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
        private CheckBox[] _chkDecrypts = Array.Empty<CheckBox>();
        private TextBox[] _txtPassphrases = Array.Empty<TextBox>();
        private ComboBox[] _cmbKeyLens = Array.Empty<ComboBox>();

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

        // ─── Video Presentation Bitmaps & Frame Conflation ────────
        private sealed class ChannelFrameHolder
        {
            public byte[]? Buffer;
            public int Width;
            public int Height;
            public int RenderScheduled;
        }

        private readonly ChannelFrameHolder[] _channelFrameHolders = new ChannelFrameHolder[MaxChannels];
        private readonly WriteableBitmap?[] _camBitmaps = new WriteableBitmap?[MaxChannels];

        public MainWindow()
        {
            for (int i = 0; i < MaxChannels; i++)
            {
                _channelFrameHolders[i] = new ChannelFrameHolder();
            }

            _playoutAlignmentEngine = new VideoPlayoutAlignmentEngine(_syncEngine);
            _playoutAlignmentEngine.FrameReadyForPlayout += OnPlayoutFrameReady;

            _receiverEngine = new MultiStreamReceiverEngine(_syncEngine);

            // Create strongly-referenced delegates for unhooking
            _logDelegate = LogEvent;
            _channelUpdatedDelegate = OnReceiverChannelUpdated;
            _frameReadyDelegate = OnFrameReady;
            _frameReadyWithPtsDelegate = OnFrameReadyWithPts;
            _audioPcmReadyDelegate = (chIdx, pcm, len) =>
            {
                if (_isShuttingDown) return;
                _audioManager.ProcessDecodedPcm(chIdx, pcm, len);
                _outputManager.FeedIsoAudio(chIdx, pcm, len);
            };
            _camLevelsUpdatedDelegate = OnAudioLevelsUpdated;
            _programLevelsUpdatedDelegate = OnProgramLevelsUpdated;
            _programMixedPcmDelegate = (pcm, offset, count) =>
            {
                if (_isShuttingDown) return;
                byte[] pcmCopy = new byte[count];
                Buffer.BlockCopy(pcm, offset, pcmCopy, 0, count);
                _outputManager.FeedMasterAudio(pcmCopy, count);
            };

            // Wire log events
            _syncEngine.LogEmitted += _logDelegate;
            _receiverEngine.LogEmitted += _logDelegate;
            _audioManager.LogEmitted += _logDelegate;
            _outputManager.LogEmitted += _logDelegate;

            // Wire receiver updates
            _receiverEngine.ChannelUpdated += _channelUpdatedDelegate;
            _receiverEngine.FrameReadyWithPts += _frameReadyWithPtsDelegate;
            _receiverEngine.FrameReady += _frameReadyDelegate;
            _receiverEngine.AudioPcmReady += _audioPcmReadyDelegate;
            _audioManager.CamLevelsUpdated += _camLevelsUpdatedDelegate;
            _audioManager.ProgramLevelsUpdated += _programLevelsUpdatedDelegate;
            _audioManager.ProgramMixedPcmAvailable += _programMixedPcmDelegate;

            InitializeComponent();
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
                _txtResFps = new[] { TxtResFpsCam1, TxtResFpsCam2, TxtResFpsCam3, TxtResFpsCam4, TxtResFpsCam5, TxtResFpsCam6, TxtResFpsCam7, TxtResFpsCam8, TxtResFpsCam9, TxtResFpsCam10 };

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

                // Cài đặt mặc định kiểm âm: Toàn bộ các kênh CAM (CAM 1..10) đều MUTE mặc định, chỉ unmute khi click
                for (int i = 0; i < MaxChannels; i++)
                {
                    if (i < _chkMuteCams.Length && _chkMuteCams[i] != null)
                    {
                        _chkMuteCams[i].IsChecked = true;
                    }
                    _audioManager.SetChannelMuted(i, true, _channelNames[i]);
                    UpdateMixerMuteUI(i, true);
                }
                UpdateSoloButtonsUI();

                // Cài đặt mặc định khi mở App: BẬT kiểm âm Preview cho CAM 1
                _audioManager.IsPreviewMuted = false;
                if (BtnMutePreview != null)
                {
                    BtnMutePreview.Content = "MUTE PVW";
                    BtnMutePreview.Background = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)); // Green
                }
                _audioManager.IsProgramMuted = false;
                if (BtnMixerMutePgm != null)
                {
                    BtnMixerMutePgm.Content = "MUTE";
                    BtnMixerMutePgm.Background = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x34));
                }

                _hudRtt = new[] { HudRttCam1, HudRttCam2, HudRttCam3, HudRttCam4, HudRttCam5, HudRttCam6, HudRttCam7, HudRttCam8, HudRttCam9, HudRttCam10 };
                _hudLoss = new[] { HudLossCam1, HudLossCam2, HudLossCam3, HudLossCam4, HudLossCam5, HudLossCam6, HudLossCam7, HudLossCam8, HudLossCam9, HudLossCam10 };
                _hudBitrate = new[] { HudBitrateCam1, HudBitrateCam2, HudBitrateCam3, HudBitrateCam4, HudBitrateCam5, HudBitrateCam6, HudBitrateCam7, HudBitrateCam8, HudBitrateCam9, HudBitrateCam10 };
                _hudDrift = new[] { HudDriftCam1, HudDriftCam2, HudDriftCam3, HudDriftCam4, HudDriftCam5, HudDriftCam6, HudDriftCam7, HudDriftCam8, HudDriftCam9, HudDriftCam10 };

                _diagRtt = new[] { DiagRttCam1, DiagRttCam2, DiagRttCam3, DiagRttCam4, DiagRttCam5, DiagRttCam6, DiagRttCam7, DiagRttCam8, DiagRttCam9, DiagRttCam10 };
                _diagLoss = new[] { DiagLossCam1, DiagLossCam2, DiagLossCam3, DiagLossCam4, DiagLossCam5, DiagLossCam6, DiagLossCam7, DiagLossCam8, DiagLossCam9, DiagLossCam10 };
                _diagBitrate = new[] { DiagBitrateCam1, DiagBitrateCam2, DiagBitrateCam3, DiagBitrateCam4, DiagBitrateCam5, DiagBitrateCam6, DiagBitrateCam7, DiagBitrateCam8, DiagBitrateCam9, DiagBitrateCam10 };
                _diagHealth = new[] { DiagHealthCam1, DiagHealthCam2, DiagHealthCam3, DiagHealthCam4, DiagHealthCam5, DiagHealthCam6, DiagHealthCam7, DiagHealthCam8, DiagHealthCam9, DiagHealthCam10 };
                _diagBandwidth = new[] { DiagBandwidthCam1, DiagBandwidthCam2, DiagBandwidthCam3, DiagBandwidthCam4, DiagBandwidthCam5, DiagBandwidthCam6, DiagBandwidthCam7, DiagBandwidthCam8, DiagBandwidthCam9, DiagBandwidthCam10 };
                _diagRexmit = new[] { DiagRexmitCam1, DiagRexmitCam2, DiagRexmitCam3, DiagRexmitCam4, DiagRexmitCam5, DiagRexmitCam6, DiagRexmitCam7, DiagRexmitCam8, DiagRexmitCam9, DiagRexmitCam10 };
                _diagDrop = new[] { DiagDropCam1, DiagDropCam2, DiagDropCam3, DiagDropCam4, DiagDropCam5, DiagDropCam6, DiagDropCam7, DiagDropCam8, DiagDropCam9, DiagDropCam10 };
                _diagUptime = new[] { DiagUptimeCam1, DiagUptimeCam2, DiagUptimeCam3, DiagUptimeCam4, DiagUptimeCam5, DiagUptimeCam6, DiagUptimeCam7, DiagUptimeCam8, DiagUptimeCam9, DiagUptimeCam10 };
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
                _chkDecrypts = new[] { ChkDecryptCam1, ChkDecryptCam2, ChkDecryptCam3, ChkDecryptCam4, ChkDecryptCam5, ChkDecryptCam6, ChkDecryptCam7, ChkDecryptCam8, ChkDecryptCam9, ChkDecryptCam10 };
                _txtPassphrases = new[] { TxtPassphraseCam1, TxtPassphraseCam2, TxtPassphraseCam3, TxtPassphraseCam4, TxtPassphraseCam5, TxtPassphraseCam6, TxtPassphraseCam7, TxtPassphraseCam8, TxtPassphraseCam9, TxtPassphraseCam10 };
                _cmbKeyLens = new[] { CmbKeyLenCam1, CmbKeyLenCam2, CmbKeyLenCam3, CmbKeyLenCam4, CmbKeyLenCam5, CmbKeyLenCam6, CmbKeyLenCam7, CmbKeyLenCam8, CmbKeyLenCam9, CmbKeyLenCam10 };

                for (int i = 0; i < MaxChannels; i++)
                {
                    if (i < _chkAutoLatencies.Length && _chkAutoLatencies[i] != null)
                    {
                        _chkAutoLatencies[i].IsChecked = false;
                    }
                    _receiverEngine.Channels[i].Config.AutoLatency = false;
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

                LogEvent("[INFO]", "Ứng dụng OME Broadcast Multi-SRT Decoder & Studio Sync đang khởi chạy...");
                TxtEngineStatus.Text = "Engine: Standalone / Host Mode";

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
                        string? serverPath = FindServerExecutable();
                        bool runtimeInit = await OpenMediaRuntime.InitializeAsync(new RuntimeOptions 
                        { 
                            AutoLaunch = false,
                            ConnectionTimeout = 1000,
                            ServerPath = serverPath 
                        });
                        _ = Dispatcher.InvokeAsync(() =>
                        {
                            if (_isShuttingDown || Dispatcher.HasShutdownStarted) return;
                            if (runtimeInit)
                            {
                                TxtEngineStatus.Text = $"Engine: Connected (v{OpenMediaRuntime.EngineVersion} - D3D11 Shared Textures)";
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

                // Khởi tạo placeholder mặc định (Không quét đồng bộ khi khởi chạy để mở app tức thì)
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

                // Áp dụng thông số cấu hình mặc định cho CAM 1 (chưa nhận luồng cho đến khi người dùng nhấn nút)
                ApplyFormInputsToChannel(0);

                LogEvent("[INFO]", "Hệ thống Master Control Room đã sẵn sàng (Nhấn 'Start CAM 1' để bắt đầu nhận luồng).");
            }
            catch (Exception ex)
            {
                LogEvent("[ERROR]", $"Lỗi khởi tạo ứng dụng: {ex.Message}");
            }
        }

        private void UnhookAllEngineEvents()
        {
            _isShuttingDown = true;

            try
            {
                if (_logDelegate != null)
                {
                    _syncEngine.LogEmitted -= _logDelegate;
                    _receiverEngine.LogEmitted -= _logDelegate;
                    _audioManager.LogEmitted -= _logDelegate;
                    _outputManager.LogEmitted -= _logDelegate;
                }

                if (_channelUpdatedDelegate != null)
                    _receiverEngine.ChannelUpdated -= _channelUpdatedDelegate;
                if (_frameReadyDelegate != null)
                    _receiverEngine.FrameReady -= _frameReadyDelegate;
                if (_frameReadyWithPtsDelegate != null)
                    _receiverEngine.FrameReadyWithPts -= _frameReadyWithPtsDelegate;
                if (_audioPcmReadyDelegate != null)
                    _receiverEngine.AudioPcmReady -= _audioPcmReadyDelegate;
                if (_camLevelsUpdatedDelegate != null)
                    _audioManager.CamLevelsUpdated -= _camLevelsUpdatedDelegate;
                if (_programLevelsUpdatedDelegate != null)
                    _audioManager.ProgramLevelsUpdated -= _programLevelsUpdatedDelegate;
                if (_programMixedPcmDelegate != null)
                    _audioManager.ProgramMixedPcmAvailable -= _programMixedPcmDelegate;
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

                // 2. Dừng ngay toàn bộ Timer UI
                _masterClockTimer?.Stop();
                _telemetryTimer?.Stop();
                _vuMeterTimer?.Stop();
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
                                await _receiverEngine.StopAllAsync().ConfigureAwait(false);
                                _receiverEngine.Dispose();
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
                int newIdx = _activeChannelCount - 1;
                _audioManager.SetChannelMuted(newIdx, true, _channelNames[newIdx]);
                if (newIdx < _chkMuteCams.Length && _chkMuteCams[newIdx] != null)
                {
                    _chkMuteCams[newIdx].IsChecked = true;
                }
                UpdateMixerMuteUI(newIdx, true);
                UpdateActiveStreamsUI();
                LogEvent("[INGEST]", $"➕ Đã thêm khung SRT Receiver Ingest #{_activeChannelCount} ({_channelNames[newIdx]}). Tổng số luồng: {_activeChannelCount}/10.");
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
                LogEvent("[INGEST]", $"➖ Đã bớt luồng SRT Ingest #{removeIdx + 1}. Đã thu hồi bộ nhớ bitmap. Còn lại: {_activeChannelCount}/10 luồng.");
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
            // 0: View (Multi-view 2x2)
            // 1: View (Multi-view 3x3)
            // 2: View (Multi-view 4x4)
            // 3: PGM+View (PGM trên + Multi-view)

            int cols = 2;
            int rows = 2;
            bool isPgmTop = false;

            switch (selected)
            {
                case 1:
                    cols = 3;
                    rows = 3;
                    isPgmTop = false;
                    break;
                case 2:
                    cols = 4;
                    rows = 4;
                    isPgmTop = false;
                    break;
                case 3:
                    cols = 2;
                    rows = 2;
                    isPgmTop = true;
                    break;
                case 0:
                default:
                    cols = 2;
                    rows = 2;
                    isPgmTop = false;
                    break;
            }

            MultiviewerContainer.RowDefinitions.Clear();
            MultiviewerContainer.ColumnDefinitions.Clear();

            for (int c = 0; c < cols; c++)
            {
                MultiviewerContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            // Đảm bảo số hàng tối thiểu luôn khớp với ma trận cấu hình (2x2, 3x3, 4x4)
            int neededRows = Math.Max(1, (_activeChannelCount + cols - 1) / cols);
            int totalRows = Math.Max(rows, neededRows);
            for (int r = 0; r < totalRows; r++)
            {
                MultiviewerContainer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            if (!isPgmTop)
            {
                CellPgmMaster.Visibility = Visibility.Collapsed;
            }
            else
            {
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
                _hudDrift[i].Foreground = sync.LockState == SyncLockState.Locked ? Brushes.LightGreen : (sync.LockState == SyncLockState.Syncing ? Brushes.Orange : Brushes.Gray);

                // Update Diag Tab & Real-Time Camera Drift Matrix in Tab 4
                if (i < _driftRows.Length && _driftRows[i] != null)
                {
                    _driftRows[i].Visibility = (i < _activeChannelCount) ? Visibility.Visible : Visibility.Collapsed;
                }

                _diagRtt[i].Text = $"⏱️ RTT: {ch.CurrentRttMs:F1} ms";
                _diagLoss[i].Text = $"📉 Loss: {ch.CurrentPacketLoss:F2} %";
                _diagLoss[i].Foreground = ch.CurrentPacketLoss > 3.0 ? Brushes.Red : (ch.CurrentPacketLoss > 1.0 ? Brushes.Orange : Brushes.LightGreen);

                _diagRexmit[i].Text = $"🔁 Rexmit: {ch.PacketsRetransmitted:N0} pkts";
                _diagRexmit[i].Foreground = ch.PacketsRetransmitted > 50 ? Brushes.Orange : new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0));

                _diagHealth[i].Text = $"🩺 Health: {ch.BufferHealthPercent:F0}% ({(ch.BufferHealthPercent >= 90 ? "Stable" : ch.BufferHealthPercent >= 70 ? "Fair" : "Degraded")})";
                _diagHealth[i].Foreground = ch.BufferHealthPercent >= 90 ? Brushes.LightGreen : (ch.BufferHealthPercent >= 70 ? Brushes.Orange : Brushes.Red);

                _diagBitrate[i].Text = $"🚀 Ingest: {ch.CurrentBitrateKbps:F0} kbps";
                _diagBandwidth[i].Text = $"📶 Bandwidth: {ch.BandwidthMbps:F2} Mbps";

                _diagDrop[i].Text = $"❌ Drop: {ch.PacketsDropped:N0} pkts";
                _diagDrop[i].Foreground = ch.PacketsDropped > 0 ? Brushes.Red : new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));

                _diagUptime[i].Text = $"🕒 Uptime: {ch.Uptime:hh\\:mm\\:ss}";
                _pbBuffer[i].Value = sync.BufferFillPercent;
                _pbBuffer[i].Foreground = sync.LockState == SyncLockState.Locked ? Brushes.LightGreen : (sync.LockState == SyncLockState.Syncing ? Brushes.Orange : Brushes.Gray);
                
                string dropRptInfo = sync.DroppedFrames > 0 || sync.RepeatedFrames > 0 ? $" • Drop:{sync.DroppedFrames} Rpt:{sync.RepeatedFrames}" : "";
                _txtDriftVal[i].Text = $"Δt: {sync.GetFormattedDrift()} ({sync.LockState}{dropRptInfo})";
                _txtDriftVal[i].Foreground = sync.LockState == SyncLockState.Locked ? Brushes.LightGreen : (sync.LockState == SyncLockState.Syncing ? Brushes.Orange : Brushes.Gray);

                // Update real incoming stream resolution & frame rate on camera preview header
                if (i < _txtResFps.Length && _txtResFps[i] != null)
                {
                    if (ch.IsConnected)
                    {
                        double fps = ch.MeasuredFps > 0.1 ? ch.MeasuredFps : (ch.CurrentFps > 0.1 ? ch.CurrentFps : 0.0);
                        int h = ch.VideoHeight > 0 ? ch.VideoHeight : 1080;
                        if (fps > 0.5)
                        {
                            _txtResFps[i].Text = $"{h}p{fps:F2}";
                            _txtResFps[i].Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
                        }
                        else
                        {
                            _txtResFps[i].Text = $"{h}p 0.0 FPS";
                            _txtResFps[i].Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
                        }
                    }
                    else
                    {
                        _txtResFps[i].Text = "STANDBY";
                        _txtResFps[i].Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
                    }
                }
            }

            // Inactive channels reset to STANDBY
            for (int i = _activeChannelCount; i < MaxChannels; i++)
            {
                if (i < _txtResFps.Length && _txtResFps[i] != null)
                {
                    _txtResFps[i].Text = "STANDBY";
                    _txtResFps[i].Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
                }
            }

            // Update PGM Master Header Resolution & FPS
            if (TxtPgmMasterFps != null)
            {
                var pgmCh = _receiverEngine.Channels[_currentProgramIndex];
                if (pgmCh.IsConnected && _currentProgramIndex < _activeChannelCount)
                {
                    double pgmFps = pgmCh.MeasuredFps > 0.1 ? pgmCh.MeasuredFps : (pgmCh.CurrentFps > 0.1 ? pgmCh.CurrentFps : 0.0);
                    int pgmH = pgmCh.VideoHeight > 0 ? pgmCh.VideoHeight : 1080;
                    TxtPgmMasterFps.Text = $"{pgmH}p{pgmFps:F2} Master Playout";
                    TxtPgmMasterFps.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
                }
                else
                {
                    TxtPgmMasterFps.Text = "No Program Signal";
                    TxtPgmMasterFps.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
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
            if (_isShuttingDown || Dispatcher.HasShutdownStarted) return;

            try
            {
                Dispatcher.InvokeAsync(() =>
                {
                    if (_isShuttingDown || index < 0 || index >= MaxChannels) return;

                    // Update Status text and LED
                    _statusTexts[index].Text = state.StatusMessage;
                    if (state.IsConnected)
                    {
                        _ledIndicators[index].Fill = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)); // Green (Live)
                    }
                    else if (state.IsRunning)
                    {
                        _ledIndicators[index].Fill = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)); // Amber / Orange (Waiting / Reconnecting)
                    }
                    else
                    {
                        _ledIndicators[index].Fill = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)); // Grey (Standby)
                    }

                    // Show fallback placeholder if disconnected
                    if (!state.IsConnected)
                    {
                        _fallbacks[index].Visibility = Visibility.Visible;
                        if (index == _currentProgramIndex)
                        {
                            FallbackPgm.Visibility = Visibility.Visible;
                        }
                        _playoutAlignmentEngine.ClearChannel(index);
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

        private void OnFrameReady(int channelIndex, byte[] frameBytes, int width, int height)
        {
            if (_isShuttingDown || Dispatcher.HasShutdownStarted) return;
            if (channelIndex < 0 || channelIndex >= MaxChannels) return;

            double rttMs = _receiverEngine.Channels[channelIndex].CurrentRttMs;
            _playoutAlignmentEngine.EnqueueFrame(channelIndex, frameBytes, width, height, rttMs);
        }

        private void OnFrameReadyWithPts(int channelIndex, byte[] frameBytes, int width, int height, long pts, long duration)
        {
            if (_isShuttingDown || Dispatcher.HasShutdownStarted) return;
            if (channelIndex < 0 || channelIndex >= MaxChannels) return;

            double rttMs = _receiverEngine.Channels[channelIndex].CurrentRttMs;
            _playoutAlignmentEngine.EnqueueFrameWithPts(channelIndex, frameBytes, width, height, pts, duration, rttMs);
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

            // Frame Conflation: Lưu tham chiếu frame mới nhất vào holder
            var holder = _channelFrameHolders[channelIndex];
            holder.Buffer = frameBytes;
            holder.Width = width;
            holder.Height = height;

            // Chỉ schedule một lần vẽ nếu Dispatcher chưa có task của camera này
            if (Interlocked.CompareExchange(ref holder.RenderScheduled, 1, 0) == 0)
            {
                Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        Interlocked.Exchange(ref holder.RenderScheduled, 0);
                        byte[]? currentBytes = holder.Buffer;
                        int w = holder.Width;
                        int h = holder.Height;
                        if (currentBytes == null || w <= 0 || h <= 0) return;

                        var bmp = _camBitmaps[channelIndex];
                        if (bmp == null || bmp.PixelWidth != w || bmp.PixelHeight != h)
                        {
                            bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
                            _camBitmaps[channelIndex] = bmp;
                            _videoViews[channelIndex].PresentBitmap(bmp);
                        }

                        int stride = w * 4;
                        bmp.WritePixels(new Int32Rect(0, 0, w, h), currentBytes, stride, 0);

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
                            if (VideoViewPgm.VideoImageControl.Source != bmp)
                            {
                                VideoViewPgm.PresentBitmap(bmp);
                            }
                        }
                    }
                    catch { }
                }, DispatcherPriority.Render);
            }
        }

        private async void BtnToggleCam_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tagStr && int.TryParse(tagStr, out int index))
            {
                // Tự động mở rộng active count nếu user bật camera cao hơn số lượng active hiện tại
                if (index >= _activeChannelCount)
                {
                    _activeChannelCount = index + 1;
                    UpdateActiveStreamsUI();
                }

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

            // Decryption controls (AES)
            if (index < _chkDecrypts.Length && _chkDecrypts[index] != null)
            {
                ch.Config.EncryptionEnabled = _chkDecrypts[index].IsChecked == true;
                ch.Config.Passphrase = _txtPassphrases[index]?.Text?.Trim() ?? string.Empty;
                if (_cmbKeyLens[index]?.SelectedItem is ComboBoxItem item && item.Content is string keyStr)
                {
                    if (keyStr.Contains("128")) ch.Config.KeyLength = 16;
                    else if (keyStr.Contains("192")) ch.Config.KeyLength = 24;
                    else ch.Config.KeyLength = 32;
                }
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
                    if (i < levels.Length && (i < _activeChannelCount || _receiverEngine.Channels[i].IsRunning))
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

        private void BtnMutePreview_Click(object sender, RoutedEventArgs e)
        {
            _audioManager.IsPreviewMuted = !_audioManager.IsPreviewMuted;
            bool isMuted = _audioManager.IsPreviewMuted;
            string bottomText = isMuted ? "UNMUTE PVW" : "MUTE PREVIEW";
            Brush bg = isMuted ? Brushes.Red : new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x38));

            if (BtnMutePreview != null)
            {
                BtnMutePreview.Content = bottomText;
                BtnMutePreview.Background = bg;
                BtnMutePreview.ToolTip = isMuted 
                    ? "Bật tiếng loa kiểm âm Preview (Không ảnh hưởng đến âm thanh PGM)" 
                    : "Tắt tiếng loa kiểm âm Preview (Không ảnh hưởng đến âm thanh PGM)";
            }

            LogEvent("[AUDIO]", isMuted 
                ? "🔇 Đã MUTE kiểm âm Preview (Loa/Tai nghe). Luồng PGM phát sóng và ghi hình vẫn giữ nguyên âm thanh!" 
                : "🔊 Đã BẬT lại kiểm âm Preview (Loa/Tai nghe).");
        }

        private void BtnMixerMutePgm_Click(object sender, RoutedEventArgs e)
        {
            _audioManager.IsProgramMuted = !_audioManager.IsProgramMuted;
            bool isMuted = _audioManager.IsProgramMuted;
            string pgmText = isMuted ? "UNMUTE" : "MUTE";
            Brush bg = isMuted 
                ? new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)) 
                : new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x34));

            if (BtnMixerMutePgm != null)
            {
                BtnMixerMutePgm.Content = pgmText;
                BtnMixerMutePgm.Background = bg;
                BtnMixerMutePgm.ToolTip = isMuted 
                    ? "Bật lại âm thanh luồng PGM (Phát sóng SDI, NDI, Ghi hình, SRT TX)" 
                    : "Mute luồng âm thanh PGM (SDI, NDI, Rec, SRT TX)";
            }

            LogEvent("[AUDIO]", isMuted 
                ? "🔇 Đã MUTE âm thanh luồng PGM (SDI, NDI, Ghi hình, SRT TX Bridge)!" 
                : "🔊 Đã BẬT lại âm thanh luồng PGM (SDI, NDI, Ghi hình, SRT TX Bridge).");
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
                _audioManager.MasterVolumePercent = slider.Value;
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
                var ch = _receiverEngine.Channels[i];
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
            if (_isShuttingDown || string.IsNullOrWhiteSpace(message)) return;

            // Bỏ qua các dòng log chứa thông số và tốc độ truyền frames trên bảng hiển thị log
            if (IsFrameStatsLog(tag, message)) return;

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

        /// <summary>
        /// Bộ lọc loại trừ các thông số và tốc độ truyền frames gây tràn console log.
        /// </summary>
        private static bool IsFrameStatsLog(string tag, string message)
        {
            // Bỏ qua thông số tốc độ giải mã & tiến trình khung hình (frame=, fps=, speed=, bitrate=, q=, size=, time=)
            if (message.Contains("frame=", StringComparison.OrdinalIgnoreCase) || 
                message.Contains("fps=", StringComparison.OrdinalIgnoreCase) || 
                message.Contains("speed=", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("bitrate=", StringComparison.OrdinalIgnoreCase) || 
                message.Contains("q=", StringComparison.OrdinalIgnoreCase) || 
                message.Contains("size=", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("kB time=", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("dup=", StringComparison.OrdinalIgnoreCase) || 
                message.Contains("drop=", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Bỏ qua các metadata dòng stream của FFmpeg (Stream #, Metadata:, Duration:)
            if (tag.Contains("FFMPEG", StringComparison.OrdinalIgnoreCase))
            {
                if (message.Contains("fps", StringComparison.OrdinalIgnoreCase) || 
                    message.Contains("tbr", StringComparison.OrdinalIgnoreCase) || 
                    message.Contains("tbn", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("Stream #", StringComparison.OrdinalIgnoreCase) || 
                    message.Contains("Metadata:", StringComparison.OrdinalIgnoreCase) || 
                    message.Contains("Duration:", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
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

        #region Server Discovery

        private static string? FindServerExecutable()
        {
            // 1. Dùng trực tiếp ServerDiscovery từ OpenMedia.Platform
            try
            {
                string? discovered = OpenMedia.Platform.Internal.ServerDiscovery.Discover();
                if (!string.IsNullOrEmpty(discovered) && System.IO.File.Exists(discovered))
                {
                    return discovered;
                }
            }
            catch { }

            // 2. Tra cứu từ biến môi trường OPENMEDIA_SERVER_PATH hoặc OPENMEDIA_SDK_DIR
            string? envServerPath = Environment.GetEnvironmentVariable("OPENMEDIA_SERVER_PATH");
            if (!string.IsNullOrEmpty(envServerPath) && System.IO.File.Exists(envServerPath))
            {
                return envServerPath;
            }

            string? sdkDir = Environment.GetEnvironmentVariable("OPENMEDIA_SDK_DIR");
            if (!string.IsNullOrEmpty(sdkDir))
            {
                string p1 = System.IO.Path.Combine(sdkDir, "bin", "OpenMediaServer.exe");
                if (System.IO.File.Exists(p1)) return p1;
                string p2 = System.IO.Path.Combine(sdkDir, "OpenMediaServer.exe");
                if (System.IO.File.Exists(p2)) return p2;
            }

            // 3. Tra cứu từ Windows Registry HKLM\Software\OpenMedia\SDK (Path) hoặc HKLM\Software\OpenMedia (ServerPath / InstallPath)
            try
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"Software\OpenMedia\SDK"))
                {
                    if (key != null)
                    {
                        string? regPath = key.GetValue("Path") as string;
                        if (!string.IsNullOrEmpty(regPath))
                        {
                            string binPath = System.IO.Path.Combine(regPath, "bin", "OpenMediaServer.exe");
                            if (System.IO.File.Exists(binPath)) return binPath;
                            string rootPath = System.IO.Path.Combine(regPath, "OpenMediaServer.exe");
                            if (System.IO.File.Exists(rootPath)) return rootPath;
                        }
                    }
                }

                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"Software\OpenMedia"))
                {
                    if (key != null)
                    {
                        string? serverPath = key.GetValue("ServerPath") as string;
                        if (!string.IsNullOrEmpty(serverPath) && System.IO.File.Exists(serverPath)) return serverPath;

                        string? installPath = key.GetValue("InstallPath") as string;
                        if (!string.IsNullOrEmpty(installPath))
                        {
                            string binPath = System.IO.Path.Combine(installPath, "bin", "OpenMediaServer.exe");
                            if (System.IO.File.Exists(binPath)) return binPath;
                        }
                    }
                }
            }
            catch { }

            // 4. Tra cứu đường dẫn mặc định trong Program Files
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var defaultPaths = new[]
            {
                System.IO.Path.Combine(programFiles, "OpenMedia", "SDK", "bin", "OpenMediaServer.exe"),
                System.IO.Path.Combine(programFiles, "OpenMedia", "bin", "OpenMediaServer.exe"),
                System.IO.Path.Combine(programFiles, "OpenMedia", "OpenMediaServer.exe")
            };
            foreach (var dp in defaultPaths)
            {
                if (System.IO.File.Exists(dp)) return dp;
            }

            // 5. Tra cứu trong thư mục chạy của ứng dụng (Co-located)
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var localCandidates = new[]
            {
                System.IO.Path.Combine(baseDir, "OpenMediaServer.exe"),
                System.IO.Path.Combine(baseDir, "bin", "OpenMediaServer.exe"),
                System.IO.Path.Combine(baseDir, "OpenMediaServer", "OpenMediaServer.exe")
            };
            foreach (var lc in localCandidates)
            {
                if (System.IO.File.Exists(lc)) return lc;
            }

            // 6. Tra cứu trong các thư mục build trong môi trường phát triển (Dev Fallback)
            string current = baseDir;
            for (int i = 0; i < 6; i++)
            {
                if (string.IsNullOrEmpty(current)) break;
                var devCandidates = new[]
                {
                    System.IO.Path.Combine(current, "build", "bin", "Release", "OpenMediaServer.exe"),
                    System.IO.Path.Combine(current, "build", "bin", "Debug", "OpenMediaServer.exe"),
                    System.IO.Path.Combine(current, "build-production", "bin", "Release", "OpenMediaServer.exe"),
                    System.IO.Path.Combine(current, "build-demo", "bin", "Release", "OpenMediaServer.exe"),
                    System.IO.Path.Combine(current, "build-demo", "bin", "Debug", "OpenMediaServer.exe"),
                    System.IO.Path.Combine(current, "dist", "sdk", "bin", "OpenMediaServer.exe"),
                    System.IO.Path.Combine(current, "dist", "sdk_staging", "bin", "OpenMediaServer.exe"),
                    System.IO.Path.Combine(current, "dist", "production", "bin", "OpenMediaServer.exe")
                };
                foreach (var dc in devCandidates)
                {
                    if (System.IO.File.Exists(dc)) return dc;
                }
                var parent = System.IO.Directory.GetParent(current);
                if (parent == null) break;
                current = parent.FullName;
            }

            return null;
        }

        #endregion

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

        #endregion
    }
}
