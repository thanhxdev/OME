using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

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
    /// Encapsulates an isolated color grading state (CCU sliders + 3D LUT + 1D precomputed lookup tables)
    /// for a single target (PGM Master, Ingest Slot 1..10, or a specific playlist video clip).
    /// </summary>
    public sealed class ColorGradingProfile
    {
        public ColorGradingParameters Parameters { get; } = new();

        // High-Performance flattened 1D LUT array: size*size*size*3 floats
        public float[]? FlatLut { get; private set; }
        public float[,,,]? LutData { get; private set; } // Maintained for backward compatibility
        public int LutSize { get; private set; } = 0;
        public string LoadedLutName { get; private set; } = "None";
        public bool IsLutEnabled { get; set; } = false;

        // Precalculated fast 1D LUT lookup tables for 8-bit channels
        public readonly byte[] LutR = new byte[256];
        public readonly byte[] LutG = new byte[256];
        public readonly byte[] LutB = new byte[256];
        private volatile bool _tablesDirty = true;
        public bool TablesDirty => _tablesDirty;

        public ColorGradingProfile()
        {
            RebuildTables();
        }

        public void Invalidate()
        {
            _tablesDirty = true;
        }

        public bool IsNeutral =>
            Parameters.IsNeutral && (!IsLutEnabled || FlatLut == null);

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

                int totalEntries = size * size * size;
                var flat = new float[totalEntries * 3];
                var cube = new float[size, size, size, 3];
                int currentEntry = 0;

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
                        r = Math.Clamp(r, 0f, 1f);
                        g = Math.Clamp(g, 0f, 1f);
                        b = Math.Clamp(b, 0f, 1f);

                        // In .cube files, the order of iteration is red fastest, then green, then blue
                        int bIdx = currentEntry / (size * size);
                        int gIdx = (currentEntry / size) % size;
                        int rIdx = currentEntry % size;

                        cube[rIdx, gIdx, bIdx, 0] = r;
                        cube[rIdx, gIdx, bIdx, 1] = g;
                        cube[rIdx, gIdx, bIdx, 2] = b;

                        int baseIdx = currentEntry * 3;
                        flat[baseIdx + 0] = r;
                        flat[baseIdx + 1] = g;
                        flat[baseIdx + 2] = b;

                        currentEntry++;
                    }
                }

                LutData = cube;
                FlatLut = flat;
                LutSize = size;
                LoadedLutName = Path.GetFileName(filePath);
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
            LutData = null;
            FlatLut = null;
            LutSize = 0;
            LoadedLutName = "None";
            IsLutEnabled = false;
            _tablesDirty = true;
        }

        public void RebuildTables()
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
                LutR[i] = (byte)Math.Clamp((int)Math.Round(rGraded * 255.0f), 0, 255);

                // Green channel
                float gLifted = Math.Max(inVal + liftG * (1.0f - inVal), 0.0f);
                float gPowered = MathF.Pow(gLifted, 1.0f / gammaG);
                float gGraded = gainG * gPowered;
                gGraded = (gGraded - 0.5f) * contrast + 0.5f + brightness;
                LutG[i] = (byte)Math.Clamp((int)Math.Round(gGraded * 255.0f), 0, 255);

                // Blue channel
                float bLifted = Math.Max(inVal + liftB * (1.0f - inVal), 0.0f);
                float bPowered = MathF.Pow(bLifted, 1.0f / gammaB);
                float bGraded = gainB * bPowered;
                bGraded = (bGraded - 0.5f) * contrast + 0.5f + brightness;
                LutB[i] = (byte)Math.Clamp((int)Math.Round(bGraded * 255.0f), 0, 255);
            }

            _tablesDirty = false;
        }
    }

    /// <summary>
    /// Multi-Target Color Grading & 3D LUT Engine.
    /// Manages independent profiles for PGM Master, Ingest Slots (1..10), and Playlist Clip files.
    /// Provides parallelized, zero-stutter 60 FPS processing with integer fixed-point arithmetic
    /// and flattened 3D LUT trilinear interpolation.
    /// </summary>
    public sealed class ColorGradingEngine
    {
        // Profiles Registry
        public ColorGradingProfile MasterProfile { get; } = new();
        private readonly ConcurrentDictionary<int, ColorGradingProfile> _slotProfiles = new();
        private readonly ConcurrentDictionary<string, ColorGradingProfile> _clipProfiles = new();

        // Convenience forwarding to MasterProfile (backward compatibility)
        public ColorGradingParameters Parameters => MasterProfile.Parameters;
        public bool IsLutEnabled { get => MasterProfile.IsLutEnabled; set => MasterProfile.IsLutEnabled = value; }
        public int LutSize => MasterProfile.LutSize;
        public string LoadedLutName => MasterProfile.LoadedLutName;

        public void Invalidate() => MasterProfile.Invalidate();
        public bool LoadCubeFile(string filePath) => MasterProfile.LoadCubeFile(filePath);
        public void UnloadLut() => MasterProfile.UnloadLut();

        /// <summary>
        /// Retrieves or creates an isolated color grading profile for a specific Ingest Slot (1..10).
        /// </summary>
        public ColorGradingProfile GetSlotProfile(int slot)
        {
            return _slotProfiles.GetOrAdd(slot, _ => new ColorGradingProfile());
        }

        /// <summary>
        /// Retrieves or creates an isolated color grading profile for a specific video clip.
        /// </summary>
        public ColorGradingProfile GetClipProfile(string clipKey)
        {
            if (string.IsNullOrWhiteSpace(clipKey)) return MasterProfile;
            return _clipProfiles.GetOrAdd(clipKey, _ => new ColorGradingProfile());
        }

        /// <summary>
        /// Resolves the active profile given a target mode (-1: Master, 1..10: Slot, -2: Clip).
        /// </summary>
        public ColorGradingProfile GetProfileForTarget(int targetMode, string? clipKey = null)
        {
            if (targetMode == -1) return MasterProfile;
            if (targetMode >= 1 && targetMode <= 10) return GetSlotProfile(targetMode);
            if (targetMode == -2 && !string.IsNullOrWhiteSpace(clipKey)) return GetClipProfile(clipKey);
            return MasterProfile;
        }

        /// <summary>
        /// High-Performance parallelized BGRA32 color grading applicator.
        /// Uses multi-threaded scanline decomposition, integer fixed-point saturation,
        /// and fast flattened 3D LUT trilinear interpolation to run in less than 2ms at 1080p60.
        /// </summary>
        public void ApplyToBgraBuffer(byte[] buffer, int width, int height, ColorGradingProfile? profile = null)
        {
            profile ??= MasterProfile;
            if (buffer == null || buffer.Length < width * height * 4) return;
            if (profile.IsNeutral) return;

            if (profile.TablesDirty)
            {
                profile.RebuildTables();
            }

            float saturation = profile.Parameters.Saturation;
            bool applySat = Math.Abs(saturation - 1.0f) > 0.01f;
            int satFixed = (int)MathF.Round(saturation * 256.0f);

            bool apply3DLut = profile.IsLutEnabled && profile.FlatLut != null && profile.LutSize >= 2;
            var flatLut = profile.FlatLut;
            int lutSize = profile.LutSize;
            float maxIdx = lutSize - 1;
            float rMul = maxIdx / 255.0f;
            int stride = width * 4;

            byte[] lutR = profile.LutR;
            byte[] lutG = profile.LutG;
            byte[] lutB = profile.LutB;

            unsafe
            {
                fixed (byte* pBuf = buffer)
                fixed (float* pFlat = flatLut)
                fixed (byte* pLutR = lutR)
                fixed (byte* pLutG = lutG)
                fixed (byte* pLutB = lutB)
                {
                    IntPtr bufPtr = (IntPtr)pBuf;
                    IntPtr flatPtr = (IntPtr)pFlat;
                    IntPtr lutRPtr = (IntPtr)pLutR;
                    IntPtr lutGPtr = (IntPtr)pLutG;
                    IntPtr lutBPtr = (IntPtr)pLutB;

                    Parallel.For(0, height, y =>
                    {
                        byte* p = (byte*)bufPtr + y * stride;
                        byte* pLutBLocal = (byte*)lutBPtr;
                        byte* pLutGLocal = (byte*)lutGPtr;
                        byte* pLutRLocal = (byte*)lutRPtr;
                        float* pFlatLocal = (float*)flatPtr;

                        for (int x = 0; x < width; x++)
                        {
                            byte b = pLutBLocal[p[0]];
                            byte g = pLutGLocal[p[1]];
                            byte r = pLutRLocal[p[2]];

                            // Fast Integer Fixed-Point Saturation (BT.709 Luma: 0.2126, 0.7152, 0.0722)
                            if (applySat)
                            {
                                int luma = (54 * r + 183 * g + 18 * b) >> 8;
                                r = (byte)Math.Clamp(luma + ((satFixed * (r - luma)) >> 8), 0, 255);
                                g = (byte)Math.Clamp(luma + ((satFixed * (g - luma)) >> 8), 0, 255);
                                b = (byte)Math.Clamp(luma + ((satFixed * (b - luma)) >> 8), 0, 255);
                            }

                            // Fast inlined Trilinear 3D LUT Interpolation using flattened float array
                            if (apply3DLut && pFlatLocal != null)
                            {
                                SampleLut3DFast(pFlatLocal, lutSize, maxIdx, rMul, r, g, b, out r, out g, out b);
                            }

                            p[0] = b;
                            p[1] = g;
                            p[2] = r;
                            // Alpha (p[3]) remains untouched
                            p += 4;
                        }
                    });
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void SampleLut3DFast(
            float* flat, int size, float maxIdx, float rMul,
            byte rIn, byte gIn, byte bIn,
            out byte rOut, out byte gOut, out byte bOut)
        {
            float rCoord = rIn * rMul;
            float gCoord = gIn * rMul;
            float bCoord = bIn * rMul;

            int r0 = (int)rCoord;
            int g0 = (int)gCoord;
            int b0 = (int)bCoord;

            int r1 = r0 < size - 1 ? r0 + 1 : r0;
            int g1 = g0 < size - 1 ? g0 + 1 : g0;
            int b1 = b0 < size - 1 ? b0 + 1 : b0;

            float rFrac = rCoord - r0;
            float gFrac = gCoord - g0;
            float bFrac = bCoord - b0;

            int b0_sz = b0 * size;
            int b1_sz = b1 * size;

            int idx000 = ((b0_sz + g0) * size + r0) * 3;
            int idx100 = ((b0_sz + g0) * size + r1) * 3;
            int idx010 = ((b0_sz + g1) * size + r0) * 3;
            int idx110 = ((b0_sz + g1) * size + r1) * 3;

            int idx001 = ((b1_sz + g0) * size + r0) * 3;
            int idx101 = ((b1_sz + g0) * size + r1) * 3;
            int idx011 = ((b1_sz + g1) * size + r0) * 3;
            int idx111 = ((b1_sz + g1) * size + r1) * 3;

            // Red Channel
            float c00_r = flat[idx000] * (1f - rFrac) + flat[idx100] * rFrac;
            float c10_r = flat[idx010] * (1f - rFrac) + flat[idx110] * rFrac;
            float c01_r = flat[idx001] * (1f - rFrac) + flat[idx101] * rFrac;
            float c11_r = flat[idx011] * (1f - rFrac) + flat[idx111] * rFrac;
            float c0_r = c00_r * (1f - gFrac) + c10_r * gFrac;
            float c1_r = c01_r * (1f - gFrac) + c11_r * gFrac;
            float resR = c0_r * (1f - bFrac) + c1_r * bFrac;

            // Green Channel
            float c00_g = flat[idx000 + 1] * (1f - rFrac) + flat[idx100 + 1] * rFrac;
            float c10_g = flat[idx010 + 1] * (1f - rFrac) + flat[idx110 + 1] * rFrac;
            float c01_g = flat[idx001 + 1] * (1f - rFrac) + flat[idx101 + 1] * rFrac;
            float c11_g = flat[idx011 + 1] * (1f - rFrac) + flat[idx111 + 1] * rFrac;
            float c0_g = c00_g * (1f - gFrac) + c10_g * gFrac;
            float c1_g = c01_g * (1f - gFrac) + c11_g * gFrac;
            float resG = c0_g * (1f - bFrac) + c1_g * bFrac;

            // Blue Channel
            float c00_b = flat[idx000 + 2] * (1f - rFrac) + flat[idx100 + 2] * rFrac;
            float c10_b = flat[idx010 + 2] * (1f - rFrac) + flat[idx110 + 2] * rFrac;
            float c01_b = flat[idx001 + 2] * (1f - rFrac) + flat[idx101 + 2] * rFrac;
            float c11_b = flat[idx011 + 2] * (1f - rFrac) + flat[idx111 + 2] * rFrac;
            float c0_b = c00_b * (1f - gFrac) + c10_b * gFrac;
            float c1_b = c01_b * (1f - gFrac) + c11_b * gFrac;
            float resB = c0_b * (1f - bFrac) + c1_b * bFrac;

            rOut = (byte)Math.Clamp((int)(resR * 255f + 0.5f), 0, 255);
            gOut = (byte)Math.Clamp((int)(resG * 255f + 0.5f), 0, 255);
            bOut = (byte)Math.Clamp((int)(resB * 255f + 0.5f), 0, 255);
        }
    }
}
