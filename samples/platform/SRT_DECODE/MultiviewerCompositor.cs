using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SRT_DECODE
{
    /// <summary>
    /// Professional Broadcast Multiviewer Compositor.
    /// Real-time GPU/CPU compositing engine generating rock-solid 1080p59.94 BGRA frames
    /// with automatic 2x2 (1-4 cams), 3x3 (5-9 cams), and 4x3 (10 cams) layouts,
    /// dynamic On-Air Tally borders (PGM Red, PVW Green), and broadcast OSD label badges.
    /// </summary>
    public sealed class MultiviewerCompositor : IDisposable
    {
        public const int OutputWidth = 1920;
        public const int OutputHeight = 1080;
        public const int OutputStride = OutputWidth * 4; // 7680 bytes
        public const int MaxChannels = 10;

        // Double-buffered canvas buffers
        private readonly byte[] _canvas = new byte[OutputStride * OutputHeight];

        // Channel raw frame buffers & metadata
        private sealed class ChannelSlot
        {
            public readonly object Lock = new();
            public byte[]? Buffer;
            public int Width;
            public int Height;
            public long LastUpdateTicks;
            public long FrameSequenceId; // Đếm số frame thực sự nhận được
        }

        private readonly ChannelSlot[] _slots = new ChannelSlot[MaxChannels];
        private readonly long[] _lastComposedSequenceId = new long[MaxChannels];
        private int _lastLayoutCols = -1;
        private int _lastLayoutRows = -1;
        private int _lastActiveCount = -1;

        // Pre-rendered OSD badges (Width: 160, Height: 28)
        private const int BadgeWidth = 160;
        private const int BadgeHeight = 28;
        private readonly byte[]?[] _badgeNormal = new byte[MaxChannels][];
        private readonly byte[]?[] _badgePgm = new byte[MaxChannels][];
        private readonly byte[]?[] _badgePvw = new byte[MaxChannels][];

        // Pre-rendered standby tiles (Width: 640, Height: 360)
        private const int StandbyTileW = 640;
        private const int StandbyTileH = 360;
        private readonly byte[]?[] _standbyTiles = new byte[MaxChannels][];

        // Compositing state
        private int _activeChannelCount = 1;
        private string[] _channelNames = new string[MaxChannels];
        private int _currentPgmIndex = 0;
        private int _currentPvwIndex = 1;

        // Dedicated compositor thread
        private Thread? _thread;
        private CancellationTokenSource? _cts;
        private Action<byte[], int, int, double>? _onFrameComposed;
        private bool _isRunning;
        private readonly object _stateLock = new();

        public bool IsRunning => _isRunning;

        public MultiviewerCompositor()
        {
            Array.Fill(_lastComposedSequenceId, -1L);
            for (int i = 0; i < MaxChannels; i++)
            {
                _slots[i] = new ChannelSlot();
                _channelNames[i] = $"CAM {i + 1}";
            }
        }

        #region Pre-rendering Badges & Standby Tiles (STA Thread)

        /// <summary>
        /// Updates layout parameters, active channels, names, and tally states.
        /// Regenerates crisp broadcast typography badges and standby graphics.
        /// </summary>
        public void UpdateConfig(int activeChannelCount, string[]? channelNames, int pgmIndex, int pvwIndex)
        {
            lock (_stateLock)
            {
                _activeChannelCount = Math.Clamp(activeChannelCount, 1, MaxChannels);
                _currentPgmIndex = pgmIndex;
                _currentPvwIndex = pvwIndex;

                if (channelNames != null)
                {
                    for (int i = 0; i < MaxChannels && i < channelNames.Length; i++)
                    {
                        if (!string.IsNullOrWhiteSpace(channelNames[i]))
                        {
                            _channelNames[i] = channelNames[i];
                        }
                    }
                }
            }

            // Generate crisp vector badges on STA thread if dispatcher available
            try
            {
                if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
                {
                    Application.Current.Dispatcher.Invoke(() => RebuildBadgesInternal());
                }
                else
                {
                    RebuildBadgesInternal();
                }
            }
            catch { }
        }

        private void RebuildBadgesInternal()
        {
            for (int i = 0; i < MaxChannels; i++)
            {
                string name = _channelNames[i];

                _badgeNormal[i] = RenderBadgeBitmap("LIVE", name, Color.FromArgb(0xDC, 0x1E, 0x1E, 0x24), BadgeWidth, BadgeHeight);
                _badgePgm[i] = RenderBadgeBitmap("PGM", name, Color.FromArgb(0xF2, 0xDC, 0x26, 0x26), BadgeWidth, BadgeHeight);
                _badgePvw[i] = RenderBadgeBitmap("PVW", name, Color.FromArgb(0xF2, 0x16, 0xA3, 0x4A), BadgeWidth, BadgeHeight);

                _standbyTiles[i] = RenderStandbyBitmap(name, StandbyTileW, StandbyTileH);
            }
        }

        private static byte[] RenderBadgeBitmap(string tallyTag, string camName, Color bgColor, int width, int height)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                // Background rounded card
                dc.DrawRoundedRectangle(new SolidColorBrush(bgColor), new Pen(new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)), 1), new Rect(0.5, 0.5, width - 1, height - 1), 3.5, 3.5);

                // Tally Tag Pill
                var tallyBrush = tallyTag == "PGM" ? new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)) :
                                 tallyTag == "PVW" ? new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)) :
                                                     new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8));

                var tallyText = new FormattedText(
                    tallyTag,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                    10.5,
                    tallyBrush,
                    96);

                dc.DrawText(tallyText, new Point(8, (height - tallyText.Height) / 2));

                // Separator Dot
                double dotX = 8 + tallyText.Width + 6;
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), null, new Point(dotX, height / 2.0), 1.5, 1.5);

                // Camera Name
                var nameText = new FormattedText(
                    camName,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                    11,
                    Brushes.White,
                    96);

                dc.DrawText(nameText, new Point(dotX + 6, (height - nameText.Height) / 2));
            }

            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);

            int stride = width * 4;
            byte[] pixels = new byte[stride * height];
            rtb.CopyPixels(pixels, stride, 0);
            return pixels;
        }

        private static byte[] RenderStandbyBitmap(string camName, int width, int height)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                // Dark background
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x0E, 0x0E, 0x12)), null, new Rect(0, 0, width, height));

                // Title
                var titleText = new FormattedText(
                    $"{camName} • STANDBY",
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                    17,
                    new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x66)),
                    96);
                titleText.TextAlignment = TextAlignment.Center;
                dc.DrawText(titleText, new Point(width / 2.0, height / 2.0 - 16));

                // Subtitle
                var subText = new FormattedText(
                    "WAITING FOR SRT INGEST FEED",
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                    10.5,
                    new SolidColorBrush(Color.FromRgb(0x3B, 0x3B, 0x48)),
                    96);
                subText.TextAlignment = TextAlignment.Center;
                dc.DrawText(subText, new Point(width / 2.0, height / 2.0 + 8));
            }

            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);

            int stride = width * 4;
            byte[] pixels = new byte[stride * height];
            rtb.CopyPixels(pixels, stride, 0);
            return pixels;
        }

        #endregion

        #region Ingestion Pipeline

        /// <summary>
        /// Ingests incoming camera video frame into the compositor slot cache.
        /// Thread-safe and allocation-free for steady 60fps operation.
        /// </summary>
        public void UpdateChannelFrame(int channelIndex, byte[] bgraBytes, int width, int height)
        {
            if (!_isRunning || channelIndex < 0 || channelIndex >= MaxChannels || bgraBytes == null || bgraBytes.Length == 0) return;

            var slot = _slots[channelIndex];
            lock (slot.Lock)
            {
                int reqSize = width * height * 4;
                if (slot.Buffer == null || slot.Buffer.Length < reqSize)
                {
                    slot.Buffer = new byte[reqSize];
                }

                Buffer.BlockCopy(bgraBytes, 0, slot.Buffer, 0, Math.Min(bgraBytes.Length, reqSize));
                slot.Width = width;
                slot.Height = height;
                slot.LastUpdateTicks = Stopwatch.GetTimestamp();
                slot.FrameSequenceId++;
            }
        }

        #endregion

        #region Compositor Thread & Loop

        /// <summary>
        /// Starts the high-precision background compositing loop.
        /// Outputs composited 1080p frames at target framerate (default 59.94 fps).
        /// </summary>
        public void Start(Action<byte[], int, int, double> onFrameComposed, double fps = 59.94)
        {
            lock (_stateLock)
            {
                if (_isRunning) return;

                _onFrameComposed = onFrameComposed;
                _cts = new CancellationTokenSource();
                _isRunning = true;

                // Ensure badges are created if not already
                if (_badgePgm[0] == null)
                {
                    try
                    {
                        if (Application.Current?.Dispatcher != null)
                        {
                            Application.Current.Dispatcher.Invoke(() => RebuildBadgesInternal());
                        }
                    }
                    catch { }
                }

                _thread = new Thread(() => RunCompositorLoop(fps, _cts.Token))
                {
                    Name = "BroadcastMultiviewerCompositor",
                    IsBackground = true,
                    Priority = ThreadPriority.AboveNormal
                };
                _thread.Start();
            }
        }

        /// <summary>
        /// Stops the background compositing loop.
        /// </summary>
        public void Stop()
        {
            lock (_stateLock)
            {
                if (!_isRunning) return;
                _isRunning = false;

                try
                {
                    _cts?.Cancel();
                    _thread?.Join(200);
                }
                catch { }
                finally
                {
                    _cts?.Dispose();
                    _cts = null;
                    _thread = null;
                    _onFrameComposed = null;
                }
            }
        }

        private void RunCompositorLoop(double fps, CancellationToken token)
        {
            long ticksPerFrame = (long)(Stopwatch.Frequency / fps);
            long nextFrameTick = Stopwatch.GetTimestamp();

            while (!token.IsCancellationRequested)
            {
                try
                {
                    ComposeFrame();

                    _onFrameComposed?.Invoke(_canvas, OutputWidth, OutputHeight, fps);

                    nextFrameTick += ticksPerFrame;
                    long currentTick = Stopwatch.GetTimestamp();
                    long waitTicks = nextFrameTick - currentTick;

                    if (waitTicks > 0)
                    {
                        int waitMs = (int)((waitTicks * 1000) / Stopwatch.Frequency);
                        if (waitMs > 1)
                        {
                            Thread.Sleep(waitMs - 1);
                        }
                        while (Stopwatch.GetTimestamp() < nextFrameTick)
                        {
                            Thread.SpinWait(40);
                        }
                    }
                    else
                    {
                        // Resync clock if lag exceeds 2 frames
                        if (currentTick - nextFrameTick > ticksPerFrame * 2)
                        {
                            nextFrameTick = currentTick;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch { }
            }
        }

        #endregion

        #region Fast Unsafe Grid Compositing

        private unsafe void ComposeFrame()
        {
            int activeCount;
            int pgmIdx;
            int pvwIdx;

            lock (_stateLock)
            {
                activeCount = _activeChannelCount;
                pgmIdx = _currentPgmIndex;
                pvwIdx = _currentPvwIndex;
            }

            // Determine Grid Geometry:
            // 1 - 4 cams: 2x2
            // 5 - 9 cams: 3x3
            // 10 cams: 4x3 (12 slots)
            int cols, rows;
            if (activeCount <= 4)
            {
                cols = 2;
                rows = 2;
            }
            else if (activeCount <= 9)
            {
                cols = 3;
                rows = 3;
            }
            else
            {
                cols = 4;
                rows = 3;
            }

            int cellW = OutputWidth / cols;
            int cellH = OutputHeight / rows;
            int totalSlots = cols * rows;

            bool layoutChanged = (cols != _lastLayoutCols || rows != _lastLayoutRows || activeCount != _lastActiveCount);
            if (layoutChanged)
            {
                _lastLayoutCols = cols;
                _lastLayoutRows = rows;
                _lastActiveCount = activeCount;
                Array.Fill(_lastComposedSequenceId, -1L);
            }

            fixed (byte* pDst = _canvas)
            {
                // Chỉ xóa toàn bộ canvas thành đen khi layout thay đổi hoặc khởi động
                if (layoutChanged)
                {
                    FillRect(pDst, OutputWidth, OutputHeight, OutputStride, 0, 0, OutputWidth, OutputHeight, 0xFF070709);
                }

                for (int slot = 0; slot < totalSlots; slot++)
                {
                    int r = slot / cols;
                    int c = slot % cols;

                    int x = c * cellW;
                    int y = r * cellH;

                    // Inner video box margins (4px border gap)
                    const int margin = 3;
                    int boxX = x + margin;
                    int boxY = y + margin;
                    int boxW = cellW - (margin * 2);
                    int boxH = cellH - (margin * 2);

                    if (slot < activeCount)
                    {
                        int chIdx = slot;
                        var chSlot = _slots[chIdx];

                        bool hasLiveFrame = false;
                        byte[]? frameBuf = null;
                        int srcW = 0;
                        int srcH = 0;
                        long currentSeqId = 0;

                        lock (chSlot.Lock)
                        {
                            if (chSlot.Buffer != null && chSlot.Width > 0 && chSlot.Height > 0)
                            {
                                // Check if frame is fresh (< 2.5 seconds old)
                                long ageTicks = Stopwatch.GetTimestamp() - chSlot.LastUpdateTicks;
                                if ((ageTicks * 1000) / Stopwatch.Frequency < 2500)
                                {
                                    hasLiveFrame = true;
                                    frameBuf = chSlot.Buffer;
                                    srcW = chSlot.Width;
                                    srcH = chSlot.Height;
                                    currentSeqId = chSlot.FrameSequenceId;
                                }
                            }
                        }

                        // Chỉ re-blit khi frame thực sự mới (giảm judder khi nguồn 25fps)
                        if (hasLiveFrame && frameBuf != null)
                        {
                            if (currentSeqId != _lastComposedSequenceId[chIdx])
                            {
                                _lastComposedSequenceId[chIdx] = currentSeqId;
                                fixed (byte* pSrc = frameBuf)
                                {
                                    ScaleBlit(pSrc, srcW, srcH, srcW * 4, pDst, OutputWidth, OutputHeight, OutputStride, boxX, boxY, boxW, boxH);
                                }
                            }
                        }
                        else
                        {
                            _lastComposedSequenceId[chIdx] = -1L;
                            // Blit standby placeholder tile
                            var standby = _standbyTiles[chIdx];
                            if (standby != null)
                            {
                                fixed (byte* pSb = standby)
                                {
                                    ScaleBlit(pSb, StandbyTileW, StandbyTileH, StandbyTileW * 4, pDst, OutputWidth, OutputHeight, OutputStride, boxX, boxY, boxW, boxH);
                                }
                            }
                            else
                            {
                                FillRect(pDst, OutputWidth, OutputHeight, OutputStride, boxX, boxY, boxW, boxH, 0xFF101015);
                            }
                        }

                        // 2. Draw Tally Border
                        bool isPgm = (chIdx == pgmIdx);
                        bool isPvw = (chIdx == pvwIdx);

                        if (isPgm)
                        {
                            // Red PGM Border (4px thickness)
                            DrawRectBorder(pDst, OutputWidth, OutputHeight, OutputStride, boxX, boxY, boxW, boxH, 0xFFDC2626, 4);
                        }
                        else if (isPvw)
                        {
                            // Green PVW Border (3px thickness)
                            DrawRectBorder(pDst, OutputWidth, OutputHeight, OutputStride, boxX, boxY, boxW, boxH, 0xFF16A34A, 3);
                        }
                        else
                        {
                            // Neutral dark border (1px)
                            DrawRectBorder(pDst, OutputWidth, OutputHeight, OutputStride, boxX, boxY, boxW, boxH, 0xFF2A2A35, 1);
                        }

                        // 3. Stamp Pre-rendered Broadcast Badge
                        byte[]? badge = isPgm ? _badgePgm[chIdx] : (isPvw ? _badgePvw[chIdx] : _badgeNormal[chIdx]);
                        if (badge != null)
                        {
                            StampBadge(pDst, OutputWidth, OutputHeight, OutputStride, boxX + 6, boxY + 6, badge, BadgeWidth, BadgeHeight);
                        }
                    }
                    else
                    {
                        // Inactive empty slot
                        FillRect(pDst, OutputWidth, OutputHeight, OutputStride, boxX, boxY, boxW, boxH, 0xFF0A0A0E);
                        DrawRectBorder(pDst, OutputWidth, OutputHeight, OutputStride, boxX, boxY, boxW, boxH, 0xFF181822, 1);
                    }
                }
            }
        }

        private static unsafe void ScaleBlit(
            byte* pSrc, int srcW, int srcH, int srcStride,
            byte* pDst, int dstW, int dstH, int dstStride,
            int dx, int dy, int dw, int dh)
        {
            if (srcW <= 0 || srcH <= 0 || dw <= 0 || dh <= 0) return;

            int xStep = (srcW << 16) / dw;
            int yStep = (srcH << 16) / dh;

            for (int r = 0; r < dh; r++)
            {
                int targetY = dy + r;
                if (targetY < 0 || targetY >= dstH) continue;

                int srcY = (r * yStep) >> 16;
                if (srcY >= srcH) srcY = srcH - 1;

                uint* pSrcRow = (uint*)(pSrc + srcY * srcStride);
                uint* pDstRow = (uint*)(pDst + targetY * dstStride) + dx;

                for (int c = 0; c < dw; c++)
                {
                    if (dx + c >= dstW) break;
                    int srcX = (c * xStep) >> 16;
                    if (srcX >= srcW) srcX = srcW - 1;
                    pDstRow[c] = pSrcRow[srcX];
                }
            }
        }

        private static unsafe void DrawRectBorder(
            byte* pDst, int dstW, int dstH, int dstStride,
            int x, int y, int w, int h, uint colorBgra, int thickness)
        {
            for (int t = 0; t < thickness; t++)
            {
                int left = x + t;
                int right = x + w - 1 - t;
                int top = y + t;
                int bottom = y + h - 1 - t;

                if (left >= right || top >= bottom) break;

                // Top & Bottom lines
                if (top >= 0 && top < dstH)
                {
                    uint* pTop = (uint*)(pDst + top * dstStride);
                    for (int c = left; c <= right; c++)
                        if (c >= 0 && c < dstW) pTop[c] = colorBgra;
                }
                if (bottom >= 0 && bottom < dstH)
                {
                    uint* pBottom = (uint*)(pDst + bottom * dstStride);
                    for (int c = left; c <= right; c++)
                        if (c >= 0 && c < dstW) pBottom[c] = colorBgra;
                }

                // Left & Right lines
                for (int r = top; r <= bottom; r++)
                {
                    if (r >= 0 && r < dstH)
                    {
                        uint* pRow = (uint*)(pDst + r * dstStride);
                        if (left >= 0 && left < dstW) pRow[left] = colorBgra;
                        if (right >= 0 && right < dstW) pRow[right] = colorBgra;
                    }
                }
            }
        }

        private static unsafe void FillRect(
            byte* pDst, int dstW, int dstH, int dstStride,
            int x, int y, int w, int h, uint colorBgra)
        {
            for (int r = 0; r < h; r++)
            {
                int curY = y + r;
                if (curY < 0 || curY >= dstH) continue;
                uint* pRow = (uint*)(pDst + curY * dstStride) + x;
                for (int c = 0; c < w; c++)
                {
                    if (x + c >= dstW) break;
                    pRow[c] = colorBgra;
                }
            }
        }

        private static unsafe void StampBadge(
            byte* pDst, int dstW, int dstH, int dstStride,
            int x, int y, byte[] badge, int badgeW, int badgeH)
        {
            fixed (byte* pSrc = badge)
            {
                for (int r = 0; r < badgeH; r++)
                {
                    int dy = y + r;
                    if (dy < 0 || dy >= dstH) continue;

                    uint* pDstRow = (uint*)(pDst + dy * dstStride) + x;
                    uint* pSrcRow = (uint*)(pSrc + r * (badgeW * 4));

                    for (int c = 0; c < badgeW; c++)
                    {
                        if (x + c >= dstW) break;

                        uint srcPix = pSrcRow[c];
                        uint alpha = (srcPix >> 24) & 0xFF;

                        if (alpha >= 250)
                        {
                            pDstRow[c] = srcPix;
                        }
                        else if (alpha > 0)
                        {
                            // Fast alpha blend
                            uint dstPix = pDstRow[c];
                            uint invA = 255 - alpha;

                            uint rSrc = (srcPix >> 16) & 0xFF;
                            uint gSrc = (srcPix >> 8) & 0xFF;
                            uint bSrc = srcPix & 0xFF;

                            uint rDst = (dstPix >> 16) & 0xFF;
                            uint gDst = (dstPix >> 8) & 0xFF;
                            uint bDst = dstPix & 0xFF;

                            uint rOut = (rSrc * alpha + rDst * invA) / 255;
                            uint gOut = (gSrc * alpha + gDst * invA) / 255;
                            uint bOut = (bSrc * alpha + bDst * invA) / 255;

                            pDstRow[c] = (0xFFu << 24) | (rOut << 16) | (gOut << 8) | bOut;
                        }
                    }
                }
            }
        }

        #endregion

        public void Dispose()
        {
            Stop();
        }
    }
}
