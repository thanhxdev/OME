using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using OpenMedia.Platform;
using OpenMedia.SDK;

namespace SRT_DECODE
{
    /// <summary>
    /// Multi-Channel Real-Time Audio Meter Analyzer for Broadcast SRT Receiver.
    /// Supports 1 to 16 independent audio channels (Mono, Stereo, 4CH, 5.1 Surround, 7.1 Surround, 16CH SDI Embedded).
    /// Computes True Peak (dBFS), 300ms EWMA RMS (Broadcast VU Meter Standard), and Peak Hold with Clipping Detection.
    /// </summary>
    public sealed class AudioMeterService : IDisposable
    {
        public const int MAX_CHANNELS = 16;
        public const double MIN_DBFS = -60.0;
        public const double MAX_DBFS = 0.0;
        private const double NOISE_FLOOR_LINEAR = 0.000999; // < -60 dBFS

        private bool _disposed;
        private readonly object _lock = new();

        // 16-Channel Analyzer State
        private readonly double[] _currentPeakDb = new double[MAX_CHANNELS];
        private readonly double[] _currentRmsDb = new double[MAX_CHANNELS];
        private readonly double[] _peakHoldDb = new double[MAX_CHANNELS];
        private readonly bool[] _isClipping = new bool[MAX_CHANNELS];
        private readonly double[] _ewmaEnergy = new double[MAX_CHANNELS];
        private int _activeChannelCount = 2;

        // Native C++ AudioMeter Handle (if OpenMedia.Core native DLL is available)
        private IntPtr _nativeMeterHandle = IntPtr.Zero;
        private bool _isNativeAvailable = false;

        public int ActiveChannelCount
        {
            get => _activeChannelCount;
            set => _activeChannelCount = Math.Clamp(value, 1, MAX_CHANNELS);
        }

        public AudioMeterService()
        {
            ResetAllChannels();
            TryInitNativeMeter();
        }

        private void TryInitNativeMeter()
        {
            try
            {
                _nativeMeterHandle = NativeBridge.ome_audio_meter_create();
                _isNativeAvailable = (_nativeMeterHandle != IntPtr.Zero);
            }
            catch
            {
                _isNativeAvailable = false;
                _nativeMeterHandle = IntPtr.Zero;
            }
        }

        public void ResetAllChannels()
        {
            lock (_lock)
            {
                for (int i = 0; i < MAX_CHANNELS; i++)
                {
                    _currentPeakDb[i] = MIN_DBFS;
                    _currentRmsDb[i] = MIN_DBFS;
                    _peakHoldDb[i] = MIN_DBFS;
                    _isClipping[i] = false;
                    _ewmaEnergy[i] = 0.0;
                }
            }

            if (_isNativeAvailable && _nativeMeterHandle != IntPtr.Zero)
            {
                try
                {
                    NativeBridge.ome_audio_meter_reset(_nativeMeterHandle);
                }
                catch { }
            }
        }

        #region Multi-Channel PCM & Level Processing (True Peak, RMS EWMA, Clipping)

        /// <summary>
        /// Process interleaved float samples for multi-channel analysis.
        /// </summary>
        public void ProcessPcmFloat(ReadOnlySpan<float> samples, int channels, int sampleRate = 48000)
        {
            if (samples.Length == 0 || channels <= 0) return;
            int numChannels = Math.Min(channels, MAX_CHANNELS);
            _activeChannelCount = numChannels;

            int sampleCount = samples.Length / channels;
            if (sampleCount == 0) return;

            float sr = sampleRate > 0 ? sampleRate : 48000.0f;
            double alphaVu = 1.0 - Math.Exp(-1.0 / (sr * 0.300)); // 300ms window

            lock (_lock)
            {
                for (int ch = 0; ch < numChannels; ch++)
                {
                    double peakLinear = 0.0;

                    for (int i = 0; i < sampleCount; i++)
                    {
                        double sample = Math.Abs(samples[i * channels + ch]);
                        if (sample > peakLinear)
                        {
                            peakLinear = sample;
                        }

                        // EWMA 300ms Energy Integration
                        _ewmaEnergy[ch] += alphaVu * (sample * sample - _ewmaEnergy[ch]);
                    }

                    // Peak dBFS calculation
                    double peakDb = (peakLinear > NOISE_FLOOR_LINEAR) ? 20.0 * Math.Log10(peakLinear) : MIN_DBFS;
                    _currentPeakDb[ch] = Math.Clamp(peakDb, MIN_DBFS, MAX_DBFS);

                    // RMS dBFS calculation
                    double rmsLinear = Math.Sqrt(Math.Max(0.0, _ewmaEnergy[ch]));
                    double rmsDb = (rmsLinear > NOISE_FLOOR_LINEAR) ? 20.0 * Math.Log10(rmsLinear) : MIN_DBFS;
                    _currentRmsDb[ch] = Math.Clamp(rmsDb, MIN_DBFS, MAX_DBFS);

                    // Peak Hold & Clipping
                    if (_currentPeakDb[ch] > _peakHoldDb[ch])
                    {
                        _peakHoldDb[ch] = _currentPeakDb[ch];
                    }
                    else
                    {
                        _peakHoldDb[ch] = Math.Max(MIN_DBFS, _peakHoldDb[ch] - 0.2);
                    }

                    _isClipping[ch] = (peakLinear >= 0.988); // >= -0.1 dBFS
                }

                // Inactive channels reset
                for (int ch = numChannels; ch < MAX_CHANNELS; ch++)
                {
                    _currentPeakDb[ch] = MIN_DBFS;
                    _currentRmsDb[ch] = MIN_DBFS;
                    _peakHoldDb[ch] = MIN_DBFS;
                    _isClipping[ch] = false;
                }
            }
        }

        /// <summary>
        /// Process raw PCM bytes (S16, S32, Float32).
        /// </summary>
        public void ProcessPcmBytes(byte[] buffer, int offset, int count, int bitsPerSample, int channels, int sampleRate, bool isFloat)
        {
            if (buffer == null || count <= 0 || channels <= 0) return;
            int numChannels = Math.Min(channels, MAX_CHANNELS);
            _activeChannelCount = numChannels;

            int bytesPerSample = bitsPerSample / 8;
            int blockAlign = channels * bytesPerSample;
            int sampleCount = count / blockAlign;
            if (sampleCount == 0) return;

            float[] floatSamples = new float[sampleCount * numChannels];

            if (bitsPerSample == 16 && !isFloat)
            {
                for (int i = 0; i < sampleCount; i++)
                {
                    int sampleOffset = offset + (i * blockAlign);
                    for (int ch = 0; ch < numChannels; ch++)
                    {
                        short val = BitConverter.ToInt16(buffer, sampleOffset + (ch * 2));
                        floatSamples[i * numChannels + ch] = val / 32768.0f;
                    }
                }
            }
            else if (bitsPerSample == 32 && isFloat)
            {
                for (int i = 0; i < sampleCount; i++)
                {
                    int sampleOffset = offset + (i * blockAlign);
                    for (int ch = 0; ch < numChannels; ch++)
                    {
                        floatSamples[i * numChannels + ch] = BitConverter.ToSingle(buffer, sampleOffset + (ch * 4));
                    }
                }
            }
            else if (bitsPerSample == 32 && !isFloat)
            {
                for (int i = 0; i < sampleCount; i++)
                {
                    int sampleOffset = offset + (i * blockAlign);
                    for (int ch = 0; ch < numChannels; ch++)
                    {
                        int val = BitConverter.ToInt32(buffer, sampleOffset + (ch * 4));
                        floatSamples[i * numChannels + ch] = val / 2147483648.0f;
                    }
                }
            }

            ProcessPcmFloat(floatSamples, numChannels, sampleRate);
        }

        /// <summary>
        /// Feed direct channel dB levels into meter analyzer.
        /// </summary>
        public void FeedChannelLevels(double[] peakDbs, double[]? rmsDbs = null, int activeChannels = 2)
        {
            if (peakDbs == null || peakDbs.Length == 0) return;
            int channels = Math.Clamp(activeChannels, 1, MAX_CHANNELS);
            _activeChannelCount = channels;

            lock (_lock)
            {
                int count = Math.Min(peakDbs.Length, channels);
                for (int i = 0; i < count; i++)
                {
                    double peak = double.IsNaN(peakDbs[i]) ? MIN_DBFS : Math.Clamp(peakDbs[i], MIN_DBFS, MAX_DBFS);
                    double rms = (rmsDbs != null && i < rmsDbs.Length && !double.IsNaN(rmsDbs[i])) 
                        ? Math.Clamp(rmsDbs[i], MIN_DBFS, MAX_DBFS) 
                        : peak - 3.0;

                    _currentPeakDb[i] = peak;
                    _currentRmsDb[i] = Math.Clamp(rms, MIN_DBFS, MAX_DBFS);

                    if (peak > _peakHoldDb[i])
                    {
                        _peakHoldDb[i] = peak;
                    }
                    else
                    {
                        _peakHoldDb[i] = Math.Max(MIN_DBFS, _peakHoldDb[i] - 0.2);
                    }

                    _isClipping[i] = (peak >= -0.1);
                }

                for (int i = count; i < MAX_CHANNELS; i++)
                {
                    _currentPeakDb[i] = MIN_DBFS;
                    _currentRmsDb[i] = MIN_DBFS;
                    _peakHoldDb[i] = MIN_DBFS;
                    _isClipping[i] = false;
                }
            }
        }

        public void DecayMeters()
        {
            lock (_lock)
            {
                for (int i = 0; i < MAX_CHANNELS; i++)
                {
                    _currentPeakDb[i] = Math.Max(MIN_DBFS, _currentPeakDb[i] - 1.5);
                    _currentRmsDb[i] = Math.Max(MIN_DBFS, _currentRmsDb[i] - 1.5);
                    _peakHoldDb[i] = Math.Max(MIN_DBFS, _peakHoldDb[i] - 0.5);
                    _isClipping[i] = false;
                    _ewmaEnergy[i] = Math.Max(0.0, _ewmaEnergy[i] * 0.9);
                }
            }
        }

        #endregion

        #region UI Data Polling

        /// <summary>
        /// Extract multi-channel audio meter levels up to 16 channels.
        /// </summary>
        public void GetPreviewAudioLevels(
            double[] channelPeakLevels,
            double[]? channelRmsLevels = null,
            bool[]? clippingFlags = null,
            bool isAudioActive = true)
        {
            if (channelPeakLevels == null || channelPeakLevels.Length == 0) return;

            if (!isAudioActive)
            {
                DecayMeters();
            }

            lock (_lock)
            {
                int len = Math.Min(channelPeakLevels.Length, MAX_CHANNELS);
                for (int i = 0; i < len; i++)
                {
                    channelPeakLevels[i] = _currentPeakDb[i];
                    if (channelRmsLevels != null && i < channelRmsLevels.Length)
                    {
                        channelRmsLevels[i] = _currentRmsDb[i];
                    }
                    if (clippingFlags != null && i < clippingFlags.Length)
                    {
                        clippingFlags[i] = _isClipping[i];
                    }
                }
            }
        }

        public static double DbToPercent(double db)
        {
            if (db <= MIN_DBFS) return 0.0;
            if (db >= MAX_DBFS) return 100.0;
            // Broadcast scaling curve: -60dB -> 0%, -24dB -> 60%, -18dB -> 70%, 0dB -> 100%
            return ((db + 60.0) / 60.0) * 100.0;
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_nativeMeterHandle != IntPtr.Zero)
            {
                try
                {
                    NativeBridge.ome_audio_meter_destroy(_nativeMeterHandle);
                }
                catch { }
                _nativeMeterHandle = IntPtr.Zero;
            }

            GC.SuppressFinalize(this);
        }
    }
}
