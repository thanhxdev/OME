using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SRT_ENCODE
{
    /// <summary>
    /// Cấu hình lưu trữ của một dynamic member socket.
    /// </summary>
    public sealed class DynamicMemberSetting
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "Path C (Backup)";
        public string Host { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 9004;
        public string NicIp { get; set; } = "0.0.0.0";
    }

    /// <summary>
    /// Cấu hình toàn diện của ứng dụng SRT_ENCODE lưu trữ qua các phiên làm việc.
    /// </summary>
    public sealed class SrtEncodeSettings
    {
        // ─── Network Transmission & SMPTE 2022-7 Toggle ─────────────────
        public bool IsSmpte2022_7Enabled { get; set; } = false;

        // ─── Single Stream Parameters ───────────────────────────────────
        public string SrtIp { get; set; } = "127.0.0.1";
        public int SrtPort { get; set; } = 9000;
        public string StreamId { get; set; } = "live/cam1/feed";
        public string SingleSourceNicIp { get; set; } = "0.0.0.0";

        // ─── SMPTE 2022-7 Group Redundancy ─────────────────────────────
        public int GroupTypeIndex { get; set; } = 0; // 0 = Broadcast, 1 = Backup
        public int DifferentialDelayMs { get; set; } = 50;

        public string MemberAHost { get; set; } = "127.0.0.1";
        public int MemberAPort { get; set; } = 9000;
        public string MemberANicIp { get; set; } = "0.0.0.0";

        public string MemberBHost { get; set; } = "127.0.0.1";
        public int MemberBPort { get; set; } = 9002;
        public string MemberBNicIp { get; set; } = "0.0.0.0";

        public List<DynamicMemberSetting> DynamicMembers { get; set; } = new();

        // ─── Video & Encoding Parameters ────────────────────────────────
        public int BitrateKbps { get; set; } = 6000;
        public int VideoCodecIndex { get; set; } = 0;
        public string HardwareEncoder { get; set; } = "Intel QuickSync Video (QSV)";
        public int StreamFrameRateIndex { get; set; } = 0;
        public int RateControlIndex { get; set; } = 0;
        public int EncoderPresetIndex { get; set; } = 0;
        public bool UltraLowLatency { get; set; } = true;

        // ─── Protocol & Encryption ──────────────────────────────────────
        public int SrtModeIndex { get; set; } = 0;
        public int LatencyMs { get; set; } = 120;
        public bool AutoLatency { get; set; } = false;
        public bool EncryptionEnabled { get; set; } = false;
        public string Passphrase { get; set; } = string.Empty;
        public int KeyLengthIndex { get; set; } = 2; // AES-256

        // ─── Audio & Wall-Clock NTP ─────────────────────────────────────
        public int AudioChannelsIndex { get; set; } = 0;
        public bool NtpSyncEnabled { get; set; } = false;
        public string NtpServer { get; set; } = "time.google.com";

        // ─── Telemetry Monitor (Giám sát tập trung) ─────────────────────
        public bool IsTelemetryMonitorEnabled { get; set; } = false;
        public string TelemetryServerUrl { get; set; } = "http://127.0.0.1:8088";
        public string TelemetryNodeName { get; set; } = "ENC_CAM_01";
    }

    /// <summary>
    /// Trình quản lý nạp và lưu cấu hình ứng dụng dạng JSON tại thư mục %LOCALAPPDATA%\OME_SRT_ENCODE.
    /// </summary>
    public static class AppSettingsManager
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public static string SettingsDirectoryPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OME_SRT_ENCODE");

        public static string SettingsFilePath => Path.Combine(SettingsDirectoryPath, "appsettings.json");

        /// <summary>
        /// Nạp cấu hình từ ổ đĩa. Trả về cấu hình mặc định nếu file chưa tồn tại hoặc lỗi đọc.
        /// </summary>
        public static SrtEncodeSettings LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    var settings = JsonSerializer.Deserialize<SrtEncodeSettings>(json, JsonOptions);
                    if (settings != null)
                    {
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AppSettingsManager] Lỗi nạp cấu hình: {ex.Message}");
            }

            return new SrtEncodeSettings();
        }

        /// <summary>
        /// Lưu cấu hình hiện tại xuống ổ đĩa an toàn.
        /// </summary>
        public static bool SaveSettings(SrtEncodeSettings settings)
        {
            try
            {
                if (!Directory.Exists(SettingsDirectoryPath))
                {
                    Directory.CreateDirectory(SettingsDirectoryPath);
                }

                string json = JsonSerializer.Serialize(settings, JsonOptions);
                File.WriteAllText(SettingsFilePath, json);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AppSettingsManager] Lỗi lưu cấu hình: {ex.Message}");
                return false;
            }
        }
    }
}
