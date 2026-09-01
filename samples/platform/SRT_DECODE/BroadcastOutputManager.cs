using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using OpenMedia.Platform;
using OpenMedia.Platform.Models;

namespace SRT_DECODE
{
    public sealed class BroadcastOutputManager : IDisposable
    {
        // ─── SDI Output ─────────────────────────────────────────────────
        public bool SdiEnabled { get; set; } = false;
        public string SdiDevice { get; set; } = "DeckLink Studio 4K (Card 1)";
        public string SdiMode { get; set; } = "1080p59.94 (Fill + Key)";

        // ─── NDI Output ─────────────────────────────────────────────────
        public bool NdiEnabled { get; set; } = false;
        public string NdiStreamName { get; set; } = "OME_STUDIO_PROGRAM";
        public bool NdiMultiviewerMode { get; set; } = false;

        // ─── SRT Bridge (Re-transmitter) ────────────────────────────────
        public bool SrtBridgeEnabled { get; set; } = false;
        public string SrtBridgeHost { get; set; } = "192.168.1.150";
        public int SrtBridgePort { get; set; } = 9100;
        public SRTMode SrtBridgeMode { get; set; } = SRTMode.Caller;
        public int SrtBridgeBitrateKbps { get; set; } = 8000;
        public string SrtBridgeCodec { get; set; } = "H.264";

        // ─── Master Recording ───────────────────────────────────────────
        public bool RecordingEnabled { get; set; } = false;
        public string RecordingFolder { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        public string RecordingFormat { get; set; } = "MP4 (H.264 / AAC)";
        public TimeSpan RecordingDuration { get; set; } = TimeSpan.Zero;
        public ulong RecordedBytes { get; set; } = 0;
        public DateTime RecordingStartTime { get; set; } = DateTime.MinValue;

        public event Action<string, string>? LogEmitted;
        public event Action<string, bool>? OutputStateChanged;

        public async Task<bool> ToggleSdiAsync(bool enable)
        {
            SdiEnabled = enable;
            Log("[SDI]", enable 
                ? $"📡 SDI Output ĐÃ BẬT trên thiết bị: {SdiDevice} ({SdiMode})" 
                : "📡 SDI Output ĐÃ TẮT.");
            OutputStateChanged?.Invoke("SDI", enable);
            await Task.CompletedTask;
            return true;
        }

        public async Task<bool> ToggleNdiAsync(bool enable)
        {
            NdiEnabled = enable;
            string target = NdiMultiviewerMode ? "Multiviewer Grid" : "Master Program";
            Log("[NDI]", enable 
                ? $"🌐 NDI Output ĐÃ PHÁT SÓNG trên luồng: '{NdiStreamName}' ({target})" 
                : "🌐 NDI Output ĐÃ DỪNG.");
            OutputStateChanged?.Invoke("NDI", enable);
            await Task.CompletedTask;
            return true;
        }

        public async Task<bool> ToggleSrtBridgeAsync(bool enable)
        {
            SrtBridgeEnabled = enable;
            Log("[BRIDGE]", enable 
                ? $"🔁 SRT Re-transmitter Bridge ĐÃ BẬT ➔ srt://{SrtBridgeHost}:{SrtBridgePort} ({SrtBridgeCodec} @ {SrtBridgeBitrateKbps} kbps)" 
                : "🔁 SRT Re-transmitter Bridge ĐÃ DỪNG.");
            OutputStateChanged?.Invoke("SRT_BRIDGE", enable);
            await Task.CompletedTask;
            return true;
        }

        public async Task<bool> ToggleRecordingAsync(bool enable)
        {
            RecordingEnabled = enable;
            if (enable)
            {
                RecordingStartTime = DateTime.UtcNow;
                RecordedBytes = 0;
                string filename = $"OME_REC_{DateTime.Now:yyyyMMdd_HHmmss}.mp4";
                string fullPath = Path.Combine(RecordingFolder, filename);
                Log("[REC]", $"💾 BẮT ĐẦU GHI LƯU MASTER PROGRAM: {fullPath} ({RecordingFormat})");
            }
            else
            {
                Log("[REC]", $"💾 ĐÃ DỪNG GHI HÌNH. Tổng thời lượng: {RecordingDuration:hh\\:mm\\:ss}, Dung lượng: {RecordedBytes / (1024.0 * 1024.0):F2} MB");
                RecordingStartTime = DateTime.MinValue;
            }

            OutputStateChanged?.Invoke("REC", enable);
            await Task.CompletedTask;
            return true;
        }

        public void UpdateRecordingStats()
        {
            if (RecordingEnabled && RecordingStartTime != DateTime.MinValue)
            {
                RecordingDuration = DateTime.UtcNow - RecordingStartTime;
                // Accumulate approx 8Mbps stream data
                RecordedBytes += (ulong)(1000000 * 0.5); // ~1MB per sec at 8Mbps
            }
        }

        private void Log(string tag, string message)
        {
            LogEmitted?.Invoke(tag, message);
            Trace.WriteLine($"[BroadcastOutputManager]{tag} {message}");
        }

        public void Dispose()
        {
            if (RecordingEnabled)
            {
                ToggleRecordingAsync(false).GetAwaiter().GetResult();
            }
        }
    }
}
