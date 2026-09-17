using System;
using System.Collections.Generic;

namespace OME_PLAYOUT
{
    public enum DveLayoutMode
    {
        Single = 0,     // Fullscreen single background
        TwoBoxSide = 1, // 2-box Side-by-Side (e.g. MC + Guest)
        ThreeBox = 2,   // 3-box (1 Large + 2 Small)
        FourBoxQuad = 3 // 4-box Quad Grid (2x2)
    }

    public struct DveBoxRect
    {
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public int CameraIndex; // 0..9 (Cam 1..10) or -1 for PGM Master

        public DveBoxRect(int x, int y, int width, int height, int cameraIndex)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
            CameraIndex = cameraIndex;
        }
    }

    /// <summary>
    /// Multi-Camera Digital Video Effects (DVE) Compositor.
    /// Composites 2, 3, or 4 ISO cameras onto a single master broadcast canvas with colored borders and drop shadows.
    /// </summary>
    public sealed class MultiCamDveCompositor
    {
        public DveLayoutMode Mode { get; set; } = DveLayoutMode.Single;

        public int Box1Camera { get; set; } = 0; // Cam 1
        public int Box2Camera { get; set; } = 1; // Cam 2
        public int Box3Camera { get; set; } = 2; // Cam 3
        public int Box4Camera { get; set; } = 3; // Cam 4

        public int BorderThickness { get; set; } = 3;
        public byte BorderR { get; set; } = 0;
        public byte BorderG { get; set; } = 229;
        public byte BorderB { get; set; } = 255; // Cyan #00E5FF
        public bool EnableDropShadow { get; set; } = true;

        /// <summary>
        /// Calculates the layout boxes based on current mode and target canvas resolution.
        /// </summary>
        public List<DveBoxRect> GetLayoutBoxes(int canvasWidth, int canvasHeight)
        {
            var boxes = new List<DveBoxRect>();

            switch (Mode)
            {
                case DveLayoutMode.Single:
                    // Full canvas
                    boxes.Add(new DveBoxRect(0, 0, canvasWidth, canvasHeight, Box1Camera));
                    break;

                case DveLayoutMode.TwoBoxSide:
                    // 2 boxes side-by-side with margins
                    {
                        int marginX = (int)(canvasWidth * 0.04);
                        int marginY = (int)(canvasHeight * 0.15);
                        int spacing = (int)(canvasWidth * 0.03);

                        int boxW = (canvasWidth - 2 * marginX - spacing) / 2;
                        int boxH = (int)(boxW * (9.0 / 16.0));

                        int topY = (canvasHeight - boxH) / 2;

                        boxes.Add(new DveBoxRect(marginX, topY, boxW, boxH, Box1Camera));
                        boxes.Add(new DveBoxRect(marginX + boxW + spacing, topY, boxW, boxH, Box2Camera));
                    }
                    break;

                case DveLayoutMode.ThreeBox:
                    // 1 large on left, 2 smaller stacked on right
                    {
                        int marginX = (int)(canvasWidth * 0.04);
                        int marginY = (int)(canvasHeight * 0.10);
                        int spacing = (int)(canvasWidth * 0.025);

                        int leftW = (int)((canvasWidth - 2 * marginX - spacing) * 0.58);
                        int leftH = (int)(leftW * (9.0 / 16.0));
                        int leftY = (canvasHeight - leftH) / 2;

                        int rightW = canvasWidth - 2 * marginX - spacing - leftW;
                        int rightH = (int)(rightW * (9.0 / 16.0));
                        int rightX = marginX + leftW + spacing;
                        int spacingY = (int)(canvasHeight * 0.025);

                        int rightTotalH = rightH * 2 + spacingY;
                        int rightStartY = (canvasHeight - rightTotalH) / 2;

                        boxes.Add(new DveBoxRect(marginX, leftY, leftW, leftH, Box1Camera));
                        boxes.Add(new DveBoxRect(rightX, rightStartY, rightW, rightH, Box2Camera));
                        boxes.Add(new DveBoxRect(rightX, rightStartY + rightH + spacingY, rightW, rightH, Box3Camera));
                    }
                    break;

                case DveLayoutMode.FourBoxQuad:
                    // 2x2 grid
                    {
                        int marginX = (int)(canvasWidth * 0.04);
                        int marginY = (int)(canvasHeight * 0.06);
                        int spacingX = (int)(canvasWidth * 0.025);
                        int spacingY = (int)(canvasHeight * 0.035);

                        int boxW = (canvasWidth - 2 * marginX - spacingX) / 2;
                        int boxH = (int)(boxW * (9.0 / 16.0));

                        int startX1 = marginX;
                        int startX2 = marginX + boxW + spacingX;
                        int startY1 = marginY;
                        int startY2 = marginY + boxH + spacingY;

                        boxes.Add(new DveBoxRect(startX1, startY1, boxW, boxH, Box1Camera));
                        boxes.Add(new DveBoxRect(startX2, startY1, boxW, boxH, Box2Camera));
                        boxes.Add(new DveBoxRect(startX1, startY2, boxW, boxH, Box3Camera));
                        boxes.Add(new DveBoxRect(startX2, startY2, boxW, boxH, Box4Camera));
                    }
                    break;
            }

            return boxes;
        }

        /// <summary>
        /// Composites multiple camera feeds onto the destination canvas.
        /// </summary>
        public void CompositeDve(
            byte[] destCanvas, int canvasWidth, int canvasHeight,
            Func<int, (byte[]? data, int w, int h)> cameraFeedProvider)
        {
            if (destCanvas == null || destCanvas.Length < canvasWidth * canvasHeight * 4) return;

            var boxes = GetLayoutBoxes(canvasWidth, canvasHeight);

            // If single mode and full screen, simply blit or let background pass
            if (Mode == DveLayoutMode.Single && boxes.Count == 1)
            {
                var (camData, camW, camH) = cameraFeedProvider(boxes[0].CameraIndex);
                if (camData != null && camW > 0 && camH > 0)
                {
                    BlitScaled(camData, camW, camH, destCanvas, canvasWidth, canvasHeight, 0, 0, canvasWidth, canvasHeight);
                }
                return;
            }

            // In multi-box mode, paint background dark studio slate
            FillBackground(destCanvas, canvasWidth, canvasHeight, 18, 22, 32);

            foreach (var box in boxes)
            {
                // 1. Draw Drop Shadow
                if (EnableDropShadow)
                {
                    DrawDropShadow(destCanvas, canvasWidth, canvasHeight, box.X + 6, box.Y + 6, box.Width, box.Height);
                }

                // 2. Draw Camera Video Feed
                var (camData, camW, camH) = cameraFeedProvider(box.CameraIndex);
                if (camData != null && camW > 0 && camH > 0)
                {
                    BlitScaled(camData, camW, camH, destCanvas, canvasWidth, canvasHeight, box.X, box.Y, box.Width, box.Height);
                }
                else
                {
                    // Draw standby placeholder
                    FillRect(destCanvas, canvasWidth, canvasHeight, box.X, box.Y, box.Width, box.Height, 35, 40, 55);
                }

                // 3. Draw Colored Border
                if (BorderThickness > 0)
                {
                    DrawBorder(destCanvas, canvasWidth, canvasHeight, box.X, box.Y, box.Width, box.Height, BorderThickness, BorderR, BorderG, BorderB);
                }
            }
        }

        private static void FillBackground(byte[] canvas, int w, int h, byte r, byte g, byte b)
        {
            int total = w * h;
            unsafe
            {
                fixed (byte* pBuf = canvas)
                {
                    byte* p = pBuf;
                    for (int i = 0; i < total; i++)
                    {
                        p[0] = b;
                        p[1] = g;
                        p[2] = r;
                        p[3] = 255;
                        p += 4;
                    }
                }
            }
        }

        private static void FillRect(byte[] canvas, int canvasW, int canvasH, int rx, int ry, int rw, int rh, byte r, byte g, byte b)
        {
            int x1 = Math.Max(0, rx);
            int y1 = Math.Max(0, ry);
            int x2 = Math.Min(canvasW, rx + rw);
            int y2 = Math.Min(canvasH, ry + rh);

            unsafe
            {
                fixed (byte* pBuf = canvas)
                {
                    for (int y = y1; y < y2; y++)
                    {
                        byte* pRow = pBuf + (y * canvasW + x1) * 4;
                        for (int x = x1; x < x2; x++)
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

        private static void DrawBorder(byte[] canvas, int canvasW, int canvasH, int rx, int ry, int rw, int rh, int th, byte r, byte g, byte b)
        {
            FillRect(canvas, canvasW, canvasH, rx, ry, rw, th, r, g, b); // Top
            FillRect(canvas, canvasW, canvasH, rx, ry + rh - th, rw, th, r, g, b); // Bottom
            FillRect(canvas, canvasW, canvasH, rx, ry, th, rh, r, g, b); // Left
            FillRect(canvas, canvasW, canvasH, rx + rw - th, ry, th, rh, r, g, b); // Right
        }

        private static void DrawDropShadow(byte[] canvas, int canvasW, int canvasH, int rx, int ry, int rw, int rh)
        {
            int x1 = Math.Max(0, rx);
            int y1 = Math.Max(0, ry);
            int x2 = Math.Min(canvasW, rx + rw);
            int y2 = Math.Min(canvasH, ry + rh);

            unsafe
            {
                fixed (byte* pBuf = canvas)
                {
                    for (int y = y1; y < y2; y++)
                    {
                        byte* pRow = pBuf + (y * canvasW + x1) * 4;
                        for (int x = x1; x < x2; x++)
                        {
                            // Dim background by 60%
                            pRow[0] = (byte)(pRow[0] * 0.4f);
                            pRow[1] = (byte)(pRow[1] * 0.4f);
                            pRow[2] = (byte)(pRow[2] * 0.4f);
                            pRow += 4;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Fast nearest-neighbor scaler blitting from source BGRA buffer to destination box.
        /// </summary>
        public static void BlitScaled(
            byte[] src, int srcW, int srcH,
            byte[] dst, int dstW, int dstH,
            int destX, int destY, int destBoxW, int destBoxH)
        {
            int xStart = Math.Max(0, destX);
            int yStart = Math.Max(0, destY);
            int xEnd = Math.Min(dstW, destX + destBoxW);
            int yEnd = Math.Min(dstH, destY + destBoxH);

            if (xStart >= xEnd || yStart >= yEnd) return;

            float scaleX = (float)srcW / destBoxW;
            float scaleY = (float)srcH / destBoxH;

            unsafe
            {
                fixed (byte* pSrc = src)
                fixed (byte* pDst = dst)
                {
                    for (int dy = yStart; dy < yEnd; dy++)
                    {
                        int sy = (int)((dy - destY) * scaleY);
                        sy = Math.Clamp(sy, 0, srcH - 1);

                        byte* pSrcRow = pSrc + sy * srcW * 4;
                        byte* pDstRow = pDst + (dy * dstW + xStart) * 4;

                        for (int dx = xStart; dx < xEnd; dx++)
                        {
                            int sx = (int)((dx - destX) * scaleX);
                            sx = Math.Clamp(sx, 0, srcW - 1);

                            byte* pPixel = pSrcRow + sx * 4;
                            pDstRow[0] = pPixel[0];
                            pDstRow[1] = pPixel[1];
                            pDstRow[2] = pPixel[2];
                            pDstRow[3] = pPixel[3];

                            pDstRow += 4;
                        }
                    }
                }
            }
        }
    }
}
