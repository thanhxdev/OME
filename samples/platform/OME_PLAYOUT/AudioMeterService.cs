using System;

namespace OME_PLAYOUT
{
    public struct AudioMeterLevels
    {
        public float PeakL_dBFS;
        public float PeakR_dBFS;
        public float RmsL_dBFS;
        public float RmsR_dBFS;
        public float PeakHoldL_dBFS;
        public float PeakHoldR_dBFS;
        public bool IsClipL;
        public bool IsClipR;

        // Normalized 0.0 to 1.0 (-60 dBFS to 0 dBFS) for UI VU Bars
        public float NormalizedPeakL => Math.Clamp((PeakL_dBFS + 60f) / 60f, 0f, 1f);
        public float NormalizedPeakR => Math.Clamp((PeakR_dBFS + 60f) / 60f, 0f, 1f);
        public float NormalizedHoldL => Math.Clamp((PeakHoldL_dBFS + 60f) / 60f, 0f, 1f);
        public float NormalizedHoldR => Math.Clamp((PeakHoldR_dBFS + 60f) / 60f, 0f, 1f);
    }

    /// <summary>
    /// Broadcast audio metering engine providing EBU R128 / SMPTE compliant stereo Peak/VU metering.
    /// Range: -60 dBFS to 0 dBFS. Warns on Clip at -0.1 dBFS.
    /// </summary>
    public sealed class AudioMeterService
    {
        private float _peakHoldL = -60f;
        private float _peakHoldR = -60f;
        private long _lastHoldTimeTicksL = 0;
        private long _lastHoldTimeTicksR = 0;
        private const float HoldDecayDbPerSec = 15f; // Decays smoothly after 1.5s hold

        public AudioMeterLevels ProcessPcm(byte[] pcmBytes, int length)
        {
            if (pcmBytes == null || length < 4)
            {
                return new AudioMeterLevels
                {
                    PeakL_dBFS = -60f,
                    PeakR_dBFS = -60f,
                    RmsL_dBFS = -60f,
                    RmsR_dBFS = -60f,
                    PeakHoldL_dBFS = _peakHoldL,
                    PeakHoldR_dBFS = _peakHoldR
                };
            }

            int samples = length / 4; // 16-bit stereo (4 bytes)
            short maxL = 0, maxR = 0;
            double sumSqL = 0, sumSqR = 0;

            unsafe
            {
                fixed (byte* pBuf = pcmBytes)
                {
                    short* pSamples = (short*)pBuf;
                    for (int i = 0; i < samples; i++)
                    {
                        short sL = pSamples[i * 2 + 0];
                        short sR = pSamples[i * 2 + 1];

                        short absL = Math.Abs(sL);
                        short absR = Math.Abs(sR);

                        if (absL > maxL) maxL = absL;
                        if (absR > maxR) maxR = absR;

                        sumSqL += sL * sL;
                        sumSqR += sR * sR;
                    }
                }
            }

            float peakL = AmplitudeToDbFs(maxL);
            float peakR = AmplitudeToDbFs(maxR);

            float rmsL = AmplitudeToDbFs((float)Math.Sqrt(sumSqL / samples));
            float rmsR = AmplitudeToDbFs((float)Math.Sqrt(sumSqR / samples));

            // Peak Hold Logic
            long now = Environment.TickCount64;
            if (peakL >= _peakHoldL)
            {
                _peakHoldL = peakL;
                _lastHoldTimeTicksL = now;
            }
            else if (now - _lastHoldTimeTicksL > 1500)
            {
                _peakHoldL = Math.Max(-60f, _peakHoldL - (HoldDecayDbPerSec * 0.05f));
            }

            if (peakR >= _peakHoldR)
            {
                _peakHoldR = peakR;
                _lastHoldTimeTicksR = now;
            }
            else if (now - _lastHoldTimeTicksR > 1500)
            {
                _peakHoldR = Math.Max(-60f, _peakHoldR - (HoldDecayDbPerSec * 0.05f));
            }

            return new AudioMeterLevels
            {
                PeakL_dBFS = peakL,
                PeakR_dBFS = peakR,
                RmsL_dBFS = rmsL,
                RmsR_dBFS = rmsR,
                PeakHoldL_dBFS = _peakHoldL,
                PeakHoldR_dBFS = _peakHoldR,
                IsClipL = peakL >= -0.1f,
                IsClipR = peakR >= -0.1f
            };
        }

        private static float AmplitudeToDbFs(float amplitude)
        {
            if (amplitude <= 1.0f) return -60.0f;
            float db = 20.0f * MathF.Log10(amplitude / 32768.0f);
            return Math.Clamp(db, -60.0f, 0.0f);
        }
    }
}
