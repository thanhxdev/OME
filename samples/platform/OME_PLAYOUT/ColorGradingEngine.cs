using System;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace OME_PLAYOUT
{
    /// <summary>
    /// CCU Color Correction parameters for Lift, Gamma, Gain, Brightness, Contrast, and Saturation.
    /// </summary>
    public sealed class ColorGradingParameters
    {
        // Lift (Shadows / Blacks): [-1.0, 1.0], default 0.0
        public float LiftR { get; set; } = 0.0f;
        public float LiftG { get; set; } = 0.0f;
        public float LiftB { get; set; } = 0.0f;

        // Gamma (Midtones): [0.2, 5.0], default 1.0
        public float GammaR { get; set; } = 1.0f;
        public float GammaG { get; set; } = 1.0f;
        public float GammaB { get; set; } = 1.0f;

        // Gain (Highlights / Whites): [0.0, 5.0], default 1.0
        public float GainR { get; set; } = 1.0f;
        public float GainG { get; set; } = 1.0f;
        public float GainB { get; set; } = 1.0f;

        // Brightness: [-1.0, 1.0], default 0.0
        public float Brightness { get; set; } = 0.0f;

        // Contrast: [0.0, 3.0], default 1.0
        public float Contrast { get; set; } = 1.0f;

        // Saturation: [0.0, 3.0], default 1.0
        public float Saturation { get; set; } = 1.0f;

        public void Reset()
        {
            LiftR = LiftG = LiftB = 0.0f;
            GammaR = GammaG = GammaB = 1.0f;
            GainR = GainG = GainB = 1.0f;
            Brightness = 0.0f;
            Contrast = 1.0f;
            Saturation = 1.0f;
        }

        public bool IsNeutral =>
            LiftR == 0f && LiftG == 0f && LiftB == 0f &&
            GammaR == 1f && GammaG == 1f && GammaB == 1f &&
            GainR == 1f && GainG == 1f && GainB == 1f &&
            Brightness == 0f && Contrast == 1f && Saturation == 1f;
    }

    /// <summary>
    /// Color Grading & 3D LUT Engine.
    /// Implements CCU math: Color_out = Gain * (Color_in + Lift * (1 - Color_in))^(1 / Gamma).
    /// Supports .cube 3D LUT parsing and real-time trilinear interpolation.
    /// </summary>
    public sealed class ColorGradingEngine
    {
        public ColorGradingParameters Parameters { get; } = new();

        // 3D LUT Cube Data
        private float[,,,]? _lutData; // [r, g, b, 3]
        private int _lutSize = 0;
        private string _loadedLutName = "None";
        public bool IsLutEnabled { get; set; } = false;
        public int LutSize => _lutSize;
        public string LoadedLutName => _loadedLutName;

        // Pre-calculated fast LUT lookup tables for 8-bit channels
        private readonly byte[] _lutR = new byte[256];
        private readonly byte[] _lutG = new byte[256];
        private readonly byte[] _lutB = new byte[256];
        private bool _tablesDirty = true;

        public void Invalidate()
        {
            _tablesDirty = true;
        }

        /// <summary>
        /// Loads and parses an industry standard Adobe / DaVinci Resolve .cube 3D LUT file.
        /// </summary>
        public bool LoadCubeFile(string filePath)
        {
            if (!File.Exists(filePath)) return false;

            try
            {
                var lines = File.ReadAllLines(filePath);
                int size = 0;
                int dataStartIndex = -1;

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;

                    if (line.StartsWith("LUT_3D_SIZE", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2 && int.TryParse(parts[1], out int parsedSize))
                        {
                            size = parsedSize;
                        }
                    }
                    else if (size > 0 && char.IsDigit(line[0]))
                    {
                        dataStartIndex = i;
                        break;
                    }
                }

                if (size < 2 || dataStartIndex < 0) return false;

                var cube = new float[size, size, size, 3];
                int currentEntry = 0;
                int totalEntries = size * size * size;

                for (int i = dataStartIndex; i < lines.Length && currentEntry < totalEntries; i++)
                {
                    string line = lines[i].Trim();
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;

                    var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3 &&
                        float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float r) &&
                        float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float g) &&
                        float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float b))
                    {
                        // In .cube files, the order of iteration is red fastest, then green, then blue
                        int bIdx = currentEntry / (size * size);
                        int gIdx = (currentEntry / size) % size;
                        int rIdx = currentEntry % size;

                        cube[rIdx, gIdx, bIdx, 0] = Math.Clamp(r, 0f, 1f);
                        cube[rIdx, gIdx, bIdx, 1] = Math.Clamp(g, 0f, 1f);
                        cube[rIdx, gIdx, bIdx, 2] = Math.Clamp(b, 0f, 1f);

                        currentEntry++;
                    }
                }

                _lutData = cube;
                _lutSize = size;
                _loadedLutName = Path.GetFileName(filePath);
                IsLutEnabled = true;
                _tablesDirty = true;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void UnloadLut()
        {
            _lutData = null;
            _lutSize = 0;
            _loadedLutName = "None";
            IsLutEnabled = false;
            _tablesDirty = true;
        }

        private void RebuildTables()
        {
            float liftR = Parameters.LiftR, liftG = Parameters.LiftG, liftB = Parameters.LiftB;
            float gammaR = Math.Max(Parameters.GammaR, 0.001f), gammaG = Math.Max(Parameters.GammaG, 0.001f), gammaB = Math.Max(Parameters.GammaB, 0.001f);
            float gainR = Parameters.GainR, gainG = Parameters.GainG, gainB = Parameters.GainB;
            float brightness = Parameters.Brightness;
            float contrast = Parameters.Contrast;

            for (int i = 0; i < 256; i++)
            {
                float inVal = i / 255.0f;

                // Red channel
                float rLifted = Math.Max(inVal + liftR * (1.0f - inVal), 0.0f);
                float rPowered = MathF.Pow(rLifted, 1.0f / gammaR);
                float rGraded = gainR * rPowered;
                rGraded = (rGraded - 0.5f) * contrast + 0.5f + brightness;
                _lutR[i] = (byte)Math.Clamp((int)Math.Round(rGraded * 255.0f), 0, 255);

                // Green channel
                float gLifted = Math.Max(inVal + liftG * (1.0f - inVal), 0.0f);
                float gPowered = MathF.Pow(gLifted, 1.0f / gammaG);
                float gGraded = gainG * gPowered;
                gGraded = (gGraded - 0.5f) * contrast + 0.5f + brightness;
                _lutG[i] = (byte)Math.Clamp((int)Math.Round(gGraded * 255.0f), 0, 255);

                // Blue channel
                float bLifted = Math.Max(inVal + liftB * (1.0f - inVal), 0.0f);
                float bPowered = MathF.Pow(bLifted, 1.0f / gammaB);
                float bGraded = gainB * bPowered;
                bGraded = (bGraded - 0.5f) * contrast + 0.5f + brightness;
                _lutB[i] = (byte)Math.Clamp((int)Math.Round(bGraded * 255.0f), 0, 255);
            }

            _tablesDirty = false;
        }

        /// <summary>
        /// Applies color grading and optional 3D LUT to a BGRA32 frame buffer in-place.
        /// </summary>
        public void ApplyToBgraBuffer(byte[] buffer, int width, int height)
        {
            if (buffer == null || buffer.Length < width * height * 4) return;
            if (Parameters.IsNeutral && (!IsLutEnabled || _lutData == null)) return;

            if (_tablesDirty)
            {
                RebuildTables();
            }

            float saturation = Parameters.Saturation;
            bool applySat = Math.Abs(saturation - 1.0f) > 0.01f;
            bool apply3DLut = IsLutEnabled && _lutData != null && _lutSize >= 2;

            int totalPixels = width * height;

            unsafe
            {
                fixed (byte* pBuf = buffer)
                {
                    byte* p = pBuf;

                    for (int i = 0; i < totalPixels; i++)
                    {
                        byte b = _lutB[p[0]];
                        byte g = _lutG[p[1]];
                        byte r = _lutR[p[2]];

                        // Apply Saturation if needed
                        if (applySat)
                        {
                            float rf = r / 255.0f;
                            float gf = g / 255.0f;
                            float bf = b / 255.0f;

                            float luma = 0.2126f * rf + 0.7152f * gf + 0.0722f * bf;
                            rf = luma + saturation * (rf - luma);
                            gf = luma + saturation * (gf - luma);
                            bf = luma + saturation * (bf - luma);

                            r = (byte)Math.Clamp((int)Math.Round(rf * 255.0f), 0, 255);
                            g = (byte)Math.Clamp((int)Math.Round(gf * 255.0f), 0, 255);
                            b = (byte)Math.Clamp((int)Math.Round(bf * 255.0f), 0, 255);
                        }

                        // Apply 3D LUT Trilinear Sample if active
                        if (apply3DLut)
                        {
                            SampleLut3D(r, g, b, out r, out g, out b);
                        }

                        p[0] = b;
                        p[1] = g;
                        p[2] = r;
                        // Alpha (p[3]) remains untouched
                        p += 4;
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SampleLut3D(byte rIn, byte gIn, byte bIn, out byte rOut, out byte gOut, out byte bOut)
        {
            int size = _lutSize;
            float maxIdx = size - 1;

            float rCoord = (rIn / 255.0f) * maxIdx;
            float gCoord = (gIn / 255.0f) * maxIdx;
            float bCoord = (bIn / 255.0f) * maxIdx;

            int r0 = (int)rCoord;
            int g0 = (int)gCoord;
            int b0 = (int)bCoord;

            int r1 = Math.Min(r0 + 1, size - 1);
            int g1 = Math.Min(g0 + 1, size - 1);
            int b1 = Math.Min(b0 + 1, size - 1);

            float rFrac = rCoord - r0;
            float gFrac = gCoord - g0;
            float bFrac = bCoord - b0;

            var cube = _lutData!;

            // Trilinear interpolation across 8 cube vertices
            float c000_r = cube[r0, g0, b0, 0];
            float c100_r = cube[r1, g0, b0, 0];
            float c010_r = cube[r0, g1, b0, 0];
            float c110_r = cube[r1, g1, b0, 0];
            float c001_r = cube[r0, g0, b1, 0];
            float c101_r = cube[r1, g0, b1, 0];
            float c011_r = cube[r0, g1, b1, 0];
            float c111_r = cube[r1, g1, b1, 0];

            float c00_r = c000_r * (1 - rFrac) + c100_r * rFrac;
            float c10_r = c010_r * (1 - rFrac) + c110_r * rFrac;
            float c01_r = c001_r * (1 - rFrac) + c101_r * rFrac;
            float c11_r = c011_r * (1 - rFrac) + c111_r * rFrac;

            float c0_r = c00_r * (1 - gFrac) + c10_r * gFrac;
            float c1_r = c01_r * (1 - gFrac) + c11_r * gFrac;
            float resR = c0_r * (1 - bFrac) + c1_r * bFrac;

            // Green
            float c000_g = cube[r0, g0, b0, 1];
            float c100_g = cube[r1, g0, b0, 1];
            float c010_g = cube[r0, g1, b0, 1];
            float c110_g = cube[r1, g1, b0, 1];
            float c001_g = cube[r0, g0, b1, 1];
            float c101_g = cube[r1, g0, b1, 1];
            float c011_g = cube[r0, g1, b1, 1];
            float c111_g = cube[r1, g1, b1, 1];

            float c00_g = c000_g * (1 - rFrac) + c100_g * rFrac;
            float c10_g = c010_g * (1 - rFrac) + c110_g * rFrac;
            float c01_g = c001_g * (1 - rFrac) + c101_g * rFrac;
            float c11_g = c011_g * (1 - rFrac) + c111_g * rFrac;

            float c0_g = c00_g * (1 - gFrac) + c10_g * gFrac;
            float c1_g = c01_g * (1 - gFrac) + c11_g * gFrac;
            float resG = c0_g * (1 - bFrac) + c1_g * bFrac;

            // Blue
            float c000_b = cube[r0, g0, b0, 2];
            float c100_b = cube[r1, g0, b0, 2];
            float c010_b = cube[r0, g1, b0, 2];
            float c110_b = cube[r1, g1, b0, 2];
            float c001_b = cube[r0, g0, b1, 2];
            float c101_b = cube[r1, g0, b1, 2];
            float c011_b = cube[r0, g1, b1, 2];
            float c111_b = cube[r1, g1, b1, 2];

            float c00_b = c000_b * (1 - rFrac) + c100_b * rFrac;
            float c10_b = c010_b * (1 - rFrac) + c110_b * rFrac;
            float c01_b = c001_b * (1 - rFrac) + c101_b * rFrac;
            float c11_b = c011_b * (1 - rFrac) + c111_b * rFrac;

            float c0_b = c00_b * (1 - gFrac) + c10_b * gFrac;
            float c1_b = c01_b * (1 - gFrac) + c11_b * gFrac;
            float resB = c0_b * (1 - bFrac) + c1_b * bFrac;

            rOut = (byte)Math.Clamp((int)Math.Round(resR * 255.0f), 0, 255);
            gOut = (byte)Math.Clamp((int)Math.Round(resG * 255.0f), 0, 255);
            bOut = (byte)Math.Clamp((int)Math.Round(resB * 255.0f), 0, 255);
        }
    }
}
