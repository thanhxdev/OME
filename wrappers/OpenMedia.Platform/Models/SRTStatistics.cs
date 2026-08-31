using System;

namespace OpenMedia.Platform.Models
{
    /// <summary>
    /// Chứa thông số giám sát và thống kê thời gian thực của luồng truyền dẫn SRT.
    /// </summary>
    public sealed class SRTStatistics
    {
        /// <summary>Độ trễ khứ hồi Round-Trip Time (ms).</summary>
        public double RttMs { get; set; } = 0.0;

        /// <summary>Tỷ lệ mất gói tin (%).</summary>
        public double PacketLossPercent { get; set; } = 0.0;

        /// <summary>Băng thông ước tính đo được qua SRT (Mbps).</summary>
        public double BandwidthMbps { get; set; } = 0.0;

        /// <summary>Tổng số gói tin đã truyền lại (Retransmitted Packets).</summary>
        public int PacketsRetransmitted { get; set; } = 0;

        /// <summary>Tổng số gói tin đã gửi đi.</summary>
        public int PacketsSent { get; set; } = 0;

        /// <summary>Tổng số gói tin đã nhận.</summary>
        public int PacketsReceived { get; set; } = 0;

        /// <summary>Tổng số gói tin bị rớt (Dropped Packets do vượt quá buffer latency).</summary>
        public int PacketsDropped { get; set; } = 0;

        /// <summary>Tổng số byte đã truyền dẫn qua kết nối.</summary>
        public ulong TotalBytesTransferred { get; set; } = 0;

        /// <summary>Bitrate thời gian thực đo được (kbps).</summary>
        public double CurrentBitrateKbps { get; set; } = 0.0;

        /// <summary>Tốc độ khung hình thực tế hiện tại (FPS).</summary>
        public double CurrentFps { get; set; } = 59.94;

        /// <summary>Thời gian luồng đã duy trì kết nối liên tục.</summary>
        public TimeSpan Uptime { get; set; } = TimeSpan.Zero;

        /// <summary>Trạng thái kết nối tín hiệu luồng SRT.</summary>
        public bool IsConnected { get; set; } = false;

        /// <summary>
        /// Tạo bản sao dữ liệu thống kê hiện tại.
        /// </summary>
        public SRTStatistics Clone()
        {
            return new SRTStatistics
            {
                RttMs = this.RttMs,
                PacketLossPercent = this.PacketLossPercent,
                BandwidthMbps = this.BandwidthMbps,
                PacketsRetransmitted = this.PacketsRetransmitted,
                PacketsSent = this.PacketsSent,
                PacketsReceived = this.PacketsReceived,
                PacketsDropped = this.PacketsDropped,
                TotalBytesTransferred = this.TotalBytesTransferred,
                CurrentBitrateKbps = this.CurrentBitrateKbps,
                CurrentFps = this.CurrentFps,
                Uptime = this.Uptime,
                IsConnected = this.IsConnected
            };
        }
    }
}
