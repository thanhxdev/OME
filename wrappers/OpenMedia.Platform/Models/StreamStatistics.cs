using System;

namespace OpenMedia.Platform.Models
{
    /// <summary>
    /// Unified telemetry and health statistics for any broadcast ingest or egress stream session.
    /// </summary>
    public sealed class StreamStatistics
    {
        /// <summary>Round trip time in milliseconds.</summary>
        public double RttMs { get; set; } = 0.0;
        /// <summary>Current packet loss percentage.</summary>
        public double PacketLossPercent { get; set; } = 0.0;
        /// <summary>Estimated available bandwidth in Mbps.</summary>
        public double BandwidthMbps { get; set; } = 0.0;
        /// <summary>Current transmission bitrate in Kbps.</summary>
        public double CurrentBitrateKbps { get; set; } = 0.0;
        /// <summary>Current transmission frame rate.</summary>
        public double CurrentFps { get; set; } = 60.0;
        /// <summary>Session uptime duration.</summary>
        public TimeSpan Uptime { get; set; } = TimeSpan.Zero;
        /// <summary>Total bytes transferred across session.</summary>
        public ulong TotalBytesTransferred { get; set; } = 0;
        /// <summary>Whether the stream connection is currently active and healthy.</summary>
        public bool IsConnected { get; set; } = false;

        /// <summary>
        /// Converts native SRT statistics into unified StreamStatistics.
        /// </summary>
        /// <param name="srt">Native SRT telemetry structure.</param>
        /// <returns>Unified stream statistics model.</returns>
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
