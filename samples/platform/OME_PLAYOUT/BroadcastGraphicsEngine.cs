using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OME_PLAYOUT
{
    /// <summary>
    /// Broadcast Graphics Engine managing on-air Station Logo / Watermark (CG):
    /// - Alpha channel PNG/APNG with normalized X, Y, Scale, Opacity, DSK on-air toggle.
    /// - Zero-tear high-performance alpha blending onto 1920x1080 BGRA frames.
    /// </summary>
    public sealed class BroadcastGraphicsEngine
    {
        // ─── Station Logo / Bug Layer ─────────────────────────────
        public bool IsLogoOnAir { get; set; } = true;
        public double LogoX { get; set; } = 0.88;       // Normalized X (0.0 .. 1.0)
        public double LogoY { get; set; } = 0.08;       // Normalized Y (0.0 .. 1.0)
        public double LogoScale { get; set; } = 1.0;    // 0.2 .. 3.0
        public double LogoOpacity { get; set; } = 0.90; // 0.0 .. 1.0

        private byte[]? _logoBgra;
        private int _logoW = 0;
        private int _logoH = 0;
        public string LoadedLogoPath { get; private set; } = "Default OME Bug";

        public BroadcastGraphicsEngine()
        {
            GenerateDefaultLogo();
        }

        #region Logo Methods

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

                _logoW = formatConverted.PixelWidth;
                _logoH = formatConverted.PixelHeight;
                int stride = _logoW * 4;
                _logoBgra = new byte[_logoH * stride];
                formatConverted.CopyPixels(_logoBgra, stride, 0);

                LoadedLogoPath = Path.GetFileName(filePath);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void GenerateDefaultLogo()
        {
            // Creates a crisp default OME broadcast watermark
            int w = 180, h = 60;
            var drawingVisual = new DrawingVisual();
            using (var dc = drawingVisual.RenderOpen())
            {
                // Rounded badge
                var brush = new LinearGradientBrush(Color.FromArgb(220, 0, 150, 255), Color.FromArgb(220, 0, 229, 255), 45.0);
                dc.DrawRoundedRectangle(brush, new Pen(new SolidColorBrush(Colors.White), 1.5), new Rect(2, 2, w - 4, h - 4), 8, 8);

                var ft = new FormattedText(
                    "OME HD",
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI, Arial"), FontStyles.Normal, FontWeights.Black, FontStretches.Normal),
                    24,
                    Brushes.White,
                    VisualTreeHelper.GetDpi(drawingVisual).PixelsPerDip);

                dc.DrawText(ft, new Point((w - ft.Width) / 2, 8));

                var ftSub = new FormattedText(
                    "BROADCAST",
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI, Arial"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                    10,
                    new SolidColorBrush(Color.FromRgb(255, 215, 0)), // Gold
                    VisualTreeHelper.GetDpi(drawingVisual).PixelsPerDip);

                dc.DrawText(ftSub, new Point((w - ftSub.Width) / 2, 38));
            }

            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(drawingVisual);
            rtb.Freeze();

            _logoW = w;
            _logoH = h;
            int stride = w * 4;
            _logoBgra = new byte[h * stride];
            rtb.CopyPixels(_logoBgra, stride, 0);
        }

        #endregion

        /// <summary>
        /// Applies active Station Logo overlay onto the destination video frame.
        /// </summary>
        public void ApplyGraphics(byte[] canvas, int width, int height)
        {
            if (canvas == null || canvas.Length < width * height * 4) return;

            // Station Logo / Bug Overlay
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
        }

        private static void AlphaBlend(
            byte[] dst, int dstW, int dstH,
            byte[] src, int srcW, int srcH,
            int posX, int posY, float globalAlpha)
        {
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

        private static void AlphaBlendScaled(
            byte[] dst, int dstW, int dstH,
            byte[] src, int srcW, int srcH,
            int posX, int posY, int targetW, int targetH, float globalAlpha)
        {
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
