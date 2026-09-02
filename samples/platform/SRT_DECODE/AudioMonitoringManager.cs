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
    /// Multi-Channel Audio Monitoring Manager for SRT Multi-Decoder.
    /// Supports up to 16 audio channels per stream (Stereo, 4CH, 5.1, 7.1, 16CH SDI Embedded),
    /// real-time True Peak / RMS EWMA VU meters, Solo listen routing, and Master monitor muting.
    /// </summary>
    public sealed class AudioMonitoringManager : IDisposable
    {
        public const int MaxCameras = 10;
        public const int MaxAudioChannels = 16;

        private readonly ChannelAudioLevels[] _camLevels = new ChannelAudioLevels[MaxCameras];
        private readonly ChannelAudioLevels _programLevels = new();
        private readonly AudioMeterService[] _camMeters = new AudioMeterService[MaxCameras];
        private readonly AudioMeterService _programMeter = new();

        private SoloAudioSource _soloSource = SoloAudioSource.ProgramMaster;
        private AudioChannelConfiguration _channelConfig = AudioChannelConfiguration.Stereo2Ch;
        private bool _isMuteAll = false;
        private double _monitorVolumePercent = 80.0;
        private readonly Random _rand = new();
        private readonly bool[] _channelMuted = new bool[MaxCameras];
        private bool _disposed;

        public event Action<ChannelAudioLevels[]>? CamLevelsUpdated;
        public event Action<ChannelAudioLevels>? ProgramLevelsUpdated;
        public event Action<string, string>? LogEmitted;

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

        public bool IsChannelMuted(int channelIndex)
        {
            if (channelIndex < 0 || channelIndex >= MaxCameras) return false;
            return _channelMuted[channelIndex];
        }

        public void SetChannelMuted(int channelIndex, bool isMuted, string? channelName = null)
        {
            if (channelIndex < 0 || channelIndex >= MaxCameras) return;
            _channelMuted[channelIndex] = isMuted;
            if (isMuted)
            {
                _audioOutput.ClearQueue();
            }
            string name = !string.IsNullOrEmpty(channelName) ? channelName : $"CAM {channelIndex + 1}";
            Log("[AUDIO]", isMuted 
                ? $"🔇 ĐÃ TẮT TIẾNG (MUTE) luồng SRT Ingest: {name}" 
                : $"🔊 ĐÃ BẬT TIẾNG (UNMUTE) luồng SRT Ingest: {name}");
        }

        public SoloAudioSource SoloSource
        {
            get => _soloSource;
            set
            {
                _soloSource = value;
                _audioOutput.ClearQueue();
                Log("[AUDIO]", $"Kiểm âm Solo chuyển sang: {GetSoloLabel(value)}");
            }
        }

        public bool IsMuteAll
        {
            get => _isMuteAll;
            set
            {
                _isMuteAll = value;
                if (_isMuteAll)
                {
                    _audioOutput.ClearQueue();
                }
                Log("[AUDIO]", _isMuteAll ? "🔇 ĐÃ TẮT TIẾNG TẤT CẢ LOA KIỂM ÂM (MUTE ALL)" : "🔊 ĐÃ BẬT LẠI TIẾNG LOA KIỂM ÂM");
            }
        }

        public double MonitorVolumePercent
        {
            get => _monitorVolumePercent;
            set => _monitorVolumePercent = Math.Clamp(value, 0.0, 100.0);
        }

        private readonly AudioOutputDevice _audioOutput = new();
        private readonly long[] _lastPcmReceivedTicks = new long[MaxCameras];

        public AudioMonitoringManager()
        {
            for (int i = 0; i < MaxCameras; i++)
            {
                _camLevels[i] = new ChannelAudioLevels();
                _camMeters[i] = new AudioMeterService();
                _channelMuted[i] = true; // Mặc định mọi màn hình preview đều được mute kiểm âm
            }
        }

        private int _currentProgramIndex = 0;
        public int CurrentProgramIndex
        {
            get => _currentProgramIndex;
            set => _currentProgramIndex = Math.Clamp(value, 0, MaxCameras - 1);
        }

        private readonly object _mixLock = new();
        private byte[]? _pendingMixBuf;
        private int _pendingMixCam = -1;
        private long _pendingMixTime = 0;

        /// <summary>
        /// Processes real decoded 48kHz 16-bit stereo PCM audio from SRT channel decoder.
        /// Feeds audio meter for preview VU meters and routes active/unmuted audio to speakers.
        /// </summary>
        public void ProcessDecodedPcm(int camIndex, byte[] pcmData, int count)
        {
            if (camIndex < 0 || camIndex >= MaxCameras || pcmData == null || count <= 0) return;

            _lastPcmReceivedTicks[camIndex] = Environment.TickCount64;

            // 1. Phân tích mức âm thanh thực vào AudioMeterService của camera
            _camMeters[camIndex].ProcessPcmBytes(pcmData, 0, count, 16, 2, 48000, isFloat: false);

            // 2. Định tuyến âm thanh ra loa / tai nghe kiểm âm theo chế độ Mute & Solo
            if (_isMuteAll) return;

            bool shouldPlay = false;
            if (_soloSource != SoloAudioSource.ProgramMaster)
            {
                // Khi bật SOLO một camera cụ thể, chỉ phát duy nhất camera đó
                shouldPlay = (camIndex == ((int)_soloSource - 1));
            }
            else
            {
                // Ở chế độ Master:
                // - Camera đang on-air ở Program Master LUÔN được xuất âm thanh ra Master
                // - HOẶC bất kỳ camera nào được Unmute trên bảng điều khiển Ingest
                shouldPlay = (camIndex == _currentProgramIndex) || !_channelMuted[camIndex];
            }

            if (!shouldPlay) return;

            double vol = _monitorVolumePercent / 100.0;

            // Kiểm tra số lượng camera đang đồng thời phát ra loa
            int unmutedActiveCount = 0;
            long now = Environment.TickCount64;
            for (int i = 0; i < MaxCameras; i++)
            {
                bool chShouldPlay = (_soloSource != SoloAudioSource.ProgramMaster)
                    ? (i == ((int)_soloSource - 1))
                    : (i == _currentProgramIndex || !_channelMuted[i]);

                if (chShouldPlay && (now - _lastPcmReceivedTicks[i]) < 1000)
                {
                    unmutedActiveCount++;
                }
            }

            if (unmutedActiveCount <= 1)
            {
                // 1 luồng duy nhất: Pass-through trực tiếp 100% âm thanh gốc từ SRT
                _audioOutput.PlayPcm(pcmData, 0, count, vol);
            }
            else
            {
                // Nhiều luồng cùng phát: Trộn mẫu âm thanh (Soft-Limiter Mixer)
                MixAndPlayPcm(camIndex, pcmData, count, vol, now);
            }
        }

        private void MixAndPlayPcm(int camIndex, byte[] pcmData, int count, double vol, long now)
        {
            lock (_mixLock)
            {
                if (_pendingMixBuf != null && _pendingMixCam != camIndex && (now - _pendingMixTime) < 25)
                {
                    int len = Math.Min(count, _pendingMixBuf.Length);
                    byte[] mixed = new byte[len];
                    for (int i = 0; i < len; i += 2)
                    {
                        short s1 = BitConverter.ToInt16(_pendingMixBuf, i);
                        short s2 = BitConverter.ToInt16(pcmData, i);
                        int sum = s1 + s2;
                        short clipped = (short)Math.Clamp(sum, short.MinValue, short.MaxValue);
                        mixed[i] = (byte)(clipped & 0xFF);
                        mixed[i + 1] = (byte)((clipped >> 8) & 0xFF);
                    }
                    _pendingMixBuf = null;
                    _pendingMixCam = -1;
                    _audioOutput.PlayPcm(mixed, 0, len, vol);
                }
                else
                {
                    if (_pendingMixBuf != null)
                    {
                        _audioOutput.PlayPcm(_pendingMixBuf, 0, _pendingMixBuf.Length, vol);
                    }
                    _pendingMixBuf = new byte[count];
                    Buffer.BlockCopy(pcmData, 0, _pendingMixBuf, 0, count);
                    _pendingMixCam = camIndex;
                    _pendingMixTime = now;
                }
            }
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
                    // 100% dữ liệu thực tế đã decode, không dùng dữ liệu mô phỏng
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

            // Xác định kênh âm thanh được định tuyến kiểm âm ra Master / Solo Monitor
            int monitorIndex = -1;
            bool monitorActive = false;

            if (_soloSource != SoloAudioSource.ProgramMaster)
            {
                int soloIdx = (int)_soloSource - 1; // Solo Cam 1..10
                if (soloIdx >= 0 && soloIdx < count && channelActive[soloIdx])
                {
                    monitorIndex = soloIdx;
                    monitorActive = true;
                }
            }
            else
            {
                // Cổng Audio Master: Luôn xuất và kiểm âm kênh Program Master
                if (programIndex >= 0 && programIndex < count && channelActive[programIndex])
                {
                    monitorIndex = programIndex;
                    monitorActive = true;
                }
            }

            if (monitorActive && monitorIndex >= 0 && !_isMuteAll)
            {
                double[] monPeaks = new double[configuredChannels];
                double[] monRms = new double[configuredChannels];
                for (int ch = 0; ch < configuredChannels; ch++)
                {
                    monPeaks[ch] = camExtractedPeaks[monitorIndex][ch];
                    monRms[ch] = camExtractedRms[monitorIndex][ch];
                }

                _programMeter.FeedChannelLevels(monPeaks, monRms, configuredChannels);
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
            }
            _programMeter.Dispose();
            _audioOutput.Dispose();
        }
    }
}
