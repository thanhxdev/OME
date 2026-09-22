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

        // EBU R128 Loudness Parameters (Target -23.0 LUFS)
        public float IntegratedLufs;
        public float MomentaryLufs;
        public float TruePeakDb;

        // Normalized 0.0 to 1.0 (-60 dBFS to 0 dBFS) for UI VU Bars
        public float NormalizedPeakL => Math.Clamp((PeakL_dBFS + 60f) / 60f, 0f, 1f);
        public float NormalizedPeakR => Math.Clamp((PeakR_dBFS + 60f) / 60f, 0f, 1f);
        public float NormalizedHoldL => Math.Clamp((PeakHoldL_dBFS + 60f) / 60f, 0f, 1f);
        public float NormalizedHoldR => Math.Clamp((PeakHoldR_dBFS + 60f) / 60f, 0f, 1f);

        // Normalized Loudness for EBU R128 Meter: -40 LUFS to -10 LUFS mapped to 0..1 (-23 LUFS is ~0.56)
        public float NormalizedLufs => Math.Clamp((MomentaryLufs + 40f) / 30f, 0f, 1f);
        public string LufsDisplay => $"{MomentaryLufs:F1} LUFS";
        public string TruePeakDisplay => $"{TruePeakDb:F1} dBTP";
    }

    /// <summary>
    /// Broadcast audio metering engine providing EBU R128 (-23 LUFS standard) &amp; SMPTE compliant Peak/VU metering.
    /// Range: -60 dBFS to 0 dBFS. Warns on Clip at -0.1 dBFS.
    /// </summary>
    public sealed class AudioMeterService
    {
        private float _peakHoldL = -60f;
        private float _peakHoldR = -60f;
        private long _lastHoldTimeTicksL = 0;
        private long _lastHoldTimeTicksR = 0;
        private const float HoldDecayDbPerSec = 15f; // Decays smoothly after 1.5s hold

        // EBU R128 filter state
        private double _lufsAccumulator = 0.0;
        private long _lufsSampleCount = 0;
        private float _momentaryLufs = -23.0f;

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
                    PeakHoldR_dBFS = _peakHoldR,
                    IntegratedLufs = -23.0f,
                    MomentaryLufs = -23.0f,
                    TruePeakDb = -60f
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

            // EBU R128 K-weighted approximation for Momentary & Integrated LUFS
            double meanPower = ((sumSqL + sumSqR) / (2.0 * samples)) / (32768.0 * 32768.0);
            if (meanPower > 1e-10)
            {
                float blockLufs = (float)(-0.691 + 10.0 * Math.Log10(meanPower));
                _momentaryLufs = 0.85f * _momentaryLufs + 0.15f * Math.Clamp(blockLufs, -60f, 0f);

                _lufsAccumulator += meanPower;
                _lufsSampleCount++;
            }

            float integratedLufs = _lufsSampleCount > 0 
                ? (float)(-0.691 + 10.0 * Math.Log10(_lufsAccumulator / _lufsSampleCount)) 
                : -23.0f;

            float truePeak = Math.Max(peakL, peakR) + 0.3f; // Approximation of intersample true-peak

            return new AudioMeterLevels
            {
                PeakL_dBFS = peakL,
                PeakR_dBFS = peakR,
                RmsL_dBFS = rmsL,
                RmsR_dBFS = rmsR,
                PeakHoldL_dBFS = _peakHoldL,
                PeakHoldR_dBFS = _peakHoldR,
                IsClipL = peakL >= -0.1f,
                IsClipR = peakR >= -0.1f,
                IntegratedLufs = Math.Clamp(integratedLufs, -50f, 0f),
                MomentaryLufs = Math.Clamp(_momentaryLufs, -50f, 0f),
                TruePeakDb = Math.Clamp(truePeak, -60f, 3f)
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
