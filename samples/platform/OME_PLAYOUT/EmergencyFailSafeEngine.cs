using System;

namespace OME_PLAYOUT
{
    public enum FailSafeFallbackMode
    {
        SmpteColorBars = 0,
        BackupIsoPort = 1
    }

    /// <summary>
    /// Broadcast Transmission Protection (Bảo vệ sóng):
    /// Continuously monitors incoming PGM signal integrity.
    /// Supports two fallback modes: SMPTE Color Bars (with 1kHz calibration tone) or automatic fail-over to Backup ISO Port.
    /// Configurable watchdog timeout delay (0.5s to 5.0s).
    /// </summary>
    public sealed class EmergencyFailSafeEngine
    {
        private long _lastSignalTicks = Environment.TickCount64;
        private bool _isFallbackActive = false;
        private byte[]? _smpteBarsBuffer;
        private byte[]? _tone1kHzBuffer;
        private int _tonePhase = 0;

        public bool IsEnabled { get; set; } = true;
        public double LossThresholdSeconds { get; set; } = 1.5;
        public FailSafeFallbackMode FallbackMode { get; set; } = FailSafeFallbackMode.SmpteColorBars;
        public int BackupIsoPort { get; set; } = 1; // Default to Cam 1 ISO (Port 1)
        public bool ForceTestFallback { get; set; } = false;

        public bool IsFallbackActive => _isFallbackActive;
        public double SecondsSinceLastSignal => (Environment.TickCount64 - _lastSignalTicks) / 1000.0;
        public bool IsPgmSignalAlive => SecondsSinceLastSignal < LossThresholdSeconds;

        /// <summary>
        /// True when fallback is active on Master Out, BUT PGM IN has regained signal and is streaming healthy frames.
        /// In this state, preview displays the live PGM IN feed while Master Out stays on fallback,
        /// allowing the operator to inspect the signal before manually switching back.
        /// </summary>
        public bool IsSignalRestored => _isFallbackActive && IsPgmSignalAlive;

        public event Action<bool>? FallbackStateChanged;

        public EmergencyFailSafeEngine()
        {
            GenerateSmpteColorBars(1920, 1080);
            Generate1kHzToneBuffer();
        }

        public void NotifySignalAlive()
        {
            _lastSignalTicks = Environment.TickCount64;
            // Broadcast safety rule: Do NOT auto-revert _isFallbackActive!
            // Master Out remains safely on fallback until the operator manually selects Port 0 to restore.
        }

        /// <summary>
        /// Operator manually acknowledges and restores Master Out to PGM IN.
        /// </summary>
        public void DismissFallback()
        {
            _isFallbackActive = false;
            ForceTestFallback = false;
            _lastSignalTicks = Environment.TickCount64;
            FallbackStateChanged?.Invoke(false);
        }

        /// <summary>
        /// Periodic check (invoked every frame or timer tick) to verify signal presence.
        /// </summary>
        public bool CheckSignalHealth()
        {
            if (!IsEnabled)
            {
                if (_isFallbackActive)
                {
                    _isFallbackActive = false;
                    FallbackStateChanged?.Invoke(false);
                }
                return true;
            }

            if (ForceTestFallback)
            {
                if (!_isFallbackActive)
                {
                    _isFallbackActive = true;
                    FallbackStateChanged?.Invoke(true);
                }
                return false;
            }

            double elapsed = SecondsSinceLastSignal;
            bool signalLost = elapsed >= LossThresholdSeconds;

            if (signalLost && !_isFallbackActive)
            {
                _isFallbackActive = true;
                FallbackStateChanged?.Invoke(true);
            }

            return !_isFallbackActive;
        }

        public byte[] GetSmpteColorBars(int width, int height)
        {
            if (_smpteBarsBuffer == null || _smpteBarsBuffer.Length < width * height * 4)
            {
                GenerateSmpteColorBars(width, height);
            }
            return _smpteBarsBuffer!;
        }

        public byte[] Get1kHzTone(int countBytes)
        {
            byte[] output = new byte[countBytes];
            int samples = countBytes / 4; // 16-bit stereo = 4 bytes/sample

            // -20 dBFS amplitude = 32767 * 10^(-20/20) = 32767 * 0.10 = ~3276
            short amplitude = 3276;

            for (int i = 0; i < samples; i++)
            {
                double angle = 2.0 * Math.PI * 1000.0 * (_tonePhase++) / 48000.0;
                short sample = (short)(amplitude * Math.Sin(angle));

                int offset = i * 4;
                output[offset + 0] = (byte)(sample & 0xFF);
                output[offset + 1] = (byte)((sample >> 8) & 0xFF);
                output[offset + 2] = (byte)(sample & 0xFF);
                output[offset + 3] = (byte)((sample >> 8) & 0xFF);
            }

            if (_tonePhase >= 48000) _tonePhase %= 48000;
            return output;
        }

        private void Generate1kHzToneBuffer()
        {
            _tone1kHzBuffer = Get1kHzTone(19200); // 100ms chunk
        }

        private void GenerateSmpteColorBars(int width, int height)
        {
            _smpteBarsBuffer = new byte[width * height * 4];

            // Standard SMPTE 75% Bars Colors (BGRA)
            // 7 main bars: 75% White, Yellow, Cyan, Green, Magenta, Red, Blue
            var barColors = new (byte B, byte G, byte R)[]
            {
                (191, 191, 191), // 75% White
                (0,   191, 191), // Yellow
                (191, 191, 0),   // Cyan
                (0,   191, 0),   // Green
                (191, 0,   191), // Magenta
                (0,   0,   191), // Red
                (191, 0,   0)    // Blue
            };

            int topH = (int)(height * 0.67);
            int midH = (int)(height * 0.08);
            int botH = height - topH - midH;

            int barCount = barColors.Length;
            int barW = width / barCount;

            unsafe
            {
                fixed (byte* pBuf = _smpteBarsBuffer)
                {
                    // 1. Top 67%: 7 Primary color bars
                    for (int y = 0; y < topH; y++)
                    {
                        byte* pRow = pBuf + y * width * 4;
                        for (int x = 0; x < width; x++)
                        {
                            int barIdx = Math.Min(x / barW, barCount - 1);
                            var col = barColors[barIdx];
                            pRow[0] = col.B;
                            pRow[1] = col.G;
                            pRow[2] = col.R;
                            pRow[3] = 255;
                            pRow += 4;
                        }
                    }

                    // 2. Middle 8%: Castellation reverse bars (Blue, Black, Magenta, Black, Cyan, Black, 75% White)
                    var midColors = new (byte B, byte G, byte R)[]
                    {
                        (191, 0,   0),   // Blue
                        (19,  19,  19),  // Black
                        (191, 0,   191), // Magenta
                        (19,  19,  19),  // Black
                        (191, 191, 0),   // Cyan
                        (19,  19,  19),  // Black
                        (191, 191, 191)  // 75% White
                    };

                    for (int y = topH; y < topH + midH; y++)
                    {
                        byte* pRow = pBuf + y * width * 4;
                        for (int x = 0; x < width; x++)
                        {
                            int barIdx = Math.Min(x / barW, barCount - 1);
                            var col = midColors[barIdx];
                            pRow[0] = col.B;
                            pRow[1] = col.G;
                            pRow[2] = col.R;
                            pRow[3] = 255;
                            pRow += 4;
                        }
                    }

                    // 3. Bottom 25%: I/Q, 100% White, Pluge pulse (-2%, 0%, +2%), Black
                    for (int y = topH + midH; y < height; y++)
                    {
                        byte* pRow = pBuf + y * width * 4;
                        for (int x = 0; x < width; x++)
                        {
                            double xNorm = (double)x / width;
                            byte b = 0, g = 0, r = 0;

                            if (xNorm < 0.18) // -I / Cyan-Navy
                            {
                                b = 130; g = 60; r = 0;
                            }
                            else if (xNorm < 0.36) // 100% White
                            {
                                b = 255; g = 255; r = 255;
                            }
                            else if (xNorm < 0.54) // +Q / Purple-Blue
                            {
                                b = 120; g = 0; r = 50;
                            }
                            else if (xNorm < 0.72) // 0% Black
                            {
                                b = 19; g = 19; r = 19;
                            }
                            else if (xNorm < 0.77) // Pluge -2%
                            {
                                b = 9; g = 9; r = 9;
                            }
                            else if (xNorm < 0.82) // Pluge 0%
                            {
                                b = 19; g = 19; r = 19;
                            }
                            else if (xNorm < 0.87) // Pluge +2%
                            {
                                b = 29; g = 29; r = 29;
                            }
                            else // Black
                            {
                                b = 19; g = 19; r = 19;
                            }

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
    }
}
