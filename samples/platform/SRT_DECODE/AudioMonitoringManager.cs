using System;
using System.Diagnostics;

namespace SRT_DECODE
{
    public enum SoloAudioSource
    {
        ProgramMaster,
        Cam1,
        Cam2,
        Cam3,
        Cam4,
        Cam5,
        Cam6,
        Cam7,
        Cam8,
        Cam9,
        Cam10
    }

    public enum AudioChannelConfiguration
    {
        Stereo2Ch = 2,
        Channels4Ch = 4,
        Surround51_6Ch = 6,
        Surround71_8Ch = 8,
        SdiEmbedded16Ch = 16
    }

    public sealed class ChannelAudioLevels
    {
        public const int MaxAudioChannels = 16;
        public double[] PeakDb { get; } = new double[MaxAudioChannels];
        public double[] RmsDb { get; } = new double[MaxAudioChannels];
        public double[] PeakPercent { get; } = new double[MaxAudioChannels];
        public double[] RmsPercent { get; } = new double[MaxAudioChannels];
        public bool[] IsChannelClipping { get; } = new bool[MaxAudioChannels];
        public bool IsClipping { get; set; }

        public double LeftDb => PeakDb[0];
        public double RightDb => PeakDb[1];
        public double LeftPercent => PeakPercent[0];
        public double RightPercent => PeakPercent[1];

        public ChannelAudioLevels()
        {
            Reset();
        }

        public void Reset()
        {
            for (int i = 0; i < MaxAudioChannels; i++)
            {
                PeakDb[i] = AudioMeterService.MIN_DBFS;
                RmsDb[i] = AudioMeterService.MIN_DBFS;
                PeakPercent[i] = 0.0;
                RmsPercent[i] = 0.0;
                IsChannelClipping[i] = false;
            }
            IsClipping = false;
        }

        public void CopyFrom(double[] peakDbs, double[]? rmsDbs, bool[]? clips, int activeChannels)
        {
            int count = Math.Clamp(activeChannels, 1, MaxAudioChannels);
            bool anyClip = false;

            for (int i = 0; i < count; i++)
            {
                double peak = (i < peakDbs.Length) ? peakDbs[i] : AudioMeterService.MIN_DBFS;
                double rms = (rmsDbs != null && i < rmsDbs.Length) ? rmsDbs[i] : AudioMeterService.MIN_DBFS;
                bool clip = (clips != null && i < clips.Length) && clips[i];

                PeakDb[i] = peak;
                RmsDb[i] = rms;
                PeakPercent[i] = AudioMeterService.DbToPercent(peak);
                RmsPercent[i] = AudioMeterService.DbToPercent(rms);
                IsChannelClipping[i] = clip;

                if (clip) anyClip = true;
            }

            for (int i = count; i < MaxAudioChannels; i++)
            {
                PeakDb[i] = AudioMeterService.MIN_DBFS;
                RmsDb[i] = AudioMeterService.MIN_DBFS;
                PeakPercent[i] = 0.0;
                RmsPercent[i] = 0.0;
                IsChannelClipping[i] = false;
            }

            IsClipping = anyClip;
        }
    }

    /// <summary>
    /// Professional Multi-Channel Software Audio Mixer & Monitoring Manager for SRT Multi-Decoder.
    /// Features:
    /// - Jitter ring buffers per camera channel for zero-drift packet absorption
    /// - Sample-accurate real-time multi-track summing mixer (0% alternating pops)
    /// - Per-channel faders (-60 dB to +12 dB) with slew-rate parameter smoothing (zero zipper noise)
    /// - Equal-power stereo panning (L/C/R)
    /// - Anti-pop 10ms micro-fade ramping on Mute/Unmute/Solo changes
    /// - Soft-knee peak limiter to eliminate clipping distortion
    /// </summary>
    public sealed class AudioMonitoringManager : IDisposable
    {
        public const int MaxCameras = 10;
        public const int MaxAudioChannels = 16;

        private readonly ChannelAudioLevels[] _camLevels = new ChannelAudioLevels[MaxCameras];
        private readonly ChannelAudioLevels _programLevels = new();
        private readonly AudioMeterService[] _camMeters = new AudioMeterService[MaxCameras];
        private readonly AudioMeterService _programMeter = new();

        private readonly AudioRingBuffer[] _camRingBuffers = new AudioRingBuffer[MaxCameras];
        private readonly double[] _channelGainDb = new double[MaxCameras];
        private readonly double[] _channelPan = new double[MaxCameras];
        private readonly bool[] _channelMuted = new bool[MaxCameras];

        // DSP runtime per-channel state
        private readonly float[] _currentGain = new float[MaxCameras];
        private readonly float[] _targetGain = new float[MaxCameras];
        private readonly float[] _currentMuteRamp = new float[MaxCameras];
        private readonly float[] _targetMuteRamp = new float[MaxCameras];
        private readonly long[] _lastPcmReceivedTicks = new long[MaxCameras];

        private SoloAudioSource _soloSource = SoloAudioSource.ProgramMaster;
        private AudioChannelConfiguration _channelConfig = AudioChannelConfiguration.Stereo2Ch;
        private bool _isMuteAll = false;
        private double _monitorVolumePercent = 80.0;
        private float _currentMasterRamp = 0.8f;
        private int _currentProgramIndex = 0;
        private bool _disposed;

        private readonly AudioOutputDevice _audioOutput = new();

        // Scratch buffers for mixer loop to prevent GC allocations
        private readonly byte[] _tempChannelPcm = new byte[9600];
        private readonly float[] _mixL = new float[2400];
        private readonly float[] _mixR = new float[2400];

        // Events
        public event Action<ChannelAudioLevels[]>? CamLevelsUpdated;
        public event Action<ChannelAudioLevels>? ProgramLevelsUpdated;
        public event Action<string, string>? LogEmitted;
        public event Action<int, double>? ChannelGainChanged;
        public event Action<int, double>? ChannelPanChanged;
        public event Action<int, bool>? ChannelMuteChanged;

        public AudioChannelConfiguration ChannelConfig
        {
            get => _channelConfig;
            set
            {
                _channelConfig = value;
                int chCount = (int)value;
                for (int i = 0; i < MaxCameras; i++)
                {
                    _camMeters[i].ActiveChannelCount = chCount;
                }
                _programMeter.ActiveChannelCount = chCount;
                Log("[AUDIO]", $"Cấu hình số kênh âm thanh giám sát: {GetChannelConfigName(value)} ({chCount} Kênh)");
            }
        }

        public int ConfiguredChannelCount => (int)_channelConfig;

        public int CurrentProgramIndex
        {
            get => _currentProgramIndex;
            set => _currentProgramIndex = Math.Clamp(value, 0, MaxCameras - 1);
        }

        public bool IsChannelMuted(int channelIndex)
        {
            if (channelIndex < 0 || channelIndex >= MaxCameras) return false;
            return _channelMuted[channelIndex];
        }

        public void SetChannelMuted(int channelIndex, bool isMuted, string? channelName = null)
        {
            if (channelIndex < 0 || channelIndex >= MaxCameras) return;
            _channelMuted[channelIndex] = isMuted;

            string name = !string.IsNullOrEmpty(channelName) ? channelName : $"CAM {channelIndex + 1}";
            Log("[AUDIO]", isMuted 
                ? $"🔇 MUTE kênh âm thanh: {name}" 
                : $"🔊 UNMUTE kênh âm thanh: {name}");

            ChannelMuteChanged?.Invoke(channelIndex, isMuted);
            _audioOutput.Wake();
        }

        public double GetChannelGain(int channelIndex)
        {
            if (channelIndex < 0 || channelIndex >= MaxCameras) return 0.0;
            return _channelGainDb[channelIndex];
        }

        public void SetChannelGain(int channelIndex, double gainDb)
        {
            if (channelIndex < 0 || channelIndex >= MaxCameras) return;
            double clamped = Math.Clamp(gainDb, -60.0, 12.0);
            _channelGainDb[channelIndex] = clamped;
            ChannelGainChanged?.Invoke(channelIndex, clamped);
            _audioOutput.Wake();
        }

        public double GetChannelPan(int channelIndex)
        {
            if (channelIndex < 0 || channelIndex >= MaxCameras) return 0.0;
            return _channelPan[channelIndex];
        }

        public void SetChannelPan(int channelIndex, double pan)
        {
            if (channelIndex < 0 || channelIndex >= MaxCameras) return;
            double clamped = Math.Clamp(pan, -1.0, 1.0);
            _channelPan[channelIndex] = clamped;
            ChannelPanChanged?.Invoke(channelIndex, clamped);
            _audioOutput.Wake();
        }

        public SoloAudioSource SoloSource
        {
            get => _soloSource;
            set
            {
                _soloSource = value;
                Log("[AUDIO]", $"Kiểm âm Solo chuyển sang: {GetSoloLabel(value)}");
                _audioOutput.Wake();
            }
        }

        public bool IsMuteAll
        {
            get => _isMuteAll;
            set
            {
                _isMuteAll = value;
                Log("[AUDIO]", _isMuteAll ? "🔇 MUTE ALL kiểm âm Master" : "🔊 UNMUTE kiểm âm Master");
                _audioOutput.Wake();
            }
        }

        public double MonitorVolumePercent
        {
            get => _monitorVolumePercent;
            set
            {
                _monitorVolumePercent = Math.Clamp(value, 0.0, 100.0);
                _audioOutput.Wake();
            }
        }

        public AudioMonitoringManager()
        {
            for (int i = 0; i < MaxCameras; i++)
            {
                _camLevels[i] = new ChannelAudioLevels();
                _camMeters[i] = new AudioMeterService();
                _camRingBuffers[i] = new AudioRingBuffer(capacityBytes: 96000, preRollMs: 50); // 500ms buffer capacity with 50ms adaptive jitter pre-roll
                _channelMuted[i] = true; // Mặc định các preview màn hình ingest đều được Mute
                _channelGainDb[i] = 0.0; // Mặc định 0 dB (Unity gain)
                _channelPan[i] = 0.0;    // Mặc định Center
                _currentGain[i] = 1.0f;
                _targetGain[i] = 1.0f;
                _currentMuteRamp[i] = 0.0f;
                _targetMuteRamp[i] = 0.0f;
            }

            // Kết nối bộ trộn thời gian thực DSP vào phần cứng phát âm thanh
            _audioOutput.MixerReader = ReadMixedPcm;
        }

        /// <summary>
        /// Feed decoded 48kHz 16-bit stereo PCM audio from SRT channel decoder.
        /// Feeds camera VU meter and writes to the camera's dedicated jitter ring buffer.
        /// </summary>
        public void ProcessDecodedPcm(int camIndex, byte[] pcmData, int count)
        {
            if (camIndex < 0 || camIndex >= MaxCameras || pcmData == null || count <= 0) return;

            _lastPcmReceivedTicks[camIndex] = Environment.TickCount64;

            // 1. Phân tích mức âm thanh thực vào AudioMeterService của camera
            _camMeters[camIndex].ProcessPcmBytes(pcmData, 0, count, 16, 2, 48000, isFloat: false);

            // 2. Đẩy PCM vào RingBuffer của camera
            _camRingBuffers[camIndex].Write(pcmData, 0, count);

            // Đánh thức thread phát âm thanh nếu đang chờ
            _audioOutput.Wake();
        }

        /// <summary>
        /// Feed raw PCM samples received from SRT camera stream.
        /// </summary>
        public void FeedStreamPcm(int camIndex, ReadOnlySpan<float> samples, int channels, int sampleRate = 48000)
        {
            if (camIndex < 0 || camIndex >= MaxCameras) return;
            _lastPcmReceivedTicks[camIndex] = Environment.TickCount64;
            _camMeters[camIndex].ProcessPcmFloat(samples, channels, sampleRate);
        }

        /// <summary>
        /// Core Real-time Multi-Channel Summing Mixer.
        /// Pulled directly by AudioOutputDevice playback thread at 48000Hz.
        /// Performs sample-accurate mixing across all unmuted channels, parameter smoothing,
        /// equal-power panning, and soft-knee peak limiting.
        /// </summary>
        private int ReadMixedPcm(byte[] destination, int offset, int count)
        {
            if (destination == null || count <= 0) return 0;

            int frameCount = count / 4; // 2 channels * 2 bytes = 4 bytes per frame
            if (frameCount > _mixL.Length) frameCount = _mixL.Length;

            // Clear mixer accumulators
            Array.Clear(_mixL, 0, frameCount);
            Array.Clear(_mixR, 0, frameCount);

            long now = Environment.TickCount64;

            // Determine which channels are active/audible
            for (int i = 0; i < MaxCameras; i++)
            {
                bool chShouldPlay = false;
                if (_soloSource != SoloAudioSource.ProgramMaster)
                {
                    chShouldPlay = (i == ((int)_soloSource - 1));
                }
                else
                {
                    chShouldPlay = (i == _currentProgramIndex || !_channelMuted[i]);
                }

                bool streamAlive = (now - _lastPcmReceivedTicks[i]) < 1000;
                _targetMuteRamp[i] = (chShouldPlay && streamAlive && !_isMuteAll) ? 1.0f : 0.0f;

                // Calculate target linear gain from dB (-60 dB to +12 dB)
                if (_channelGainDb[i] <= -58.0)
                {
                    _targetGain[i] = 0.0f;
                }
                else
                {
                    _targetGain[i] = (float)Math.Pow(10.0, _channelGainDb[i] / 20.0);
                }

                // If channel is completely silent and not transitioning, skip to save CPU
                if (_currentMuteRamp[i] <= 0.001f && _targetMuteRamp[i] <= 0.001f)
                {
                    // Discard old samples in buffer if buffer grows too large while muted
                    if (_camRingBuffers[i].AvailableBytes > 19200)
                    {
                        _camRingBuffers[i].Clear();
                    }
                    continue;
                }

                // Read audio samples from camera's jitter ring buffer
                int bytesRead = _camRingBuffers[i].Read(_tempChannelPcm, 0, count, padSilence: true);
                if (bytesRead > 0 || _currentMuteRamp[i] > 0.001f)
                {
                    // Equal-power stereo pan calculation
                    float pan = (float)Math.Clamp(_channelPan[i], -1.0, 1.0);
                    float panAngle = (pan + 1.0f) * 0.25f * (float)Math.PI;
                    float panL = (float)Math.Cos(panAngle) * 1.4142f;
                    float panR = (float)Math.Sin(panAngle) * 1.4142f;

                    // Sample-by-sample summing with slew-rate parameter smoothing
                    for (int n = 0; n < frameCount; n++)
                    {
                        // 10ms smooth ramp for mute/unmute and fader movements (zero clicks & zipper noise)
                        _currentMuteRamp[i] += (_targetMuteRamp[i] - _currentMuteRamp[i]) * 0.005f;
                        _currentGain[i] += (_targetGain[i] - _currentGain[i]) * 0.005f;

                        float effGain = _currentGain[i] * _currentMuteRamp[i];

                        int byteIdx = n * 4;
                        short rawL = BitConverter.ToInt16(_tempChannelPcm, byteIdx);
                        short rawR = BitConverter.ToInt16(_tempChannelPcm, byteIdx + 2);

                        float normL = rawL / 32768.0f;
                        float normR = rawR / 32768.0f;

                        _mixL[n] += normL * effGain * panL;
                        _mixR[n] += normR * effGain * panR;
                    }
                }
            }

            // Master Output Stage: Apply master volume fader & soft-knee peak limiter
            float targetMasterRamp = _isMuteAll ? 0.0f : (float)(_monitorVolumePercent / 100.0);

            for (int n = 0; n < frameCount; n++)
            {
                _currentMasterRamp += (targetMasterRamp - _currentMasterRamp) * 0.005f;
                float sampleL = _mixL[n] * _currentMasterRamp;
                float sampleR = _mixR[n] * _currentMasterRamp;

                // Soft-Knee Saturation Limiter: Transparent up to 0.85 (-1.4 dBFS),
                // smoothly saturates peaks above 0.85 without hard clipping or pops
                sampleL = SoftLimit(sampleL);
                sampleR = SoftLimit(sampleR);

                short outL = (short)Math.Clamp(sampleL * 32767.0f, short.MinValue, short.MaxValue);
                short outR = (short)Math.Clamp(sampleR * 32767.0f, short.MinValue, short.MaxValue);

                int destIdx = offset + (n * 4);
                destination[destIdx] = (byte)(outL & 0xFF);
                destination[destIdx + 1] = (byte)((outL >> 8) & 0xFF);
                destination[destIdx + 2] = (byte)(outR & 0xFF);
                destination[destIdx + 3] = (byte)((outR >> 8) & 0xFF);
            }

            // Feed actual mixed PCM into program meter
            _programMeter.ProcessPcmBytes(destination, offset, count, 16, 2, 48000, isFloat: false);

            return count;
        }

        /// <summary>
        /// Smooth-knee saturation limiter protecting against digital clipping while preserving dynamics.
        /// </summary>
        private static float SoftLimit(float x)
        {
            const float threshold = 0.85f;
            if (x > threshold)
            {
                return threshold + (1.0f - threshold) * (float)Math.Tanh((x - threshold) / (1.0f - threshold));
            }
            if (x < -threshold)
            {
                return -threshold - (1.0f - threshold) * (float)Math.Tanh((-x - threshold) / (1.0f - threshold));
            }
            return x;
        }

        /// <summary>
        /// Periodic VU meter update tick (invoked ~30fps from DispatcherTimer).
        /// </summary>
        public void ProcessAudioTick(bool[] channelActive, int programIndex)
        {
            _currentProgramIndex = Math.Clamp(programIndex, 0, MaxCameras - 1);
            int configuredChannels = (int)_channelConfig;
            int count = Math.Min(channelActive.Length, MaxCameras);

            double[] tempPeaks = new double[MaxAudioChannels];
            double[] tempRms = new double[MaxAudioChannels];
            bool[] tempClips = new bool[MaxAudioChannels];

            double[][] camExtractedPeaks = new double[MaxCameras][];
            double[][] camExtractedRms = new double[MaxCameras][];
            bool[][] camExtractedClips = new bool[MaxCameras][];

            long now = Environment.TickCount64;

            for (int i = 0; i < MaxCameras; i++)
            {
                camExtractedPeaks[i] = new double[MaxAudioChannels];
                camExtractedRms[i] = new double[MaxAudioChannels];
                camExtractedClips[i] = new bool[MaxAudioChannels];

                bool hasRealPcm = (now - _lastPcmReceivedTicks[i]) < 1000;
                if (i < count && channelActive[i] && hasRealPcm)
                {
                    _camMeters[i].GetPreviewAudioLevels(camExtractedPeaks[i], camExtractedRms[i], camExtractedClips[i], isAudioActive: true);
                    _camLevels[i].CopyFrom(camExtractedPeaks[i], camExtractedRms[i], camExtractedClips[i], configuredChannels);
                }
                else
                {
                    _camMeters[i].DecayMeters();
                    _camMeters[i].GetPreviewAudioLevels(camExtractedPeaks[i], camExtractedRms[i], camExtractedClips[i], isAudioActive: false);
                    _camLevels[i].CopyFrom(camExtractedPeaks[i], camExtractedRms[i], camExtractedClips[i], configuredChannels);
                }
            }

            // Fetch Master Program meter levels (which are fed continuously from the real mixer output)
            if (!_isMuteAll)
            {
                _programMeter.GetPreviewAudioLevels(tempPeaks, tempRms, tempClips, isAudioActive: true);
                _programLevels.CopyFrom(tempPeaks, tempRms, tempClips, configuredChannels);
            }
            else
            {
                _programMeter.DecayMeters();
                _programMeter.GetPreviewAudioLevels(tempPeaks, tempRms, tempClips, isAudioActive: false);
                _programLevels.CopyFrom(tempPeaks, tempRms, tempClips, configuredChannels);
            }

            CamLevelsUpdated?.Invoke(_camLevels);
            ProgramLevelsUpdated?.Invoke(_programLevels);
        }

        public static string GetChannelConfigName(AudioChannelConfiguration config) => config switch
        {
            AudioChannelConfiguration.Stereo2Ch => "Stereo 2.0 (L/R)",
            AudioChannelConfiguration.Channels4Ch => "4-Channel (L, R, C, S)",
            AudioChannelConfiguration.Surround51_6Ch => "5.1 Surround (L, R, C, LFE, Ls, Rs)",
            AudioChannelConfiguration.Surround71_8Ch => "7.1 Surround (8 Channels)",
            AudioChannelConfiguration.SdiEmbedded16Ch => "16-Ch SDI Embedded Broadcast",
            _ => "Stereo 2.0"
        };

        public static string GetChannelLabel(int channelIndex, AudioChannelConfiguration config)
        {
            if (config == AudioChannelConfiguration.Stereo2Ch)
            {
                return channelIndex switch { 0 => "L", 1 => "R", _ => $"CH{channelIndex + 1}" };
            }
            if (config == AudioChannelConfiguration.Channels4Ch)
            {
                return channelIndex switch { 0 => "L", 1 => "R", 2 => "C", 3 => "S", _ => $"CH{channelIndex + 1}" };
            }
            if (config == AudioChannelConfiguration.Surround51_6Ch)
            {
                return channelIndex switch { 0 => "L", 1 => "R", 2 => "C", 3 => "LFE", 4 => "Ls", 5 => "Rs", _ => $"CH{channelIndex + 1}" };
            }
            if (config == AudioChannelConfiguration.Surround71_8Ch)
            {
                return channelIndex switch { 0 => "L", 1 => "R", 2 => "C", 3 => "LFE", 4 => "Ls", 5 => "Rs", 6 => "Bsl", 7 => "Bsr", _ => $"CH{channelIndex + 1}" };
            }
            return $"CH{channelIndex + 1}";
        }

        public string GetSoloLabel(SoloAudioSource src) => src switch
        {
            SoloAudioSource.ProgramMaster => "PROGRAM MASTER",
            SoloAudioSource.Cam1 => "CAM 1 SOLO",
            SoloAudioSource.Cam2 => "CAM 2 SOLO",
            SoloAudioSource.Cam3 => "CAM 3 SOLO",
            SoloAudioSource.Cam4 => "CAM 4 SOLO",
            SoloAudioSource.Cam5 => "CAM 5 SOLO",
            SoloAudioSource.Cam6 => "CAM 6 SOLO",
            SoloAudioSource.Cam7 => "CAM 7 SOLO",
            SoloAudioSource.Cam8 => "CAM 8 SOLO",
            SoloAudioSource.Cam9 => "CAM 9 SOLO",
            SoloAudioSource.Cam10 => "CAM 10 SOLO",
            _ => "PGM"
        };

        private void Log(string tag, string message)
        {
            LogEmitted?.Invoke(tag, message);
            Trace.WriteLine($"[AudioMonitoringManager]{tag} {message}");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            for (int i = 0; i < MaxCameras; i++)
            {
                _camMeters[i].Dispose();
                _camRingBuffers[i].Clear();
            }
            _programMeter.Dispose();
            _audioOutput.Dispose();
        }
    }
}
