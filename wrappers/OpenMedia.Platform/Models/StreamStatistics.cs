using System;

namespace OpenMedia.Platform.Models
{
    /// <summary>
    /// Unified telemetry and health statistics for any broadcast ingest or egress stream session.
    /// </summary>
    public sealed class StreamStatistics
    {
        public double RttMs { get; set; } = 0.0;
        public double PacketLossPercent { get; set; } = 0.0;
        public double BandwidthMbps { get; set; } = 0.0;
        public double CurrentBitrateKbps { get; set; } = 0.0;
        public double CurrentFps { get; set; } = 60.0;
        public TimeSpan Uptime { get; set; } = TimeSpan.Zero;
        public ulong TotalBytesTransferred { get; set; } = 0;
        public bool IsConnected { get; set; } = false;

        public static StreamStatistics FromSrtStatistics(SRTStatistics srt)
        {
            if (srt == null) return new StreamStatistics();
            return new StreamStatistics
            {
                RttMs = srt.RttMs,
                PacketLossPercent = srt.PacketLossPercent,
                BandwidthMbps = srt.BandwidthMbps,
                CurrentBitrateKbps = srt.CurrentBitrateKbps,
                CurrentFps = srt.CurrentFps,
                Uptime = srt.Uptime,
                TotalBytesTransferred = srt.TotalBytesTransferred,
                IsConnected = srt.IsConnected
            };
        }
    }
}
