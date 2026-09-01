using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace SRT_DECODE
{
    public enum SyncLockState
    {
        FreeRun,
        Syncing,
        Locked
    }

    /// <summary>
    /// Metadata & Drift Metrics for an individual camera stream.
    /// </summary>
    public sealed class CameraSyncMetrics
    {
        public int ChannelId { get; set; }
        public string ChannelName { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime LastNtpTimestamp { get; set; } = DateTime.MinValue;
        public long LastPts { get; set; }
        public double DriftMs { get; set; }
        public double BufferFillPercent { get; set; } = 100.0;
        public ulong DroppedFrames { get; set; }
        public ulong RepeatedFrames { get; set; }
        public SyncLockState LockState { get; set; } = SyncLockState.FreeRun;

        public string GetFormattedDrift()
        {
            if (!IsActive) return "Off-Air";
            string sign = DriftMs >= 0 ? "+" : "";
            return $"{sign}{DriftMs:F1} ms";
        }
    }

    /// <summary>
    /// Multi-Camera NTP & Wall-Clock Synchronization Engine.
    /// Manages playout jitter alignment buffers, frame alignment, and drift compensation across Cam 1-10.
    /// </summary>
    public sealed class NtpSyncEngine : IDisposable
    {
        public const int MaxChannels = 10;
        private bool _masterSyncEnabled = false;
        private string _ntpServer = "time.google.com";
        private int _targetSyncWindowMs = 350; // Target sync jitter buffer delay
        private NtpSyncResult? _lastNtpResult;
        private Timer? _ntpQueryTimer;
        private readonly CameraSyncMetrics[] _channels = new CameraSyncMetrics[MaxChannels];
        private readonly object _lock = new();

        public event Action<string, string>? LogEmitted;
        public event Action<CameraSyncMetrics[]>? SyncMetricsUpdated;

        public bool MasterSyncEnabled
        {
            get => _masterSyncEnabled;
            set
            {
                _masterSyncEnabled = value;
                Log("[SYNC]", _masterSyncEnabled 
                    ? $"KÍCH HOẠT Multi-Camera Master Synchronization (Target Window: {_targetSyncWindowMs}ms, NTP: {_ntpServer})" 
                    : "ĐÃ TẮT Multi-Camera Master Synchronization (Chuyển sang chế độ Free-Run)");
            }
        }

        public string NtpServer
        {
            get => _ntpServer;
            set => _ntpServer = value;
        }

        public int TargetSyncWindowMs
        {
            get => _targetSyncWindowMs;
            set => _targetSyncWindowMs = Math.Clamp(value, 50, 2000);
        }

        public NtpSyncResult? LastNtpResult => _lastNtpResult;

        public NtpSyncEngine()
        {
            for (int i = 0; i < MaxChannels; i++)
            {
                _channels[i] = new CameraSyncMetrics
                {
                    ChannelId = i + 1,
                    ChannelName = $"CAM {i + 1}",
                    IsActive = false,
                    DriftMs = 0.0,
                    BufferFillPercent = 100.0,
                    LockState = SyncLockState.FreeRun
                };
            }

            // Periodic NTP query every 30 seconds
            _ntpQueryTimer = new Timer(async _ => await QueryNtpMasterAsync(), null, 1000, 30000);
        }

        public async Task<bool> QueryNtpMasterAsync()
        {
            try
            {
                var res = await NtpClient.QueryTimeAsync(_ntpServer, 3000);
                lock (_lock)
                {
                    _lastNtpResult = res;
                }

                if (res.Success)
                {
                    Log("[NTP]", $"Master Clock đồng bộ thành công: {res.GetFormattedOffset()}");
                    return true;
                }
                else
                {
                    Log("[WARN]", $"Không thể đồng bộ Master Clock với NTP Server ({_ntpServer}): {res.ErrorMessage}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log("[ERROR]", $"Lỗi truy vấn NTP: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Updates stream frame metadata from incoming SRT SEI/PTS stream.
        /// </summary>
        public void IngestFrameMetadata(int channelIndex, DateTime wallClockTime, long pts, double rttMs)
        {
            if (channelIndex < 0 || channelIndex >= MaxChannels) return;

            lock (_lock)
            {
                var ch = _channels[channelIndex];
                ch.IsActive = true;
                ch.LastNtpTimestamp = wallClockTime;
                ch.LastPts = pts;

                if (!_masterSyncEnabled)
                {
                    ch.LockState = SyncLockState.FreeRun;
                    ch.DriftMs = 0.0;
                    ch.BufferFillPercent = 95.0;
                    return;
                }

                // Reference time is Master UTC adjusted by NTP offset if available
                DateTime nowUtc = DateTime.UtcNow;
                if (_lastNtpResult?.Success == true)
                {
                    nowUtc = nowUtc.AddMilliseconds(_lastNtpResult.OffsetMs);
                }

                // Calculate stream latency and drift against the target sync window
                double latency = (nowUtc - wallClockTime).TotalMilliseconds;
                double drift = latency - _targetSyncWindowMs;
                ch.DriftMs = drift;

                // Drift Compensation & Lock assessment
                if (Math.Abs(drift) <= 15.0)
                {
                    ch.LockState = SyncLockState.Locked;
                    ch.BufferFillPercent = 100.0;
                }
                else if (Math.Abs(drift) <= 60.0)
                {
                    ch.LockState = SyncLockState.Syncing;
                    ch.BufferFillPercent = Math.Clamp(100.0 - (Math.Abs(drift) / 2.0), 40.0, 100.0);
                }
                else
                {
                    ch.LockState = SyncLockState.Syncing;
                    if (drift > 100.0)
                    {
                        // Camera is running behind -> smooth drop frames
                        ch.DroppedFrames++;
                        ch.BufferFillPercent = 60.0;
                    }
                    else if (drift < -100.0)
                    {
                        // Camera is running ahead -> repeat/hold frame in buffer
                        ch.RepeatedFrames++;
                        ch.BufferFillPercent = 120.0;
                    }
                }
            }

            SyncMetricsUpdated?.Invoke(GetSnapshot());
        }

        public void SetChannelActive(int channelIndex, bool active)
        {
            if (channelIndex < 0 || channelIndex >= MaxChannels) return;
            lock (_lock)
            {
                _channels[channelIndex].IsActive = active;
                if (!active)
                {
                    _channels[channelIndex].LockState = SyncLockState.FreeRun;
                    _channels[channelIndex].DriftMs = 0.0;
                }
            }
        }

        public CameraSyncMetrics[] GetSnapshot()
        {
            lock (_lock)
            {
                var copy = new CameraSyncMetrics[MaxChannels];
                for (int i = 0; i < MaxChannels; i++)
                {
                    copy[i] = new CameraSyncMetrics
                    {
                        ChannelId = _channels[i].ChannelId,
                        ChannelName = _channels[i].ChannelName,
                        IsActive = _channels[i].IsActive,
                        LastNtpTimestamp = _channels[i].LastNtpTimestamp,
                        LastPts = _channels[i].LastPts,
                        DriftMs = _channels[i].DriftMs,
                        BufferFillPercent = _channels[i].BufferFillPercent,
                        DroppedFrames = _channels[i].DroppedFrames,
                        RepeatedFrames = _channels[i].RepeatedFrames,
                        LockState = _channels[i].LockState
                    };
                }
                return copy;
            }
        }

        private void Log(string tag, string message)
        {
            LogEmitted?.Invoke(tag, message);
            Trace.WriteLine($"[NtpSyncEngine]{tag} {message}");
        }

        public void Dispose()
        {
            _ntpQueryTimer?.Dispose();
            _ntpQueryTimer = null;
        }
    }
}
