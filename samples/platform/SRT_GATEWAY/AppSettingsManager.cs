using System;
using System.IO;
using System.Text.Json;

namespace SRT_GATEWAY
{
    public class GatewaySettings
    {
        // ─── SRT Ingest Parameters ──────────────────────────────────────
        public int SrtModeIndex { get; set; } = 0; // 0: Listener, 1: Caller, 2: Rendezvous
        public string IngestHost { get; set; } = "0.0.0.0";
        public int IngestPort { get; set; } = 9000;
        public bool IsSmpte2022_7IngestEnabled { get; set; } = false;
        public string MemberAHost { get; set; } = "0.0.0.0";
        public int MemberAPort { get; set; } = 9000;
        public string MemberBHost { get; set; } = "0.0.0.0";
        public int MemberBPort { get; set; } = 9002;
        public int DifferentialDelayMs { get; set; } = 50;
        public int LatencyMs { get; set; } = 120;
        public string Passphrase { get; set; } = string.Empty;
        public string StreamId { get; set; } = "gateway/live";

        // ─── Multi-Protocol Egress Parameters ───────────────────────────
        // 1. SRT Out
        public bool SrtOutEnabled { get; set; } = true;
        public int SrtOutModeIndex { get; set; } = 0; // 0: Caller, 1: Listener
        public string SrtOutHost { get; set; } = "127.0.0.1";
        public int SrtOutPort { get; set; } = 9100;
        public string SrtOutStreamId { get; set; } = "gateway/out";
        public string SrtOutPassphrase { get; set; } = string.Empty;
        public bool SrtOutSmpte2022_7 { get; set; } = false;
        public int SrtOutMemberBPort { get; set; } = 9102;

        // 2. WebRTC Out
        public bool WebRtcOutEnabled { get; set; } = true;
        public int WebRtcPort { get; set; } = 8889;

        // 3. RTMP Out
        public bool RtmpOutEnabled { get; set; } = false;
        public string RtmpUrl { get; set; } = "rtmp://127.0.0.1:1935/live";
        public string RtmpStreamKey { get; set; } = "live_feed";

        // 4. RTSP Out
        public bool RtspOutEnabled { get; set; } = true;
        public int RtspPort { get; set; } = 8554;
        public string RtspPath { get; set; } = "/live";

        // 5. LRT Out
        public bool LrtOutEnabled { get; set; } = false;
        public string LrtPath1Host { get; set; } = "127.0.0.1";
        public int LrtPath1Port { get; set; } = 9200;
        public string LrtPath2Host { get; set; } = "127.0.0.1";
        public int LrtPath2Port { get; set; } = 9202;

        // 6. HLS Out
        public bool HlsOutEnabled { get; set; } = true;
        public int HlsPort { get; set; } = 8080;
        public int HlsSegmentDurationSec { get; set; } = 2;

        // 7. DASH Out
        public bool DashOutEnabled { get; set; } = true;
        public int DashPort { get; set; } = 8080;

        // ─── Integrated SRT_MONITOR Settings ────────────────────────────
        public bool IsMonitorServerEnabled { get; set; } = true;
        public int MonitorPort { get; set; } = 8088;
        public bool BeeperAlertEnabled { get; set; } = true;

        // ─── Multiview Matrix Channels ──────────────────────────────────
        public List<GatewayChannelConfig> Channels { get; set; } = new();
        public int MultiviewLayoutMode { get; set; } = 0; // 0: Auto, 1: 1x1, 2: 1x2, 3: 2x2
    }

    public class GatewayChannelConfig : GatewaySettings
    {
        public int ChannelId { get; set; } = 1;
        public string ChannelName { get; set; } = "CH 1: SRT GATEWAY";
        public bool IsStarted { get; set; } = false;
    }

    public static class AppSettingsManager
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public static string SettingsDirectoryPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OME_SRT_GATEWAY");

        public static string SettingsFilePath => Path.Combine(SettingsDirectoryPath, "appsettings.json");

        public static GatewaySettings LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    var settings = JsonSerializer.Deserialize<GatewaySettings>(json, JsonOptions);
                    if (settings != null)
                    {
                        if (settings.Channels == null || settings.Channels.Count == 0)
                        {
                            settings.Channels = new List<GatewayChannelConfig>
                            {
                                CreateDefaultChannel(1, settings)
                            };
                        }
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AppSettingsManager] Error loading settings: {ex.Message}");
            }

            var defaultSettings = new GatewaySettings();
            defaultSettings.Channels = new List<GatewayChannelConfig>
            {
                CreateDefaultChannel(1, defaultSettings)
            };
            return defaultSettings;
        }

        public static GatewayChannelConfig CreateDefaultChannel(int channelId, GatewaySettings baseSettings)
        {
            int offset = (channelId - 1) * 10;
            return new GatewayChannelConfig
            {
                ChannelId = channelId,
                ChannelName = $"CH {channelId}: SRT GATEWAY",
                SrtModeIndex = baseSettings.SrtModeIndex,
                IngestHost = baseSettings.IngestHost,
                IngestPort = 9000 + offset,
                IsSmpte2022_7IngestEnabled = baseSettings.IsSmpte2022_7IngestEnabled,
                MemberAHost = baseSettings.MemberAHost,
                MemberAPort = 9000 + offset,
                MemberBHost = baseSettings.MemberBHost,
                MemberBPort = 9002 + offset,
                DifferentialDelayMs = baseSettings.DifferentialDelayMs,
                LatencyMs = baseSettings.LatencyMs,
                Passphrase = baseSettings.Passphrase,
                StreamId = $"gateway/ch{channelId}",

                SrtOutEnabled = baseSettings.SrtOutEnabled,
                SrtOutModeIndex = baseSettings.SrtOutModeIndex,
                SrtOutHost = baseSettings.SrtOutHost,
                SrtOutPort = 9100 + offset,
                SrtOutStreamId = $"gateway/out{channelId}",
                SrtOutPassphrase = baseSettings.SrtOutPassphrase,
                SrtOutSmpte2022_7 = baseSettings.SrtOutSmpte2022_7,
                SrtOutMemberBPort = 9102 + offset,

                WebRtcOutEnabled = baseSettings.WebRtcOutEnabled,
                WebRtcPort = 8889 + (channelId - 1),

                RtmpOutEnabled = baseSettings.RtmpOutEnabled,
                RtmpUrl = baseSettings.RtmpUrl,
                RtmpStreamKey = $"live_feed_{channelId}",

                RtspOutEnabled = baseSettings.RtspOutEnabled,
                RtspPort = 8554 + (channelId - 1),
                RtspPath = $"/ch{channelId}",

                LrtOutEnabled = baseSettings.LrtOutEnabled,
                LrtPath1Host = baseSettings.LrtPath1Host,
                LrtPath1Port = 9200 + offset,
                LrtPath2Host = baseSettings.LrtPath2Host,
                LrtPath2Port = 9202 + offset,

                HlsOutEnabled = baseSettings.HlsOutEnabled,
                HlsPort = 8080 + (channelId - 1),
                HlsSegmentDurationSec = baseSettings.HlsSegmentDurationSec,

                DashOutEnabled = baseSettings.DashOutEnabled,
                DashPort = 8080 + (channelId - 1)
            };
        }

        public static void SaveSettings(GatewaySettings settings)
        {
            try
            {
                if (settings == null) return;
                Directory.CreateDirectory(SettingsDirectoryPath);
                string json = JsonSerializer.Serialize(settings, JsonOptions);
                File.WriteAllText(SettingsFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AppSettingsManager] Error saving settings: {ex.Message}");
            }
        }
    }
}
