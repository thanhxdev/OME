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
        public string HardwareEncoder { get; set; } = "NVIDIA NVENC";

        // ─── Ultra Low-Latency (ULL) Profile ────────────────────────────
        public bool UltraLowLatency { get; set; } = true;
        public double GopSeconds { get; set; } = 1.0;
        public int BFrames { get; set; } = 0;

        // ─── Multi-Cam NTP & Wall-Clock Synchronization ─────────────────
        public bool NtpSyncEnabled { get; set; } = false;
        public string NtpServer { get; set; } = "time.google.com";

        // ─── Audio Configuration ────────────────────────────────────────
        public int AudioChannels { get; set; } = 2;
        public int AudioSampleRate { get; set; } = 48000;
        public int AudioBitrateKbps { get; set; } = 192;
        public string AudioCodec { get; set; } = "AAC";

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

            sb.Append($"srt://{Host}:{Port}?mode={modeParam}");

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
