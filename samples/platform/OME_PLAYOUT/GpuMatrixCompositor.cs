using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using OpenMedia.Platform.IPC;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace OME_PLAYOUT
{
    public sealed class MasterVideoCodecConfig
    {
        public string Resolution { get; set; } = "1080p (1920x1080 Full HD)";
        public string Codec { get; set; } = "Uncompressed UYVY 4:2:2 (Direct SDI)";
        public string Bitrate { get; set; } = "Uncompressed / Auto";
        public string Fps { get; set; } = "59.94 fps (Broadcast Standard)";

        public (int width, int height) ParseResolution(int defaultWidth = 1920, int defaultHeight = 1080)
        {
            if (Resolution.Contains("3840x2160") || Resolution.Contains("2160") || Resolution.Contains("4K")) return (3840, 2160);
            if (Resolution.Contains("2560x1440") || Resolution.Contains("1440") || Resolution.Contains("2K")) return (2560, 1440);
            if (Resolution.Contains("1280x720") || Resolution.Contains("720")) return (1280, 720);
            if (Resolution.Contains("1920x1080") || Resolution.Contains("1080")) return (1920, 1080);
            return (defaultWidth, defaultHeight);
        }

        public double ParseFps(double defaultFps = 59.94) => ParseFpsString(Fps, defaultFps);

        public static double ParseFpsString(string fpsStr, double defaultFps = 59.94)
        {
            if (string.IsNullOrEmpty(fpsStr)) return defaultFps;
            if (fpsStr.Contains("59.94")) return 59.94;
            if (fpsStr.Contains("29.97")) return 29.97;
            if (fpsStr.Contains("23.976") || fpsStr.Contains("23.98")) return 23.98;
            if (fpsStr.Contains("24")) return 24.0;
            if (fpsStr.Contains("25")) return 25.0;
            if (fpsStr.Contains("30")) return 30.0;
            if (fpsStr.Contains("50")) return 50.0;
            if (fpsStr.Contains("60")) return 60.0;
            return defaultFps;
        }

        public bool IsInterlaced => Resolution.Contains("1080i") || Resolution.Contains("1440i") || Resolution.Contains("2160i") || Resolution.Contains("Interlaced");
    }

    /// <summary>
    /// Master Broadcast GPU Compositor & Production Playout Engine.
    /// Implements the standard broadcast processing pipeline:
    /// [Matrix Ingest 10 ISO + PGM] -> [DVE Multi-Box Compositor] -> [LipSync Frame Buffer] -> [Color Grading CCU/LUT] -> [CG / Logo Overlays] -> [Final Broadcast Target & Fail-Safe].
    /// </summary>
    public sealed class GpuMatrixCompositor : IDisposable
    {
        private readonly object _pipelineLock = new();
        private bool _isDisposed;
        private volatile bool _isRunning;

        // D3D11 Hardware Subsystem
        private ID3D11Device? _device;
        private ID3D11DeviceContext? _context;
        private ID3D11Texture2D?[] _stagingTextures = new ID3D11Texture2D?[MatrixChannelConstants.TotalMatrixChannels];
        private readonly int[] _stagingWidths = new int[MatrixChannelConstants.TotalMatrixChannels];
        private readonly int[] _stagingHeights = new int[MatrixChannelConstants.TotalMatrixChannels];
        private byte[]? _rescaleScratchBuffer;

        // IPC Ingest Subscriber
        private readonly MatrixRouterSubscriber _routerSubscriber = new();

        // Pipeline Sub-Engines
        public MultiCamDveCompositor DveCompositor { get; } = new();
        public LipSyncDelayEngine DelayEngine { get; } = new();
        public ColorGradingEngine ColorEngine { get; } = new();
        public BroadcastColorScopesEngine ScopesEngine { get; } = new();
        public BroadcastGraphicsEngine GraphicsEngine { get; } = new();
        public EmergencyFailSafeEngine FailSafeEngine { get; } = new();
        public AudioMeterService MeterService { get; } = new();

        // Working Canvas Buffers (BGRA32 1920x1080)
        private const int CanvasWidth = 1920;
        private const int CanvasHeight = 1080;
        private const int FrameBytes = CanvasWidth * CanvasHeight * 4;

        // Monitor Preview Proxy (960x540 qHD) for ultra-lightweight, zero-stutter WPF rendering
        public const int PreviewWidth = 960;
        public const int PreviewHeight = 540;
        public const int PreviewBytes = PreviewWidth * PreviewHeight * 4;

        private readonly byte[] _rawPgmBuffer = new byte[FrameBytes];
        private readonly byte[] _delayedBuffer = new byte[FrameBytes];
        private readonly byte[] _finalProgramBuffer = new byte[FrameBytes];
        private readonly byte[] _frontProgramBuffer = new byte[FrameBytes];
        private readonly object _masterOutLock = new object();

        // High-Performance Preview Proxy Buffers (Only 2MB per monitor instead of 8.3MB)
        private readonly byte[] _previewPgmBuffer = new byte[PreviewBytes];
        private readonly byte[] _previewMasterBuffer = new byte[PreviewBytes];
        private readonly byte[] _previewFallbackBars = new byte[PreviewBytes];

        // Scope Async Snapshot Buffer & Worker State
        private readonly byte[] _scopeSnapshotBuffer = new byte[FrameBytes];
        private volatile bool _isScopeRendering = false;
        private int _scopeRenderCounter = 0;

        // Cached Camera Frames from IPC Matrix
        private readonly byte[]?[] _cachedIsoFrames = new byte[]?[MatrixChannelConstants.TotalMatrixChannels];
        private readonly int[] _cachedWidths = new int[MatrixChannelConstants.TotalMatrixChannels];
        private readonly int[] _cachedHeights = new int[MatrixChannelConstants.TotalMatrixChannels];
        private readonly long[] _cachedFrameIds = new long[MatrixChannelConstants.TotalMatrixChannels];

        // Audio Subsystem
        private readonly byte[] _masterAudioBuffer = new byte[38400]; // ~200ms
        public AudioMeterLevels CurrentAudioLevels { get; private set; }

        // Selected Background Source for Matrix Ingest: 0 = PGM Master, 1..10 = ISO Cam 1..10
        public int SelectedIngestSource { get; set; } = 0;

        public const int TotalMatrixSlots = MatrixChannelConstants.TotalMatrixChannels;

        // 11 Playout Ports (Port 0 = PGM, Port 1..10 = Cam 1..10 ISO) mapped to 32 Matrix Slots
        private readonly int[] _portToSlotMap = new int[11];

        public void SetPortRoute(int playoutPort, int matrixSourceSlot)
        {
            if (playoutPort < 0 || playoutPort >= 11) return;
            _portToSlotMap[playoutPort] = Math.Clamp(matrixSourceSlot, 0, MatrixChannelConstants.TotalMatrixChannels - 1);
            Log("[ROUTER]", $"🔀 Gán cổng Playout {playoutPort} ➔ Matrix Slot {_portToSlotMap[playoutPort]}");
        }

        public int GetPortRoute(int playoutPort)
        {
            if (playoutPort < 0 || playoutPort >= 11) return playoutPort;
            return _portToSlotMap[playoutPort];
        }

        public System.Collections.Generic.List<(int slot, string name, int width, int height, double fps, bool isActive)> GetDiscoveredSources()
        {
            return _routerSubscriber.GetDiscoveredSources();
        }

        // Broadcast Network Out (NDI / SDI / SRT)
        private NdiNativeSender? _ndiMasterSender;
        public bool IsNdiMasterOutEnabled { get; private set; } = false;

        private readonly SdiNativeOutputWorker _sdiWorker = new();
        public SdiNativeOutputWorker SdiWorker => _sdiWorker;
        public bool IsSdiMasterOutEnabled => _sdiWorker.IsRunning;
        public MasterVideoCodecConfig MasterCodecConfig { get; } = new();

        // Engine Telemetry
        public double EngineFps { get; private set; } = 59.94;
        public long DroppedFrames { get; private set; } = 0;
        public long ProcessedFrames { get; private set; } = 0;
        public double VramUsageMb { get; private set; } = 142.5; // Estimated VRAM
        public bool IsRouterSyncActive => _routerSubscriber.IsPublisherActive();

        // WPF Presentation Bitmaps (Updatable from UI thread)
        public WriteableBitmap? PgmInBitmap { get; private set; }
        public WriteableBitmap? MasterOutBitmap { get; private set; }

        // Render Loop Worker
        private Thread? _engineThread;

        public event Action<string, string>? LogEmitted;

        public bool Initialize()
        {
            lock (_pipelineLock)
            {
                try
                {
                    // 1. Create Direct3D11 Hardware Device
                    var result = D3D11.D3D11CreateDevice(
                        adapter: null!,
                        DriverType.Hardware,
                        DeviceCreationFlags.BgraSupport,
                        featureLevels: new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 },
                        out _device,
                        out _context);

                    if (result.Failure || _device == null || _context == null)
                    {
                        D3D11.D3D11CreateDevice(
                            adapter: null!,
                            DriverType.Warp,
                            DeviceCreationFlags.BgraSupport,
                            featureLevels: new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 },
                            out _device,
                            out _context);
                    }

                    if (_device == null || _context == null)
                    {
                        Log("[ERROR]", "Không thể tạo D3D11 Device cho OME_PLAYOUT Engine.");
                        return false;
                    }

                    // 2. Connect to IP Video Matrix Router (Staging textures created dynamically per port)
                    _routerSubscriber.Connect(_device);
                    _routerSubscriber.ChannelAudioReceived += OnRouterAudioReceived;

                    _sdiWorker.LogEmitted += (tag, msg) => Log(tag, msg);

                    // Initialize default 1-to-1 Crosspoint Route Map (Port 0..10 -> Slot 0..10)
                    for (int i = 0; i < 11; i++) _portToSlotMap[i] = i;

                    // 4. Initialize Local Cached Frames
                    for (int i = 0; i < MatrixChannelConstants.TotalMatrixChannels; i++)
                    {
                        _cachedIsoFrames[i] = new byte[FrameBytes];
                        _cachedWidths[i] = CanvasWidth;
                        _cachedHeights[i] = CanvasHeight;
                    }

                    // Pre-generate 960x540 SMPTE Color Bars for instant, zero-CPU preview fallback
                    var fullBars = FailSafeEngine.GetSmpteColorBars(CanvasWidth, CanvasHeight);
                    unsafe
                    {
                        fixed (byte* pFull = fullBars)
                        fixed (byte* pPrev = _previewFallbackBars)
                        {
                            int* pSrc32 = (int*)pFull;
                            int* pDst32 = (int*)pPrev;
                            for (int y = 0; y < PreviewHeight; y++)
                            {
                                int* srcRow = pSrc32 + (y * 2) * CanvasWidth;
                                int* dstRow = pDst32 + y * PreviewWidth;
                                for (int x = 0; x < PreviewWidth; x++)
                                {
                                    dstRow[x] = srcRow[x * 2];
                                }
                            }
                        }
                    }

                    _isRunning = true;
                    _engineThread = new Thread(EnginePipelineLoop)
                    {
                        IsBackground = true,
                        Priority = ThreadPriority.Highest,
                        Name = "GpuMatrixCompositorPipeline"
                    };
                    _engineThread.Start();

                    Log("[PLAYOUT]", "✅ OME_PLAYOUT Master GPU Engine khởi chạy thành công (qHD Preview Mode).");
                    return true;
                }
                catch (Exception ex)
                {
                    Log("[ERROR]", $"Lỗi khởi tạo Playout Engine: {ex.Message}");
                    return false;
                }
            }
        }

        #region Engine Pipeline Processing Loop (60 FPS)

        private void EnginePipelineLoop()
        {
            long frameIntervalTicks = (long)(Stopwatch.Frequency / 59.94);
            long nextTick = Stopwatch.GetTimestamp();

            long fpsCalcTick = Stopwatch.GetTimestamp();
            int fpsCounter = 0;

            while (_isRunning && !_isDisposed)
            {
                try
                {
                    // Step 1: Ingest active video from Matrix Router
                    IngestMatrixFrames();

                    // Step 2: Assemble DVE Multi-Box Composition (No-op in 2-Monitor Mode)
                    AssembleDveCompositor();

                    // Step 3: Lip-Sync Delay Ring Buffer
                    ProcessLipSyncDelay();

                    // Step 4: Color Grading CCU & 3D LUT Shader
                    ProcessColorGrading();

                    // Step 5: Broadcast CG / Logo Overlays & Downsample for Preview
                    ProcessBroadcastGraphics();

                    // Step 6: Master Output & Fail-Safe Emergency Fallback
                    ProcessMasterOutput();

                    ProcessedFrames++;
                    fpsCounter++;

                    // Telemetry FPS calculation
                    long nowTick = Stopwatch.GetTimestamp();
                    double elapsedFpsSec = (double)(nowTick - fpsCalcTick) / Stopwatch.Frequency;
                    if (elapsedFpsSec >= 1.0)
                    {
                        EngineFps = Math.Round(fpsCounter / elapsedFpsSec, 2);
                        fpsCounter = 0;
                        fpsCalcTick = nowTick;
                    }

                    // Broadcast-grade microsecond precision frame cadence
                    nextTick += frameIntervalTicks;
                    long now = Stopwatch.GetTimestamp();
                    long remainingTicks = nextTick - now;

                    if (remainingTicks > 0)
                    {
                        // Sleep if more than 2ms to yield CPU
                        long yieldThresholdTicks = (long)(Stopwatch.Frequency * 0.002);
                        while (nextTick - Stopwatch.GetTimestamp() > yieldThresholdTicks)
                        {
                            Thread.Sleep(1);
                        }

                        // Spin-wait for remaining microseconds for rock-solid 59.94 / 60.0 FPS accuracy
                        while (Stopwatch.GetTimestamp() < nextTick)
                        {
                            Thread.SpinWait(20);
                        }
                    }
                    else if (-remainingTicks > frameIntervalTicks * 2)
                    {
                        // Drop frame recovery: catch up if pipeline fell behind
                        DroppedFrames++;
                        nextTick = Stopwatch.GetTimestamp();
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[GpuMatrixCompositor] Loop exception: {ex.Message}");
                }
            }
        }

        #endregion

        #region Pipeline Steps

        private void IngestMatrixFrames()
        {
            if (_device == null || _context == null) return;

            int pgmSlot = GetPortRoute(0);
            int onAirSlot;

            if (FailSafeEngine.IsFallbackActive && FailSafeEngine.FallbackMode == FailSafeFallbackMode.BackupIsoPort)
            {
                int backupPort = Math.Clamp(FailSafeEngine.BackupIsoPort, 1, 10);
                int backupSlot = GetPortRoute(backupPort);
                var backupInfo = _routerSubscriber.GetChannelInfo(backupSlot);
                bool isBackupAlive = backupInfo.IsActive == 1 && backupInfo.Width > 0 && (DateTime.UtcNow.Ticks - backupInfo.TimestampUtcTicks) < TimeSpan.FromSeconds(3).Ticks;

                onAirSlot = isBackupAlive ? backupSlot : pgmSlot;
            }
            else
            {
                int activePort = Math.Clamp(SelectedIngestSource, 0, 10);
                onAirSlot = GetPortRoute(activePort);
            }

            // Lightweight metadata polling for all 32 ports/slots to keep UI badges alive (zero GPU copy)
            for (int slot = 0; slot < MatrixChannelConstants.TotalMatrixChannels; slot++)
            {
                _routerSubscriber.RefreshChannel(slot);
            }

            // 1. Ingest ON-AIR channel into _rawPgmBuffer
            var sharedTex = _routerSubscriber.GetOrOpenTexture(onAirSlot);
            if (sharedTex != null)
            {
                var info = _routerSubscriber.GetChannelInfo(onAirSlot);
                if (info.FrameIndex != _cachedFrameIds[onAirSlot] && info.Width > 0 && info.Height > 0)
                {
                    try
                    {
                        // Ensure staging texture matches slot's dimensions
                        if (_stagingTextures[onAirSlot] == null || _stagingWidths[onAirSlot] != info.Width || _stagingHeights[onAirSlot] != info.Height)
                        {
                            _stagingTextures[onAirSlot]?.Dispose();
                            var stagingDesc = new Texture2DDescription
                            {
                                Width = (uint)info.Width,
                                Height = (uint)info.Height,
                                MipLevels = 1,
                                ArraySize = 1,
                                Format = Format.B8G8R8A8_UNorm,
                                SampleDescription = new SampleDescription(1, 0),
                                Usage = ResourceUsage.Staging,
                                BindFlags = BindFlags.None,
                                CPUAccessFlags = CpuAccessFlags.Read
                            };
                            _stagingTextures[onAirSlot] = _device.CreateTexture2D(stagingDesc);
                            _stagingWidths[onAirSlot] = info.Width;
                            _stagingHeights[onAirSlot] = info.Height;
                        }

                        var stagingTex = _stagingTextures[onAirSlot];
                        if (stagingTex != null)
                        {
                            _context.CopyResource(stagingTex, sharedTex);
                            var mapped = _context.Map(stagingTex, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                            if (mapped.DataPointer != IntPtr.Zero)
                            {
                                int rowPitch = (int)mapped.RowPitch;
                                int w = info.Width;
                                int h = info.Height;

                                if (w == CanvasWidth && h == CanvasHeight)
                                {
                                    unsafe
                                    {
                                        byte* pSrc = (byte*)mapped.DataPointer;
                                        fixed (byte* pDst = _rawPgmBuffer)
                                        fixed (byte* pPrev = _previewPgmBuffer)
                                        {
                                            for (int y = 0; y < h; y++)
                                            {
                                                Buffer.MemoryCopy(pSrc + y * rowPitch, pDst + y * CanvasWidth * 4, CanvasWidth * 4, w * 4);
                                            }

                                            // If on-air slot is also the PGM IN feed, downsample directly to preview
                                            if (onAirSlot == pgmSlot)
                                            {
                                                int* pSrc32 = (int*)pSrc;
                                                int* pDst32 = (int*)pPrev;
                                                int srcPitch32 = rowPitch / 4;

                                                for (int y = 0; y < PreviewHeight; y++)
                                                {
                                                    int* srcRow = pSrc32 + (y * 2) * srcPitch32;
                                                    int* dstRow = pDst32 + y * PreviewWidth;
                                                    for (int x = 0; x < PreviewWidth; x++)
                                                    {
                                                        dstRow[x] = srcRow[x * 2];
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    // Rescale to 1920x1080 canvas
                                    int srcBytes = w * h * 4;
                                    if (_rescaleScratchBuffer == null || _rescaleScratchBuffer.Length < srcBytes)
                                    {
                                        _rescaleScratchBuffer = new byte[srcBytes];
                                    }

                                    unsafe
                                    {
                                        byte* pSrc = (byte*)mapped.DataPointer;
                                        fixed (byte* pScratch = _rescaleScratchBuffer)
                                        {
                                            for (int y = 0; y < h; y++)
                                            {
                                                Buffer.MemoryCopy(pSrc + y * rowPitch, pScratch + y * w * 4, w * 4, w * 4);
                                            }
                                        }
                                    }

                                    MultiCamDveCompositor.BlitScaled(_rescaleScratchBuffer, w, h, _rawPgmBuffer, CanvasWidth, CanvasHeight, 0, 0, CanvasWidth, CanvasHeight);

                                    if (onAirSlot == pgmSlot)
                                    {
                                        unsafe
                                        {
                                            fixed (byte* pFull = _rawPgmBuffer)
                                            fixed (byte* pPrev = _previewPgmBuffer)
                                            {
                                                int* pSrc32 = (int*)pFull;
                                                int* pDst32 = (int*)pPrev;
                                                for (int y = 0; y < PreviewHeight; y++)
                                                {
                                                    int* srcRow = pSrc32 + (y * 2) * CanvasWidth;
                                                    int* dstRow = pDst32 + y * PreviewWidth;
                                                    for (int x = 0; x < PreviewWidth; x++)
                                                    {
                                                        dstRow[x] = srcRow[x * 2];
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }

                                _context.Unmap(stagingTex, 0);

                                _cachedWidths[onAirSlot] = w;
                                _cachedHeights[onAirSlot] = h;
                                _cachedFrameIds[onAirSlot] = info.FrameIndex;

                                if (onAirSlot == pgmSlot)
                                {
                                    FailSafeEngine.NotifySignalAlive();
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"[IngestMatrixFrames] Slot {onAirSlot} error: {ex.Message}");
                    }
                }
            }

            // 2. If on-air is NOT PGM IN (e.g. FailSafe Backup Port or ISO Camera is active),
            // independently ingest PGM IN (Port 0) so preview monitor shows live video and detects recovery!
            if (onAirSlot != pgmSlot)
            {
                IngestPgmPreviewOnly(pgmSlot);
            }
        }

        private void IngestPgmPreviewOnly(int pgmSlot)
        {
            var sharedTex = _routerSubscriber.GetOrOpenTexture(pgmSlot);
            if (sharedTex == null) return;

            var info = _routerSubscriber.GetChannelInfo(pgmSlot);
            if (info.FrameIndex == _cachedFrameIds[pgmSlot] || info.Width <= 0 || info.Height <= 0) return;

            try
            {
                if (_stagingTextures[pgmSlot] == null || _stagingWidths[pgmSlot] != info.Width || _stagingHeights[pgmSlot] != info.Height)
                {
                    _stagingTextures[pgmSlot]?.Dispose();
                    var stagingDesc = new Texture2DDescription
                    {
                        Width = (uint)info.Width,
                        Height = (uint)info.Height,
                        MipLevels = 1,
                        ArraySize = 1,
                        Format = Format.B8G8R8A8_UNorm,
                        SampleDescription = new SampleDescription(1, 0),
                        Usage = ResourceUsage.Staging,
                        BindFlags = BindFlags.None,
                        CPUAccessFlags = CpuAccessFlags.Read
                    };
                    _stagingTextures[pgmSlot] = _device!.CreateTexture2D(stagingDesc);
                    _stagingWidths[pgmSlot] = info.Width;
                    _stagingHeights[pgmSlot] = info.Height;
                }

                var stagingTex = _stagingTextures[pgmSlot];
                if (stagingTex != null)
                {
                    _context!.CopyResource(stagingTex, sharedTex);
                    var mapped = _context.Map(stagingTex, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                    if (mapped.DataPointer != IntPtr.Zero)
                    {
                        int rowPitch = (int)mapped.RowPitch;
                        int w = info.Width;
                        int h = info.Height;

                        unsafe
                        {
                            byte* pSrc = (byte*)mapped.DataPointer;
                            fixed (byte* pPrev = _previewPgmBuffer)
                            {
                                int* pSrc32 = (int*)pSrc;
                                int* pDst32 = (int*)pPrev;
                                int srcPitch32 = rowPitch / 4;

                                int stepY = Math.Max(1, h / PreviewHeight);
                                int stepX = Math.Max(1, w / PreviewWidth);

                                for (int y = 0; y < PreviewHeight; y++)
                                {
                                    int srcY = Math.Min(y * stepY, h - 1);
                                    int* srcRow = pSrc32 + srcY * srcPitch32;
                                    int* dstRow = pDst32 + y * PreviewWidth;
                                    for (int x = 0; x < PreviewWidth; x++)
                                    {
                                        int srcX = Math.Min(x * stepX, w - 1);
                                        dstRow[x] = srcRow[srcX];
                                    }
                                }
                            }
                        }

                        _context.Unmap(stagingTex, 0);

                        _cachedWidths[pgmSlot] = w;
                        _cachedHeights[pgmSlot] = h;
                        _cachedFrameIds[pgmSlot] = info.FrameIndex;

                        FailSafeEngine.NotifySignalAlive();
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[IngestPgmPreviewOnly] Error: {ex.Message}");
            }
        }

        private void AssembleDveCompositor()
        {
            // Direct pass-through - no extra DVE preview buffer needed
        }

        private void ProcessLipSyncDelay()
        {
            if (DelayEngine.VideoDelayMs <= 0.5)
            {
                // Zero delay: direct pass-through
                Buffer.BlockCopy(_rawPgmBuffer, 0, _delayedBuffer, 0, FrameBytes);
                return;
            }

            // Push active frame into Video Delay Engine
            DelayEngine.PushVideoFrame(_rawPgmBuffer, CanvasWidth, CanvasHeight);

            // Pull delayed frame
            if (!DelayEngine.TryGetDelayedVideoFrame(out byte[] delayedData, out int w, out int h))
            {
                Buffer.BlockCopy(_rawPgmBuffer, 0, _delayedBuffer, 0, FrameBytes);
            }
            else
            {
                Buffer.BlockCopy(delayedData, 0, _delayedBuffer, 0, FrameBytes);
            }
        }

        private void ProcessColorGrading()
        {
            // Apply CCU math: Gain * (In + Lift * (1 - In))^(1/Gamma) + Saturation + 3D LUT
            ColorEngine.ApplyToBgraBuffer(_delayedBuffer, CanvasWidth, CanvasHeight);

            // Asynchronously render broadcast scopes at ~30 FPS without stalling the 60 FPS video pipeline
            if ((++_scopeRenderCounter % 2) == 0 && !_isScopeRendering)
            {
                _isScopeRendering = true;
                Buffer.BlockCopy(_delayedBuffer, 0, _scopeSnapshotBuffer, 0, FrameBytes);
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        ScopesEngine.RenderScope(_scopeSnapshotBuffer, CanvasWidth, CanvasHeight);
                    }
                    catch { }
                    finally
                    {
                        _isScopeRendering = false;
                    }
                });
            }
        }

        private void ProcessBroadcastGraphics()
        {
            // Copy into final buffer and apply Graphics Overlays (Station Logo / Watermark)
            Buffer.BlockCopy(_delayedBuffer, 0, _finalProgramBuffer, 0, FrameBytes);
            GraphicsEngine.ApplyGraphics(_finalProgramBuffer, CanvasWidth, CanvasHeight);

            // Ultra-fast 2x downsample for smooth UI monitor preview (960x540)
            unsafe
            {
                fixed (byte* pFull = _finalProgramBuffer)
                fixed (byte* pPrev = _previewMasterBuffer)
                {
                    int* pSrc32 = (int*)pFull;
                    int* pDst32 = (int*)pPrev;
                    for (int y = 0; y < PreviewHeight; y++)
                    {
                        int* srcRow = pSrc32 + (y * 2) * CanvasWidth;
                        int* dstRow = pDst32 + y * PreviewWidth;
                        for (int x = 0; x < PreviewWidth; x++)
                        {
                            dstRow[x] = srcRow[x * 2];
                        }
                    }
                }
            }

            // Atomically publish fully rendered frame with logo for UI monitors & NDI
            lock (_masterOutLock)
            {
                Buffer.BlockCopy(_finalProgramBuffer, 0, _frontProgramBuffer, 0, FrameBytes);
            }
        }

        private void ProcessMasterOutput()
        {
            // Check Emergency Fail-Safe (Bảo vệ sóng)
            bool isSignalHealthy = FailSafeEngine.CheckSignalHealth();
            byte[] outputFrame = _finalProgramBuffer;

            if (!isSignalHealthy)
            {
                if (FailSafeEngine.FallbackMode == FailSafeFallbackMode.BackupIsoPort)
                {
                    // Lựa chọn 2: Tự động chuyển sang Cổng ISO Dự Phòng
                    int backupPort = Math.Clamp(FailSafeEngine.BackupIsoPort, 1, 10);
                    int backupSlot = GetPortRoute(backupPort);
                    var backupInfo = _routerSubscriber.GetChannelInfo(backupSlot);
                    bool isBackupAlive = backupInfo.IsActive == 1 && backupInfo.Width > 0 && backupInfo.Height > 0 && (DateTime.UtcNow.Ticks - backupInfo.TimestampUtcTicks) < TimeSpan.FromSeconds(3).Ticks;

                    if (isBackupAlive)
                    {
                        // _finalProgramBuffer was ingested from backupSlot in IngestMatrixFrames
                        outputFrame = _finalProgramBuffer;
                    }
                    else
                    {
                        // Cổng backup cũng mất tín hiệu: rơi vào chốt chặn cuối cùng SMPTE Bars
                        outputFrame = FailSafeEngine.GetSmpteColorBars(CanvasWidth, CanvasHeight);
                    }
                }
                else
                {
                    // Lựa chọn 1: Bảng màu chuẩn SMPTE Color Bars
                    outputFrame = FailSafeEngine.GetSmpteColorBars(CanvasWidth, CanvasHeight);
                }
            }

            // Feed Master NDI Out if enabled
            if (IsNdiMasterOutEnabled && _ndiMasterSender != null)
            {
                _ndiMasterSender.FeedVideo(outputFrame, CanvasWidth, CanvasHeight, 59.94);
            }

            // Feed Master SDI Hardware Out if enabled
            if (IsSdiMasterOutEnabled)
            {
                _sdiWorker.FeedVideo(outputFrame, CanvasWidth, CanvasHeight);
            }
        }

        #endregion

        #region Audio IPC Event & Metering

        private void OnRouterAudioReceived(int slot, byte[] pcmBytes, int length)
        {
            if (pcmBytes == null || length <= 0) return;

            int onAirPort;
            if (FailSafeEngine.IsFallbackActive && FailSafeEngine.FallbackMode == FailSafeFallbackMode.BackupIsoPort)
            {
                onAirPort = Math.Clamp(FailSafeEngine.BackupIsoPort, 1, 10);
            }
            else
            {
                onAirPort = Math.Clamp(SelectedIngestSource, 0, 10);
            }

            int onAirSlot = GetPortRoute(onAirPort);

            if (slot == onAirSlot)
            {
                DelayEngine.PushAudioPcm(pcmBytes, length);

                byte[] delayedAudio = new byte[length];
                if (DelayEngine.TryGetDelayedAudioPcm(delayedAudio, length))
                {
                    CurrentAudioLevels = MeterService.ProcessPcm(delayedAudio, length);

                    // Feed delayed audio to NDI
                    if (IsNdiMasterOutEnabled && _ndiMasterSender != null)
                    {
                        _ndiMasterSender.FeedAudio(delayedAudio, length, 48000, 2);
                    }

                    // Feed delayed audio to SDI
                    if (IsSdiMasterOutEnabled)
                    {
                        _sdiWorker.FeedAudio(delayedAudio, length, 48000, 2);
                    }
                }
                else
                {
                    CurrentAudioLevels = MeterService.ProcessPcm(pcmBytes, length);
                }
            }
        }

        #endregion

        #region Presentation to WPF Monitors (UI Thread)

        /// <summary>
        /// Updates the 2 WPF WriteableBitmap monitors (PGM In & Master Out) ultra-lightweight at 60 FPS using 960x540 qHD proxies.
        /// </summary>
        public void UpdateWpfMonitors(
            ref WriteableBitmap? pgmBmp,
            ref WriteableBitmap? masterBmp)
        {
            EnsurePreviewBitmap(ref pgmBmp);
            EnsurePreviewBitmap(ref masterBmp);

            int stride = PreviewWidth * 4;
            var rect = new Int32Rect(0, 0, PreviewWidth, PreviewHeight);

            // PGM IN (Router Feed) monitor always receives the live PGM IN (Port 0) feed
            pgmBmp!.WritePixels(rect, _previewPgmBuffer, stride, 0);

            byte[] finalToShow;
            if (FailSafeEngine.IsFallbackActive)
            {
                if (FailSafeEngine.FallbackMode == FailSafeFallbackMode.BackupIsoPort)
                {
                    int backupPort = Math.Clamp(FailSafeEngine.BackupIsoPort, 1, 10);
                    int backupSlot = GetPortRoute(backupPort);
                    var backupInfo = _routerSubscriber.GetChannelInfo(backupSlot);
                    bool isBackupAlive = backupInfo.IsActive == 1 && backupInfo.Width > 0 && (DateTime.UtcNow.Ticks - backupInfo.TimestampUtcTicks) < TimeSpan.FromSeconds(3).Ticks;

                    if (isBackupAlive)
                    {
                        lock (_masterOutLock)
                        {
                            finalToShow = _previewMasterBuffer;
                        }
                    }
                    else
                    {
                        finalToShow = _previewFallbackBars;
                    }
                }
                else
                {
                    finalToShow = _previewFallbackBars;
                }
            }
            else
            {
                lock (_masterOutLock)
                {
                    finalToShow = _previewMasterBuffer;
                }
            }
            masterBmp!.WritePixels(rect, finalToShow, stride, 0);
        }

        private static void EnsurePreviewBitmap(ref WriteableBitmap? bmp)
        {
            if (bmp == null || bmp.PixelWidth != PreviewWidth || bmp.PixelHeight != PreviewHeight)
            {
                bmp = new WriteableBitmap(PreviewWidth, PreviewHeight, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);
            }
        }

        #endregion

        #region Broadcast Out Controls

        public bool ToggleMasterNdi(bool enable)
        {
            lock (_pipelineLock)
            {
                IsNdiMasterOutEnabled = enable;
                if (enable)
                {
                    _ndiMasterSender?.Dispose();
                    _ndiMasterSender = new NdiNativeSender("OME MASTER BROADCAST");
                    bool started = _ndiMasterSender.Start();
                    Log("[NDI]", started ? "✅ NDI Master Out phát sóng: OME MASTER BROADCAST" : "❌ Lỗi khởi động NDI Master Out");
                    return started;
                }
                else
                {
                    _ndiMasterSender?.Stop();
                    _ndiMasterSender?.Dispose();
                    _ndiMasterSender = null;
                    Log("[NDI]", "Đã dừng NDI Master Out.");
                    return true;
                }
            }
        }

        public bool ToggleMasterSdi(bool enable, string device, string mode, double? customFps = null)
        {
            lock (_pipelineLock)
            {
                if (enable)
                {
                    var (w, h) = MasterCodecConfig.ParseResolution(CanvasWidth, CanvasHeight);
                    double fps = customFps ?? MasterCodecConfig.ParseFps(59.94);

                    if (!string.IsNullOrEmpty(mode))
                    {
                        if (mode.Contains("2160") || mode.Contains("4K")) { w = 3840; h = 2160; }
                        else if (mode.Contains("1440") || mode.Contains("2K")) { w = 2560; h = 1440; }
                        else if (mode.Contains("1080")) { w = 1920; h = 1080; }
                        else if (mode.Contains("720")) { w = 1280; h = 720; }

                        if (customFps == null)
                        {
                            if (mode.Contains("59.94")) fps = 59.94;
                            else if (mode.Contains("29.97")) fps = 29.97;
                            else if (mode.Contains("25")) fps = 25.0;
                            else if (mode.Contains("50")) fps = 50.0;
                            else if (mode.Contains("60")) fps = 60.0;
                            else if (mode.Contains("30")) fps = 30.0;
                            else if (mode.Contains("24")) fps = 24.0;
                        }
                    }

                    bool started = _sdiWorker.Start(device, mode, w, h, fps, MasterCodecConfig.Codec);
                    Log("[SDI]", started ? $"✅ Bật phát sóng SDI: {device} ({mode} @ {fps:F2} fps, {w}x{h})" : $"❌ Không thể mở thiết bị SDI: {device}");
                    return started;
                }
                else
                {
                    _sdiWorker.Stop();
                    Log("[SDI]", "Đã tắt phát sóng SDI.");
                    return true;
                }
            }
        }

        #endregion

        private void Log(string tag, string msg)
        {
            LogEmitted?.Invoke(tag, msg);
            Trace.WriteLine($"[GpuMatrixCompositor]{tag} {msg}");
        }

        public (bool isActive, int width, int height, double fps, long frameIndex, string sourceName) GetChannelStatus(int port)
        {
            int slot = GetPortRoute(port);
            var info = _routerSubscriber.GetChannelInfo(slot);
            bool active = info.IsActive == 1 && info.Width > 0 && info.Height > 0 && info.FrameIndex > 0 && (DateTime.UtcNow.Ticks - info.TimestampUtcTicks) < TimeSpan.FromSeconds(3).Ticks;
            string name = !string.IsNullOrWhiteSpace(info.SourceName) ? info.SourceName : (slot == 0 ? "PGM Master" : $"IP {slot}");
            return (active, info.Width, info.Height, info.Fps, info.FrameIndex, name);
        }

        public void Dispose()
        {
            lock (_pipelineLock)
            {
                if (_isDisposed) return;
                _isDisposed = true;
                _isRunning = false;

                _engineThread?.Join(300);

                ToggleMasterNdi(false);
                _sdiWorker.Dispose();

                _routerSubscriber.Dispose();
                DelayEngine.Dispose();
                for (int i = 0; i < _stagingTextures.Length; i++)
                {
                    _stagingTextures[i]?.Dispose();
                    _stagingTextures[i] = null;
                }
                _context?.Dispose();
                _device?.Dispose();
            }
        }
    }
}
