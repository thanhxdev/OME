using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using OpenMedia.Platform;
using OpenMedia.Platform.Models;

using OpenMedia.Platform.IPC;

namespace WEBRTC_DECODE
{
    public sealed class VideoCodecConfig
    {
        public string Resolution { get; set; } = "1920x1080 (1080p)";
        public string Codec { get; set; } = "H.264 / AVC (NVENC)";
        public string Bitrate { get; set; } = "8 Mbps";
        public string Fps { get; set; } = "59.94 fps";

        public (int width, int height) ParseResolution(int defaultWidth = 1920, int defaultHeight = 1080)
        {
            if (Resolution.Contains("3840x2160") || Resolution.Contains("4K")) return (3840, 2160);
            if (Resolution.Contains("1280x720") || Resolution.Contains("720p")) return (1280, 720);
            if (Resolution.Contains("1920x1080") || Resolution.Contains("1080p")) return (1920, 1080);
            return (defaultWidth, defaultHeight);
        }

        public double ParseFps(double defaultFps = 59.94)
        {
            if (Fps.Contains("24")) return 24.0;
            if (Fps.Contains("25")) return 25.0;
            if (Fps.Contains("29.97")) return 29.97;
            if (Fps.Contains("30")) return 30.0;
            if (Fps.Contains("50")) return 50.0;
            if (Fps.Contains("60.00") || Fps.Contains("60 fps")) return 60.0;
            if (Fps.Contains("59.94")) return 59.94;
            return defaultFps;
        }
    }

    public sealed class ReceiverOutputConfig
    {
        public int ChannelIndex { get; set; }
        public string ChannelName { get; set; } = "CAM 1";

        // Output Port Routing
        public bool SdiEnabled { get; set; } = false;
        public string SdiPort { get; set; } = "Blackmagic DeckLink (Port 1)";
        public bool NdiEnabled { get; set; } = false;
        public string NdiName { get; set; } = "OME ISO CAM 01";
        public bool SrtBridgeEnabled { get; set; } = false;
        public string SrtBridgeHost { get; set; } = "192.168.1.150";
        public int SrtBridgePort { get; set; } = 9101;
        public SRTMode SrtBridgeMode { get; set; } = SRTMode.Caller;
        public string SrtBridgeCodec { get; set; } = "H.264 (NVENC)";
        public string SrtBridgeBitrate { get; set; } = "8000 kbps";
        public bool RecEnabled { get; set; } = false;
        public string RecFormat { get; set; } = "MP4 (H.264 / AAC)";

        // Codec Settings
        public string Codec { get; set; } = "Passthrough (Native Source)";
        public string Resolution { get; set; } = "Match Source (Passthrough)";
        public string Bitrate { get; set; } = "Match Source";
        public string Fps { get; set; } = "Match Source";

        public VideoCodecConfig ToCodecConfig()
        {
            return new VideoCodecConfig
            {
                Resolution = Resolution,
                Codec = Codec,
                Bitrate = Bitrate,
                Fps = Fps
            };
        }
    }

    /// <summary>
    /// Master Broadcast Output Manager coordinating physical SDI card playout,
    /// NDI network feeds, SRT Re-transmitter bridges, File Recorders,
    /// and the 11-Channel D3D11 Shared Texture Zero-Copy / IPC Audio Matrix Router.
    /// </summary>
    public sealed class BroadcastOutputManager : IDisposable
    {
        public const int MaxChannels = 10;

        // ─── IP Video Matrix Router Publisher (On-Demand) ───────────────
        private MatrixRouterPublisher? _matrixPublisher;
        public MatrixRouterPublisher? MatrixPublisher => _matrixPublisher;
        public bool IsMatrixIpcActive => _matrixPublisher?.IsInitialized == true;

        public bool StartMatrixIpc(int width = 1920, int height = 1080)
        {
            try
            {
                if (_matrixPublisher == null || !_matrixPublisher.IsInitialized)
                {
                    _matrixPublisher?.Dispose();
                    _matrixPublisher = new MatrixRouterPublisher { SlotBaseIndex = 11 };
                    bool ok = _matrixPublisher.Initialize(width, height);
                    if (ok)
                    {
                        Log("[MATRIX-ROUTER]", "✅ Đã mở IP Video Matrix Router liên tiến trình (Slots 11..20 dành cho WebRTC + Audio MMF).");
                        return true;
                    }
                    else
                    {
                        Log("[MATRIX-ROUTER]", $"❌ Khởi tạo Matrix Router thất bại: {_matrixPublisher.LastError}");
                        return false;
                    }
                }
                return _matrixPublisher?.IsInitialized == true;
            }
            catch (Exception ex)
            {
                Log("[MATRIX-ROUTER]", $"❌ Lỗi khởi tạo IP Video Matrix Router: {ex.Message}");
                return false;
            }
        }

        public void StopMatrixIpc()
        {
            try
            {
                if (_matrixPublisher != null)
                {
                    _matrixPublisher.Dispose();
                    _matrixPublisher = null;
                    Log("[MATRIX-ROUTER]", "⏹ Đã dừng IP Video Matrix Router liên tiến trình.");
                }
            }
            catch (Exception ex)
            {
                Log("[MATRIX-ROUTER]", $"Lỗi dừng Matrix Router: {ex.Message}");
            }
        }

        // ─── Master Video Codec Configuration ───────────────────────────
        public VideoCodecConfig MasterVideoCodec { get; } = new();

        // ─── Per-Receiver Output Configurations (Up to 10) ──────────────
        public ReceiverOutputConfig[] ReceiverOutputs { get; } = new ReceiverOutputConfig[MaxChannels];

        // ─── Output Workers ─────────────────────────────────────────────
        private readonly BroadcastOutputWorker _masterWorker;
        private readonly BroadcastOutputWorker[] _isoWorkers = new BroadcastOutputWorker[MaxChannels];

        // ─── SDI Output ─────────────────────────────────────────────────
        public bool SdiEnabled { get; set; } = false;
        public string SdiDevice { get; set; } = "DeckLink Studio 4K (Card 1)";
        public string SdiMode { get; set; } = "1080p59.94 (Fill + Key)";

        // ─── NDI Output ─────────────────────────────────────────────────
        public bool NdiEnabled { get; set; } = false;
        public string NdiStreamName { get; set; } = "OME PGM MASTER";
        public bool NdiMultiviewerMode { get; set; } = false;
        public MultiviewerCompositor Compositor { get; } = new();

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
        public TimeSpan RecordingDuration => _masterWorker.RecordingDuration;
        public ulong RecordedBytes => _masterWorker.RecordedBytes;
        public DateTime RecordingStartTime { get; set; } = DateTime.MinValue;

        public event Action<string, string>? LogEmitted;
        public event Action<string, bool>? OutputStateChanged;

        public BroadcastOutputManager()
        {
            _masterWorker = new BroadcastOutputWorker(-1, "PROGRAM MASTER");
            _masterWorker.LogEmitted += (tag, msg) => Log(tag, msg);

            for (int i = 0; i < MaxChannels; i++)
            {
                ReceiverOutputs[i] = new ReceiverOutputConfig
                {
                    ChannelIndex = i,
                    ChannelName = $"CAM {i + 1}",
                    SdiPort = $"Blackmagic DeckLink (Port {i + 1})",
                    NdiName = $"OME ISO CAM {i + 1:D2}"
                };

                _isoWorkers[i] = new BroadcastOutputWorker(i, $"CAM {i + 1} ISO");
                int idx = i;
                _isoWorkers[i].LogEmitted += (tag, msg) => Log($"[CAM {idx + 1}]", msg);
            }

            // IP Video Matrix Router is now on-demand (Click '▶ Start Matrix Output' on Tab 7 to start)
        }

        #region Master Program Output Control

        public async Task<bool> ToggleSdiAsync(bool enable)
        {
            SdiEnabled = enable;
            if (enable)
            {
                var (w, h) = MasterVideoCodec.ParseResolution(1920, 1080);
                double fps = MasterVideoCodec.ParseFps(59.94);
                _masterWorker.StartSdi(SdiDevice, SdiMode, w, h, fps);
            }
            else
            {
                _masterWorker.StopSdi();
            }

            OutputStateChanged?.Invoke("SDI", enable);
            await Task.CompletedTask;
            return true;
        }

        public async Task<bool> ToggleNdiAsync(bool enable)
        {
            NdiEnabled = enable;
            if (enable)
            {
                _masterWorker.StartNdi(NdiStreamName);
                if (NdiMultiviewerMode)
                {
                    Compositor.Start((frame, w, h, fps) =>
                    {
                        _masterWorker.FeedNdiVideo(frame, w, h, fps);
                    }, 59.94);
                }
            }
            else
            {
                Compositor.Stop();
                _masterWorker.StopNdi();
            }

            OutputStateChanged?.Invoke("NDI", enable);
            await Task.CompletedTask;
            return true;
        }

        public void SetNdiMultiviewerMode(bool isMultiviewer)
        {
            NdiMultiviewerMode = isMultiviewer;
            if (NdiEnabled)
            {
                if (isMultiviewer)
                {
                    Compositor.Start((frame, w, h, fps) =>
                    {
                        _masterWorker.FeedNdiVideo(frame, w, h, fps);
                    }, 59.94);
                }
                else
                {
                    Compositor.Stop();
                }
            }
        }

        public async Task<bool> ToggleSrtBridgeAsync(bool enable)
        {
            SrtBridgeEnabled = enable;
            if (enable)
            {
                var (w, h) = MasterVideoCodec.ParseResolution(1920, 1080);
                double fps = MasterVideoCodec.ParseFps(59.94);
                _masterWorker.StartSrtBridge(SrtBridgeHost, SrtBridgePort, SrtBridgeCodec, SrtBridgeBitrateKbps, w, h, fps, SrtBridgeMode);
            }
            else
            {
                _masterWorker.StopSrtBridge();
            }

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
                var (w, h) = MasterVideoCodec.ParseResolution(1920, 1080);
                double fps = MasterVideoCodec.ParseFps(59.94);
                _masterWorker.StartRecording(RecordingFolder, RecordingFormat, MasterVideoCodec, w, h, fps);
            }
            else
            {
                _masterWorker.StopRecording();
                RecordingStartTime = DateTime.MinValue;
            }

            OutputStateChanged?.Invoke("REC", enable);
            await Task.CompletedTask;
            return true;
        }

        public void UpdateRecordingStats()
        {
            // Worker calculates duration and file size directly from disk file
        }

        #endregion

        #region ISO Feeds (CAM 1..10) Output Control

        public async Task<bool> ToggleIsoSdiAsync(int camIndex, bool enable)
        {
            if (camIndex < 0 || camIndex >= MaxChannels) return false;
            var cfg = ReceiverOutputs[camIndex];
            cfg.SdiEnabled = enable;

            if (enable)
            {
                var codecCfg = cfg.ToCodecConfig();
                var (w, h) = codecCfg.ParseResolution(1920, 1080);
                double fps = codecCfg.ParseFps(59.94);
                _isoWorkers[camIndex].StartSdi(cfg.SdiPort, "1080p59.94", w, h, fps);
            }
            else
            {
                _isoWorkers[camIndex].StopSdi();
            }

            await Task.CompletedTask;
            return true;
        }

        public async Task<bool> ToggleIsoNdiAsync(int camIndex, bool enable)
        {
            if (camIndex < 0 || camIndex >= MaxChannels) return false;
            var cfg = ReceiverOutputs[camIndex];
            cfg.NdiEnabled = enable;

            if (enable)
            {
                _isoWorkers[camIndex].StartNdi(cfg.NdiName);
            }
            else
            {
                _isoWorkers[camIndex].StopNdi();
            }

            await Task.CompletedTask;
            return true;
        }

        public async Task<bool> ToggleIsoSrtBridgeAsync(int camIndex, bool enable)
        {
            if (camIndex < 0 || camIndex >= MaxChannels) return false;
            var cfg = ReceiverOutputs[camIndex];
            cfg.SrtBridgeEnabled = enable;

            if (enable)
            {
                var codecCfg = cfg.ToCodecConfig();
                var (w, h) = codecCfg.ParseResolution(1920, 1080);
                double fps = codecCfg.ParseFps(59.94);

                int bitrate = 8000;
                if (int.TryParse(cfg.SrtBridgeBitrate.Split(' ')[0], out int b)) bitrate = b;

                _isoWorkers[camIndex].StartSrtBridge(cfg.SrtBridgeHost, cfg.SrtBridgePort, cfg.SrtBridgeCodec, bitrate, w, h, fps, cfg.SrtBridgeMode);
            }
            else
            {
                _isoWorkers[camIndex].StopSrtBridge();
            }

            await Task.CompletedTask;
            return true;
        }

        public async Task<bool> ToggleIsoRecordingAsync(int camIndex, bool enable)
        {
            if (camIndex < 0 || camIndex >= MaxChannels) return false;
            var cfg = ReceiverOutputs[camIndex];
            cfg.RecEnabled = enable;

            if (enable)
            {
                var codecCfg = cfg.ToCodecConfig();
                var (w, h) = codecCfg.ParseResolution(1920, 1080);
                double fps = codecCfg.ParseFps(59.94);
                _isoWorkers[camIndex].StartRecording(RecordingFolder, cfg.RecFormat, codecCfg, w, h, fps);
            }
            else
            {
                _isoWorkers[camIndex].StopRecording();
            }

            await Task.CompletedTask;
            return true;
        }

        #endregion

        private bool _isDisposed;

        #region Feed Pipeline (Video BGRA & Audio PCM)

        public void FeedMasterVideo(byte[] bgraBytes, int width, int height, double fps = 59.94)
        {
            if (_isDisposed) return;

            // Zero-copy local IPC: update D3D11 Shared Texture Port 0 (OME_TEX_PGM_MASTER) when active
            if (_matrixPublisher?.IsInitialized == true)
            {
                _matrixPublisher.UpdateMasterVideo(bgraBytes, width, height, fps);
            }

            // If NDI is in Multiviewer mode, NDI gets its frames from the Compositor engine.
            // SDI, SRT Bridge, and File Recording still receive clean Master Program.
            bool sendToNdi = !NdiMultiviewerMode;
            _masterWorker.FeedVideoFrame(bgraBytes, width, height, fps, sendToNdi);
        }

        public void FeedMasterAudio(byte[] pcmBytes, int count)
        {
            if (_isDisposed) return;

            // IPC Audio Sync: update MMF ring buffer slot 0 when active
            if (_matrixPublisher?.IsInitialized == true)
            {
                _matrixPublisher.UpdateMasterAudio(pcmBytes, count);
            }

            _masterWorker.FeedAudioPcm(pcmBytes, count, 48000, 2);
        }

        public void FeedIsoVideo(int camIndex, byte[] bgraBytes, int width, int height, double fps = 59.94)
        {
            if (_isDisposed) return;
            if (camIndex >= 0 && camIndex < MaxChannels)
            {
                // Zero-copy local IPC: update D3D11 Shared Texture with [WebRTC] label
                if (_matrixPublisher?.IsInitialized == true)
                {
                    string label = ReceiverOutputs[camIndex]?.ChannelName ?? $"CAM {camIndex + 1}";
                    if (!label.StartsWith("[WebRTC]")) label = $"[WebRTC] {label}";
                    _matrixPublisher.UpdateIsoVideo(camIndex, label, bgraBytes, width, height, fps);
                }

                _isoWorkers[camIndex].FeedVideoFrame(bgraBytes, width, height, fps);
                Compositor.UpdateChannelFrame(camIndex, bgraBytes, width, height);
            }
        }

        public void FeedIsoAudio(int camIndex, byte[] pcmBytes, int count)
        {
            if (_isDisposed) return;
            if (camIndex >= 0 && camIndex < MaxChannels)
            {
                // IPC Audio Sync: update MMF ring buffer slot 1..10 when active
                if (_matrixPublisher?.IsInitialized == true)
                {
                    _matrixPublisher.UpdateIsoAudio(camIndex, pcmBytes, count);
                }

                _isoWorkers[camIndex].FeedAudioPcm(pcmBytes, count, 48000, 2);
            }
        }

        #endregion

        private void Log(string tag, string message)
        {
            LogEmitted?.Invoke(tag, message);
            Trace.WriteLine($"[BroadcastOutputManager]{tag} {message}");
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            _matrixPublisher?.Dispose();
            _matrixPublisher = null;
            Compositor.Dispose();
            _masterWorker.Dispose();
            for (int i = 0; i < MaxChannels; i++)
            {
                _isoWorkers[i].Dispose();
            }
        }
    }
}
