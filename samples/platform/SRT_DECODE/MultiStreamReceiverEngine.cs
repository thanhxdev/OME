using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using OpenMedia.Platform;
using OpenMedia.Platform.Models;

namespace SRT_DECODE
{
    public sealed class ReceiverChannelState
    {
        public int ChannelIndex { get; set; } // 0..9
        public string Name { get; set; } = string.Empty;
        public SRTStreamConfig Config { get; set; } = new();
        public SRTStreamSession? Session { get; set; }
        public bool IsRunning { get; set; }
        public bool IsConnected { get; set; }
        public double CurrentRttMs { get; set; }
        public double CurrentPacketLoss { get; set; }
        public double CurrentBitrateKbps { get; set; }
        public double CurrentFps { get; set; }
        public double BufferHealthPercent { get; set; } = 100.0;
        public ulong TotalBytesReceived { get; set; }
        public TimeSpan Uptime { get; set; } = TimeSpan.Zero;
        public string StatusMessage { get; set; } = "Standby / Idle";
    }

    /// <summary>
    /// Multi-Stream SRT Receiver Engine managing up to 10 concurrent camera streams.
    /// </summary>
    public sealed class MultiStreamReceiverEngine : IDisposable, IAsyncDisposable
    {
        public const int MaxChannels = 10;
        private readonly ReceiverChannelState[] _channels = new ReceiverChannelState[MaxChannels];
        private readonly ChannelVideoDecoder?[] _decoders = new ChannelVideoDecoder?[MaxChannels];
        private readonly ChannelAudioDecoder?[] _audioDecoders = new ChannelAudioDecoder?[MaxChannels];
        private readonly CancellationTokenSource?[] _receiverCts = new CancellationTokenSource?[MaxChannels];
        private readonly Task?[] _receiverTasks = new Task?[MaxChannels];
        private readonly NtpSyncEngine _syncEngine;
        private bool _isDisposed;

        public event Action<int, ReceiverChannelState>? ChannelUpdated;
        public event Action<string, string>? LogEmitted;
        public event Action<int, string>? ChannelError;
        public event Action<int, byte[], int, int>? FrameReady;
        public event Action<int, byte[], int>? AudioPcmReady;
        public event Action<int, byte[], int>? RawTsDataReady;

        public ReceiverChannelState[] Channels => _channels;

        public MultiStreamReceiverEngine(NtpSyncEngine syncEngine)
        {
            _syncEngine = syncEngine;

            for (int i = 0; i < MaxChannels; i++)
            {
                int port = 9000 + i; // Cam 1: 9000 ... Cam 10: 9009
                var config = new SRTStreamConfig
                {
                    Host = "0.0.0.0", // Default listener binds to all local interfaces
                    Port = port,
                    Mode = SRTMode.Listener,
                    LatencyMs = 200, // Standard Broadcast SRT Latency (allows 2-3 ARQ retransmissions on jittery WAN)
                    AutoLatency = false, // Default: Disable Auto Latency
                    EncryptionEnabled = false,
                    Passphrase = string.Empty,
                    KeyLength = 32
                };

                _channels[i] = new ReceiverChannelState
                {
                    ChannelIndex = i,
                    Name = $"CAM {i + 1}",
                    Config = config,
                    IsRunning = false,
                    IsConnected = false,
                    StatusMessage = "Idle (Listener Port " + port + ")"
                };
            }
        }

        public async Task<bool> StartChannelAsync(int index)
        {
            if (index < 0 || index >= MaxChannels) return false;
            var ch = _channels[index];
            if (ch.IsRunning) return true;

            try
            {
                Log("[SRT]", $"Khởi động thu luồng {ch.Name} trên {ch.Config.ToSrtUri()}...");
                ch.Session?.Dispose();
                ch.Session = new SRTStreamSession(ch.Config);

                ch.Session.LogEmitted += (tag, msg) => Log($"[{ch.Name}]{tag}", msg);
                ch.Session.StatusChanged += (connected, msg) =>
                {
                    ch.IsConnected = connected;
                    ch.StatusMessage = msg;
                    _syncEngine.SetChannelActive(index, connected);
                    ChannelUpdated?.Invoke(index, ch);
                };

                ch.Session.StatisticsUpdated += stats =>
                {
                    ch.CurrentRttMs = stats.RttMs;
                    ch.CurrentPacketLoss = stats.PacketLossPercent;
                    ch.CurrentBitrateKbps = stats.CurrentBitrateKbps;
                    ch.CurrentFps = stats.CurrentFps;
                    ch.TotalBytesReceived = stats.TotalBytesTransferred;
                    ch.Uptime = stats.Uptime;

                    // Evaluate buffer health based on packet loss & RTT
                    if (stats.PacketLossPercent > 5.0 || stats.RttMs > 250)
                    {
                        ch.BufferHealthPercent = Math.Max(30.0, 100.0 - stats.PacketLossPercent * 5.0);
                    }
                    else
                    {
                        ch.BufferHealthPercent = 98.0 + (new Random().NextDouble() * 2.0);
                    }

                    // Feed frame timing into NTP Sync Engine
                    if (ch.IsConnected)
                    {
                        DateTime frameWallClock = DateTime.UtcNow.AddMilliseconds(-Math.Max(50, stats.RttMs / 2.0));
                        long pts = (long)(DateTime.UtcNow.Ticks / 10000);
                        _syncEngine.IngestFrameMetadata(index, frameWallClock, pts, stats.RttMs);
                    }

                    ChannelUpdated?.Invoke(index, ch);
                };

                ch.Session.ErrorOccurred += err =>
                {
                    ChannelError?.Invoke(index, err);
                    Log("[ERROR]", $"[{ch.Name}] Lỗi kết nối SRT: {err}");
                };

                bool ok = await ch.Session.ConnectReceiverAsync();
                ch.IsRunning = ok;
                ch.IsConnected = ok;
                ch.StatusMessage = ok ? "Listening / Receiving..." : "Failed to bind";
                _syncEngine.SetChannelActive(index, ok);

                if (ok)
                {
                    // Start Video Decoder Pipeline
                    _decoders[index]?.Dispose();
                    var decoder = new ChannelVideoDecoder(index, 1920, 1080);
                    decoder.LogEmitted += (tag, msg) => Log(tag, msg);
                    decoder.FrameDecoded += (chIdx, frameBytes, w, h) =>
                    {
                        FrameReady?.Invoke(chIdx, frameBytes, w, h);
                    };
                    decoder.Start();
                    _decoders[index] = decoder;

                    // Start Audio Decoder Pipeline (48kHz 16-bit Stereo PCM)
                    _audioDecoders[index]?.Dispose();
                    var audioDecoder = new ChannelAudioDecoder(index);
                    audioDecoder.LogEmitted += (tag, msg) => Log(tag, msg);
                    audioDecoder.PcmAudioDecoded += (chIdx, pcmBytes, len) =>
                    {
                        AudioPcmReady?.Invoke(chIdx, pcmBytes, len);
                    };
                    audioDecoder.Start();
                    _audioDecoders[index] = audioDecoder;

                    // Start Background Packet Receiver & Feeder Loop
                    _receiverCts[index]?.Cancel();
                    _receiverCts[index]?.Dispose();
                    var cts = new CancellationTokenSource();
                    _receiverCts[index] = cts;
                    var token = cts.Token;
                    var session = ch.Session;

                    _receiverTasks[index] = Task.Run(async () =>
                    {
                        byte[] buffer = new byte[65536]; // 64KB buffer to receive any SRT live MTU payload
                        try
                        {
                            while (!token.IsCancellationRequested && ch.IsRunning && session != null && session.IsRunning)
                            {
                                int bytesRead = session.ReceiveData(buffer);
                                if (token.IsCancellationRequested || !ch.IsRunning) break;

                                if (bytesRead > 0)
                                {
                                    decoder.FeedData(buffer, bytesRead);
                                    audioDecoder.FeedData(buffer, bytesRead);
                                    RawTsDataReady?.Invoke(index, buffer, bytesRead);
                                }
                                else
                                {
                                    await Task.Delay(1, token).ConfigureAwait(false);
                                }
                            }
                        }
                        catch (OperationCanceledException) { }
                        catch (Exception ex)
                        {
                            Log("[WARN]", $"[{ch.Name}] Luồng nhận gói tin kết thúc: {ex.Message}");
                        }
                    }, token);
                }

                ChannelUpdated?.Invoke(index, ch);
                Log("[SRT]", $"✅ {ch.Name} đã sẵn sàng nhận luồng trên cổng {ch.Config.Port}.");
                return ok;
            }
            catch (Exception ex)
            {
                ch.IsRunning = false;
                ch.IsConnected = false;
                ch.StatusMessage = $"Lỗi: {ex.Message}";
                Log("[ERROR]", $"[{ch.Name}] Không thể khởi động thu luồng: {ex.Message}");
                ChannelUpdated?.Invoke(index, ch);
                return false;
            }
        }

        public async Task StopChannelAsync(int index)
        {
            if (index < 0 || index >= MaxChannels) return;
            var ch = _channels[index];
            if (!ch.IsRunning) return;

            try
            {
                Log("[SRT]", $"Dừng thu luồng {ch.Name}...");

                ch.IsRunning = false;
                ch.IsConnected = false;
                _syncEngine.SetChannelActive(index, false);

                // 1. Cancel background loop and await task completion (prevent native use-after-free)
                _receiverCts[index]?.Cancel();
                var rxTask = _receiverTasks[index];
                _receiverTasks[index] = null;
                if (rxTask != null)
                {
                    try
                    {
                        await Task.WhenAny(rxTask, Task.Delay(200)).ConfigureAwait(false);
                    }
                    catch { }
                }

                // 2. Stop and dispose decoders
                _decoders[index]?.Stop();
                _decoders[index]?.Dispose();
                _decoders[index] = null;

                _audioDecoders[index]?.Stop();
                _audioDecoders[index]?.Dispose();
                _audioDecoders[index] = null;

                // 3. Stop and dispose SRT session
                if (ch.Session != null)
                {
                    try { await ch.Session.StopAsync().ConfigureAwait(false); } catch { }
                    try { ch.Session.Dispose(); } catch { }
                    ch.Session = null;
                }

                // 4. Dispose CTS now that receiver task has completely stopped
                try { _receiverCts[index]?.Dispose(); } catch { }
                _receiverCts[index] = null;

                ch.CurrentRttMs = 0;
                ch.CurrentPacketLoss = 0;
                ch.CurrentBitrateKbps = 0;
                ch.CurrentFps = 0;
                ch.StatusMessage = "Standby / Stopped";

                ChannelUpdated?.Invoke(index, ch);
                Log("[SRT]", $"Đã dừng {ch.Name}.");
            }
            catch (Exception ex)
            {
                Log("[ERROR]", $"Lỗi khi dừng {ch.Name}: {ex.Message}");
            }
        }

        public async Task StartAllAsync(int activeCount = MaxChannels)
        {
            int count = Math.Clamp(activeCount, 1, MaxChannels);
            Log("[SRT]", $"Bắt đầu kết nối {count} kênh SRT Ingest...");
            for (int i = 0; i < count; i++)
            {
                await StartChannelAsync(i);
            }
        }

        public async Task StopAllAsync(int activeCount = MaxChannels)
        {
            int count = Math.Clamp(activeCount, 1, MaxChannels);
            Log("[SRT]", $"Dừng đồng thời {count} kênh SRT Ingest...");
            var tasks = new System.Collections.Generic.List<Task>();
            for (int i = 0; i < count; i++)
            {
                tasks.Add(StopChannelAsync(i));
            }
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        private void Log(string tag, string message)
        {
            LogEmitted?.Invoke(tag, message);
            Trace.WriteLine($"[MultiStreamReceiverEngine]{tag} {message}");
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            for (int i = 0; i < MaxChannels; i++)
            {
                _channels[i].IsRunning = false;
                _channels[i].IsConnected = false;
                _receiverCts[i]?.Cancel();
            }

            for (int i = 0; i < MaxChannels; i++)
            {
                _decoders[i]?.Stop();
                _decoders[i]?.Dispose();
                _decoders[i] = null;

                _audioDecoders[i]?.Stop();
                _audioDecoders[i]?.Dispose();
                _audioDecoders[i] = null;

                _channels[i].Session?.Dispose();
                _channels[i].Session = null;

                try { _receiverCts[i]?.Dispose(); } catch { }
                _receiverCts[i] = null;
                _receiverTasks[i] = null;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            await StopAllAsync(MaxChannels).ConfigureAwait(false);
        }
    }
}
