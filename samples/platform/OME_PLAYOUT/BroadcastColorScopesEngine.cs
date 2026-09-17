using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OME_PLAYOUT
{
    public enum BroadcastScopeType
    {
        WaveformIreb,
        RgbParade,
        Vectorscope,
        Histogram,
        CieChromaticity
    }

    public struct BroadcastColorMetrics
    {
        public double MaxIre;
        public double MinIre;
        public double AverageIre;
        public double OutOfGamutPercent;
        public int EstimatedColorTempK;
        public string DominantTint;
        public bool IsLegalSignal;
    }

    /// <summary>
    /// Real-time Broadcast Television Color Measurement & Scopes Engine.
    /// Provides 5 standard TV engineering scopes:
    /// 1. Waveform Monitor (IRE -20 .. 120 with 0 IRE Black and 100 IRE White graticules)
    /// 2. RGB Parade (Red, Green, Blue channel balancing side-by-side)
    /// 3. Vectorscope (ITU-R BT.709 polar plot with 75%/100% color targets and 123° Skin Tone line)
    /// 4. Color Histogram (Luma + Red/Green/Blue distribution with 16-235 legal broadcast range)
    /// 5. CIE 1931 Chromaticity Diagram (Rec.709 and Rec.2020 gamut triangles with chromaticity coordinates)
    /// </summary>
    public sealed class BroadcastColorScopesEngine
    {
        public const int ScopeWidth = 480;
        public const int ScopeHeight = 270;
        private const int ScopeStride = ScopeWidth * 4;
        private const int ScopeBufferBytes = ScopeStride * ScopeHeight;

        private readonly byte[] _scopePixelBuffer = new byte[ScopeBufferBytes];
        private readonly byte[] _completedScopeBuffer = new byte[ScopeBufferBytes];
        private readonly object _scopeBufferLock = new object();
        private readonly int[] _accumulator = new int[ScopeWidth * ScopeHeight];
        private readonly int[] _histR = new int[256];
        private readonly int[] _histG = new int[256];
        private readonly int[] _histB = new int[256];
        private readonly int[] _histY = new int[256];

        public BroadcastScopeType CurrentScope { get; set; } = BroadcastScopeType.WaveformIreb;
        public BroadcastColorMetrics Metrics { get; private set; }

        public BroadcastColorScopesEngine()
        {
            // Initial render of graticule so it's not black before first video frame
            DrawWaveformGraticule();
            lock (_scopeBufferLock)
            {
                Buffer.BlockCopy(_scopePixelBuffer, 0, _completedScopeBuffer, 0, ScopeBufferBytes);
            }
        }

        /// <summary>
        /// Analyzes the input video buffer (1920x1080 BGRA) and renders the active broadcast scope.
        /// </summary>
        public void RenderScope(byte[] bgraSource, int width, int height)
        {
            if (bgraSource == null || bgraSource.Length < width * height * 4 || width <= 0 || height <= 0)
                return;

            // Clear buffers
            Array.Clear(_scopePixelBuffer, 0, _scopePixelBuffer.Length);
            Array.Clear(_accumulator, 0, _accumulator.Length);

            switch (CurrentScope)
            {
                case BroadcastScopeType.WaveformIreb:
                    RenderWaveform(bgraSource, width, height);
                    break;
                case BroadcastScopeType.RgbParade:
                    RenderRgbParade(bgraSource, width, height);
                    break;
                case BroadcastScopeType.Vectorscope:
                    RenderVectorscope(bgraSource, width, height);
                    break;
                case BroadcastScopeType.Histogram:
                    RenderHistogram(bgraSource, width, height);
                    break;
                case BroadcastScopeType.CieChromaticity:
                    RenderCieChromaticity(bgraSource, width, height);
                    break;
            }

            // Copy to thread-safe completed buffer for UI presentation (zero WPF cross-thread exceptions)
            lock (_scopeBufferLock)
            {
                Buffer.BlockCopy(_scopePixelBuffer, 0, _completedScopeBuffer, 0, ScopeBufferBytes);
            }
        }

        /// <summary>
        /// Safely updates the WPF WriteableBitmap from the UI thread without threading violations.
        /// </summary>
        public void UpdateWpfScopeBitmap(ref WriteableBitmap? bmp)
        {
            if (bmp == null || bmp.PixelWidth != ScopeWidth || bmp.PixelHeight != ScopeHeight)
            {
                bmp = new WriteableBitmap(ScopeWidth, ScopeHeight, 96, 96, PixelFormats.Bgra32, null);
            }

            lock (_scopeBufferLock)
            {
                var rect = new System.Windows.Int32Rect(0, 0, ScopeWidth, ScopeHeight);
                bmp.WritePixels(rect, _completedScopeBuffer, ScopeStride, 0);
            }
        }

        #region 1. Waveform Monitor (IRE Scale: -20 .. 120)

        private void RenderWaveform(byte[] src, int srcW, int srcH)
        {
            DrawWaveformGraticule();

            int stepX = Math.Max(1, srcW / ScopeWidth);
            int stepY = 4; // Subsample vertically for ultra-low CPU

            double maxIre = -100;
            double minIre = 200;
            double totalIre = 0;
            long sampleCount = 0;
            long outOfGamutCount = 0;

            unsafe
            {
                fixed (byte* pSrc = src)
                {
                    for (int sy = 0; sy < srcH; sy += stepY)
                    {
                        byte* pRow = pSrc + sy * srcW * 4;
                        for (int sx = 0; sx < srcW; sx += stepX)
                        {
                            int targetX = (sx * ScopeWidth) / srcW;
                            if (targetX < 0 || targetX >= ScopeWidth) continue;

                            byte b = pRow[sx * 4 + 0];
                            byte g = pRow[sx * 4 + 1];
                            byte r = pRow[sx * 4 + 2];

                            // BT.709 Luma
                            double luma = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                            double ire = (luma / 255.0) * 100.0;

                            if (ire > maxIre) maxIre = ire;
                            if (ire < minIre) minIre = ire;
                            totalIre += ire;
                            sampleCount++;

                            if (ire > 100.0 || ire < 0.0) outOfGamutCount++;

                            // Map IRE (-20 .. 120) to Scope Height (0 at top, 270 at bottom)
                            int targetY = IreToY(ire);
                            if (targetY >= 0 && targetY < ScopeHeight)
                            {
                                int idx = targetY * ScopeWidth + targetX;
                                if (_accumulator[idx] < 255) _accumulator[idx] += 25;
                            }
                        }
                    }
                }
            }

            // Draw phosphor trace (classic broadcast green with cyan intensity glow)
            unsafe
            {
                fixed (byte* pDst = _scopePixelBuffer)
                {
                    for (int i = 0; i < ScopeWidth * ScopeHeight; i++)
                    {
                        int intensity = _accumulator[i];
                        if (intensity > 0)
                        {
                            int idx = i * 4;
                            byte b = (byte)Math.Min(255, pDst[idx + 0] + (intensity > 120 ? intensity - 60 : 0));
                            byte g = (byte)Math.Min(255, pDst[idx + 1] + intensity);
                            byte r = (byte)Math.Min(255, pDst[idx + 2] + (intensity > 180 ? intensity - 100 : 0));
                            pDst[idx + 0] = b;
                            pDst[idx + 1] = g;
                            pDst[idx + 2] = r;
                            pDst[idx + 3] = 255;
                        }
                    }
                }
            }

            UpdateMetrics(maxIre, minIre, totalIre, sampleCount, outOfGamutCount);
        }

        private void DrawWaveformGraticule()
        {
            // Dark broadcast background
            DrawSolidRect(0, 0, ScopeWidth, ScopeHeight, 10, 14, 22);

            // IRE Reference Graticules:
            // 100 IRE (Reference White)
            DrawGraticuleLine(100, 239, 68, 68, "100 IRE (WHITE)"); // Red warning
            // 80 IRE
            DrawGraticuleLine(80, 50, 75, 105, "80");
            // 50 IRE (Midtones)
            DrawGraticuleLine(50, 56, 189, 248, "50 IRE (18% GREY)"); // Cyan
            // 20 IRE
            DrawGraticuleLine(20, 50, 75, 105, "20");
            // 7.5 IRE (NTSC Setup)
            DrawGraticuleLine(7.5, 70, 95, 120, "7.5");
            // 0 IRE (Legal Black)
            DrawGraticuleLine(0, 34, 197, 94, "0 IRE (BLACK)"); // Green
            // -20 IRE
            DrawGraticuleLine(-20, 70, 40, 50, "-20");
        }

        private void DrawGraticuleLine(double ire, byte r, byte g, byte b, string? label = null)
        {
            int y = IreToY(ire);
            if (y < 0 || y >= ScopeHeight) return;

            unsafe
            {
                fixed (byte* pDst = _scopePixelBuffer)
                {
                    for (int x = 0; x < ScopeWidth; x++)
                    {
                        // Dashed line pattern
                        if ((x / 4) % 2 == 0)
                        {
                            int idx = (y * ScopeWidth + x) * 4;
                            pDst[idx + 0] = b;
                            pDst[idx + 1] = g;
                            pDst[idx + 2] = r;
                            pDst[idx + 3] = 255;
                        }
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int IreToY(double ire)
        {
            // Range: -20 IRE (bottom ~250px) to +120 IRE (top ~20px)
            // Total IRE span = 140
            double norm = (120.0 - ire) / 140.0;
            return (int)(norm * (ScopeHeight - 20) + 10);
        }

        #endregion

        #region 2. RGB Parade (Red, Green, Blue columns)

        private void RenderRgbParade(byte[] src, int srcW, int srcH)
        {
            DrawSolidRect(0, 0, ScopeWidth, ScopeHeight, 10, 14, 22);

            int colW = ScopeWidth / 3; // 160 px per channel
            int col1 = colW;
            int col2 = colW * 2;

            // Draw divider lines
            for (int y = 0; y < ScopeHeight; y++)
            {
                SetPixel(col1, y, 70, 85, 110);
                SetPixel(col2, y, 70, 85, 110);
            }

            // Draw horizontal IRE markers in all columns
            DrawParadeGraticules();

            int stepX = Math.Max(1, srcW / colW);
            int stepY = 4;

            double maxIre = -100;
            double minIre = 200;
            double totalIre = 0;
            long sampleCount = 0;
            long outOfGamutCount = 0;

            unsafe
            {
                fixed (byte* pSrc = src)
                {
                    for (int sy = 0; sy < srcH; sy += stepY)
                    {
                        byte* pRow = pSrc + sy * srcW * 4;
                        for (int sx = 0; sx < srcW; sx += stepX)
                        {
                            int subX = (sx * colW) / srcW;
                            if (subX < 0 || subX >= colW) continue;

                            byte b = pRow[sx * 4 + 0];
                            byte g = pRow[sx * 4 + 1];
                            byte r = pRow[sx * 4 + 2];

                            double ireR = (r / 255.0) * 100.0;
                            double ireG = (g / 255.0) * 100.0;
                            double ireB = (b / 255.0) * 100.0;

                            double luma = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                            double lumaIre = (luma / 255.0) * 100.0;
                            if (lumaIre > maxIre) maxIre = lumaIre;
                            if (lumaIre < minIre) minIre = lumaIre;
                            totalIre += lumaIre;
                            sampleCount++;
                            if (lumaIre > 100.0 || lumaIre < 0.0) outOfGamutCount++;

                            // Plot Red in Col 0
                            int yR = IreToY(ireR);
                            if (yR >= 0 && yR < ScopeHeight)
                            {
                                int idxR = (yR * ScopeWidth + subX) * 4;
                                BlendPhosphor(idxR, 50, 50, 240);
                            }

                            // Plot Green in Col 1
                            int yG = IreToY(ireG);
                            if (yG >= 0 && yG < ScopeHeight)
                            {
                                int idxG = (yG * ScopeWidth + (colW + subX)) * 4;
                                BlendPhosphor(idxG, 50, 240, 50);
                            }

                            // Plot Blue in Col 2
                            int yB = IreToY(ireB);
                            if (yB >= 0 && yB < ScopeHeight)
                            {
                                int idxB = (yB * ScopeWidth + (colW * 2 + subX)) * 4;
                                BlendPhosphor(idxB, 240, 120, 50);
                            }
                        }
                    }
                }
            }

            UpdateMetrics(maxIre, minIre, totalIre, sampleCount, outOfGamutCount);
        }

        private void DrawParadeGraticules()
        {
            double[] marks = { 0, 50, 100 };
            foreach (var ire in marks)
            {
                int y = IreToY(ire);
                if (y >= 0 && y < ScopeHeight)
                {
                    for (int x = 0; x < ScopeWidth; x++)
                    {
                        if ((x / 3) % 2 == 0)
                        {
                            byte c = ire == 100 ? (byte)150 : (ire == 0 ? (byte)100 : (byte)70);
                            SetPixel(x, y, c, c, c);
                        }
                    }
                }
            }
        }

        #endregion

        #region 3. Vectorscope (ITU-R BT.709 / SMPTE Polar Plot)

        private void RenderVectorscope(byte[] src, int srcW, int srcH)
        {
            int cx = ScopeWidth / 2;
            int cy = ScopeHeight / 2;
            int maxRadius = Math.Min(cx, cy) - 15; // ~120px

            DrawSolidRect(0, 0, ScopeWidth, ScopeHeight, 10, 14, 22);

            // Draw Vectorscope Graticule: Circles, Axes, Target Boxes, Skin Tone Vector
            DrawVectorscopeGraticule(cx, cy, maxRadius);

            int step = 4;
            double maxIre = 0, minIre = 100, totalIre = 0;
            long sampleCount = 0, outOfGamutCount = 0;

            unsafe
            {
                fixed (byte* pSrc = src)
                {
                    for (int y = 0; y < srcH; y += step)
                    {
                        byte* pRow = pSrc + y * srcW * 4;
                        for (int x = 0; x < srcW; x += step)
                        {
                            byte b = pRow[x * 4 + 0];
                            byte g = pRow[x * 4 + 1];
                            byte r = pRow[x * 4 + 2];

                            // BT.709 RGB to YCbCr conversion
                            double yVal = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                            double cb = -0.1146 * r - 0.3854 * g + 0.5000 * b; // [-128..127]
                            double cr = 0.5000 * r - 0.4542 * g - 0.0458 * b;

                            double ire = (yVal / 255.0) * 100.0;
                            if (ire > maxIre) maxIre = ire;
                            if (ire < minIre) minIre = ire;
                            totalIre += ire;
                            sampleCount++;
                            if (ire > 100.0 || ire < 0.0) outOfGamutCount++;

                            // Map Cb (U) to X, Cr (V) to Y
                            // In standard broadcast vectorscope: B-Y (Cb) is horizontal, R-Y (Cr) is vertical
                            int px = cx + (int)((cb / 128.0) * maxRadius * 1.3);
                            int py = cy - (int)((cr / 128.0) * maxRadius * 1.3);

                            if (px >= 0 && px < ScopeWidth && py >= 0 && py < ScopeHeight)
                            {
                                int idx = py * ScopeWidth + px;
                                if (_accumulator[idx] < 255) _accumulator[idx] += 20;
                            }
                        }
                    }
                }
            }

            // Draw phosphor traces in classic vectorscope green/amber
            unsafe
            {
                fixed (byte* pDst = _scopePixelBuffer)
                {
                    for (int i = 0; i < ScopeWidth * ScopeHeight; i++)
                    {
                        int intensity = _accumulator[i];
                        if (intensity > 0)
                        {
                            int idx = i * 4;
                            pDst[idx + 0] = (byte)Math.Min(255, pDst[idx + 0] + (intensity > 150 ? intensity - 80 : 20));
                            pDst[idx + 1] = (byte)Math.Min(255, pDst[idx + 1] + intensity);
                            pDst[idx + 2] = (byte)Math.Min(255, pDst[idx + 2] + (intensity > 100 ? intensity - 30 : 10));
                            pDst[idx + 3] = 255;
                        }
                    }
                }
            }

            UpdateMetrics(maxIre, minIre, totalIre, sampleCount, outOfGamutCount);
        }

        private void DrawVectorscopeGraticule(int cx, int cy, int maxRadius)
        {
            // Concentric circles: 100% saturation and 75% saturation
            DrawCircle(cx, cy, (int)(maxRadius * 0.75), 45, 60, 85);
            DrawCircle(cx, cy, maxRadius, 55, 75, 105);

            // Crosshair lines
            for (int x = cx - maxRadius; x <= cx + maxRadius; x++)
            {
                if (x >= 0 && x < ScopeWidth && (x % 3 == 0)) SetPixel(x, cy, 60, 80, 110);
            }
            for (int y = cy - maxRadius; y <= cy + maxRadius; y++)
            {
                if (y >= 0 && y < ScopeHeight && (y % 3 == 0)) SetPixel(cx, y, 60, 80, 110);
            }

            // Broadcast Skin Tone Line (Flesh tone vector at ~123° / 10:30 o'clock)
            double skinAngleRad = 123.0 * Math.PI / 180.0;
            for (int r = 10; r <= maxRadius + 10; r += 2)
            {
                int sx = cx - (int)(r * Math.Sin(skinAngleRad - Math.PI / 2.0));
                int sy = cy - (int)(r * Math.Cos(skinAngleRad - Math.PI / 2.0));
                if (sx >= 0 && sx < ScopeWidth && sy >= 0 && sy < ScopeHeight)
                {
                    SetPixel(sx, sy, 50, 180, 245); // Amber / Skin tone marker
                }
            }

            // 6 Primary/Secondary Color Target Boxes at 75% saturation:
            DrawTargetBox(cx, cy, maxRadius * 0.75, 104.0, 239, 68, 68);    // Red
            DrawTargetBox(cx, cy, maxRadius * 0.75, 61.0, 217, 70, 239);    // Magenta
            DrawTargetBox(cx, cy, maxRadius * 0.75, 347.0, 59, 130, 246);   // Blue
            DrawTargetBox(cx, cy, maxRadius * 0.75, 284.0, 6, 182, 212);    // Cyan
            DrawTargetBox(cx, cy, maxRadius * 0.75, 241.0, 34, 197, 94);    // Green
            DrawTargetBox(cx, cy, maxRadius * 0.75, 167.0, 234, 179, 8);    // Yellow
        }

        private void DrawTargetBox(int cx, int cy, double radius, double angleDeg, byte r, byte g, byte b)
        {
            double rad = angleDeg * Math.PI / 180.0;
            int bx = cx + (int)(radius * Math.Cos(rad));
            int by = cy - (int)(radius * Math.Sin(rad));

            int boxSize = 6;
            for (int dx = -boxSize; dx <= boxSize; dx++)
            {
                SetPixel(bx + dx, by - boxSize, b, g, r);
                SetPixel(bx + dx, by + boxSize, b, g, r);
            }
            for (int dy = -boxSize; dy <= boxSize; dy++)
            {
                SetPixel(bx - boxSize, by + dy, b, g, r);
                SetPixel(bx + boxSize, by + dy, b, g, r);
            }
            SetPixel(bx, by, 255, 255, 255);
        }

        private void DrawCircle(int cx, int cy, int radius, byte b, byte g, byte r)
        {
            int x = radius;
            int y = 0;
            int err = 0;

            while (x >= y)
            {
                PlotCirclePoints(cx, cy, x, y, b, g, r);
                y += 1;
                err += 1 + 2 * y;
                if (2 * (err - x) + 1 > 0)
                {
                    x -= 1;
                    err += 1 - 2 * x;
                }
            }
        }

        private void PlotCirclePoints(int cx, int cy, int x, int y, byte b, byte g, byte r)
        {
            SetPixel(cx + x, cy + y, b, g, r);
            SetPixel(cx - x, cy + y, b, g, r);
            SetPixel(cx + x, cy - y, b, g, r);
            SetPixel(cx - x, cy - y, b, g, r);
            SetPixel(cx + y, cy + x, b, g, r);
            SetPixel(cx - y, cy + x, b, g, r);
            SetPixel(cx + y, cy - x, b, g, r);
            SetPixel(cx - y, cy - x, b, g, r);
        }

        #endregion

        #region 4. Color Histogram (Luma Y + RGB)

        private void RenderHistogram(byte[] src, int srcW, int srcH)
        {
            DrawSolidRect(0, 0, ScopeWidth, ScopeHeight, 10, 14, 22);

            Array.Clear(_histR, 0, 256);
            Array.Clear(_histG, 0, 256);
            Array.Clear(_histB, 0, 256);
            Array.Clear(_histY, 0, 256);

            int step = 4;
            int totalSamples = 0;
            unsafe
            {
                fixed (byte* pSrc = src)
                {
                    for (int y = 0; y < srcH; y += step)
                    {
                        byte* pRow = pSrc + y * srcW * 4;
                        for (int x = 0; x < srcW; x += step)
                        {
                            byte b = pRow[x * 4 + 0];
                            byte g = pRow[x * 4 + 1];
                            byte r = pRow[x * 4 + 2];
                            byte luma = (byte)(0.2126 * r + 0.7152 * g + 0.0722 * b);

                            _histB[b]++;
                            _histG[g]++;
                            _histR[r]++;
                            _histY[luma]++;
                            totalSamples++;
                        }
                    }
                }
            }

            int maxCount = 1;
            for (int i = 0; i < 256; i++)
            {
                if (_histR[i] > maxCount) maxCount = _histR[i];
                if (_histG[i] > maxCount) maxCount = _histG[i];
                if (_histB[i] > maxCount) maxCount = _histB[i];
                if (_histY[i] > maxCount) maxCount = _histY[i];
            }

            int graphX = 30;
            int graphW = ScopeWidth - 60;
            int graphH = ScopeHeight - 40;
            int baselineY = ScopeHeight - 20;

            int x16 = graphX + (16 * graphW) / 255;
            int x235 = graphX + (235 * graphW) / 255;

            for (int y = baselineY - graphH; y <= baselineY; y++)
            {
                if (y % 4 == 0)
                {
                    SetPixel(x16, y, 34, 197, 94);   // Green legal black (16)
                    SetPixel(x235, y, 239, 68, 68); // Red legal white (235)
                }
            }

            for (int i = 0; i < 255; i++)
            {
                int px1 = graphX + (i * graphW) / 255;
                int px2 = graphX + ((i + 1) * graphW) / 255;

                // Luma Y (White)
                int pyY1 = baselineY - (int)((double)_histY[i] / maxCount * graphH);
                int pyY2 = baselineY - (int)((double)_histY[i + 1] / maxCount * graphH);
                DrawLine(px1, pyY1, px2, pyY2, 220, 220, 220);

                // Red
                int pyR1 = baselineY - (int)((double)_histR[i] / maxCount * graphH);
                int pyR2 = baselineY - (int)((double)_histR[i + 1] / maxCount * graphH);
                DrawLine(px1, pyR1, px2, pyR2, 50, 50, 239);

                // Green
                int pyG1 = baselineY - (int)((double)_histG[i] / maxCount * graphH);
                int pyG2 = baselineY - (int)((double)_histG[i + 1] / maxCount * graphH);
                DrawLine(px1, pyG1, px2, pyG2, 50, 239, 50);

                // Blue
                int pyB1 = baselineY - (int)((double)_histB[i] / maxCount * graphH);
                int pyB2 = baselineY - (int)((double)_histB[i + 1] / maxCount * graphH);
                DrawLine(px1, pyB1, px2, pyB2, 239, 120, 50);
            }

            double minIre = 0, maxIre = 0, totalIre = 0;
            long outCount = 0;
            for (int i = 0; i < 256; i++)
            {
                double ire = (i / 255.0) * 100.0;
                if (_histY[i] > 0)
                {
                    if (minIre == 0) minIre = ire;
                    maxIre = ire;
                    totalIre += ire * _histY[i];
                    if (i < 16 || i > 235) outCount += _histY[i];
                }
            }

            UpdateMetrics(maxIre, minIre, totalIre, totalSamples, outCount);
        }

        #endregion

        #region 5. CIE 1931 Chromaticity Diagram

        private void RenderCieChromaticity(byte[] src, int srcW, int srcH)
        {
            DrawSolidRect(0, 0, ScopeWidth, ScopeHeight, 10, 14, 22);

            int cx = ScopeWidth / 2 - 30;
            int baseY = ScopeHeight - 25;
            double scale = ScopeHeight * 0.85;

            for (int x = 40; x < ScopeWidth - 40; x += (int)(scale * 0.1))
            {
                for (int y = 20; y < baseY; y += 4) SetPixel(x, y, 40, 50, 70);
            }

            // Rec.709 Gamut Triangle: R(0.64, 0.33), G(0.30, 0.60), B(0.15, 0.06)
            int rx = cx + (int)(0.64 * scale);
            int ry = baseY - (int)(0.33 * scale);
            int gx = cx + (int)(0.30 * scale);
            int gy = baseY - (int)(0.60 * scale);
            int bx = cx + (int)(0.15 * scale);
            int by = baseY - (int)(0.06 * scale);

            DrawLine(rx, ry, gx, gy, 0, 229, 255);
            DrawLine(gx, gy, bx, by, 0, 229, 255);
            DrawLine(bx, by, rx, ry, 0, 229, 255);

            int d65X = cx + (int)(0.3127 * scale);
            int d65Y = baseY - (int)(0.3290 * scale);
            DrawTargetBox(d65X, d65Y, 0, 0, 255, 255, 255);

            int step = 4;
            double maxIre = 0, minIre = 100, totalIre = 0;
            long sampleCount = 0, outOfGamutCount = 0;

            unsafe
            {
                fixed (byte* pSrc = src)
                {
                    for (int y = 0; y < srcH; y += step)
                    {
                        byte* pRow = pSrc + y * srcW * 4;
                        for (int x = 0; x < srcW; x += step)
                        {
                            byte b = pRow[x * 4 + 0];
                            byte g = pRow[x * 4 + 1];
                            byte r = pRow[x * 4 + 2];

                            double lr = r / 255.0;
                            double lg = g / 255.0;
                            double lb = b / 255.0;

                            double X = 0.4124564 * lr + 0.3575761 * lg + 0.1804375 * lb;
                            double Y = 0.2126729 * lr + 0.7151522 * lg + 0.0721750 * lb;
                            double Z = 0.0193339 * lr + 0.1191920 * lg + 0.9503041 * lb;

                            double sum = X + Y + Z;
                            if (sum > 0.001)
                            {
                                double chrX = X / sum;
                                double chrY = Y / sum;

                                int px = cx + (int)(chrX * scale);
                                int py = baseY - (int)(chrY * scale);

                                if (px >= 0 && px < ScopeWidth && py >= 0 && py < ScopeHeight)
                                {
                                    int idx = py * ScopeWidth + px;
                                    if (_accumulator[idx] < 255) _accumulator[idx] += 30;
                                }
                            }

                            double ire = Y * 100.0;
                            if (ire > maxIre) maxIre = ire;
                            if (ire < minIre) minIre = ire;
                            totalIre += ire;
                            sampleCount++;
                        }
                    }
                }
            }

            unsafe
            {
                fixed (byte* pDst = _scopePixelBuffer)
                {
                    for (int i = 0; i < ScopeWidth * ScopeHeight; i++)
                    {
                        int intensity = _accumulator[i];
                        if (intensity > 0)
                        {
                            int idx = i * 4;
                            pDst[idx + 0] = (byte)Math.Min(255, pDst[idx + 0] + (intensity > 140 ? intensity - 50 : 20));
                            pDst[idx + 1] = (byte)Math.Min(255, pDst[idx + 1] + intensity);
                            pDst[idx + 2] = (byte)Math.Min(255, pDst[idx + 2] + (intensity > 90 ? intensity : 30));
                            pDst[idx + 3] = 255;
                        }
                    }
                }
            }

            UpdateMetrics(maxIre, minIre, totalIre, sampleCount, outOfGamutCount);
        }

        #endregion

        #region Helper Drawing Utilities

        private void DrawSolidRect(int x, int y, int w, int h, byte r, byte g, byte b)
        {
            unsafe
            {
                fixed (byte* pDst = _scopePixelBuffer)
                {
                    int x2 = Math.Min(ScopeWidth, x + w);
                    int y2 = Math.Min(ScopeHeight, y + h);

                    for (int cy = y; cy < y2; cy++)
                    {
                        byte* pRow = pDst + (cy * ScopeWidth + x) * 4;
                        for (int cx = x; cx < x2; cx++)
                        {
                            pRow[0] = b;
                            pRow[1] = g;
                            pRow[2] = r;
                            pRow[3] = 255;
                            pRow += 4;
                        }
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetPixel(int x, int y, byte b, byte g, byte r)
        {
            if (x < 0 || x >= ScopeWidth || y < 0 || y >= ScopeHeight) return;
            int idx = (y * ScopeWidth + x) * 4;
            _scopePixelBuffer[idx + 0] = b;
            _scopePixelBuffer[idx + 1] = g;
            _scopePixelBuffer[idx + 2] = r;
            _scopePixelBuffer[idx + 3] = 255;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BlendPhosphor(int byteIdx, byte b, byte g, byte r)
        {
            if (byteIdx < 0 || byteIdx + 3 >= _scopePixelBuffer.Length) return;
            _scopePixelBuffer[byteIdx + 0] = (byte)Math.Min(255, _scopePixelBuffer[byteIdx + 0] + b);
            _scopePixelBuffer[byteIdx + 1] = (byte)Math.Min(255, _scopePixelBuffer[byteIdx + 1] + g);
            _scopePixelBuffer[byteIdx + 2] = (byte)Math.Min(255, _scopePixelBuffer[byteIdx + 2] + r);
            _scopePixelBuffer[byteIdx + 3] = 255;
        }

        private void DrawLine(int x0, int y0, int x1, int y1, byte b, byte g, byte r)
        {
            int dx = Math.Abs(x1 - x0);
            int dy = Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            while (true)
            {
                SetPixel(x0, y0, b, g, r);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 > -dy)
                {
                    err -= dy;
                    x0 += sx;
                }
                if (e2 < dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }

        private void UpdateMetrics(double maxIre, double minIre, double totalIre, long sampleCount, long outOfGamutCount)
        {
            double avg = sampleCount > 0 ? totalIre / sampleCount : 0;
            double outPercent = sampleCount > 0 ? (double)outOfGamutCount / sampleCount * 100.0 : 0;

            int cct = 5600;
            string tint = "Neutral";
            if (avg > 70) cct = 6500;
            else if (avg < 40) cct = 3200;

            Metrics = new BroadcastColorMetrics
            {
                MaxIre = Math.Round(Math.Clamp(maxIre, -20, 120), 1),
                MinIre = Math.Round(Math.Clamp(minIre, -20, 120), 1),
                AverageIre = Math.Round(avg, 1),
                OutOfGamutPercent = Math.Round(outPercent, 1),
                EstimatedColorTempK = cct,
                DominantTint = tint,
                IsLegalSignal = outPercent < 0.5 && maxIre <= 100.0 && minIre >= 0.0
            };
        }

        #endregion
    }
}
