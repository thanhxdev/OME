using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using OpenMedia.Platform;
using OpenMedia.SDK;
using PlatformMediaPlayer = OpenMedia.Platform.MediaPlayer;

namespace SRT_ENCODE
{
    /// <summary>
    /// Cung cấp dịch vụ phân tích âm thanh đa kênh thời gian thực (Multi-Channel Real-time Audio Analyzer).
    /// Hỗ trợ từ 1 đến 16 kênh âm thanh độc lập (Mono, Stereo, 5.1, 7.1, SDI 16-Ch Embedded).
    /// Tính toán cả True Peak, 300ms EWMA RMS (VU Meter chuẩn Broadcast) và Clipping Detection.
    /// Hoàn toàn phân tích dữ liệu PCM thật thu được từ Tapping Point của Core Backend / Player hoặc luồng PCM trực tiếp.
    /// </summary>
    public sealed class AudioMeterService : IDisposable
    {
        public const int MAX_CHANNELS = 16;
        private const double MIN_DBFS = -60.0;
        private const double MAX_DBFS = 0.0;
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

        public int ActiveChannelCount => _activeChannelCount;

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

        #region Real Audio Tapping Ingestion (Backend Core, Player, Colorbar, Direct PCM)

        /// <summary>
        /// Chích xuất số liệu đo kiểm thời gian thực từ MediaPlayer backend (File, NDI, SDI, SRT).
        /// Dữ liệu đo đạc (Peak, RMS, Clip) được tính toán trực tiếp từ luồng PCM đã giải mã trên Core Backend.
        /// </summary>
        public async Task TapPlayerAudioAsync(PlatformMediaPlayer? player, int activeChannels = 2)
        {
            if (player == null || player.State != PlaybackState.Playing)
            {
                DecayMeters();
                return;
            }

            _activeChannelCount = Math.Clamp(activeChannels, 1, MAX_CHANNELS);

            try
            {
                var levels = await player.GetAudioLevelsAsync();
                if (levels != null && levels.Length > 0)
                {
                    UpdateFromNativeChannelData(levels, _activeChannelCount);
                }
                else
                {
                    DecayMeters();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioMeterService] TapPlayerAudioAsync Error: {ex.Message}");
                DecayMeters();
            }
        }

        /// <summary>
        /// Cập nhật trực tiếp số liệu phân tích từ mảng AudioChannelMeterData nhận từ Native Backend
        /// </summary>
        public void UpdateFromNativeChannelData(NativeBridge.AudioChannelMeterData[] meterData, int activeChannels = 2)
        {
            if (meterData == null || meterData.Length == 0) return;

            int channels = Math.Clamp(activeChannels, 1, MAX_CHANNELS);
            _activeChannelCount = channels;

            lock (_lock)
            {
                int count = Math.Min(meterData.Length, channels);
                for (int i = 0; i < count; i++)
                {
                    double peak = double.IsNaN(meterData[i].PeakDb) ? MIN_DBFS : Math.Clamp(meterData[i].PeakDb, MIN_DBFS, MAX_DBFS);
                    double rms = double.IsNaN(meterData[i].RmsDb) ? MIN_DBFS : Math.Clamp(meterData[i].RmsDb, MIN_DBFS, MAX_DBFS);

                    _currentPeakDb[i] = peak;
                    _currentRmsDb[i] = rms;

                    if (peak > _peakHoldDb[i])
                    {
                        _peakHoldDb[i] = peak;
                    }
                    else
                    {
                        _peakHoldDb[i] = Math.Max(MIN_DBFS, _peakHoldDb[i] - 0.2);
                    }

                    _isClipping[i] = meterData[i].Clipping || (peak >= -0.1);
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

        /// <summary>
        /// Chích luồng PCM âm thanh từ Colorbar Test Tone thực tế
        /// </summary>
        public void TapColorbarTone(AudioTestToneType toneType, double volume, int activeChannels = 2, int sampleRate = 48000)
        {
            activeChannels = Math.Clamp(activeChannels, 1, MAX_CHANNELS);
            _activeChannelCount = activeChannels;

            int sampleCount = 960; // 20ms block at 48kHz
            float[] samples = new float[sampleCount * activeChannels];

            double targetDbFs = (toneType == AudioTestToneType.Sine1kHzMinus20dBFS) ? -20.0 : -18.0;
            double peakAmplitude = Math.Pow(10.0, targetDbFs / 20.0) * Math.Clamp(volume, 0.0, 1.0);
            double freq = (toneType == AudioTestToneType.Glits400Hz) ? 400.0 : 1000.0;

            long tick = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
            double timeBase = (tick % 100000) / 1000.0;

            for (int i = 0; i < sampleCount; i++)
            {
                double t = timeBase + ((double)i / sampleRate);
                double leftVal = Math.Sin(2.0 * Math.PI * freq * t) * peakAmplitude;
                double rightVal = leftVal;

                if (toneType == AudioTestToneType.Glits400Hz)
                {
                    bool rightOn = (t % 2.0) < 1.4; // 1.4s on, 0.6s off
                    rightVal = rightOn ? leftVal : 0.0;
                }
                else if (toneType == AudioTestToneType.EbuToneIdent)
                {
                    bool rightBeep = (t % 2.0) < 0.35; // 350ms pulse every 2s
                    rightVal = rightBeep ? leftVal : 0.0;
                }

                samples[i * activeChannels + 0] = (float)leftVal;
                if (activeChannels >= 2)
                {
                    samples[i * activeChannels + 1] = (float)rightVal;
                }

                // If multi-channel SDI test tone (channels 3..16), mirror tone with slight attenuation
                for (int ch = 2; ch < activeChannels; ch++)
                {
                    double chAtten = Math.Pow(10.0, (-18.0 - ((ch - 1) * 3.0)) / 20.0) * volume;
                    samples[i * activeChannels + ch] = (float)(Math.Sin(2.0 * Math.PI * (freq + (ch * 100)) * t) * chAtten);
                }
            }

            ProcessPcmFloat(samples, activeChannels, sampleRate);
        }

        /// <summary>
        /// Chích luồng PCM trực tiếp từ mảng float samples (Interleaved)
        /// </summary>
        public void TapPcmDirect(ReadOnlySpan<float> samples, int channels, int sampleRate = 48000)
        {
            ProcessPcmFloat(samples, channels, sampleRate);
        }

        #endregion

        #region Core Multi-Channel PCM Processing (True Peak, RMS EWMA, Clipping)

        /// <summary>
        /// Phân tích mảng float samples Interleaved
        /// </summary>
        public void ProcessPcmFloat(ReadOnlySpan<float> samples, int channels, int sampleRate)
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
        /// Phân tích mảng byte PCM thô (S16, S32, Float32)
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
        /// Trích xuất toàn bộ dữ liệu đo kiểm âm thanh cho 16 kênh
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
