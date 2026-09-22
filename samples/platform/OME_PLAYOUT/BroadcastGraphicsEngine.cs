using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OME_PLAYOUT
{
    /// <summary>
    /// Master Broadcast Downstream Keyer (DSK) Multi-Layer Graphics Engine.
    /// Manages 4 independent DSK overlay layers:
    /// - DSK 1: Station Logo / Bug (Alpha PNG, Scalable, Positional)
    /// - DSK 2: Animated Broadcast Lower-Third (Gradient Name/Title Banner)
    /// - DSK 3: News Crawl Ticker (Smooth 60 FPS horizontal scrolling text)
    /// - DSK 4: Content Rating / Technical Watermark (16+, 18+, P)
    /// Thread-safe: fully protects shared memory from concurrent mutations (0xC0000005 crash prevention).
    /// </summary>
    public sealed class BroadcastGraphicsEngine
    {
        private readonly object _graphicsLock = new();

        // ─── DSK 1: Station Logo / Bug ────────────────────────────
        public bool IsLogoOnAir { get; set; } = true;
        public double LogoX { get; set; } = 0.88;
        public double LogoY { get; set; } = 0.08;
        public double LogoScale { get; set; } = 1.0;
        public double LogoOpacity { get; set; } = 0.90;

        private byte[]? _logoBgra;
        private int _logoW = 0;
        private int _logoH = 0;
        public string LoadedLogoPath { get; private set; } = "Default OME Bug";
        public int LogoPixelWidth => _logoW;
        public int LogoPixelHeight => _logoH;

        // ─── DSK 2: Lower-Third (Name / Program Banner) ───────────
        public bool IsLowerThirdOnAir { get; set; } = false;
        public double LowerThirdOpacity { get; set; } = 0.95;
        private byte[]? _lowerThirdBgra;
        private int _ltW = 750;
        private int _ltH = 110;
        private string _ltTitle = "MC TRẦN ANH";
        private string _ltSubtitle = "BẢN TIN THỜI SỰ TRỰC TIẾP 19H • BAN THỜI SỰ OME";

        // ─── DSK 3: News Crawl Ticker (60 FPS Smooth Scroll) ───────
        public bool IsTickerOnAir { get; set; } = true;
        private byte[]? _tickerBgra;
        private int _tickerTotalW = 0;
        private int _tickerH = 54;
        private int _tickerScrollOffset = 0;
        private const int TickerSpeed = 3; // Pixels per frame at 59.94 fps
        private string _tickerText = "🔴 [BẢN TIN OME HD] PHÁT SÓNG TRUYỀN HÌNH CHẤT LƯỢNG CAO 4K/FULL HD 60 FPS • HỆ THỐNG MASTER CONTROL PLAYOUT & CHANNEL-IN-A-BOX TỰ ĐỘNG HÓA RUNDOWN 24/7 • TIẾP SÓNG TRỰC TIẾP WEBRTC/SRT 0MS IPC • CHUYỂN ĐỔI SÓNG FRAME-ACCURATE •";

        // ─── DSK 4: Rating / Watermark ─────────────────────────────
        public bool IsRatingOnAir { get; set; } = false;
        private byte[]? _ratingBgra;
        private int _ratingW = 56;
        private int _ratingH = 56;
        private string _ratingText = "16+";

        public BroadcastGraphicsEngine()
        {
            GenerateDefaultLogo();
            GenerateLowerThirdBitmap(_ltTitle, _ltSubtitle);
            GenerateTickerBitmap(_tickerText);
            GenerateRatingBitmap(_ratingText);
        }

        #region DSK 1: Logo Methods

        public bool LoadLogoFromFile(string filePath)
        {
            if (!File.Exists(filePath)) return false;

            try
            {
                var uri = new Uri(filePath, UriKind.Absolute);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = uri;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();

                var formatConverted = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
                formatConverted.Freeze();

                int w = formatConverted.PixelWidth;
                int h = formatConverted.PixelHeight;
                int stride = w * 4;
                var raw = new byte[h * stride];
                formatConverted.CopyPixels(raw, stride, 0);

                lock (_graphicsLock)
                {
                    _logoW = w;
                    _logoH = h;
                    _logoBgra = raw;
                    LoadedLogoPath = Path.GetFileName(filePath);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void GenerateDefaultLogo()
        {
            int w = 180, h = 60;
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var brush = new LinearGradientBrush(Color.FromArgb(230, 2, 132, 199), Color.FromArgb(230, 0, 229, 255), 45.0);
                dc.DrawRoundedRectangle(brush, new Pen(new SolidColorBrush(Colors.White), 1.5), new Rect(2, 2, w - 4, h - 4), 8, 8);

                var ft = new FormattedText(
                    "OME HD",
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI, Arial"), FontStyles.Normal, FontWeights.Black, FontStretches.Normal),
                    24,
                    Brushes.White,
                    VisualTreeHelper.GetDpi(visual).PixelsPerDip);

                dc.DrawText(ft, new Point((w - ft.Width) / 2, 8));

                var ftSub = new FormattedText(
                    "ON AIR",
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI, Arial"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                    10,
                    new SolidColorBrush(Color.FromRgb(255, 215, 0)),
                    VisualTreeHelper.GetDpi(visual).PixelsPerDip);

                dc.DrawText(ftSub, new Point((w - ftSub.Width) / 2, 38));
            }

            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();

            int stride = w * 4;
            var raw = new byte[h * stride];
            rtb.CopyPixels(raw, stride, 0);

            lock (_graphicsLock)
            {
                _logoW = w;
                _logoH = h;
                _logoBgra = raw;
            }
        }

        #endregion

        #region DSK 2: Lower-Third Methods

        public void UpdateLowerThird(string title, string subtitle)
        {
            _ltTitle = title;
            _ltSubtitle = subtitle;
            GenerateLowerThirdBitmap(title, subtitle);
        }

        private void GenerateLowerThirdBitmap(string title, string subtitle)
        {
            int w = 750, h = 110;
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var bgBrush = new LinearGradientBrush(
                    Color.FromArgb(230, 10, 16, 32),
                    Color.FromArgb(210, 15, 23, 42),
                    0.0);
                dc.DrawRoundedRectangle(bgBrush, new Pen(new SolidColorBrush(Color.FromArgb(180, 0, 229, 255)), 1.5), new Rect(0, 0, w, h), 6, 6);

                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(239, 68, 68)), null, new Rect(0, 0, 8, h), 3, 3);

                var ftTitle = new FormattedText(
                    title.ToUpperInvariant(),
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI, Arial"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                    26,
                    Brushes.White,
                    VisualTreeHelper.GetDpi(visual).PixelsPerDip);
                dc.DrawText(ftTitle, new Point(24, 18));

                var ftSub = new FormattedText(
                    subtitle,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI, Arial"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                    16,
                    new SolidColorBrush(Color.FromRgb(0, 229, 255)),
                    VisualTreeHelper.GetDpi(visual).PixelsPerDip);
                dc.DrawText(ftSub, new Point(24, 62));
            }

            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();

            int stride = w * 4;
            var raw = new byte[h * stride];
            rtb.CopyPixels(raw, stride, 0);

            lock (_graphicsLock)
            {
                _ltW = w;
                _ltH = h;
                _lowerThirdBgra = raw;
            }
        }

        #endregion

        #region DSK 3: News Crawl Ticker Methods

        public void UpdateTickerText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            _tickerText = text;
            _tickerScrollOffset = 0;
            GenerateTickerBitmap(text);
        }

        private void GenerateTickerBitmap(string text)
        {
            var formatted = new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI, Arial"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                22,
                Brushes.White,
                96.0);

            int textW = (int)Math.Ceiling(formatted.Width) + 300;
            int totalW = Math.Max(2500, textW);
            int h = _tickerH;

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var bg = new SolidColorBrush(Color.FromArgb(220, 8, 12, 24));
                dc.DrawRectangle(bg, null, new Rect(0, 0, totalW, h));

                dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(0, 229, 255)), 2), new Point(0, 0), new Point(totalW, 0));

                dc.DrawText(formatted, new Point(20, (h - formatted.Height) / 2));
            }

            var rtb = new RenderTargetBitmap(totalW, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();

            int stride = totalW * 4;
            var raw = new byte[h * stride];
            rtb.CopyPixels(raw, stride, 0);

            lock (_graphicsLock)
            {
                _tickerTotalW = totalW;
                _tickerBgra = raw;
                _tickerScrollOffset = 0;
            }
        }

        #endregion

        #region DSK 4: Rating Methods

        public void UpdateRating(string rating)
        {
            _ratingText = rating;
            GenerateRatingBitmap(rating);
        }

        private void GenerateRatingBitmap(string rating)
        {
            int w = 56, h = 56;
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var brush = new SolidColorBrush(Color.FromArgb(200, 220, 38, 38));
                dc.DrawRoundedRectangle(brush, new Pen(Brushes.White, 1.5), new Rect(2, 2, w - 4, h - 4), 6, 6);

                var ft = new FormattedText(
                    rating,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI, Arial"), FontStyles.Normal, FontWeights.Black, FontStretches.Normal),
                    20,
                    Brushes.White,
                    VisualTreeHelper.GetDpi(visual).PixelsPerDip);

                dc.DrawText(ft, new Point((w - ft.Width) / 2, (h - ft.Height) / 2));
            }

            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();

            int stride = w * 4;
            var raw = new byte[h * stride];
            rtb.CopyPixels(raw, stride, 0);

            lock (_graphicsLock)
            {
                _ratingW = w;
                _ratingH = h;
                _ratingBgra = raw;
            }
        }

        #endregion

        /// <summary>
        /// Applies all active DSK layers onto destination 1920x1080 video frame.
        /// Thread-safe: fully locked to prevent concurrent buffer reallocations.
        /// </summary>
        public void ApplyGraphics(byte[] canvas, int width, int height)
        {
            if (canvas == null || canvas.Length < width * height * 4) return;

            lock (_graphicsLock)
            {
                // 1. DSK 1: Station Logo / Bug
                if (IsLogoOnAir && _logoBgra != null && _logoW > 0 && _logoH > 0)
                {
                    int scaledW = (int)(_logoW * LogoScale);
                    int scaledH = (int)(_logoH * LogoScale);
                    int posX = (int)(width * LogoX);
                    int posY = (int)(height * LogoY);

                    if (Math.Abs(LogoScale - 1.0) < 0.05)
                    {
                        AlphaBlend(canvas, width, height, _logoBgra, _logoW, _logoH, posX, posY, (float)LogoOpacity);
                    }
                    else
                    {
                        AlphaBlendScaled(canvas, width, height, _logoBgra, _logoW, _logoH, posX, posY, scaledW, scaledH, (float)LogoOpacity);
                    }
                }

                // 2. DSK 2: Animated Lower-Third (Bottom-Left)
                if (IsLowerThirdOnAir && _lowerThirdBgra != null && _ltW > 0 && _ltH > 0)
                {
                    int ltX = 80;
                    int ltY = height - _ltH - (IsTickerOnAir ? 80 : 40);
                    AlphaBlend(canvas, width, height, _lowerThirdBgra, _ltW, _ltH, ltX, ltY, (float)LowerThirdOpacity);
                }

                // 3. DSK 3: News Crawl Ticker (60 FPS Smooth Scroll at Canvas Bottom)
                if (IsTickerOnAir && _tickerBgra != null && _tickerTotalW > 0 && _tickerH > 0)
                {
                    int tickerY = height - _tickerH;
                    AlphaBlendTicker(canvas, width, height, _tickerBgra, _tickerTotalW, _tickerH, tickerY, _tickerScrollOffset);
                    _tickerScrollOffset = (_tickerScrollOffset + TickerSpeed) % _tickerTotalW;
                }

                // 4. DSK 4: Content Rating (Top-Left)
                if (IsRatingOnAir && _ratingBgra != null && _ratingW > 0 && _ratingH > 0)
                {
                    int ratingX = 60;
                    int ratingY = 50;
                    AlphaBlend(canvas, width, height, _ratingBgra, _ratingW, _ratingH, ratingX, ratingY, 0.90f);
                }
            }
        }

        private static void AlphaBlend(
            byte[] dst, int dstW, int dstH,
            byte[] src, int srcW, int srcH,
            int posX, int posY, float globalAlpha)
        {
            if (src == null || dst == null) return;
            if (src.Length < srcW * srcH * 4 || dst.Length < dstW * dstH * 4) return;

            int xStart = Math.Max(0, posX);
            int yStart = Math.Max(0, posY);
            int xEnd = Math.Min(dstW, posX + srcW);
            int yEnd = Math.Min(dstH, posY + srcH);

            if (xStart >= xEnd || yStart >= yEnd) return;

            unsafe
            {
                fixed (byte* pDst = dst)
                fixed (byte* pSrc = src)
                {
                    for (int y = yStart; y < yEnd; y++)
                    {
                        int srcY = y - posY;
                        byte* pSrcRow = pSrc + (srcY * srcW + (xStart - posX)) * 4;
                        byte* pDstRow = pDst + (y * dstW + xStart) * 4;

                        for (int x = xStart; x < xEnd; x++)
                        {
                            float srcA = (pSrcRow[3] / 255.0f) * globalAlpha;
                            if (srcA > 0.005f)
                            {
                                float invA = 1.0f - srcA;
                                pDstRow[0] = (byte)(pSrcRow[0] * srcA + pDstRow[0] * invA);
                                pDstRow[1] = (byte)(pSrcRow[1] * srcA + pDstRow[1] * invA);
                                pDstRow[2] = (byte)(pSrcRow[2] * srcA + pDstRow[2] * invA);
                            }

                            pSrcRow += 4;
                            pDstRow += 4;
                        }
                    }
                }
            }
        }

        private static void AlphaBlendTicker(
            byte[] dst, int dstW, int dstH,
            byte[] tickerSrc, int tickerTotalW, int tickerH,
            int posY, int scrollX)
        {
            if (tickerSrc == null || dst == null) return;
            if (tickerSrc.Length < tickerTotalW * tickerH * 4 || dst.Length < dstW * dstH * 4) return;
            if (posY < 0 || posY + tickerH > dstH) return;

            unsafe
            {
                fixed (byte* pDst = dst)
                fixed (byte* pSrc = tickerSrc)
                {
                    for (int y = 0; y < tickerH; y++)
                    {
                        byte* pDstRow = pDst + ((posY + y) * dstW) * 4;
                        byte* pSrcRow = pSrc + (y * tickerTotalW) * 4;

                        for (int x = 0; x < dstW; x++)
                        {
                            int srcX = (x + scrollX) % tickerTotalW;
                            byte* pPixel = pSrcRow + srcX * 4;
                            byte* pTarget = pDstRow + x * 4;

                            float srcA = (pPixel[3] / 255.0f) * 0.95f;
                            if (srcA > 0.005f)
                            {
                                float invA = 1.0f - srcA;
                                pTarget[0] = (byte)(pPixel[0] * srcA + pTarget[0] * invA);
                                pTarget[1] = (byte)(pPixel[1] * srcA + pTarget[1] * invA);
                                pTarget[2] = (byte)(pPixel[2] * srcA + pTarget[2] * invA);
                            }
                        }
                    }
                }
            }
        }

        private static void AlphaBlendScaled(
            byte[] dst, int dstW, int dstH,
            byte[] src, int srcW, int srcH,
            int posX, int posY, int targetW, int targetH, float globalAlpha)
        {
            if (src == null || dst == null) return;
            if (src.Length < srcW * srcH * 4 || dst.Length < dstW * dstH * 4) return;

            int xStart = Math.Max(0, posX);
            int yStart = Math.Max(0, posY);
            int xEnd = Math.Min(dstW, posX + targetW);
            int yEnd = Math.Min(dstH, posY + targetH);

            if (xStart >= xEnd || yStart >= yEnd || targetW <= 0 || targetH <= 0) return;

            float scaleX = (float)srcW / targetW;
            float scaleY = (float)srcH / targetH;

            unsafe
            {
                fixed (byte* pDst = dst)
                fixed (byte* pSrc = src)
                {
                    for (int y = yStart; y < yEnd; y++)
                    {
                        int sy = (int)((y - posY) * scaleY);
                        sy = Math.Clamp(sy, 0, srcH - 1);
                        byte* pSrcRow = pSrc + sy * srcW * 4;
                        byte* pDstRow = pDst + (y * dstW + xStart) * 4;

                        for (int x = xStart; x < xEnd; x++)
                        {
                            int sx = (int)((x - posX) * scaleX);
                            sx = Math.Clamp(sx, 0, srcW - 1);
                            byte* pPixel = pSrcRow + sx * 4;

                            float srcA = (pPixel[3] / 255.0f) * globalAlpha;
                            if (srcA > 0.005f)
                            {
                                float invA = 1.0f - srcA;
                                pDstRow[0] = (byte)(pPixel[0] * srcA + pDstRow[0] * invA);
                                pDstRow[1] = (byte)(pPixel[1] * srcA + pDstRow[1] * invA);
                                pDstRow[2] = (byte)(pPixel[2] * srcA + pDstRow[2] * invA);
                            }

                            pDstRow += 4;
                        }
                    }
                }
            }
        }
    }
}
