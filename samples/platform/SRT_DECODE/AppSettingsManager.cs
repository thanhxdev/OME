using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SRT_DECODE
{
    /// <summary>
    /// Cấu hình lưu trữ của một dynamic member socket gắn thêm vào luồng SMPTE 2022-7.
    /// </summary>
    public sealed class DynamicMemberSetting
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "Path C (Starlink/5G)";
        public string Host { get; set; } = "0.0.0.0";
        public int Port { get; set; } = 9020;
        public string NicIp { get; set; } = "0.0.0.0";
    }

    /// <summary>
    /// Cấu hình toàn diện cho một kênh Ingest CAM độc lập (CAM 1..10).
    /// </summary>
    public sealed class ChannelSettings
    {
        // ─── Thông tin cơ bản & Chế độ SRT ──────────────────────────────
        public string DisplayName { get; set; } = "CAM 1";
        public int SrtModeIndex { get; set; } = 0; // 0: Listener, 1: Caller, 2: Rendezvous
        public string StreamId { get; set; } = "live/cam1";
        public int LatencyMs { get; set; } = 300;
        public bool AutoLatency { get; set; } = false;
        public bool EncryptionEnabled { get; set; } = false;
        public string Passphrase { get; set; } = string.Empty;
        public int KeyLengthIndex { get; set; } = 0; // 0: 256-bit, 1: 192-bit, 2: 128-bit

        // ─── Toggle SMPTE ST 2022-7 Hitless Redundancy ──────────────────
        public bool IsSmpte2022_7Enabled { get; set; } = false;

        // ─── Chế độ Đơn Luồng (Single Stream) ───────────────────────────
        public string BindIpOrHost { get; set; } = "0.0.0.0";
        public int Port { get; set; } = 9000;
        public string SourceNicIp { get; set; } = "0.0.0.0";

        // ─── Chế độ SMPTE ST 2022-7 Group Socket ────────────────────────
        public int DifferentialDelayMs { get; set; } = 50;

        public string MemberAHost { get; set; } = "0.0.0.0";
        public int MemberAPort { get; set; } = 9000;
        public string MemberANicIp { get; set; } = "0.0.0.0";

        public string MemberBHost { get; set; } = "0.0.0.0";
        public int MemberBPort { get; set; } = 9002;
        public string MemberBNicIp { get; set; } = "0.0.0.0";

        public List<DynamicMemberSetting> DynamicMembers { get; set; } = new();

        // ─── ISO Output Routing (Tab 2) ─────────────────────────────────
        public bool SdiEnabled { get; set; } = false;
        public int SdiPortIndex { get; set; } = 0;
        public bool NdiEnabled { get; set; } = false;
        public string NdiName { get; set; } = "OME_DECODE_CAM1";
        public bool SrtEnabled { get; set; } = false;
        public string SrtHost { get; set; } = "127.0.0.1";
        public int SrtPort { get; set; } = 10000;
        public int SrtMode { get; set; } = 0; // Caller
        public int SrtCodecIndex { get; set; } = 0; // H.264
        public int SrtBitrateKbps { get; set; } = 8000;
        public bool RecEnabled { get; set; } = false;
        public int RecFormatIndex { get; set; } = 0;
        public int ResIndex { get; set; } = 0;
        public int CodecIndex { get; set; } = 0;
        public int BitrateIndex { get; set; } = 1;
        public int FpsIndex { get; set; } = 0;
    }

    /// <summary>
    /// Cấu hình cho đầu ra PGM Master (Tab 3).
    /// </summary>
    public sealed class MasterOutputSettings
    {
        public bool SdiEnabled { get; set; } = false;
        public int SdiPortIndex { get; set; } = 0;
        public bool NdiEnabled { get; set; } = false;
        public string NdiName { get; set; } = "OME_DECODE_PGM_MASTER";
        public bool RecEnabled { get; set; } = false;
        public int RecFormatIndex { get; set; } = 0;
        public int ResIndex { get; set; } = 0;
        public int CodecIndex { get; set; } = 0;
        public int BitrateIndex { get; set; } = 1;
        public int FpsIndex { get; set; } = 0;
        public bool LowLatency { get; set; } = true;
        public bool AutoReconnect { get; set; } = true;
    }

    /// <summary>
    /// Toàn bộ cấu hình của ứng dụng SRT_DECODE lưu trữ qua các phiên làm việc.
    /// </summary>
    public sealed class SrtDecodeSettings
    {
        public int ActiveStreamCount { get; set; } = 1;
        public int LayoutModeIndex { get; set; } = 0;
        public bool MasterSyncEnabled { get; set; } = false;
        public string NtpServer { get; set; } = "time.google.com";

        // ─── Telemetry Monitor (Giám sát tập trung) ─────────────────────
        public bool IsTelemetryMonitorEnabled { get; set; } = false;
        public string TelemetryServerUrl { get; set; } = "http://127.0.0.1:8088";
        public string TelemetryNodeName { get; set; } = "DEC_STATION_01";

        public List<ChannelSettings> Channels { get; set; } = new();
        public MasterOutputSettings MasterOutput { get; set; } = new();

        public static SrtDecodeSettings CreateDefault()
        {
            var settings = new SrtDecodeSettings
            {
                ActiveStreamCount = 1,
                LayoutModeIndex = 0,
                MasterSyncEnabled = false,
                NtpServer = "time.google.com",
                MasterOutput = new MasterOutputSettings()
            };

            for (int i = 0; i < 10; i++)
            {
                int camNum = i + 1;
                settings.Channels.Add(new ChannelSettings
                {
                    DisplayName = $"CAM {camNum}",
                    SrtModeIndex = 0, // Listener
                    StreamId = $"live/cam{camNum}",
                    LatencyMs = 300,
                    AutoLatency = false,
                    IsSmpte2022_7Enabled = false,
                    BindIpOrHost = "0.0.0.0",
                    Port = 9000 + i,
                    SourceNicIp = "0.0.0.0",
                    DifferentialDelayMs = 50,
                    MemberAHost = "0.0.0.0",
                    MemberAPort = 9000 + i,
                    MemberANicIp = "0.0.0.0",
                    MemberBHost = "0.0.0.0",
                    MemberBPort = 9000 + i + 2,
                    MemberBNicIp = "0.0.0.0",
                    NdiName = $"OME_DECODE_CAM{camNum}"
                });
            }

            return settings;
        }
    }

    /// <summary>
    /// Trình quản lý nạp và lưu cấu hình ứng dụng dạng JSON tại thư mục %LOCALAPPDATA%\OME_SRT_DECODE.
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
            "OME_SRT_DECODE");

        public static string SettingsFilePath => Path.Combine(SettingsDirectoryPath, "appsettings.json");

        /// <summary>
        /// Nạp cấu hình từ ổ đĩa. Trả về cấu hình mặc định nếu file chưa tồn tại hoặc lỗi đọc.
        /// </summary>
        public static SrtDecodeSettings LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    var settings = JsonSerializer.Deserialize<SrtDecodeSettings>(json, JsonOptions);
                    if (settings != null)
                    {
                        // Đảm bảo đủ 10 kênh
                        if (settings.Channels == null || settings.Channels.Count == 0)
                        {
                            return SrtDecodeSettings.CreateDefault();
                        }

                        while (settings.Channels.Count < 10)
                        {
                            int i = settings.Channels.Count;
                            int camNum = i + 1;
                            settings.Channels.Add(new ChannelSettings
                            {
                                DisplayName = $"CAM {camNum}",
                                SrtModeIndex = 0,
                                StreamId = $"live/cam{camNum}",
                                LatencyMs = 300,
                                AutoLatency = false,
                                IsSmpte2022_7Enabled = false,
                                BindIpOrHost = "0.0.0.0",
                                Port = 9000 + i,
                                SourceNicIp = "0.0.0.0",
                                DifferentialDelayMs = 50,
                                MemberAHost = "0.0.0.0",
                                MemberAPort = 9000 + i,
                                MemberANicIp = "0.0.0.0",
                                MemberBHost = "0.0.0.0",
                                MemberBPort = 9000 + i + 2,
                                MemberBNicIp = "0.0.0.0",
                                NdiName = $"OME_DECODE_CAM{camNum}"
                            });
                        }

                        settings.MasterOutput ??= new MasterOutputSettings();
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AppSettingsManager] Lỗi nạp cấu hình: {ex.Message}");
            }

            return SrtDecodeSettings.CreateDefault();
        }

        /// <summary>
        /// Lưu cấu hình hiện tại xuống ổ đĩa an toàn.
        /// </summary>
        public static bool SaveSettings(SrtDecodeSettings settings)
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
