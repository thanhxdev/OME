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

    public sealed class ChannelAudioLevels
    {
        public double LeftDb { get; set; } = -60.0;
        public double RightDb { get; set; } = -60.0;
        public double LeftPercent { get; set; } = 0.0; // 0..100%
        public double RightPercent { get; set; } = 0.0; // 0..100%
        public bool IsClipping { get; set; }
    }

    /// <summary>
    /// Multi-Channel Audio Monitoring Manager.
    /// Computes real-time stereo audio VU levels (dBFS), handles Solo listen routing, and Master monitor muting for up to 10 channels.
    /// </summary>
    public sealed class AudioMonitoringManager
    {
        public const int MaxChannels = 10;
        private readonly ChannelAudioLevels[] _camLevels = new ChannelAudioLevels[MaxChannels];
        private readonly ChannelAudioLevels _programLevels = new();
        private SoloAudioSource _soloSource = SoloAudioSource.ProgramMaster;
        private bool _isMuteAll = false;
        private double _monitorVolumePercent = 80.0;
        private readonly Random _rand = new();

        public event Action<ChannelAudioLevels[]>? CamLevelsUpdated;
        public event Action<ChannelAudioLevels>? ProgramLevelsUpdated;
        public event Action<string, string>? LogEmitted;

        private readonly bool[] _channelMuted = new bool[MaxChannels];

        public bool IsChannelMuted(int channelIndex)
        {
            if (channelIndex < 0 || channelIndex >= MaxChannels) return false;
            return _channelMuted[channelIndex];
        }

        public void SetChannelMuted(int channelIndex, bool isMuted, string? channelName = null)
        {
            if (channelIndex < 0 || channelIndex >= MaxChannels) return;
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
            for (int i = 0; i < MaxChannels; i++)
            {
                _camLevels[i] = new ChannelAudioLevels();
            }
        }

        /// <summary>
        /// Simulates/Processes real-time audio frame levels for active streams.
        /// </summary>
        public void ProcessAudioTick(bool[] channelActive, int programIndex)
        {
            double pgmLeftSum = 0;
            double pgmRightSum = 0;
            int activeCount = 0;
            int count = Math.Min(channelActive.Length, MaxChannels);

            for (int i = 0; i < MaxChannels; i++)
            {
                if (i < count && channelActive[i] && !_channelMuted[i] && !_isMuteAll)
                {
                    // Generate realistic broadcast dialogue/ambient audio levels (-24dBFS to -6dBFS)
                    double baseLevel = -18.0 + Math.Sin(DateTime.UtcNow.Ticks / 10000000.0 + i) * 6.0 + (_rand.NextDouble() * 3.0 - 1.5);
                    double leftDb = Math.Clamp(baseLevel + (_rand.NextDouble() * 2.0 - 1.0), -60.0, 0.0);
                    double rightDb = Math.Clamp(baseLevel + (_rand.NextDouble() * 2.0 - 1.0), -60.0, 0.0);

                    _camLevels[i].LeftDb = leftDb;
                    _camLevels[i].RightDb = rightDb;
                    _camLevels[i].LeftPercent = DbToPercent(leftDb);
                    _camLevels[i].RightPercent = DbToPercent(rightDb);
                    _camLevels[i].IsClipping = leftDb >= -0.5 || rightDb >= -0.5;

                    if (i == programIndex)
                    {
                        pgmLeftSum += leftDb;
                        pgmRightSum += rightDb;
                        activeCount++;
                    }
                }
                else
                {
                    _camLevels[i].LeftDb = -60.0;
                    _camLevels[i].RightDb = -60.0;
                    _camLevels[i].LeftPercent = 0.0;
                    _camLevels[i].RightPercent = 0.0;
                    _camLevels[i].IsClipping = false;
                }
            }

            if (activeCount > 0 && !_isMuteAll)
            {
                _programLevels.LeftDb = pgmLeftSum / activeCount;
                _programLevels.RightDb = pgmRightSum / activeCount;
                _programLevels.LeftPercent = DbToPercent(_programLevels.LeftDb);
                _programLevels.RightPercent = DbToPercent(_programLevels.RightDb);
                _programLevels.IsClipping = _programLevels.LeftDb >= -0.5 || _programLevels.RightDb >= -0.5;
            }
            else
            {
                _programLevels.LeftDb = -60.0;
                _programLevels.RightDb = -60.0;
                _programLevels.LeftPercent = 0.0;
                _programLevels.RightPercent = 0.0;
                _programLevels.IsClipping = false;
            }

            CamLevelsUpdated?.Invoke(_camLevels);
            ProgramLevelsUpdated?.Invoke(_programLevels);
        }

        private static double DbToPercent(double db)
        {
            // Maps -60dB -> 0%, -24dB -> 60%, -18dB -> 70%, 0dB -> 100%
            if (db <= -60.0) return 0.0;
            if (db >= 0.0) return 100.0;
            return ((db + 60.0) / 60.0) * 100.0;
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
    }
}
