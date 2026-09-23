using System;
using System.Collections.Generic;
using System.Text;

namespace OpenMedia.Platform.Models
{
    /// <summary>
    /// Chứa toàn bộ thông số kỹ thuật cấu hình truyền dẫn luồng SRT phát sóng và thu nhận.
    /// Bao gồm mạng, mã hóa, video/audio codec, phần cứng tăng tốc, đồng bộ Multi-Cam NTP và độ trễ siêu thấp.
    /// </summary>
    public sealed class SRTStreamConfig
    {
        // ─── Network & Protocol ─────────────────────────────────────────
        public string Host { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 9000;
        public SRTMode Mode { get; set; } = SRTMode.Caller;
        public string StreamId { get; set; } = string.Empty;

        // ─── Latency & Buffering ────────────────────────────────────────
        public int LatencyMs { get; set; } = 120;
        public bool AutoLatency { get; set; } = true;
        public int MinLatencyMs { get; set; } = 120;

        // ─── Encryption & Security ──────────────────────────────────────
        public bool EncryptionEnabled { get; set; } = false;
        public string Passphrase { get; set; } = string.Empty;
        public int KeyLength { get; set; } = 32; // 16: AES-128, 24: AES-192, 32: AES-256

        // ─── Video Encoding & Hardware Acceleration ─────────────────────
        public string VideoCodec { get; set; } = "H.264";
        public int Width { get; set; } = 1920;
        public int Height { get; set; } = 1080;
        public double FrameRate { get; set; } = 59.94;
        public int BitrateKbps { get; set; } = 6000;
        public string RateControl { get; set; } = "CBR";
        public string EncoderPreset { get; set; } = "Low-Latency / Zerolatency";
        /// <summary>Hardware encoder preference (NVENC, QuickSync, etc.)</summary>
        public string HardwareEncoder { get; set; } = "NVIDIA NVENC";

        // ─── Ultra Low-Latency (ULL) Profile ────────────────────────────
        /// <summary>Enables ultra low-latency tuning flags.</summary>
        public bool UltraLowLatency { get; set; } = true;
        /// <summary>GOP size in seconds.</summary>
        public double GopSeconds { get; set; } = 1.0;
        /// <summary>Number of consecutive B-frames (0 for low-latency).</summary>
        public int BFrames { get; set; } = 0;

        // ─── Multi-Cam NTP & Wall-Clock Synchronization ─────────────────
        /// <summary>Enables NTP clock synchronization.</summary>
        public bool NtpSyncEnabled { get; set; } = false;
        /// <summary>NTP time server hostname or IP.</summary>
        public string NtpServer { get; set; } = "time.google.com";

        // ─── Audio Configuration ────────────────────────────────────────
        /// <summary>Number of audio channels.</summary>
        public int AudioChannels { get; set; } = 2;
        /// <summary>Audio sampling rate in Hz.</summary>
        public int AudioSampleRate { get; set; } = 48000;
        /// <summary>Audio bitrate in Kbps.</summary>
        public int AudioBitrateKbps { get; set; } = 192;
        /// <summary>Audio codec name (e.g. AAC or Opus).</summary>
        public string AudioCodec { get; set; } = "AAC";

        // ─── 4G/5G Cellular Bonding & Multi-Interface Redundancy ───────
        /// <summary>Enables multi-adapter bonding and seamless link failover.</summary>
        public bool BondingEnabled { get; set; } = false;
        /// <summary>Primary network adapter IP address.</summary>
        public string PrimaryInterfaceIp { get; set; } = string.Empty;
        /// <summary>Backup secondary adapter (e.g. 4G/5G cellular modem) IP address.</summary>
        public string BackupInterfaceIp { get; set; } = string.Empty;
        /// <summary>Packet loss threshold percentage triggering cellular failover.</summary>
        public double CellularLossThresholdPercent { get; set; } = 4.5;

        // ─── SMPTE ST 2022-7 & SRT Group Socket Mechanism ──────────────
        /// <summary>Bật/tắt cơ chế Group Socket chuẩn SMPTE ST 2022-7 với khả năng tự động kết nối member socket mới.</summary>
        public bool GroupSocketEnabled { get; set; } = false;
        /// <summary>Loại nhóm socket (Broadcast Hitless Redundancy hoặc Backup Active/Standby).</summary>
        public SRTGroupType GroupType { get; set; } = SRTGroupType.Broadcast_SMPTE2022_7;
        /// <summary>Danh sách cấu hình các member socket trong Group.</summary>
        public List<SRTGroupMemberConfig> GroupMembers { get; set; } = new();
        /// <summary>Độ trễ bù sai lệch đường truyền SMPTE 2022-7 (Differential Delay Buffer, 10ms - 500ms).</summary>
        public int HitlessDifferentialDelayMs { get; set; } = 50;

        /// <summary>
        /// Tạo cấu hình mặc định chuẩn phát sóng truyền hình (Broadcast Reference).
        /// </summary>
        public static SRTStreamConfig CreateDefault() => new SRTStreamConfig();

        /// <summary>
        /// Tạo URL chuẩn giao thức SRT với các query parameters cấu hình.
        /// </summary>
        public string ToSrtUri()
        {
            var sb = new StringBuilder();
            string modeParam = Mode switch
            {
                SRTMode.Listener => "listener",
                SRTMode.Rendezvous => "rendezvous",
                _ => "caller"
            };

            string host = string.IsNullOrWhiteSpace(Host) || (Mode == SRTMode.Caller && Host == "0.0.0.0")
                ? (Mode == SRTMode.Caller ? "127.0.0.1" : "0.0.0.0")
                : Host;

            sb.Append($"srt://{host}:{Port}?mode={modeParam}");

            if (LatencyMs > 0)
            {
                sb.Append($"&latency={LatencyMs}");
            }

            if (EncryptionEnabled && !string.IsNullOrWhiteSpace(Passphrase))
            {
                sb.Append($"&passphrase={Uri.EscapeDataString(Passphrase)}");
                sb.Append($"&pbkeylen={KeyLength}");
            }

            if (!string.IsNullOrWhiteSpace(StreamId))
            {
                sb.Append($"&streamid={Uri.EscapeDataString(StreamId)}");
            }

            if (!string.IsNullOrWhiteSpace(PrimaryInterfaceIp))
            {
                sb.Append($"&localip={Uri.EscapeDataString(PrimaryInterfaceIp)}");
            }

            if (BondingEnabled)
            {
                sb.Append("&bonding=1");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Tạo URL chuẩn giao thức SRT dành riêng cho một Member Socket trong Group.
        /// </summary>
        public string ToMemberSrtUri(SRTGroupMemberConfig member)
        {
            var sb = new StringBuilder();
            string modeParam = Mode switch
            {
                SRTMode.Listener => "listener",
                SRTMode.Rendezvous => "rendezvous",
                _ => "caller"
            };

            string host = string.IsNullOrWhiteSpace(member.Host) || (Mode == SRTMode.Caller && member.Host == "0.0.0.0")
                ? (Mode == SRTMode.Caller ? "127.0.0.1" : "0.0.0.0")
                : member.Host;

            sb.Append($"srt://{host}:{member.Port}?mode={modeParam}");

            int latency = member.LatencyMs > 0 ? member.LatencyMs : LatencyMs;
            if (latency > 0)
            {
                sb.Append($"&latency={latency}");
            }

            if (EncryptionEnabled && !string.IsNullOrWhiteSpace(Passphrase))
            {
                sb.Append($"&passphrase={Uri.EscapeDataString(Passphrase)}");
                sb.Append($"&pbkeylen={KeyLength}");
            }

            if (!string.IsNullOrWhiteSpace(StreamId))
            {
                sb.Append($"&streamid={Uri.EscapeDataString(StreamId)}");
            }

            string bindIp = !string.IsNullOrWhiteSpace(member.LocalInterfaceIp) ? member.LocalInterfaceIp : PrimaryInterfaceIp;
            if (!string.IsNullOrWhiteSpace(bindIp))
            {
                sb.Append($"&localip={Uri.EscapeDataString(bindIp)}");
            }

            sb.Append("&group=1");
            return sb.ToString();
        }

        /// <summary>
        /// Chuyển đổi toàn bộ thông số sang dictionary phục vụ đóng gói IPC command.
        /// </summary>
        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>
            {
                ["host"] = Host,
                ["port"] = Port,
                ["mode"] = Mode.ToString(),
                ["streamId"] = StreamId,
                ["latency"] = LatencyMs,
                ["autoLatency"] = AutoLatency,
                ["encryption"] = EncryptionEnabled,
                ["passphrase"] = Passphrase,
                ["pbkeylen"] = KeyLength,
                ["videoCodec"] = VideoCodec,
                ["width"] = Width,
                ["height"] = Height,
                ["frameRate"] = FrameRate,
                ["bitrateKbps"] = BitrateKbps,
                ["rateControl"] = RateControl,
                ["preset"] = EncoderPreset,
                ["hardwareEncoder"] = HardwareEncoder,
                ["ultraLowLatency"] = UltraLowLatency,
                ["gopSeconds"] = GopSeconds,
                ["bFrames"] = BFrames,
                ["ntpSync"] = NtpSyncEnabled,
                ["ntpServer"] = NtpServer,
                ["audioChannels"] = AudioChannels,
                ["audioSampleRate"] = AudioSampleRate,
                ["audioBitrateKbps"] = AudioBitrateKbps,
                ["audioCodec"] = AudioCodec,
                ["srtUri"] = ToSrtUri()
            };
        }

        /// <summary>
        /// Tính toán độ trễ tự động dựa trên giá trị RTT (3 x RTT, tối thiểu MinLatencyMs).
        /// </summary>
        public int CalculateAutoLatency(double rttMs)
        {
            int calculated = (int)Math.Max(MinLatencyMs, Math.Round(rttMs * 3.0));
            LatencyMs = calculated;
            return calculated;
        }
    }
}
