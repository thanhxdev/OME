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
        public bool AutoLatencyEnabled { get; set; } = false;
        public bool EncryptionEnabled { get; set; } = false;
        public int KeyLengthIndex { get; set; } = 0; // 0: 256-bit, 1: 192-bit, 2: 128-bit
        public string Passphrase { get; set; } = string.Empty;
        public string StreamId { get; set; } = "gateway/live";
        public string SourceNicIp { get; set; } = "0.0.0.0";
        public string MemberANicIp { get; set; } = "0.0.0.0";
        public string MemberBNicIp { get; set; } = "0.0.0.0";
        public string NewMemberName { get; set; } = "Path C (5G)";
        public string NewMemberHost { get; set; } = "0.0.0.0";
        public int NewMemberPort { get; set; } = 9020;
        public string NewMemberNicIp { get; set; } = "0.0.0.0";
        public System.Collections.Generic.List<OpenMedia.Platform.Models.SRTGroupMemberConfig> ExtraGroupMembers { get; set; } = new();

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

        // ─── Dynamic Egress Streams List (Tab 2: Egress Fan-Out) ────────
        public List<GatewayEgressStreamConfig> EgressStreams { get; set; } = new();
    }

    public class GatewayEgressStreamConfig
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "Luồng Egress";
        public string Protocol { get; set; } = "SRT"; // SRT, WebRTC, RTMP, RTSP, LRT, HLS, DASH
        public bool IsEnabled { get; set; } = false;

        // ─── SRT Egress (Học từ Tab 1 SRT Config của App SRT_ENCODE) ───
        public int SrtModeIndex { get; set; } = 0; // 0: Caller, 1: Listener, 2: Rendezvous
        public string SrtHost { get; set; } = "127.0.0.1";
        public int SrtPort { get; set; } = 9100;
        public string SrtStreamId { get; set; } = "gateway/out";
        public int LatencyMs { get; set; } = 120;
        public bool AutoLatencyEnabled { get; set; } = false;
        public bool EncryptionEnabled { get; set; } = false;
        public string Passphrase { get; set; } = string.Empty;
        public int KeyLengthIndex { get; set; } = 2; // 0: 128-bit, 1: 192-bit, 2: 256-bit
        public string SourceNicIp { get; set; } = "0.0.0.0";
        public bool IsSmpte2022_7Enabled { get; set; } = false;
        public int GroupTypeIndex { get; set; } = 0; // 0: Broadcast, 1: Backup
        public int DifferentialDelayMs { get; set; } = 50;
        public string MemberAHost { get; set; } = "127.0.0.1";
        public int MemberAPort { get; set; } = 9100;
        public string MemberANicIp { get; set; } = "0.0.0.0";
        public string MemberBHost { get; set; } = "127.0.0.1";
        public int MemberBPort { get; set; } = 9102;
        public string MemberBNicIp { get; set; } = "0.0.0.0";

        // ─── WebRTC Egress (Học từ Tab 1 WebRTC Transmit của App WEBRTC_ENCODE) ───
        public string CameraId { get; set; } = "cam-01";
        public string CameraDisplayName { get; set; } = "Máy quay Gateway 01";
        public string SessionId { get; set; } = "broadcast-01";
        public string SignalingUrl { get; set; } = "http://127.0.0.1:8889/webrtc";
        public bool EnableIce { get; set; } = false;
        public string StunServer { get; set; } = "stun:stun.l.google.com:19302";
        public string TurnServer { get; set; } = "turn:your-server.com:3478";
        public string TurnUsername { get; set; } = "";
        public string TurnPassword { get; set; } = "";
        public string SfuHost { get; set; } = "127.0.0.1";
        public int WebRtcPort { get; set; } = 8889;
        public int PortModeIndex { get; set; } = 0; // 0: 2 Ports (Split A/V), 1: 1 Port (Muxed BUNDLE)
        public bool AntiEcho { get; set; } = true;
        public int VideoCodecIndex { get; set; } = 0; // 0: H.264, 1: H.265, 2: Passthrough
        public string VideoCodec { get; set; } = "H.264 / AVC (Payload Type 96)";
        public int TargetBitrateKbps { get; set; } = 8000;

        // Convenient Aliases
        public string DisplayName { get => CameraDisplayName; set => CameraDisplayName = value; }
        public string SessionRoom { get => SessionId; set => SessionId = value; }
        public bool AntiEchoGuard { get => AntiEcho; set => AntiEcho = value; }

        // ─── RTMP Egress ───
        public string RtmpUrl { get; set; } = "rtmp://127.0.0.1:1935/live";
        public string RtmpStreamKey { get; set; } = "live_feed_1";

        // ─── RTSP Egress ───
        public int RtspPort { get; set; } = 8554;
        public string RtspPath { get; set; } = "/ch1";

        // ─── LRT Egress ───
        public string LrtPath1Host { get; set; } = "127.0.0.1";
        public int LrtPath1Port { get; set; } = 9200;
        public string LrtPath2Host { get; set; } = "127.0.0.1";
        public int LrtPath2Port { get; set; } = 9202;

        // ─── HLS Egress ───
        public int HlsPort { get; set; } = 8080;
        public int HlsSegmentDurationSec { get; set; } = 2;

        // ─── DASH Egress ───
        public int DashPort { get; set; } = 8080;
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
                        else
                        {
                            foreach (var ch in settings.Channels)
                            {
                                if (ch.EgressStreams == null)
                                {
                                    ch.EgressStreams = new List<GatewayEgressStreamConfig>();
                                }
                            }
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

        public static List<GatewayEgressStreamConfig> CreateDefaultEgressStreams(int channelId, GatewaySettings? baseSettings = null)
        {
            int offset = (channelId - 1) * 10;
            return new List<GatewayEgressStreamConfig>
            {
                new GatewayEgressStreamConfig
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = $"SRT OUT #{channelId}",
                    Protocol = "SRT",
                    IsEnabled = true,
                    SrtModeIndex = 0,
                    SrtHost = "127.0.0.1",
                    SrtPort = 9100 + offset,
                    SrtStreamId = $"gateway/out{channelId}",
                    LatencyMs = 120,
                    AutoLatencyEnabled = false,
                    EncryptionEnabled = false,
                    Passphrase = "",
                    KeyLengthIndex = 2,
                    SourceNicIp = "0.0.0.0",
                    IsSmpte2022_7Enabled = false,
                    MemberAHost = "127.0.0.1",
                    MemberAPort = 9100 + offset,
                    MemberBHost = "127.0.0.1",
                    MemberBPort = 9102 + offset
                },
                new GatewayEgressStreamConfig
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = $"WebRTC Transmit #{channelId}",
                    Protocol = "WebRTC",
                    IsEnabled = true,
                    CameraId = $"cam-0{Math.Min(channelId, 9)}",
                    CameraDisplayName = $"Máy quay Gateway 0{channelId}",
                    SessionId = $"broadcast-0{channelId}",
                    SignalingUrl = "ws://127.0.0.1:3000/ws",
                    SfuHost = "127.0.0.1",
                    WebRtcPort = 8889 + (channelId - 1),
                    PortModeIndex = 0,
                    AntiEcho = true,
                    VideoCodecIndex = 0
                },
                new GatewayEgressStreamConfig
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = $"RTMP OUT #{channelId}",
                    Protocol = "RTMP",
                    IsEnabled = false,
                    RtmpUrl = "rtmp://127.0.0.1:1935/live",
                    RtmpStreamKey = $"live_feed_{channelId}"
                },
                new GatewayEgressStreamConfig
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = $"RTSP OUT #{channelId}",
                    Protocol = "RTSP",
                    IsEnabled = true,
                    RtspPort = 8554 + (channelId - 1),
                    RtspPath = $"/ch{channelId}"
                },
                new GatewayEgressStreamConfig
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = $"LRT Bonding #{channelId}",
                    Protocol = "LRT",
                    IsEnabled = false,
                    LrtPath1Host = "127.0.0.1",
                    LrtPath1Port = 9200 + offset,
                    LrtPath2Host = "127.0.0.1",
                    LrtPath2Port = 9202 + offset
                },
                new GatewayEgressStreamConfig
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = $"HLS Stream #{channelId}",
                    Protocol = "HLS",
                    IsEnabled = true,
                    HlsPort = 8080 + (channelId - 1),
                    HlsSegmentDurationSec = 2
                },
                new GatewayEgressStreamConfig
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = $"DASH Stream #{channelId}",
                    Protocol = "DASH",
                    IsEnabled = true,
                    DashPort = 8080 + (channelId - 1)
                }
            };
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
                AutoLatencyEnabled = baseSettings.AutoLatencyEnabled,
                EncryptionEnabled = baseSettings.EncryptionEnabled,
                KeyLengthIndex = baseSettings.KeyLengthIndex,
                Passphrase = baseSettings.Passphrase,
                StreamId = $"gateway/ch{channelId}",
                SourceNicIp = baseSettings.SourceNicIp,
                MemberANicIp = baseSettings.MemberANicIp,
                MemberBNicIp = baseSettings.MemberBNicIp,
                NewMemberName = $"Path C (5G)",
                NewMemberHost = "0.0.0.0",
                NewMemberPort = 9020 + offset,
                NewMemberNicIp = "0.0.0.0",
                ExtraGroupMembers = new System.Collections.Generic.List<OpenMedia.Platform.Models.SRTGroupMemberConfig>(),

                EgressStreams = new List<GatewayEgressStreamConfig>(),

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
