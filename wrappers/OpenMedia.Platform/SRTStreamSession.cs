using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using OpenMedia.Platform.Internal;
using OpenMedia.Platform.Models;
using OpenMedia.SDK;

namespace OpenMedia.Platform
{
    /// <summary>
    /// Quản lý phiên truyền dẫn và thu nhận SRT chuẩn phát thanh truyền hình.
    /// Hỗ trợ chuẩn SMPTE ST 2022-7 (Hitless Redundancy / Seamless Protection Switching)
    /// và cơ chế Group Socket tự động gắn kết nối các member socket mới vào phiên làm việc
    /// mà không làm gián đoạn luồng truyền hiện tại.
    /// </summary>
    public class SRTStreamSession : IDisposable, IAsyncDisposable, IMediaIngestSession
    {
        private readonly SRTStreamConfig _config;
        private readonly SRTStatistics _statistics = new();
        private SRTSource? _nativeSource;
        private SRTOutput? _nativeOutput;
        private StreamOutput? _platformOutput;
        private Timer? _statsTimer;
        private bool _isRunning = false;
        private bool _disposed = false;
        private DateTime _connectTime = DateTime.MinValue;
        private ulong _lastTotalBytes = 0;
        private DateTime _lastStatsSampleTime = DateTime.UtcNow;
        private bool _isUsingBackupInterface = false;

        // ─── SMPTE ST 2022-7 & Group Socket Management ──────────────────
        private readonly object _membersLock = new();
        private readonly List<SRTMemberSender> _memberSenders = new();
        private readonly List<SRTMemberReceiver> _memberReceivers = new();
        private readonly HitlessMergeDeduplicator _hitlessDeduplicator = new();
        private readonly SMPTE2022_7Stats _groupStats = new();
        private CancellationTokenSource? _groupSupervisorCts;

        /// <summary>Cấu hình luồng SRT hiện tại.</summary>
        public SRTStreamConfig Config => _config;

        /// <summary>Cho biết luồng có đang chuyển tiếp qua card mạng dự phòng 4G/5G hay không.</summary>
        public bool IsUsingBackupInterface => _isUsingBackupInterface;

        /// <summary>Thống kê telemetry thời gian thực.</summary>
        public SRTStatistics Statistics => _statistics;

        /// <summary>Thống kê chuẩn SMPTE 2022-7 và Group Socket.</summary>
        public SMPTE2022_7Stats GroupStats
        {
            get
            {
                var stats = _hitlessDeduplicator.Stats;
                lock (_membersLock)
                {
                    stats.IsGroupActive = _config.GroupSocketEnabled;
                    if (_memberSenders.Count > 0)
                    {
                        stats.TotalMembersCount = _memberSenders.Count;
                        stats.ConnectedMembersCount = _memberSenders.Count(m => m.Status.IsConnected);
                        if (_memberSenders.Count > 0) stats.PathAPackets = _memberSenders[0].Status.PacketsSent;
                        if (_memberSenders.Count > 1) stats.PathBPackets = _memberSenders[1].Status.PacketsSent;
                    }
                    else if (_memberReceivers.Count > 0)
                    {
                        stats.TotalMembersCount = _memberReceivers.Count;
                        stats.ConnectedMembersCount = _memberReceivers.Count(m => m.Status.IsConnected);
                    }
                }
                return stats;
            }
        }

        /// <summary>Danh sách trạng thái của từng member socket trong Group.</summary>
        public IReadOnlyList<SRTGroupMemberStatus> MemberStatuses
        {
            get
            {
                lock (_membersLock)
                {
                    if (_memberSenders.Count > 0)
                    {
                        return _memberSenders.Select(m => m.Status.Clone()).ToList();
                    }
                    if (_memberReceivers.Count > 0)
                    {
                        return _memberReceivers.Select(m => m.Status.Clone()).ToList();
                    }
                    return Array.Empty<SRTGroupMemberStatus>();
                }
            }
        }

        /// <summary>Unified broadcast telemetry implementing <see cref="IMediaIngestSession"/>.</summary>
        public StreamStatistics CurrentStats => StreamStatistics.FromSrtStatistics(_statistics);

        /// <summary>Indicates whether the stream session is currently active.</summary>
        public bool IsRunning => _isRunning;

        /// <summary>Gets the standardized SRT connection URI based on current configuration.</summary>
        public string SrtUri => _config.ToSrtUri();

        /// <summary>Native SRTSource instance if in receiving mode.</summary>
        public SRTSource? NativeSource => _nativeSource;

        /// <summary>Native SRTOutput instance if in transmission mode.</summary>
        public SRTOutput? NativeOutput => _nativeOutput;

        // ─── Events ─────────────────────────────────────────────────────
        /// <summary>Fired when the connection or transmission status changes.</summary>
        public event Action<bool, string>? StatusChanged;

        /// <summary>Fired periodically when updated telemetry metrics are calculated.</summary>
        public event Action<SRTStatistics>? StatisticsUpdated;

        /// <summary>Fired when SMPTE 2022-7 group telemetry metrics update.</summary>
        public event Action<SMPTE2022_7Stats>? GroupStatsUpdated;

        /// <summary>Fired when an individual member socket's status changes.</summary>
        public event Action<SRTGroupMemberStatus>? MemberStatusChanged;

        /// <summary>Fired when operational log messages are produced.</summary>
        public event Action<string, string>? LogEmitted;

        /// <summary>Fired when an unrecoverable or stream error occurs.</summary>
        public event Action<string>? ErrorOccurred;

        /// <summary>
        /// Initializes a new SRT stream session with the specified configuration.
        /// </summary>
        public SRTStreamSession(SRTStreamConfig? config = null)
        {
            _config = config ?? SRTStreamConfig.CreateDefault();
            _hitlessDeduplicator.Configure(_config.HitlessDifferentialDelayMs, 8192);
        }

        /// <summary>
        /// Initializes a new SRT stream session with basic connection parameters.
        /// </summary>
        public SRTStreamSession(string host, int port, SRTMode mode = SRTMode.Caller)
        {
            _config = new SRTStreamConfig
            {
                Host = host,
                Port = port,
                Mode = mode
            };
            _hitlessDeduplicator.Configure(_config.HitlessDifferentialDelayMs, 8192);
        }

        /// <summary>
        /// Starts transmitting video/audio packages over SRT (Transmission / Egress mode).
        /// Hỗ trợ cả chế độ Single Socket lẫn Group Socket chuẩn SMPTE 2022-7.
        /// </summary>
        public virtual async Task<bool> StartTransmissionAsync()
        {
            ThrowIfDisposed();
            if (_isRunning) return true;

            try
            {
                Log("[SRT]", $"Bắt đầu khởi động luồng phát SRT: {_config.ToSrtUri()}");
                Log("[SRT]", $"Cấu hình Codec: {_config.VideoCodec} @ {_config.BitrateKbps:N0} kbps, FPS: {_config.FrameRate}, Encoder: {_config.HardwareEncoder}");

                if (_config.UltraLowLatency)
                {
                    Log("[SRT]", "⚡ Chế độ Ultra Low-Latency KÍCH HOẠT: B-Frames=0, GOP=1.0s, Preset Zerolatency, CBR.");
                }

                if (_config.EncryptionEnabled)
                {
                    Log("[SRT]", $"🔒 Bảo mật AES-{_config.KeyLength * 8} KÍCH HOẠT cho luồng truyền dẫn.");
                }

                if (_config.NtpSyncEnabled)
                {
                    Log("[SRT]", $"🕒 Multi-Cam NTP Synchronization: Đã kích hoạt đồng bộ qua {_config.NtpServer}.");
                }

                if (_config.GroupSocketEnabled)
                {
                    // ─── CHẾ ĐỘ GROUP SOCKET (SMPTE 2022-7 HITLESS REDUNDANCY) ────────
                    Log("[GROUP_SMPTE2022_7]", "🛡️ KÍCH HOẠT CHUẨN SMPTE 2022-7 & GROUP SOCKET: Khởi động phát sóng đa đường truyền song song...");
                    EnsureDefaultGroupMembersConfigured();

                    lock (_membersLock)
                    {
                        _memberSenders.Clear();
                        foreach (var m in _config.GroupMembers.Where(m => m.IsEnabled))
                        {
                            _memberSenders.Add(new SRTMemberSender(m));
                        }
                    }

                    // Bắt đầu kết nối các member socket ban đầu
                    int connectedCount = 0;
                    var connectTasks = new List<Task<bool>>();

                    lock (_membersLock)
                    {
                        foreach (var sender in _memberSenders)
                        {
                            connectTasks.Add(ConnectMemberSenderAsync(sender));
                        }
                    }

                    bool[] results = await Task.WhenAll(connectTasks).ConfigureAwait(false);
                    connectedCount = results.Count(r => r);

                    if (connectedCount > 0)
                    {
                        lock (_membersLock)
                        {
                            var primary = _memberSenders.FirstOrDefault(s => s.Status.IsConnected);
                            if (primary != null)
                            {
                                _nativeOutput = primary.Output;
                            }
                        }

                        _isRunning = true;
                        _connectTime = DateTime.UtcNow;
                        _lastStatsSampleTime = DateTime.UtcNow;
                        _statistics.IsConnected = true;
                        _statistics.CurrentFps = _config.FrameRate;

                        StartStatisticsPolling();
                        StartGroupSupervisor();

                        StatusChanged?.Invoke(true, $"SRT Transmitting LIVE (Group: {connectedCount}/{_memberSenders.Count} Active)");
                        Log("[GROUP_SMPTE2022_7]", $"✅ [SUCCESS] Khởi động Group Socket thành công với {connectedCount} Member Socket sẵn sàng. SMPTE 2022-7 ARMED.");
                        return true;
                    }
                    else
                    {
                        Log("[WARN]", "⚠️ Chưa kết nối được Member Socket nào trong Group. Kiểm tra địa chỉ đích và cổng mạng.");
                        _isRunning = false;
                        _statistics.IsConnected = false;
                        StatusChanged?.Invoke(false, "SRT Group Connect Failed");
                        return false;
                    }
                }
                else
                {
                    // ─── CHẾ ĐỘ SINGLE SOCKET (LEGACY REFERENCE) ──────────────────────
                    bool nativeOpened = false;
                    try
                    {
                        _nativeOutput?.Dispose();
                        _nativeOutput = new SRTOutput();
                        nativeOpened = _nativeOutput.Open(_config.ToSrtUri());
                        if (nativeOpened)
                        {
                            Log("[SRT]", $"✅ Native SRTOutput đã mở thành công trên: {_config.ToSrtUri()}");
                        }
                        else
                        {
                            Log("[WARN]", $"⚠️ Native SRTOutput chưa kết nối/lắng nghe được trên URI: {_config.ToSrtUri()}");
                            try { _nativeOutput.Dispose(); } catch { }
                            _nativeOutput = null;
                            _isRunning = false;
                            _statistics.IsConnected = false;
                            StatusChanged?.Invoke(false, "SRT Output Open Failed");
                            return false;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log("[ERROR]", $"Direct native SRTOutput initialization note: {ex.Message}");
                        _isRunning = false;
                        _statistics.IsConnected = false;
                        StatusChanged?.Invoke(false, $"Error: {ex.Message}");
                        return false;
                    }

                    _isRunning = true;
                    _connectTime = DateTime.UtcNow;
                    _lastStatsSampleTime = DateTime.UtcNow;
                    _statistics.IsConnected = nativeOpened || _nativeOutput?.IsConnected == true;
                    _statistics.CurrentFps = _config.FrameRate;

                    StartStatisticsPolling();

                    StatusChanged?.Invoke(true, "SRT Transmitting LIVE");
                    Log("[SRT]", "✅ [SUCCESS] Khởi động phiên truyền dữ liệu SRT hoàn tất.");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _isRunning = false;
                _statistics.IsConnected = false;
                ErrorOccurred?.Invoke($"Lỗi khởi động phát SRT: {ex.Message}");
                Log("[ERROR]", $"Lỗi phát sóng SRT: {ex.Message}");
                StatusChanged?.Invoke(false, $"Error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Connects to a remote SRT stream endpoint to receive incoming video/audio (Ingest / Receiver mode).
        /// Hỗ trợ cả chế độ Single Socket lẫn Group Socket chuẩn SMPTE 2022-7 Hitless Merge.
        /// </summary>
        public virtual async Task<bool> ConnectReceiverAsync()
        {
            ThrowIfDisposed();
            if (_isRunning) return true;

            try
            {
                Log("[SRT]", $"Đang kết nối nhận luồng SRT từ: {_config.ToSrtUri()}...");

                if (_config.GroupSocketEnabled)
                {
                    // ─── CHẾ ĐỘ GROUP SOCKET INGEST (SMPTE 2022-7 HITLESS DEDUPLICATION) ───
                    Log("[GROUP_SMPTE2022_7]", "🛡️ KÍCH HOẠT CHUẨN SMPTE 2022-7 HITLESS INGEST: Khởi động nhận đa đường truyền và bộ đệm khử trùng lặp...");
                    _hitlessDeduplicator.Configure(_config.HitlessDifferentialDelayMs, 8192);
                    _hitlessDeduplicator.Reset();
                    EnsureDefaultGroupMembersConfigured();

                    lock (_membersLock)
                    {
                        _memberReceivers.Clear();
                        foreach (var m in _config.GroupMembers.Where(m => m.IsEnabled))
                        {
                            _memberReceivers.Add(new SRTMemberReceiver(m));
                        }
                    }

                    _isRunning = true;

                    int connectedCount = 0;
                    var connectTasks = new List<Task<bool>>();

                    lock (_membersLock)
                    {
                        for (int i = 0; i < _memberReceivers.Count; i++)
                        {
                            var r = _memberReceivers[i];
                            int pathIdx = i;
                            connectTasks.Add(ConnectMemberReceiverAsync(r, pathIdx));
                        }
                    }

                    bool[] results = await Task.WhenAll(connectTasks).ConfigureAwait(false);
                    connectedCount = results.Count(r => r);

                    if (connectedCount > 0)
                    {
                        lock (_membersLock)
                        {
                            var primary = _memberReceivers.FirstOrDefault(r => r.Source != null && r.Source.IsConnected);
                            if (primary != null)
                            {
                                _nativeSource = primary.Source;
                            }
                        }

                        _isRunning = true;
                        _connectTime = DateTime.UtcNow;
                        _lastStatsSampleTime = DateTime.UtcNow;
                        _statistics.IsConnected = true;
                        _statistics.CurrentFps = _config.FrameRate;

                        StartStatisticsPolling();
                        StartGroupSupervisor();

                        StatusChanged?.Invoke(true, $"SRT Receiver CONNECTED (Group: {connectedCount}/{_memberReceivers.Count} Active)");
                        Log("[GROUP_SMPTE2022_7]", $"✅ [SUCCESS] Kết nối Ingest Group Socket thành công với {connectedCount} Member Socket. SMPTE 2022-7 Merging ACTIVE.");
                        return true;
                    }
                    else
                    {
                        Log("[WARN]", "⚠️ Chưa kết nối được Member Ingest Socket nào trong Group.");
                        _isRunning = false;
                        _statistics.IsConnected = false;
                        StatusChanged?.Invoke(false, "SRT Group Receiver Connect Failed");
                        return false;
                    }
                }
                else
                {
                    // ─── CHẾ ĐỘ SINGLE SOCKET RECEIVER (LEGACY REFERENCE) ─────────────
                    bool nativeConnected = false;
                    try
                    {
                        _nativeSource?.Dispose();
                        _nativeSource = new SRTSource();
                        nativeConnected = _nativeSource.Connect(_config.ToSrtUri());
                        if (nativeConnected)
                        {
                            Log("[SRT]", $"✅ Native SRTSource kết nối thành công: {_config.ToSrtUri()}");
                        }
                        else
                        {
                            Log("[WARN]", $"⚠️ Native SRTSource chưa kết nối được tới: {_config.ToSrtUri()}");
                            try { _nativeSource.Dispose(); } catch { }
                            _nativeSource = null;
                            _isRunning = false;
                            _statistics.IsConnected = false;
                            StatusChanged?.Invoke(false, "SRT Receiver Connect Failed");
                            return false;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log("[ERROR]", $"Direct native SRTSource initialization note: {ex.Message}");
                        _isRunning = false;
                        _statistics.IsConnected = false;
                        StatusChanged?.Invoke(false, $"Error: {ex.Message}");
                        return false;
                    }

                    _isRunning = true;
                    _connectTime = DateTime.UtcNow;
                    _lastStatsSampleTime = DateTime.UtcNow;
                    _statistics.IsConnected = nativeConnected || _nativeSource?.IsActiveConnected == true;
                    _statistics.CurrentFps = _config.FrameRate;

                    StartStatisticsPolling();

                    StatusChanged?.Invoke(true, "SRT Receiver CONNECTED");
                    Log("[SRT]", "✅ [SUCCESS] Đã kết nối nhận luồng SRT thành công.");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _isRunning = false;
                _statistics.IsConnected = false;
                ErrorOccurred?.Invoke($"Lỗi kết nối thu nhận SRT: {ex.Message}");
                Log("[ERROR]", $"Lỗi nhận luồng SRT: {ex.Message}");
                StatusChanged?.Invoke(false, $"Error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Stops the current SRT transmission or reception stream session.
        /// </summary>
        public virtual async Task StopAsync()
        {
            if (!_isRunning && !_disposed) return;

            Log("[SRT]", "Đang dừng luồng truyền dẫn SRT...");
            StopStatisticsPolling();
            StopGroupSupervisor();

            _isRunning = false;
            _statistics.IsConnected = false;

            _platformOutput?.Dispose();
            _platformOutput = null;

            lock (_membersLock)
            {
                foreach (var sender in _memberSenders)
                {
                    sender.Dispose();
                }
                _memberSenders.Clear();

                foreach (var receiver in _memberReceivers)
                {
                    receiver.Dispose();
                }
                _memberReceivers.Clear();
            }

            _hitlessDeduplicator.Reset();

            if (_nativeOutput != null)
            {
                _nativeOutput.Close();
                _nativeOutput.Dispose();
                _nativeOutput = null;
            }

            if (_nativeSource != null)
            {
                _nativeSource.Disconnect();
                _nativeSource.Dispose();
                _nativeSource = null;
            }

            StatusChanged?.Invoke(false, "SRT Stopped / Idle");
            Log("[SRT]", "Đã dừng luồng SRT.");
            await Task.CompletedTask;
        }

        /// <summary>
        /// Starts the ingest session asynchronously conforming to <see cref="IMediaIngestSession"/>.
        /// </summary>
        public virtual async ValueTask<bool> StartAsync(CancellationToken ct = default)
        {
            return await ConnectReceiverAsync().ConfigureAwait(false);
        }

        async ValueTask IMediaIngestSession.StopAsync()
        {
            await StopAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Reads incoming broadcast media frames as an asynchronous stream of zero-copy spans.
        /// </summary>
        public async IAsyncEnumerable<MediaFrameSpan> ReadFramesAsync([EnumeratorCancellation] CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (!_isRunning)
            {
                bool connected = await ConnectReceiverAsync().ConfigureAwait(false);
                if (!connected)
                {
                    yield break;
                }
            }

            const int bufferSize = 65536;
            IntPtr nativeBuffer = Marshal.AllocHGlobal(bufferSize);
            byte[] managedBuffer = new byte[bufferSize];

            try
            {
                while (_isRunning && !ct.IsCancellationRequested)
                {
                    int bytesRead = ReceiveData(managedBuffer);
                    if (bytesRead > 0)
                    {
                        Marshal.Copy(managedBuffer, 0, nativeBuffer, bytesRead);
                        yield return new MediaFrameSpan(
                            dataPointer: nativeBuffer,
                            length: bytesRead,
                            stride: bytesRead,
                            width: _config.Width,
                            height: _config.Height,
                            format: PixelFormat.NV12,
                            timestampUs: (long)(DateTime.UtcNow - _connectTime).TotalMicroseconds);
                    }
                    else
                    {
                        try
                        {
                            await Task.Delay(5, ct).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(nativeBuffer);
            }
        }

        /// <summary>
        /// Creates or retrieves a configured <see cref="StreamOutput"/> instance for routing this SRT stream to a <see cref="VideoMixer"/>.
        /// </summary>
        public StreamOutput GetStreamOutput()
        {
            ThrowIfDisposed();
            if (_platformOutput == null)
            {
                _platformOutput = StreamOutput.SRT(_config);
            }
            return _platformOutput;
        }

        /// <summary>
        /// Sends raw video/audio transport packet bytes directly over the active SRT connection.
        /// Trong chế độ Group Socket (SMPTE 2022-7), gói tin được nhân bản gửi đồng thời qua tất cả các member socket đang kết nối.
        /// </summary>
        public bool SendData(byte[] data, int length = -1, int ttlMs = 0, bool inOrder = true)
        {
            if (data == null || data.Length == 0) return false;
            int size = length > 0 && length <= data.Length ? length : data.Length;

            if (_config.GroupSocketEnabled)
            {
                bool anySuccess = false;
                lock (_membersLock)
                {
                    for (int i = 0; i < _memberSenders.Count; i++)
                    {
                        var member = _memberSenders[i];
                        if (member.Output != null && member.Output.IsOpen && member.Status.IsConnected)
                        {
                            bool sent;
                            if (ttlMs > 0 || !inOrder)
                            {
                                sent = member.Output.SendMsg(data, size, ttlMs, inOrder);
                            }
                            else
                            {
                                sent = member.Output.Send(data, size);
                            }

                            if (sent)
                            {
                                anySuccess = true;
                                member.Status.PacketsSent++;
                                member.Status.BytesTransferred += (ulong)size;
                                member.Status.LastActiveTime = DateTime.UtcNow;
                                member.ConsecutiveFailures = 0;
                            }
                            else
                            {
                                member.ConsecutiveFailures++;
                                if (member.ConsecutiveFailures >= 10)
                                {
                                    member.Status.IsConnected = false;
                                    member.Status.StatusText = "Link Dropped (Reconnecting)";
                                    member.NextReconnectTime = DateTime.UtcNow.AddSeconds(1.5);
                                    try { member.Output?.Close(); } catch { }
                                    member.Output = null;
                                    Log("[GROUP_SMPTE2022_7]", $"⚠️ Member socket {member.Config.Name} bị mất kết nối. Đang tự động kết nối lại ngầm...");
                                    MemberStatusChanged?.Invoke(member.Status.Clone());
                                }
                            }
                        }
                    }
                }
                return anySuccess;
            }

            if (_nativeOutput != null && _nativeOutput.IsOpen)
            {
                if (ttlMs > 0 || !inOrder)
                {
                    bool sent = _nativeOutput.SendMsg(data, size, ttlMs, inOrder);
                    if (sent) return true;
                }
                return _nativeOutput.Send(data, size);
            }
            return false;
        }

        /// <summary>
        /// Receives raw transport packet bytes from the active SRT receiver connection.
        /// Trong chế độ Group Socket (SMPTE 2022-7), dữ liệu được tự động khử trùng lặp và bù sai lệch vi sai (Hitless Merging).
        /// </summary>
        public int ReceiveData(byte[] buffer)
        {
            if (buffer == null || buffer.Length == 0) return -1;

            if (_config.GroupSocketEnabled)
            {
                return _hitlessDeduplicator.ReadMergedData(buffer);
            }

            if (_nativeSource != null && _nativeSource.IsConnected)
            {
                return _nativeSource.Receive(buffer);
            }
            return -1;
        }

        /// <summary>
        /// Tự động thêm một Member Socket mới vào phiên làm việc đang chạy mà KHÔNG làm gián đoạn luồng truyền hiện tại.
        /// </summary>
        public async Task<bool> AddMemberSocketAsync(SRTGroupMemberConfig memberConfig)
        {
            ThrowIfDisposed();
            if (memberConfig == null) return false;

            SRTMemberSender? newSender = null;
            SRTMemberReceiver? newReceiver = null;
            int newPathIdx = 0;

            lock (_membersLock)
            {
                if (_config.GroupMembers.All(m => m.Id != memberConfig.Id))
                {
                    _config.GroupMembers.Add(memberConfig);
                }

                if (_isRunning)
                {
                    if (_nativeOutput != null || _memberSenders.Count > 0)
                    {
                        newSender = new SRTMemberSender(memberConfig);
                        _memberSenders.Add(newSender);
                    }
                    else if (_nativeSource != null || _memberReceivers.Count > 0)
                    {
                        newReceiver = new SRTMemberReceiver(memberConfig);
                        newPathIdx = _memberReceivers.Count;
                        _memberReceivers.Add(newReceiver);
                    }
                }
            }

            if (newSender != null)
            {
                Log("[GROUP_SMPTE2022_7]", $"🔄 [DYNAMIC-ATTACH] Đang tự động kết nối member socket mới ({memberConfig.Name} @ {memberConfig.Host}:{memberConfig.Port}) vào phiên làm việc...");
                _ = Task.Run(async () =>
                {
                    bool ok = await ConnectMemberSenderAsync(newSender).ConfigureAwait(false);
                    if (ok)
                    {
                        Log("[GROUP_SMPTE2022_7]", $"✅ [DYNAMIC-ATTACH] Member socket {memberConfig.Name} đã gia nhập vào luồng phát LIVE thành công mà không ngắt luồng!");
                    }
                });
            }
            else if (newReceiver != null)
            {
                Log("[GROUP_SMPTE2022_7]", $"🔄 [DYNAMIC-ATTACH] Đang tự động kết nối member socket thu mới ({memberConfig.Name} @ {memberConfig.Host}:{memberConfig.Port}) vào phiên làm việc...");
                _ = Task.Run(async () =>
                {
                    bool ok = await ConnectMemberReceiverAsync(newReceiver, newPathIdx).ConfigureAwait(false);
                    if (ok)
                    {
                        Log("[GROUP_SMPTE2022_7]", $"✅ [DYNAMIC-ATTACH] Member socket {memberConfig.Name} đã gia nhập vào luồng thu LIVE thành công!");
                    }
                });
            }

            await Task.CompletedTask;
            return true;
        }

        /// <summary>
        /// Xóa an toàn một Member Socket khỏi Group mà không ảnh hưởng tới các member socket khác.
        /// </summary>
        public async Task<bool> RemoveMemberSocketAsync(string memberId)
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(memberId)) return false;

            lock (_membersLock)
            {
                _config.GroupMembers.RemoveAll(m => m.Id == memberId);

                var sender = _memberSenders.FirstOrDefault(s => s.Config.Id == memberId);
                if (sender != null)
                {
                    _memberSenders.Remove(sender);
                    sender.Dispose();
                    Log("[GROUP_SMPTE2022_7]", $"Member socket phát {sender.Config.Name} đã được gỡ khỏi Group.");
                }

                var receiver = _memberReceivers.FirstOrDefault(r => r.Config.Id == memberId);
                if (receiver != null)
                {
                    _memberReceivers.Remove(receiver);
                    receiver.Dispose();
                    Log("[GROUP_SMPTE2022_7]", $"Member socket thu {receiver.Config.Name} đã được gỡ khỏi Group.");
                }
            }

            await Task.CompletedTask;
            return true;
        }

        /// <summary>
        /// Proactively marks the session as disconnected (e.g. on socket error or timeout).
        /// </summary>
        public void MarkDisconnected(string reason = "Connection lost")
        {
            if (_statistics.IsConnected)
            {
                _statistics.IsConnected = false;
                _statistics.CurrentBitrateKbps = 0;
                _statistics.CurrentFps = 0;
                StatusChanged?.Invoke(false, reason);
                StatisticsUpdated?.Invoke(_statistics.Clone());
            }
        }

        #region Internal Group Socket Connection & Supervisor Logic

        private void EnsureDefaultGroupMembersConfigured()
        {
            lock (_membersLock)
            {
                if (_config.GroupMembers.Count == 0)
                {
                    // Tạo sẵn Member 1 (Path A - Primary LAN)
                    _config.GroupMembers.Add(new SRTGroupMemberConfig(
                        name: "Path A (Primary)",
                        host: _config.Host,
                        port: _config.Port,
                        localInterfaceIp: _config.PrimaryInterfaceIp,
                        weight: 10
                    ));

                    // Tạo sẵn Member 2 (Path B - Secondary Redundancy / 4G/5G)
                    string backupIp = !string.IsNullOrEmpty(_config.BackupInterfaceIp) ? _config.BackupInterfaceIp : string.Empty;
                    int backupPort = _config.Mode == SRTMode.Listener ? _config.Port + 2 : _config.Port;
                    _config.GroupMembers.Add(new SRTGroupMemberConfig(
                        name: "Path B (Secondary / 4G)",
                        host: _config.Host,
                        port: backupPort,
                        localInterfaceIp: backupIp,
                        weight: 10
                    ));
                }
            }
        }

        private async Task<bool> ConnectMemberSenderAsync(SRTMemberSender member)
        {
            if (member.IsConnecting) return false;
            member.IsConnecting = true;

            try
            {
                member.Output?.Close();
                member.Output?.Dispose();

                var output = new SRTOutput();
                string memberUri = _config.ToMemberSrtUri(member.Config);
                Log("[GROUP_SMPTE2022_7]", $"Đang kết nối Member Sender [{member.Config.Name}] trên: {memberUri}");

                bool opened = await Task.Run(() => output.Open(memberUri)).ConfigureAwait(false);
                if (opened)
                {
                    member.Output = output;
                    member.Status.IsConnected = true;
                    member.Status.StatusText = "Active LIVE";
                    member.Status.LastConnectedTime = DateTime.UtcNow;
                    member.ConsecutiveFailures = 0;

                    Log("[GROUP_SMPTE2022_7]", $"✅ Member Sender [{member.Config.Name}] kết nối thành công: {memberUri}");
                    MemberStatusChanged?.Invoke(member.Status.Clone());
                    return true;
                }
                else
                {
                    output.Dispose();
                    member.Output = null;
                    member.Status.IsConnected = false;
                    member.Status.StatusText = "Connection Failed";
                    member.NextReconnectTime = DateTime.UtcNow.AddSeconds(2.0);
                    MemberStatusChanged?.Invoke(member.Status.Clone());
                    return false;
                }
            }
            catch (Exception ex)
            {
                member.Status.IsConnected = false;
                member.Status.StatusText = $"Error: {ex.Message}";
                member.NextReconnectTime = DateTime.UtcNow.AddSeconds(2.0);
                MemberStatusChanged?.Invoke(member.Status.Clone());
                return false;
            }
            finally
            {
                member.IsConnecting = false;
            }
        }

        private async Task<bool> ConnectMemberReceiverAsync(SRTMemberReceiver member, int pathIndex)
        {
            if (member.IsConnecting) return false;
            member.IsConnecting = true;

            try
            {
                member.Source?.Disconnect();
                member.Source?.Dispose();

                var source = new SRTSource();
                string memberUri = _config.ToMemberSrtUri(member.Config);
                Log("[GROUP_SMPTE2022_7]", $"Đang kết nối Member Receiver [{member.Config.Name}] trên: {memberUri}");

                bool connected = await Task.Run(() => source.Connect(memberUri)).ConfigureAwait(false);
                if (connected)
                {
                    member.Source = source;
                    member.Status.IsConnected = true;
                    member.Status.StatusText = "Receiving LIVE";
                    member.Status.LastConnectedTime = DateTime.UtcNow;

                    StartMemberReceiverPump(member, pathIndex);

                    Log("[GROUP_SMPTE2022_7]", $"✅ Member Receiver [{member.Config.Name}] kết nối thành công: {memberUri}");
                    MemberStatusChanged?.Invoke(member.Status.Clone());
                    return true;
                }
                else
                {
                    source.Dispose();
                    member.Source = null;
                    member.Status.IsConnected = false;
                    member.Status.StatusText = "Connection Failed";
                    member.NextReconnectTime = DateTime.UtcNow.AddSeconds(2.0);
                    MemberStatusChanged?.Invoke(member.Status.Clone());
                    return false;
                }
            }
            catch (Exception ex)
            {
                member.Status.IsConnected = false;
                member.Status.StatusText = $"Error: {ex.Message}";
                member.NextReconnectTime = DateTime.UtcNow.AddSeconds(2.0);
                MemberStatusChanged?.Invoke(member.Status.Clone());
                return false;
            }
            finally
            {
                member.IsConnecting = false;
            }
        }

        private void StartMemberReceiverPump(SRTMemberReceiver member, int pathIndex)
        {
            member.WorkerCts?.Cancel();
            member.WorkerCts = new CancellationTokenSource();
            var token = member.WorkerCts.Token;

            _ = Task.Run(() =>
            {
                byte[] pumpBuffer = new byte[65536];
                while (!token.IsCancellationRequested && _isRunning && member.Source != null)
                {
                    try
                    {
                        if (_config.Mode == SRTMode.Listener)
                        {
                            if (!member.Source.IsActiveConnected)
                            {
                                if (member.Status.IsConnected)
                                {
                                    member.Status.IsConnected = false;
                                    member.Status.StatusText = "Listening (Waiting for client)...";
                                    MemberStatusChanged?.Invoke(member.Status.Clone());
                                }
                                Thread.Sleep(5);
                                continue;
                            }

                            int read = member.Source.Receive(pumpBuffer);
                            if (read > 0)
                            {
                                member.Status.PacketsReceived++;
                                member.Status.BytesTransferred += (ulong)read;
                                member.Status.LastActiveTime = DateTime.UtcNow;
                                if (!member.Status.IsConnected)
                                {
                                    member.Status.IsConnected = true;
                                    member.Status.StatusText = "Receiving LIVE";
                                    MemberStatusChanged?.Invoke(member.Status.Clone());
                                }

                                _hitlessDeduplicator.PushPacket(pathIndex, pumpBuffer, read);
                            }
                            else
                            {
                                Thread.Sleep(1);
                            }
                        }
                        else
                        {
                            // Caller Mode
                            if (!member.Source.IsConnected || !member.Source.IsActiveConnected)
                            {
                                member.Status.IsConnected = false;
                                member.Status.StatusText = "Disconnected";
                                member.NextReconnectTime = DateTime.UtcNow.AddSeconds(1.5);
                                MemberStatusChanged?.Invoke(member.Status.Clone());
                                break;
                            }

                            int read = member.Source.Receive(pumpBuffer);
                            if (read > 0)
                            {
                                member.Status.PacketsReceived++;
                                member.Status.BytesTransferred += (ulong)read;
                                member.Status.LastActiveTime = DateTime.UtcNow;
                                if (!member.Status.IsConnected)
                                {
                                    member.Status.IsConnected = true;
                                    member.Status.StatusText = "Receiving LIVE";
                                    MemberStatusChanged?.Invoke(member.Status.Clone());
                                }

                                _hitlessDeduplicator.PushPacket(pathIndex, pumpBuffer, read);
                            }
                            else
                            {
                                Thread.Sleep(1);
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"[SRTMemberReceiverPump] Error: {ex.Message}");
                        Thread.Sleep(10);
                    }
                }
            }, token);
        }

        private void StartGroupSupervisor()
        {
            StopGroupSupervisor();
            _groupSupervisorCts = new CancellationTokenSource();
            var token = _groupSupervisorCts.Token;

            _ = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested && _isRunning)
                {
                    try
                    {
                        await Task.Delay(1500, token).ConfigureAwait(false);

                        // Giám sát và tự động gắn kết nối các Member Sockets phát (Sender)
                        lock (_membersLock)
                        {
                            var now = DateTime.UtcNow;
                            foreach (var member in _memberSenders)
                            {
                                if (!member.Status.IsConnected && !member.IsConnecting && now >= member.NextReconnectTime)
                                {
                                    _ = ConnectMemberSenderAsync(member);
                                }
                            }

                            for (int i = 0; i < _memberReceivers.Count; i++)
                            {
                                var member = _memberReceivers[i];
                                int idx = i;
                                bool needsReconnect = false;
                                if (_config.Mode == SRTMode.Listener)
                                {
                                    // Trong chế độ Listener: socket server duy trì lắng nghe đón client,
                                    // chỉ kết nối lại nếu socket listener bị đóng hoặc lỗi (Source == null || !Source.IsConnected).
                                    // TUYỆT ĐỐI không reconnect khi socket đang lắng nghe vì sẽ ngắt kết nối client đang bắt tay.
                                    needsReconnect = (member.Source == null || !member.Source.IsConnected) && !member.IsConnecting && now >= member.NextReconnectTime;
                                }
                                else
                                {
                                    needsReconnect = !member.Status.IsConnected && !member.IsConnecting && now >= member.NextReconnectTime;
                                }

                                if (needsReconnect)
                                {
                                    _ = ConnectMemberReceiverAsync(member, idx);
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
                        Trace.WriteLine($"[GroupSupervisor] Error: {ex.Message}");
                    }
                }
            }, token);
        }

        private void StopGroupSupervisor()
        {
            _groupSupervisorCts?.Cancel();
            _groupSupervisorCts?.Dispose();
            _groupSupervisorCts = null;
        }

        #endregion

        private void StartStatisticsPolling()
        {
            _statsTimer?.Dispose();
            _statsTimer = new Timer(OnPollStatistics, null, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500));
        }

        private void StopStatisticsPolling()
        {
            _statsTimer?.Dispose();
            _statsTimer = null;
        }

        private void OnPollStatistics(object? state)
        {
            if (!_isRunning || _disposed) return;

            try
            {
                var now = DateTime.UtcNow;
                var elapsed = now - _lastStatsSampleTime;
                if (elapsed.TotalSeconds < 0.1) return;

                bool wasConnected = _statistics.IsConnected;

                if (_config.GroupSocketEnabled)
                {
                    // Thu thập thống kê từ các Member Sockets
                    lock (_membersLock)
                    {
                        bool anyMemberConnected = false;
                        double totalBitrate = 0.0;
                        ulong totalBytes = 0;
                        double bestRtt = double.MaxValue;
                        double avgLoss = 0.0;
                        int validRttCount = 0;

                        foreach (var sender in _memberSenders)
                        {
                            if (sender.Output != null && sender.Output.IsOpen)
                            {
                                sender.Status.IsConnected = sender.Output.IsConnected;
                                if (sender.Output.GetStatistics(out var nativeStats))
                                {
                                    sender.Status.RttMs = nativeStats.msRTT;
                                    sender.Status.PacketLossPercent = nativeStats.pktLossTotal;
                                    sender.Status.BitrateKbps = nativeStats.mbpsBandwidth * 1000.0;
                                    totalBytes += nativeStats.bytesSentTotal;

                                    if (nativeStats.msRTT > 0)
                                    {
                                        bestRtt = Math.Min(bestRtt, nativeStats.msRTT);
                                        validRttCount++;
                                    }
                                    avgLoss += nativeStats.pktLossTotal;
                                }
                            }
                            if (sender.Status.IsConnected) anyMemberConnected = true;
                        }

                        foreach (var receiver in _memberReceivers)
                        {
                            if (receiver.Source != null && receiver.Source.IsConnected)
                            {
                                receiver.Status.IsConnected = receiver.Source.IsActiveConnected;
                                if (receiver.Source.GetStatistics(out var nativeStats))
                                {
                                    receiver.Status.RttMs = nativeStats.msRTT;
                                    receiver.Status.PacketLossPercent = nativeStats.pktLossTotal;
                                    receiver.Status.BitrateKbps = nativeStats.mbpsBandwidth * 1000.0;
                                    totalBytes += nativeStats.bytesRecvTotal;

                                    if (nativeStats.msRTT > 0)
                                    {
                                        bestRtt = Math.Min(bestRtt, nativeStats.msRTT);
                                        validRttCount++;
                                    }
                                    avgLoss += nativeStats.pktLossTotal;
                                }
                            }
                            if (receiver.Status.IsConnected) anyMemberConnected = true;
                        }

                        _statistics.IsConnected = anyMemberConnected;
                        if (validRttCount > 0)
                        {
                            _statistics.RttMs = bestRtt;
                            _statistics.PacketLossPercent = avgLoss / validRttCount;
                        }

                        if (totalBytes >= _lastTotalBytes)
                        {
                            ulong bytesDiff = totalBytes - _lastTotalBytes;
                            _statistics.CurrentBitrateKbps = (bytesDiff * 8.0) / (elapsed.TotalSeconds * 1000.0);
                        }
                        _statistics.TotalBytesTransferred = totalBytes;
                    }

                    GroupStatsUpdated?.Invoke(GroupStats);
                }
                else
                {
                    if (_nativeOutput != null && _nativeOutput.IsOpen)
                    {
                        _statistics.IsConnected = _nativeOutput.IsConnected;
                        if (_nativeOutput.GetStatistics(out var nativeStats))
                        {
                            _statistics.RttMs = nativeStats.msRTT;
                            _statistics.PacketLossPercent = nativeStats.pktLossTotal;
                            _statistics.BandwidthMbps = nativeStats.mbpsBandwidth;
                            _statistics.PacketsRetransmitted = nativeStats.pktRetransmitTotal;
                            _statistics.PacketsSent = nativeStats.pktSentTotal;
                            _statistics.PacketsReceived = nativeStats.pktRecvTotal;
                            _statistics.PacketsDropped = nativeStats.pktDropTotal;
                            _statistics.TotalBytesTransferred = nativeStats.bytesSentTotal;
                        }
                    }
                    else if (_nativeSource != null && _nativeSource.IsConnected)
                    {
                        _statistics.IsConnected = _nativeSource.IsActiveConnected;
                        if (_nativeSource.GetStatistics(out var nativeStats))
                        {
                            _statistics.RttMs = nativeStats.msRTT;
                            _statistics.PacketLossPercent = nativeStats.pktLossTotal;
                            _statistics.BandwidthMbps = nativeStats.mbpsBandwidth;
                            _statistics.PacketsRetransmitted = nativeStats.pktRetransmitTotal;
                            _statistics.PacketsSent = nativeStats.pktSentTotal;
                            _statistics.PacketsReceived = nativeStats.pktRecvTotal;
                            _statistics.PacketsDropped = nativeStats.pktDropTotal;
                            _statistics.TotalBytesTransferred = nativeStats.bytesRecvTotal;
                        }
                    }

                    if (_statistics.TotalBytesTransferred >= _lastTotalBytes)
                    {
                        ulong bytesDiff = _statistics.TotalBytesTransferred - _lastTotalBytes;
                        _statistics.CurrentBitrateKbps = (bytesDiff * 8.0) / (elapsed.TotalSeconds * 1000.0);
                    }
                }

                if (wasConnected != _statistics.IsConnected)
                {
                    string statusMsg = _statistics.IsConnected
                        ? (_nativeOutput != null || _memberSenders.Count > 0 ? "SRT Transmitting LIVE" : "SRT Receiver CONNECTED")
                        : (_nativeOutput != null || _memberSenders.Count > 0 ? "SRT Output Disconnected" : "SRT Receiver DISCONNECTED");
                    StatusChanged?.Invoke(_statistics.IsConnected, statusMsg);
                }

                if (_config.AutoLatency && _statistics.RttMs > 0)
                {
                    _config.CalculateAutoLatency(_statistics.RttMs);
                }

                if (_config.BondingEnabled && !string.IsNullOrEmpty(_config.BackupInterfaceIp))
                {
                    EvaluateCellularLinkAndFailover();
                }

                _statistics.Uptime = _isRunning ? (now - _connectTime) : TimeSpan.Zero;
                _lastTotalBytes = _statistics.TotalBytesTransferred;
                _lastStatsSampleTime = now;

                StatisticsUpdated?.Invoke(_statistics.Clone());
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[SRTStreamSession] Stats Polling Error: {ex.Message}");
            }
        }

        private void EvaluateCellularLinkAndFailover()
        {
            if (!_isUsingBackupInterface)
            {
                if (_statistics.PacketLossPercent >= _config.CellularLossThresholdPercent || (!_statistics.IsConnected && _isRunning))
                {
                    _isUsingBackupInterface = true;
                    Log("[BONDING]", $"⚡ Suy hao sóng/mạng chính cao ({_statistics.PacketLossPercent:F1}%). Tự động chuyển luồng phát sang giao diện 4G/5G dự phòng ({_config.BackupInterfaceIp}).");
                    StatusChanged?.Invoke(true, $"Active Interface Switched to Backup Cellular ({_config.BackupInterfaceIp})");
                }
            }
            else
            {
                if (_statistics.PacketLossPercent < 1.0 && _statistics.IsConnected)
                {
                    _isUsingBackupInterface = false;
                    Log("[BONDING]", $"✅ Tín hiệu cáp quang chính phục hồi ổn định. Tự động chuyển lại luồng phát về ({_config.PrimaryInterfaceIp}).");
                    StatusChanged?.Invoke(true, $"Active Interface Restored to Primary ({_config.PrimaryInterfaceIp})");
                }
            }
        }

        private void Log(string tag, string message)
        {
            LogEmitted?.Invoke(tag, message);
            Trace.WriteLine($"[SRTStreamSession]{tag} {message}");
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        /// <summary>
        /// Releases all managed and unmanaged resources used by the stream session.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            StopAsync().GetAwaiter().GetResult();
            _statsTimer?.Dispose();
            _statsTimer = null;
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Asynchronously releases resources used by the stream session.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            await StopAsync().ConfigureAwait(false);
            _statsTimer?.Dispose();
            _statsTimer = null;
            GC.SuppressFinalize(this);
        }

        #region Helper Member Wrapper Classes

        private sealed class SRTMemberSender : IDisposable
        {
            public SRTGroupMemberConfig Config { get; }
            public SRTOutput? Output { get; set; }
            public SRTGroupMemberStatus Status { get; }
            public bool IsConnecting { get; set; }
            public int ConsecutiveFailures { get; set; }
            public DateTime NextReconnectTime { get; set; } = DateTime.MinValue;

            public SRTMemberSender(SRTGroupMemberConfig config)
            {
                Config = config;
                Status = new SRTGroupMemberStatus
                {
                    Id = config.Id,
                    Name = config.Name,
                    Endpoint = $"{config.Host}:{config.Port}",
                    LocalInterfaceIp = config.LocalInterfaceIp,
                    IsConnected = false,
                    StatusText = "Pending"
                };
            }

            public void Dispose()
            {
                try { Output?.Close(); Output?.Dispose(); } catch { }
                Output = null;
                Status.IsConnected = false;
                Status.StatusText = "Closed";
            }
        }

        private sealed class SRTMemberReceiver : IDisposable
        {
            public SRTGroupMemberConfig Config { get; }
            public SRTSource? Source { get; set; }
            public SRTGroupMemberStatus Status { get; }
            public bool IsConnecting { get; set; }
            public DateTime NextReconnectTime { get; set; } = DateTime.MinValue;
            public CancellationTokenSource? WorkerCts { get; set; }

            public SRTMemberReceiver(SRTGroupMemberConfig config)
            {
                Config = config;
                Status = new SRTGroupMemberStatus
                {
                    Id = config.Id,
                    Name = config.Name,
                    Endpoint = $"{config.Host}:{config.Port}",
                    LocalInterfaceIp = config.LocalInterfaceIp,
                    IsConnected = false,
                    StatusText = "Pending"
                };
            }

            public void Dispose()
            {
                try { WorkerCts?.Cancel(); WorkerCts?.Dispose(); } catch { }
                WorkerCts = null;
                try { Source?.Disconnect(); Source?.Dispose(); } catch { }
                Source = null;
                Status.IsConnected = false;
                Status.StatusText = "Closed";
            }
        }

        #endregion
    }
}
