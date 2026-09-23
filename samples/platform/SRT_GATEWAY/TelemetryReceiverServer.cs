using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenMedia.Platform.Telemetry;

namespace SRT_GATEWAY
{
    public enum NodeHealthStatus
    {
        Live,
        Warning,
        Critical,
        Offline
    }

    public sealed class TelemetryAuditLogItem
    {
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
        public string NodeName { get; set; } = string.Empty;
        public string NodeType { get; set; } = string.Empty;
        public string EventType { get; set; } = string.Empty; // "CONNECTED", "DISCONNECTED", "HIGH_LOSS", "HIGH_RTT", "SMPTE_FAILOVER"
        public string Detail { get; set; } = string.Empty;
    }

    public sealed class TelemetryNodeState
    {
        public string NodeName { get; set; } = string.Empty;
        public string NodeType { get; set; } = "Encoder";
        public SRTTelemetryPacket LatestPacket { get; set; } = new();
        public DateTime LastHeartbeatUtc { get; set; } = DateTime.UtcNow;
        public NodeHealthStatus Status { get; set; } = NodeHealthStatus.Live;

        // Lịch sử 60 điểm cho Sparkline (1 điểm / giây)
        public List<double> BitrateHistory { get; } = new(60);
        public List<double> RttHistory { get; } = new(60);
        public List<double> LossHistory { get; } = new(60);

        private readonly object _historyLock = new();

        public void AddSample(double bitrateKbps, double rttMs, double lossPercent)
        {
            lock (_historyLock)
            {
                BitrateHistory.Add(bitrateKbps);
                if (BitrateHistory.Count > 60) BitrateHistory.RemoveAt(0);

                RttHistory.Add(rttMs);
                if (RttHistory.Count > 60) RttHistory.RemoveAt(0);

                LossHistory.Add(lossPercent);
                if (LossHistory.Count > 60) LossHistory.RemoveAt(0);
            }
        }

        public (double[] Bitrate, double[] Rtt, double[] Loss) GetHistorySnapshots()
        {
            lock (_historyLock)
            {
                return (BitrateHistory.ToArray(), RttHistory.ToArray(), LossHistory.ToArray());
            }
        }
    }

    /// <summary>
    /// Máy chủ giám sát sức khỏe luồng tập trung (SRT_MONITOR) nhận Telemetry từ Encoder / Decoder qua HTTP POST JSON.
    /// </summary>
    public sealed class TelemetryReceiverServer : IDisposable
    {
        private HttpListener? _listener;
        private CancellationTokenSource? _cts;
        private Task? _listenerTask;
        private Timer? _healthCheckTimer;
        private bool _isRunning;
        private bool _disposed;

        private readonly ConcurrentDictionary<string, TelemetryNodeState> _nodes = new();
        private readonly List<TelemetryAuditLogItem> _auditLogs = new();
        private readonly object _logsLock = new();

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public int Port { get; private set; } = 8088;
        public bool IsRunning => _isRunning;
        public IReadOnlyDictionary<string, TelemetryNodeState> ActiveNodes => _nodes;
        public IReadOnlyList<TelemetryAuditLogItem> AuditLogs
        {
            get
            {
                lock (_logsLock) return _auditLogs.ToList();
            }
        }

        public event Action<TelemetryNodeState>? NodeUpdated;
        public event Action<TelemetryNodeState>? NodeOffline;
        public event Action<TelemetryAuditLogItem>? LogEmitted;
        public event Action<string, string>? AlertTriggered; // (NodeName, AlertMessage)

        public bool Start(int port = 8088)
        {
            if (_isRunning) return true;
            Port = port;

            try
            {
                _listener = new HttpListener();
                
                // Thử bind wildcard trước, nếu quyền hạn User không cho phép thì fallback localhost
                bool bound = false;
                try
                {
                    _listener.Prefixes.Add($"http://*:{port}/api/telemetry/");
                    _listener.Start();
                    bound = true;
                }
                catch
                {
                    _listener.Close();
                    _listener = new HttpListener();
                    _listener.Prefixes.Add($"http://+:{port}/api/telemetry/");
                    try
                    {
                        _listener.Start();
                        bound = true;
                    }
                    catch
                    {
                        _listener.Close();
                        _listener = new HttpListener();
                        _listener.Prefixes.Add($"http://localhost:{port}/api/telemetry/");
                        _listener.Prefixes.Add($"http://127.0.0.1:{port}/api/telemetry/");
                        _listener.Start();
                        bound = true;
                    }
                }

                if (!bound) return false;

                _isRunning = true;
                _cts = new CancellationTokenSource();
                var token = _cts.Token;

                _listenerTask = Task.Run(() => ListenLoopAsync(token), token);
                _healthCheckTimer = new Timer(OnHealthCheckTick, null, 1000, 1000);

                AddAuditLog("SYSTEM", "Monitor", "SERVER_STARTED", $"Máy chủ SRT Monitor khởi động lắng nghe trên cổng {port}");
                return true;
            }
            catch (Exception ex)
            {
                AddAuditLog("SYSTEM", "Monitor", "START_ERROR", $"Lỗi khởi chạy SRT Monitor: {ex.Message}");
                return false;
            }
        }

        public void Stop()
        {
            if (!_isRunning) return;
            _isRunning = false;

            _healthCheckTimer?.Dispose();
            _healthCheckTimer = null;

            try { _cts?.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
            try { _listener?.Close(); } catch { }
            _listener = null;

            AddAuditLog("SYSTEM", "Monitor", "SERVER_STOPPED", "Máy chủ SRT Monitor đã dừng.");
        }

        private async Task ListenLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _isRunning && _listener != null)
            {
                try
                {
                    var context = await _listener.GetContextAsync().ConfigureAwait(false);
                    _ = ProcessRequestAsync(context);
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (token.IsCancellationRequested) break;
                    System.Diagnostics.Debug.WriteLine($"[SRT_MONITOR] Listener Error: {ex.Message}");
                }
            }
        }

        private async Task ProcessRequestAsync(HttpListenerContext context)
        {
            var req = context.Request;
            var resp = context.Response;

            // CORS headers for Web dashboards if needed
            resp.AddHeader("Access-Control-Allow-Origin", "*");
            resp.AddHeader("Access-Control-Allow-Methods", "POST, GET, OPTIONS");
            resp.AddHeader("Access-Control-Allow-Headers", "Content-Type");

            if (req.HttpMethod.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                resp.StatusCode = (int)HttpStatusCode.NoContent;
                resp.Close();
                return;
            }

            if (req.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
                    string body = await reader.ReadToEndAsync().ConfigureAwait(false);

                    var packet = JsonSerializer.Deserialize<SRTTelemetryPacket>(body, JsonOptions);
                    if (packet != null && !string.IsNullOrWhiteSpace(packet.NodeName))
                    {
                        ProcessIncomingPacket(packet);

                        byte[] okBytes = Encoding.UTF8.GetBytes("{\"status\":\"ok\"}");
                        resp.ContentType = "application/json";
                        resp.StatusCode = (int)HttpStatusCode.OK;
                        resp.ContentLength64 = okBytes.Length;
                        await resp.OutputStream.WriteAsync(okBytes).ConfigureAwait(false);
                        resp.Close();
                        return;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SRT_MONITOR] Parse error: {ex.Message}");
                }
            }

            resp.StatusCode = (int)HttpStatusCode.BadRequest;
            resp.Close();
        }

        private void ProcessIncomingPacket(SRTTelemetryPacket packet)
        {
            string nodeKey = packet.NodeName.Trim();
            bool isNew = !_nodes.ContainsKey(nodeKey);

            var state = _nodes.GetOrAdd(nodeKey, name => new TelemetryNodeState
            {
                NodeName = name,
                NodeType = packet.NodeType
            });

            bool wasOffline = state.Status == NodeHealthStatus.Offline;
            state.LastHeartbeatUtc = DateTime.UtcNow;
            state.LatestPacket = packet;
            state.NodeType = packet.NodeType;

            // Đánh giá trạng thái sức khỏe
            if (!packet.IsConnected)
            {
                state.Status = NodeHealthStatus.Offline;
            }
            else if (packet.LossPercent > 2.0 || packet.RttMs > 150.0)
            {
                state.Status = NodeHealthStatus.Critical;
                AlertTriggered?.Invoke(state.NodeName, $"🔴 NGUY HIỂM: Loss {packet.LossPercent:F1}% | RTT {packet.RttMs:F0}ms");
            }
            else if (packet.LossPercent > 0.5 || packet.RttMs > 100.0)
            {
                state.Status = NodeHealthStatus.Warning;
            }
            else
            {
                state.Status = NodeHealthStatus.Live;
            }

            // Ghi nhận mẫu biểu đồ Sparkline
            state.AddSample(packet.BitrateKbps, packet.RttMs, packet.LossPercent);

            // Audit Logs
            if (isNew || wasOffline)
            {
                AddAuditLog(state.NodeName, state.NodeType, "CONNECTED", $"Trạm {state.NodeName} ({state.NodeType}) đã kết nối gửi nhịp tim.");
            }

            // Cảnh báo nếu SMPTE 2022-7 bị đứt 1 đường
            if (packet.IsSmpte2022_7Active)
            {
                if (packet.PathAConnected && !packet.PathBConnected)
                {
                    AddAuditLog(state.NodeName, state.NodeType, "SMPTE_FAILOVER", "⚠️ Đường Path B bị ngắt, đang duy trì truyền dẫn qua Path A.");
                }
                else if (!packet.PathAConnected && packet.PathBConnected)
                {
                    AddAuditLog(state.NodeName, state.NodeType, "SMPTE_FAILOVER", "⚠️ Đường Path A bị ngắt, đang duy trì truyền dẫn qua Path B.");
                }
            }

            NodeUpdated?.Invoke(state);
        }

        private void OnHealthCheckTick(object? _)
        {
            var now = DateTime.UtcNow;
            foreach (var kvp in _nodes)
            {
                var state = kvp.Value;
                if (state.Status != NodeHealthStatus.Offline && (now - state.LastHeartbeatUtc) > TimeSpan.FromSeconds(3.0))
                {
                    state.Status = NodeHealthStatus.Offline;
                    state.LatestPacket.IsConnected = false;
                    AddAuditLog(state.NodeName, state.NodeType, "DISCONNECTED", $"Mất liên lạc với trạm {state.NodeName} (Timeout > 3.0s).");
                    NodeOffline?.Invoke(state);
                    NodeUpdated?.Invoke(state);
                }
            }
        }

        public void AddAuditLog(string nodeName, string nodeType, string eventType, string detail)
        {
            var log = new TelemetryAuditLogItem
            {
                TimestampUtc = DateTime.UtcNow,
                NodeName = nodeName,
                NodeType = nodeType,
                EventType = eventType,
                Detail = detail
            };

            lock (_logsLock)
            {
                _auditLogs.Add(log);
                if (_auditLogs.Count > 1000) _auditLogs.RemoveAt(0);
            }

            LogEmitted?.Invoke(log);
        }

        public void ExportCsv(string filePath)
        {
            lock (_logsLock)
            {
                using var sw = new StreamWriter(filePath, false, Encoding.UTF8);
                sw.WriteLine("Timestamp (UTC),Node Name,Node Type,Event Type,Detail");
                foreach (var item in _auditLogs)
                {
                    string safeDetail = $"\"{item.Detail.Replace("\"", "\"\"")}\"";
                    sw.WriteLine($"{item.TimestampUtc:yyyy-MM-dd HH:mm:ss},{item.NodeName},{item.NodeType},{item.EventType},{safeDetail}");
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
