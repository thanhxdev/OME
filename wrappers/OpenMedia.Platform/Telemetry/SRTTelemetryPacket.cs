using System;

namespace OpenMedia.Platform.Telemetry
{
    /// <summary>
    /// Gói tin Telemetry đo kiểm sức khỏe luồng SRT thời gian thực gửi từ trạm Encoder/Decoder về SRT_MONITOR.
    /// </summary>
    public sealed class SRTTelemetryPacket
    {
        /// <summary>Tên định danh node/trạm (ví dụ: ENC_CAM_01, DEC_STATION_01).</summary>
        public string NodeName { get; set; } = "UNKNOWN_NODE";

        /// <summary>Loại trạm: "Encoder", "Decoder", hoặc "Gateway".</summary>
        public string NodeType { get; set; } = "Encoder";

        /// <summary>Thời điểm gửi (UTC).</summary>
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        /// <summary>Trạng thái kết nối luồng.</summary>
        public bool IsConnected { get; set; } = false;

        /// <summary>Tốc độ khung hình thực tế (FPS).</summary>
        public double Fps { get; set; } = 0.0;

        /// <summary>Băng thông truyền dẫn (kbps).</summary>
        public double BitrateKbps { get; set; } = 0.0;

        /// <summary>Độ trễ khứ hồi Round Trip Time (ms).</summary>
        public double RttMs { get; set; } = 0.0;

        /// <summary>Tỷ lệ mất gói tin (%).</summary>
        public double LossPercent { get; set; } = 0.0;

        /// <summary>Thời gian duy trì luồng liên tục (giây).</summary>
        public double UptimeSeconds { get; set; } = 0.0;

        /// <summary>Địa chỉ URI hoặc endpoint của luồng SRT.</summary>
        public string StreamUri { get; set; } = string.Empty;

        // ─── Chuẩn SMPTE 2022-7 Hitless Redundancy ───────────────────────
        /// <summary>Cho biết luồng có đang bật chế độ dự phòng SMPTE 2022-7 hay không.</summary>
        public bool IsSmpte2022_7Active { get; set; } = false;

        /// <summary>Trạng thái kết nối Path A.</summary>
        public bool PathAConnected { get; set; } = false;

        /// <summary>Trạng thái kết nối Path B.</summary>
        public bool PathBConnected { get; set; } = false;

        /// <summary>Số gói tin đã truyền/nhận trên Path A.</summary>
        public long PathAPackets { get; set; } = 0;

        /// <summary>Số gói tin đã truyền/nhận trên Path B.</summary>
        public long PathBPackets { get; set; } = 0;

        /// <summary>Số gói tin hợp nhất thành công sau khi khử trùng lặp (Hitless Merged).</summary>
        public long MergedPackets { get; set; } = 0;

        /// <summary>Số gói tin trùng lặp đã loại bỏ.</summary>
        public long DroppedDuplicates { get; set; } = 0;

        /// <summary>Độ trễ bù vi sai (Differential Delay) tính theo ms.</summary>
        public int DifferentialDelayMs { get; set; } = 50;
    }
}
