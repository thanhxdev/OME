using System;
using System.Collections.Generic;

namespace OpenMedia.Platform.Models
{
    /// <summary>
    /// Các chế độ nhóm socket theo chuẩn SRT và SMPTE 2022-7.
    /// </summary>
    public enum SRTGroupType
    {
        /// <summary>
        /// Chuẩn SMPTE ST 2022-7: Gửi đồng thời gói tin qua tất cả các member socket (dual-path / multi-path hitless redundancy).
        /// </summary>
        Broadcast_SMPTE2022_7,

        /// <summary>
        /// Chế độ Active / Standby (Main &amp; Backup failover): Chỉ 1 link truyền, tự động chuyển khi link chính mất tín hiệu.
        /// </summary>
        Backup_ActiveStandby
    }

    /// <summary>
    /// Cấu hình cho từng member socket trong Group Socket.
    /// </summary>
    public sealed class SRTGroupMemberConfig
    {
        /// <summary>Định danh duy nhất của member socket.</summary>
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>Tên hiển thị mô tả đường truyền (ví dụ: Path A (LAN 1), Path B (4G/5G)).</summary>
        public string Name { get; set; } = "Member Link";

        /// <summary>Địa chỉ IP hoặc hostname đích (Caller) hoặc lắng nghe (Listener).</summary>
        public string Host { get; set; } = "127.0.0.1";

        /// <summary>Cổng kết nối UDP SRT.</summary>
        public int Port { get; set; } = 9000;

        /// <summary>Địa chỉ IP của card mạng nguồn cục bộ (Local network adapter / NIC binding).</summary>
        public string LocalInterfaceIp { get; set; } = string.Empty;

        /// <summary>Trọng số ưu tiên (Weight) cho phân bổ đường truyền (nếu áp dụng).</summary>
        public int Weight { get; set; } = 10;

        /// <summary>Độ trễ buffer dành riêng cho member này (ms, 0 = dùng chung cấu hình group).</summary>
        public int LatencyMs { get; set; } = 0;

        /// <summary>Cho biết member này có được kích hoạt sẵn hay không.</summary>
        public bool IsEnabled { get; set; } = true;

        public SRTGroupMemberConfig() { }

        public SRTGroupMemberConfig(string name, string host, int port, string localInterfaceIp = "", int weight = 10)
        {
            Name = name;
            Host = host;
            Port = port;
            LocalInterfaceIp = localInterfaceIp;
            Weight = weight;
        }

        public SRTGroupMemberConfig Clone()
        {
            return new SRTGroupMemberConfig
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = Name,
                Host = Host,
                Port = Port,
                LocalInterfaceIp = LocalInterfaceIp,
                Weight = Weight,
                LatencyMs = LatencyMs,
                IsEnabled = IsEnabled
            };
        }
    }

    /// <summary>
    /// Trạng thái thời gian thực và telemetry của từng member socket trong nhóm.
    /// </summary>
    public sealed class SRTGroupMemberStatus
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Endpoint { get; set; } = string.Empty;
        public string LocalInterfaceIp { get; set; } = string.Empty;
        public bool IsConnected { get; set; } = false;
        public bool IsActiveSender { get; set; } = false;
        public double RttMs { get; set; } = 0.0;
        public double PacketLossPercent { get; set; } = 0.0;
        public double BitrateKbps { get; set; } = 0.0;
        public ulong PacketsSent { get; set; } = 0;
        public ulong PacketsReceived { get; set; } = 0;
        public ulong BytesTransferred { get; set; } = 0;
        public string StatusText { get; set; } = "Idle";
        public DateTime LastConnectedTime { get; set; } = DateTime.MinValue;
        public DateTime LastActiveTime { get; set; } = DateTime.MinValue;

        public SRTGroupMemberStatus Clone()
        {
            return (SRTGroupMemberStatus)MemberwiseClone();
        }
    }

    /// <summary>
    /// Báo cáo thống kê tổng hợp chuẩn SMPTE ST 2022-7 và trạng thái Group Socket.
    /// </summary>
    public sealed class SMPTE2022_7Stats
    {
        public bool IsGroupActive { get; set; } = false;
        public int TotalMembersCount { get; set; } = 0;
        public int ConnectedMembersCount { get; set; } = 0;
        public ulong PathAPackets { get; set; } = 0;
        public ulong PathBPackets { get; set; } = 0;
        public ulong DuplicatesDropped { get; set; } = 0;
        public ulong RecoveredFromRedundantPath { get; set; } = 0;
        public ulong OutputPacketsTotal { get; set; } = 0;
        public ulong LostPacketsTotal { get; set; } = 0;
        public double EstimatedSkewMs { get; set; } = 0.0;
        public double ProtectionRedundancyPercent => TotalMembersCount > 1 && ConnectedMembersCount >= 2 ? 100.0 : (ConnectedMembersCount == 1 ? 50.0 : 0.0);

        public SMPTE2022_7Stats Clone()
        {
            return (SMPTE2022_7Stats)MemberwiseClone();
        }
    }
}
