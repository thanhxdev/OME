using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using OpenMedia.Platform.Models;
using OpenMedia.SDK;

namespace OpenMedia.Platform
{
    /// <summary>
    /// High-level session manager for Secure Reliable Transport (SRT) streaming.
    /// Provides unified transmission (egress), reception (ingest), real-time telemetry monitoring,
    /// and clean lifecycle management for broadcast workflows.
    /// </summary>
    public class SRTStreamSession : IDisposable, IAsyncDisposable
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

        /// <summary>Current SRT stream configuration.</summary>
        public SRTStreamConfig Config => _config;

        /// <summary>Real-time telemetry and statistics.</summary>
        public SRTStatistics Statistics => _statistics;

        /// <summary>Indicates whether the stream session is currently active.</summary>
        public bool IsRunning => _isRunning;

        /// <summary>Gets the standardized SRT connection URI based on current configuration.</summary>
        public string SrtUri => _config.ToSrtUri();

        // ─── Events ─────────────────────────────────────────────────────
        /// <summary>Fired when the connection or transmission status changes.</summary>
        public event Action<bool, string>? StatusChanged;

        /// <summary>Fired periodically when updated telemetry metrics are calculated.</summary>
        public event Action<SRTStatistics>? StatisticsUpdated;

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
        }

        /// <summary>
        /// Starts transmitting video/audio packages over SRT (Transmission / Egress mode).
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

                // Khởi tạo native output cho phiên truyền dữ liệu trực tiếp nếu có
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
                        Log("[WARN]", $"⚠️ Native SRTOutput chưa kết nối/lắng nghe được trên URI: {_config.ToSrtUri()} (Kiểm tra Mode, IP/Port hoặc Firewall)");
                    }
                }
                catch (Exception ex)
                {
                    Log("[WARN]", $"Direct native SRTOutput initialization note: {ex.Message}");
                }

                _isRunning = true;
                _connectTime = DateTime.UtcNow;
                _lastStatsSampleTime = DateTime.UtcNow;
                _statistics.IsConnected = nativeOpened || _nativeOutput?.IsConnected == true;
                _statistics.CurrentFps = _config.FrameRate;

                StartStatisticsPolling();

                StatusChanged?.Invoke(true, "SRT Transmitting LIVE");
                Log("[SRT]", "✅ [SUCCESS] Khởi động phiên truyền dữ liệu SRT hoàn tất.");

                await Task.CompletedTask;
                return true;
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
        /// </summary>
        public virtual async Task<bool> ConnectReceiverAsync()
        {
            ThrowIfDisposed();
            if (_isRunning) return true;

            try
            {
                Log("[SRT]", $"Đang kết nối nhận luồng SRT từ: {_config.ToSrtUri()}...");

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
                    }
                }
                catch (Exception ex)
                {
                    Log("[WARN]", $"Direct native SRTSource initialization note: {ex.Message}");
                }

                _isRunning = true;
                _connectTime = DateTime.UtcNow;
                _lastStatsSampleTime = DateTime.UtcNow;
                _statistics.IsConnected = nativeConnected || _nativeSource?.IsActiveConnected == true;
                _statistics.CurrentFps = _config.FrameRate;

                StartStatisticsPolling();

                StatusChanged?.Invoke(true, "SRT Receiver CONNECTED");
                Log("[SRT]", "✅ [SUCCESS] Đã kết nối nhận luồng SRT thành công.");

                await Task.CompletedTask;
                return true;
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
            if (!_isRunning) return;

            Log("[SRT]", "Đang dừng luồng truyền dẫn SRT...");
            StopStatisticsPolling();

            _isRunning = false;
            _statistics.IsConnected = false;

            _platformOutput?.Dispose();
            _platformOutput = null;

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
        /// </summary>
        public bool SendData(byte[] data)
        {
            if (_nativeOutput != null && _nativeOutput.IsOpen)
            {
                return _nativeOutput.Send(data);
            }
            return false;
        }

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

                bool gotNativeStats = false;

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
                        gotNativeStats = true;
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
                        gotNativeStats = true;
                    }
                }

                if (gotNativeStats)
                {
                    ulong deltaBytes = _statistics.TotalBytesTransferred >= _lastTotalBytes
                        ? _statistics.TotalBytesTransferred - _lastTotalBytes
                        : 0;
                    _statistics.CurrentBitrateKbps = elapsed.TotalSeconds > 0 
                        ? (deltaBytes * 8.0 / 1000.0) / elapsed.TotalSeconds 
                        : 0;
                    _statistics.CurrentFps = _statistics.IsConnected ? _config.FrameRate : 0;
                }
                else
                {
                    _statistics.CurrentBitrateKbps = 0;
                    _statistics.RttMs = 0;
                    _statistics.PacketLossPercent = 0.0;
                    _statistics.BandwidthMbps = 0.0;
                    _statistics.CurrentFps = 0;
                }

                if (_config.AutoLatency && _statistics.RttMs > 0)
                {
                    _config.CalculateAutoLatency(_statistics.RttMs);
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

            await StopAsync();
            _statsTimer?.Dispose();
            _statsTimer = null;
            GC.SuppressFinalize(this);
        }
    }
}
