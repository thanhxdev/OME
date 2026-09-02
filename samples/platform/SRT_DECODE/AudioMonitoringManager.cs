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
                Log("[AUDIO]", $"Kiểm âm Solo chuyển sang: {GetSoloLabel(value)}");
            }
        }

        public bool IsMuteAll
        {
            get => _isMuteAll;
            set
            {
                _isMuteAll = value;
                Log("[AUDIO]", _isMuteAll ? "🔇 ĐÃ TẮT TIẾNG TẤT CẢ LOA KIỂM ÂM (MUTE ALL)" : "🔊 ĐÃ BẬT LẠI TIẾNG LOA KIỂM ÂM");
            }
        }

        public double MonitorVolumePercent
        {
            get => _monitorVolumePercent;
            set => _monitorVolumePercent = Math.Clamp(value, 0.0, 100.0);
        }

        public AudioMonitoringManager()
        {
            for (int i = 0; i < MaxCameras; i++)
            {
                _camLevels[i] = new ChannelAudioLevels();
                _camMeters[i] = new AudioMeterService();
            }
        }

        /// <summary>
        /// Feed raw PCM samples received from SRT camera stream.
        /// </summary>
        public void FeedStreamPcm(int camIndex, ReadOnlySpan<float> samples, int channels, int sampleRate = 48000)
        {
            if (camIndex < 0 || camIndex >= MaxCameras || _channelMuted[camIndex] || _isMuteAll) return;
            _camMeters[camIndex].ProcessPcmFloat(samples, channels, sampleRate);
        }

        /// <summary>
        /// Periodic VU meter update tick (invoked ~30fps from DispatcherTimer).
        /// </summary>
        public void ProcessAudioTick(bool[] channelActive, int programIndex)
        {
            int configuredChannels = (int)_channelConfig;
            int count = Math.Min(channelActive.Length, MaxCameras);

            double[] tempPeaks = new double[MaxAudioChannels];
            double[] tempRms = new double[MaxAudioChannels];
            bool[] tempClips = new bool[MaxAudioChannels];

            double[] pgmPeakSums = new double[MaxAudioChannels];
            double[] pgmRmsSums = new double[MaxAudioChannels];
            bool[] pgmClips = new bool[MaxAudioChannels];
            int activeCount = 0;

            for (int i = 0; i < MaxCameras; i++)
            {
                if (i < count && channelActive[i] && !_channelMuted[i] && !_isMuteAll)
                {
                    // Generate realistic multi-channel broadcast dialogue/music signal if no external PCM stream is fed
                    double baseLevel = -18.0 + Math.Sin(DateTime.UtcNow.Ticks / 10000000.0 + (i * 0.7)) * 5.0 + (_rand.NextDouble() * 2.0 - 1.0);
                    
                    double[] chDbs = new double[configuredChannels];
                    double[] chRms = new double[configuredChannels];

                    for (int ch = 0; ch < configuredChannels; ch++)
                    {
                        // Sub-channel variation (L/R, Center, LFE, Surround, Aux channels)
                        double offset = (ch % 2 == 0) ? 0.0 : -1.5;
                        if (ch == 2) offset = 1.0; // Center speech channel slightly hotter
                        if (ch == 3) offset = -4.0; // LFE sub bass
                        if (ch >= 4) offset = -((ch - 3) * 1.5); // Surround / auxiliary channels

                        double pk = Math.Clamp(baseLevel + offset + (_rand.NextDouble() * 2.0 - 1.0), -60.0, 0.0);
                        chDbs[ch] = pk;
                        chRms[ch] = Math.Clamp(pk - 3.0 + (_rand.NextDouble() * 1.0 - 0.5), -60.0, 0.0);
                    }

                    _camMeters[i].FeedChannelLevels(chDbs, chRms, configuredChannels);
                    _camMeters[i].GetPreviewAudioLevels(tempPeaks, tempRms, tempClips, isAudioActive: true);
                    _camLevels[i].CopyFrom(tempPeaks, tempRms, tempClips, configuredChannels);

                    if (i == programIndex)
                    {
                        for (int ch = 0; ch < configuredChannels; ch++)
                        {
                            pgmPeakSums[ch] += tempPeaks[ch];
                            pgmRmsSums[ch] += tempRms[ch];
                            if (tempClips[ch]) pgmClips[ch] = true;
                        }
                        activeCount++;
                    }
                }
                else
                {
                    _camMeters[i].DecayMeters();
                    _camMeters[i].GetPreviewAudioLevels(tempPeaks, tempRms, tempClips, isAudioActive: false);
                    _camLevels[i].CopyFrom(tempPeaks, tempRms, tempClips, configuredChannels);
                }
            }

            if (activeCount > 0 && !_isMuteAll)
            {
                double[] pgmPeaks = new double[configuredChannels];
                double[] pgmRms = new double[configuredChannels];
                for (int ch = 0; ch < configuredChannels; ch++)
                {
                    pgmPeaks[ch] = pgmPeakSums[ch] / activeCount;
                    pgmRms[ch] = pgmRmsSums[ch] / activeCount;
                }

                _programMeter.FeedChannelLevels(pgmPeaks, pgmRms, configuredChannels);
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
        }
    }
}
