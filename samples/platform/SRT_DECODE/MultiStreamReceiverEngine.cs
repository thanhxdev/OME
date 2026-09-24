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
        public double MeasuredFps { get; set; }
        public int VideoWidth { get; set; } = 1920;
        public int VideoHeight { get; set; } = 1080;
        public double BufferHealthPercent { get; set; } = 100.0;
        public double BandwidthMbps { get; set; }
        public int PacketsRetransmitted { get; set; }
        public int PacketsReceived { get; set; }
        public int PacketsDropped { get; set; }
        public ulong TotalBytesReceived { get; set; }
        public TimeSpan Uptime { get; set; } = TimeSpan.Zero;
        public string StatusMessage { get; set; } = "Standby / Idle";

        // SMPTE 2022-7 & Group Socket States
        public bool GroupSocketEnabled { get; set; }
        public SMPTE2022_7Stats GroupStats { get; set; } = new();
        public int ConnectedMembersCount { get; set; }
        public string RedundancyStatus { get; set; } = "N/A";
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
        public event Action<int, byte[], int, int, long, long>? FrameReadyWithPts;
        public event Action<int, byte[], int, int>? FrameReady;
        public event Action<int, byte[], int>? AudioPcmReady;
        public event Action<int, byte[], int>? RawTsDataReady;
        public event Action<int>? ChannelStreamReset;

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
                Log("[SRT]", $"Khởi động thu luồng tự động cho {ch.Name} trên {ch.Config.ToSrtUri()}...");

                // Cancel and dispose any lingering previous CTS/Task
                _receiverCts[index]?.Cancel();
                var oldTask = _receiverTasks[index];
                _receiverTasks[index] = null;
                if (oldTask != null && !oldTask.IsCompleted)
                {
                    try { await Task.WhenAny(oldTask, Task.Delay(500)).ConfigureAwait(false); } catch { }
                }
                try { _receiverCts[index]?.Dispose(); } catch { }

                ch.IsRunning = true;
                ch.IsConnected = false;
                ch.StatusMessage = ch.Config.Mode == SRTMode.Listener
                    ? $"Đang chờ luồng (Port {ch.Config.Port})..."
                    : "Đang kết nối...";
                _syncEngine.SetChannelActive(index, false);
                ChannelUpdated?.Invoke(index, ch);

                var cts = new CancellationTokenSource();
                _receiverCts[index] = cts;
                var token = cts.Token;

                _receiverTasks[index] = Task.Run(() => RunChannelSupervisorAsync(index, token), token);
                await Task.CompletedTask;
                return true;
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

        private async Task RunChannelSupervisorAsync(int index, CancellationToken token)
        {
            if (index < 0 || index >= MaxChannels) return;
            var ch = _channels[index];
            byte[] buffer = new byte[65536];
            int reconnectAttempt = 0;

            while (!token.IsCancellationRequested && ch.IsRunning)
            {
                SRTStreamSession? session = null;
                ChannelVideoDecoder? decoder = null;
                ChannelAudioDecoder? audioDecoder = null;

                try
                {
                    reconnectAttempt++;
                    if (reconnectAttempt > 1)
                    {
                        Log("[SRT]", $"🔄 [{ch.Name}] Tự động bắt tay lại và kết nối (Lần thử #{reconnectAttempt}) trên {ch.Config.ToSrtUri()}...");
                        ch.StatusMessage = ch.Config.Mode == SRTMode.Listener
                            ? $"Đang chờ kết nối lại (Port {ch.Config.Port})..."
                            : $"Đang bắt tay lại (#{reconnectAttempt})...";
                        ch.IsConnected = false;
                        ChannelUpdated?.Invoke(index, ch);
                    }

                    // 1. Tạo session mới cho luồng
                    ch.Session?.Dispose();
                    session = new SRTStreamSession(ch.Config);
                    ch.Session = session;

                    session.LogEmitted += (tag, msg) => Log($"[{ch.Name}]{tag}", msg);
                    session.StatusChanged += (connected, msg) =>
                    {
                        ch.IsConnected = connected;
                        ch.StatusMessage = connected ? "Receiving LIVE" : msg;
                        _syncEngine.SetChannelActive(index, connected);
                        ChannelUpdated?.Invoke(index, ch);
                    };

                    session.StatisticsUpdated += stats =>
                    {
                        ch.CurrentRttMs = stats.RttMs;
                        ch.CurrentPacketLoss = stats.PacketLossPercent;
                        ch.CurrentBitrateKbps = stats.CurrentBitrateKbps;
                        ch.CurrentFps = (ch.MeasuredFps > 0.1) ? ch.MeasuredFps : (_decoders[index] != null ? _decoders[index]!.GetCurrentFps() : 0.0);
                        ch.TotalBytesReceived = stats.TotalBytesTransferred;
                        ch.Uptime = stats.Uptime;
                        ch.BandwidthMbps = stats.BandwidthMbps;
                        ch.PacketsRetransmitted = stats.PacketsRetransmitted;
                        ch.PacketsReceived = stats.PacketsReceived;
                        ch.PacketsDropped = stats.PacketsDropped;

                        if (!ch.IsConnected)
                        {
                            ch.BufferHealthPercent = 0.0;
                        }
                        else
                        {
                            double health = 100.0;
                            health -= stats.PacketLossPercent * 6.0;
                            if (stats.RttMs > 100.0) health -= Math.Min(30.0, (stats.RttMs - 100.0) * 0.2);
                            if (stats.PacketsDropped > 0) health -= Math.Min(25.0, stats.PacketsDropped * 2.0);
                            ch.BufferHealthPercent = Math.Clamp(Math.Round(health, 1), 0.0, 100.0);
                        }

                        if (ch.IsConnected)
                        {
                            DateTime frameWallClock = DateTime.UtcNow.AddMilliseconds(-Math.Max(50, stats.RttMs / 2.0));
                            long pts = (long)(DateTime.UtcNow.Ticks / 10000);
                            _syncEngine.IngestFrameMetadata(index, frameWallClock, pts, stats.RttMs);
                        }

                        ChannelUpdated?.Invoke(index, ch);
                    };

                    session.ErrorOccurred += err =>
                    {
                        ChannelError?.Invoke(index, err);
                        Log("[WARN]", $"[{ch.Name}] SRT Event: {err}");
                    };

                    session.GroupStatsUpdated += groupStats =>
                    {
                        ch.GroupStats = groupStats;
                        ch.ConnectedMembersCount = groupStats.ConnectedMembersCount;
                        if (ch.Config.GroupSocketEnabled)
                        {
                            ch.RedundancyStatus = groupStats.ConnectedMembersCount >= 2
                                ? $"SMPTE 2022-7 ARMED ({groupStats.ConnectedMembersCount} Paths)"
                                : groupStats.ConnectedMembersCount == 1
                                    ? $"DEGRADED (1 Path - Recovered: {groupStats.RecoveredFromRedundantPath})"
                                    : "NO LINK";
                        }
                        ChannelUpdated?.Invoke(index, ch);
                    };

                    session.MemberStatusChanged += memberStatus =>
                    {
                        Log("[GROUP_MEMBER]", $"[{ch.Name}] Member {memberStatus.Name} ({memberStatus.Endpoint}): {memberStatus.StatusText}, Loss={memberStatus.PacketLossPercent:F1}%");
                        ChannelUpdated?.Invoke(index, ch);
                    };

                    // 2. Khởi tạo kết nối / lắng nghe
                    bool ok = await session.ConnectReceiverAsync().ConfigureAwait(false);
                    if (!ok || token.IsCancellationRequested || !ch.IsRunning)
                    {
                        Log("[WARN]", $"[{ch.Name}] Chưa thể kết nối tới nguồn SRT trên {ch.Config.Port}. Tự động thử lại sau 1.5 giây...");
                        ch.StatusMessage = ch.Config.Mode == SRTMode.Listener
                            ? $"Lỗi mở cổng {ch.Config.Port}. Thử lại (#{reconnectAttempt})..."
                            : $"Không có kết nối. Thử lại (#{reconnectAttempt})...";
                        ch.IsConnected = false;
                        ChannelUpdated?.Invoke(index, ch);
                        await Task.Delay(1500, token).ConfigureAwait(false);
                        continue;
                    }

                    // 3. Định nghĩa factory khởi tạo Video & Audio Decoders khi có dữ liệu thực tế
                    _decoders[index]?.Dispose();
                    _decoders[index] = null;
                    _audioDecoders[index]?.Dispose();
                    _audioDecoders[index] = null;

                    ChannelVideoDecoder CreateVideoDecoder()
                    {
                        var dec = new ChannelVideoDecoder(index, 1920, 1080);
                        dec.LogEmitted += (tag, msg) => Log(tag, msg);

                        string streamId = session.Config?.StreamId ?? ch.Config.StreamId;
                        if (!string.IsNullOrEmpty(streamId) && MediaStreamInfo.TryParseFromStreamId(streamId, out var parsedInfo, out _))
                        {
                            dec.SetExpectedFormat(parsedInfo);
                            ch.VideoWidth = parsedInfo.Width;
                            ch.VideoHeight = parsedInfo.Height;
                            ch.MeasuredFps = parsedInfo.FrameRateDouble;
                            ch.CurrentFps = parsedInfo.FrameRateDouble;
                            Log("[SRT_META]", $"[{ch.Name}] Nhận diện MediaStreamInfo từ SRT StreamID: {parsedInfo.Width}x{parsedInfo.Height} @ {parsedInfo.FrameRateDouble:F2} FPS ({parsedInfo.FrameRateNum}/{parsedInfo.FrameRateDen}), Codec: {parsedInfo.VideoCodec}");
                        }

                        dec.FrameDecodedWithPts += (chIdx, frameBytes, w, h, pts, duration) =>
                        {
                            ch.VideoWidth = dec.DetectedWidth;
                            ch.VideoHeight = dec.DetectedHeight;
                            ch.MeasuredFps = dec.GetCurrentFps();
                            ch.CurrentFps = ch.MeasuredFps;
                            if (FrameReadyWithPts != null)
                            {
                                FrameReadyWithPts.Invoke(chIdx, frameBytes, w, h, pts, duration);
                            }
                            else
                            {
                                FrameReady?.Invoke(chIdx, frameBytes, w, h);
                            }
                        };
                        dec.Start();
                        return dec;
                    }

                    ChannelAudioDecoder CreateAudioDecoder()
                    {
                        var aDec = new ChannelAudioDecoder(index);
                        aDec.LogEmitted += (tag, msg) => Log(tag, msg);
                        aDec.PcmAudioDecoded += (chIdx, pcmBytes, len) =>
                        {
                            AudioPcmReady?.Invoke(chIdx, pcmBytes, len);
                        };
                        aDec.Start();
                        return aDec;
                    }

                    if (ch.Config.Mode == SRTMode.Listener)
                    {
                        ch.StatusMessage = $"Đang chờ luồng (Port {ch.Config.Port})...";
                    }
                    else
                    {
                        ch.StatusMessage = "Đang kết nối nhận luồng...";
                    }
                    ChannelUpdated?.Invoke(index, ch);

                    // 4. Vòng lặp nhận gói tin & Watchdog giám sát rớt mạng
                    DateTime lastDataReceivedTime = DateTime.MinValue;
                    bool hasReceivedData = false;

                    while (!token.IsCancellationRequested && ch.IsRunning && session.IsRunning)
                    {
                        int bytesRead = session.ReceiveData(buffer);
                        if (token.IsCancellationRequested || !ch.IsRunning) break;

                        if (bytesRead > 0)
                        {
                            do
                            {
                                lastDataReceivedTime = DateTime.UtcNow;
                                if (!hasReceivedData || !ch.IsConnected)
                                {
                                    hasReceivedData = true;
                                    reconnectAttempt = 0; // Reset số lần thử lại khi đã có luồng ổn định
                                    ch.IsConnected = true;
                                    ch.StatusMessage = "Receiving LIVE";
                                    _syncEngine.SetChannelActive(index, true);
                                    ChannelUpdated?.Invoke(index, ch);
                                    Log("[SRT]", $"✅ [{ch.Name}] Đã bắt tay và đang nhận luồng dữ liệu trực tiếp trên cổng {ch.Config.Port}!");
                                }

                                if (decoder == null || !decoder.IsRunning)
                                {
                                    try { decoder?.Dispose(); } catch { }
                                    decoder = CreateVideoDecoder();
                                    _decoders[index] = decoder;
                                }
                                if (audioDecoder == null || !audioDecoder.IsRunning)
                                {
                                    try { audioDecoder?.Dispose(); } catch { }
                                    audioDecoder = CreateAudioDecoder();
                                    _audioDecoders[index] = audioDecoder;
                                }

                                decoder.FeedData(buffer, bytesRead);
                                audioDecoder.FeedData(buffer, bytesRead);
                                RawTsDataReady?.Invoke(index, buffer, bytesRead);

                                bytesRead = session.ReceiveData(buffer);
                            } while (bytesRead > 0 && !token.IsCancellationRequested && ch.IsRunning);

                            // Đẩy ngay toàn bộ byte tích lũy trong bộ đệm C# vào pipe của FFmpeg Decoders
                            decoder?.Flush();
                            audioDecoder?.Flush();
                        }
                        else
                        {
                            if (ch.Config.Mode == SRTMode.Listener)
                            {
                                // Trong chế độ Listener: Duy trì socket lắng nghe vĩnh viễn, KHÔNG break phiên
                                if (hasReceivedData)
                                {
                                    bool activeConnected = ch.Config.GroupSocketEnabled
                                        ? (session.GroupStats.ConnectedMembersCount > 0 || session.NativeSource?.IsActiveConnected == true)
                                        : (session.NativeSource?.IsActiveConnected == true);
                                    if (!activeConnected && (DateTime.UtcNow - lastDataReceivedTime).TotalSeconds > 3.0)
                                    {
                                        Log("[WARN]", $"⚠️ [{ch.Name}] Khách đã ngắt luồng SRT. Đặt lại decoder và duy trì lắng nghe đón kết nối mới trên cổng {ch.Config.Port}...");
                                        hasReceivedData = false;
                                        ch.IsConnected = false;
                                        _syncEngine.SetChannelActive(index, false);
                                        ch.CurrentFps = 0;
                                        ch.MeasuredFps = 0;
                                        ch.CurrentBitrateKbps = 0;
                                        ch.CurrentRttMs = 0;
                                        ch.BufferHealthPercent = 0.0;
                                        ch.StatusMessage = $"Đang chờ kết nối lại (Port {ch.Config.Port})...";
                                        ChannelUpdated?.Invoke(index, ch);

                                        try { decoder?.Stop(); decoder?.Dispose(); } catch { }
                                        decoder = null;
                                        _decoders[index] = null;

                                        try { audioDecoder?.Stop(); audioDecoder?.Dispose(); } catch { }
                                        audioDecoder = null;
                                        _audioDecoders[index] = null;

                                        ChannelStreamReset?.Invoke(index);
                                    }
                                }

                                // Socket tạm thời chưa có gói tin: Sleep 1ms để nhường CPU
                                await Task.Delay(1, token).ConfigureAwait(false);
                            }
                            else
                            {
                                // Trong chế độ Caller: Nếu mất kết nối hoặc quá thời gian chờ, thoát để tạo phiên bắt tay lại
                                if (hasReceivedData && (DateTime.UtcNow - lastDataReceivedTime).TotalSeconds > 2.5)
                                {
                                    bool activeConnected = ch.Config.GroupSocketEnabled
                                        ? (session.GroupStats.ConnectedMembersCount > 0 || session.NativeSource?.IsActiveConnected == true)
                                        : (session.NativeSource?.IsActiveConnected == true);
                                    if (!activeConnected || (DateTime.UtcNow - lastDataReceivedTime).TotalSeconds > 3.5)
                                    {
                                        Log("[WARN]", $"⚠️ [{ch.Name}] Mất tín hiệu luồng SRT (Timeout). Đang kết nối lại...");
                                        break;
                                    }
                                }

                                if (!ch.Config.GroupSocketEnabled && session.NativeSource != null && !session.NativeSource.IsActiveConnected)
                                {
                                    Log("[WARN]", $"⚠️ [{ch.Name}] Mất kết nối tới SRT Host từ xa. Đang kết nối lại...");
                                    break;
                                }

                                await Task.Delay(1, token).ConfigureAwait(false);
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log("[WARN]", $"[{ch.Name}] Sự cố trong luồng nhận: {ex.Message}");
                }
                finally
                {
                    // Dọn dẹp session và decoder trước khi thử lại hoặc dừng hẳn
                    try
                    {
                        ch.IsConnected = false;
                        _syncEngine.SetChannelActive(index, false);
                        ch.CurrentFps = 0;
                        ch.MeasuredFps = 0;
                        ch.CurrentBitrateKbps = 0;
                        ch.CurrentRttMs = 0;
                        ch.BufferHealthPercent = 0.0;

                        decoder?.Stop();
                        decoder?.Dispose();
                        _decoders[index] = null;

                        audioDecoder?.Stop();
                        audioDecoder?.Dispose();
                        _audioDecoders[index] = null;

                        ChannelStreamReset?.Invoke(index);

                        if (session != null)
                        {
                            try { await session.StopAsync().ConfigureAwait(false); } catch { }
                            try { session.Dispose(); } catch { }
                            ch.Session = null;
                        }
                    }
                    catch { }
                }

                if (ch.IsRunning && !token.IsCancellationRequested)
                {
                    ch.StatusMessage = ch.Config.Mode == SRTMode.Listener
                        ? $"Đang chờ kết nối lại (Port {ch.Config.Port})..."
                        : $"Mất kết nối - Đang bắt tay lại (#{reconnectAttempt + 1})...";
                    ChannelUpdated?.Invoke(index, ch);
                    await Task.Delay(1500, token).ConfigureAwait(false);
                }
            }

            ch.IsConnected = false;
            ch.StatusMessage = "Standby / Stopped";
            ChannelUpdated?.Invoke(index, ch);
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

                // 1. Cancel background loop
                _receiverCts[index]?.Cancel();

                // 2. Stop SRT session immediately to unblock native srt_recv
                if (ch.Session != null)
                {
                    try { await ch.Session.StopAsync().ConfigureAwait(false); } catch { }
                }

                // 3. Await receiver task completion cleanly
                var rxTask = _receiverTasks[index];
                _receiverTasks[index] = null;
                if (rxTask != null)
                {
                    try
                    {
                        await Task.WhenAny(rxTask, Task.Delay(1500)).ConfigureAwait(false);
                    }
                    catch { }
                }

                // 4. Stop and dispose decoders
                _decoders[index]?.Stop();
                _decoders[index]?.Dispose();
                _decoders[index] = null;

                _audioDecoders[index]?.Stop();
                _audioDecoders[index]?.Dispose();
                _audioDecoders[index] = null;

                ChannelStreamReset?.Invoke(index);

                // 5. Dispose session
                if (ch.Session != null)
                {
                    try { ch.Session.Dispose(); } catch { }
                    ch.Session = null;
                }

                // 6. Dispose CTS
                try { _receiverCts[index]?.Dispose(); } catch { }
                _receiverCts[index] = null;

                ch.CurrentRttMs = 0;
                ch.CurrentPacketLoss = 0;
                ch.CurrentBitrateKbps = 0;
                ch.CurrentFps = 0;
                ch.MeasuredFps = 0;
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

        public async Task<bool> AddChannelMemberSocketAsync(int channelIndex, SRTGroupMemberConfig member)
        {
            if (channelIndex < 0 || channelIndex >= MaxChannels) return false;
            var ch = _channels[channelIndex];
            if (ch.Session != null && ch.IsRunning)
            {
                return await ch.Session.AddMemberSocketAsync(member).ConfigureAwait(false);
            }
            ch.Config.GroupMembers.Add(member);
            return true;
        }

        public async Task<bool> RemoveChannelMemberSocketAsync(int channelIndex, string memberId)
        {
            if (channelIndex < 0 || channelIndex >= MaxChannels) return false;
            var ch = _channels[channelIndex];
            if (ch.Session != null && ch.IsRunning)
            {
                return await ch.Session.RemoveMemberSocketAsync(memberId).ConfigureAwait(false);
            }
            ch.Config.GroupMembers.RemoveAll(m => m.Id == memberId);
            return true;
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
