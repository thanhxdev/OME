using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OpenMedia.Platform.Telemetry
{
    /// <summary>
    /// Tiến trình gửi telemetry heartbeat ngầm (1.0 giây/lần) từ Encoder/Decoder/Gateway về máy chủ giám sát SRT_MONITOR.
    /// Hoàn toàn bất đồng bộ, non-blocking, không bao giờ làm gián đoạn luồng phát nếu máy chủ giám sát chưa bật.
    /// </summary>
    public sealed class SRTTelemetryReporter : IDisposable
    {
        private static readonly HttpClient SharedHttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(2.0)
        };

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        private readonly Func<SRTTelemetryPacket?> _packetProvider;
        private Timer? _timer;
        private bool _isRunning;
        private bool _disposed;
        private int _inFlight;

        /// <summary>URL của máy chủ SRT Monitor (ví dụ: http://127.0.0.1:8088).</summary>
        public string ServerUrl { get; set; } = "http://127.0.0.1:8088";

        /// <summary>Tên định danh node gửi telemetry.</summary>
        public string NodeName { get; set; } = "ENC_CAM_01";

        /// <summary>Loại node: "Encoder" hoặc "Decoder" hoặc "Gateway".</summary>
        public string NodeType { get; set; } = "Encoder";

        /// <summary>Cho biết tiến trình gửi nhịp tim có đang hoạt động hay không.</summary>
        public bool IsRunning => _isRunning;

        /// <summary>Sự kiện báo trạng thái gửi nhịp tim (thành công / thất bại) để giao diện nhấp nháy đèn LED.</summary>
        public event Action<bool, string?>? HeartbeatPulse;

        /// <summary>
        /// Khởi tạo reporter với hàm delegate cung cấp dữ liệu packet thời gian thực.
        /// </summary>
        public SRTTelemetryReporter(Func<SRTTelemetryPacket?> packetProvider)
        {
            _packetProvider = packetProvider ?? throw new ArgumentNullException(nameof(packetProvider));
        }

        /// <summary>
        /// Khởi động gửi nhịp tim 1 giây / lần.
        /// </summary>
        public void Start()
        {
            if (_disposed || _isRunning) return;
            _isRunning = true;
            _timer = new Timer(async _ => await SendHeartbeatAsync().ConfigureAwait(false), null, 500, 1000);
        }

        /// <summary>
        /// Dừng gửi nhịp tim.
        /// </summary>
        public void Stop()
        {
            _isRunning = false;
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
            _timer?.Dispose();
            _timer = null;
        }

        private async Task SendHeartbeatAsync()
        {
            if (!_isRunning || _disposed) return;

            // Đảm bảo không chồng chéo request nếu mạng bị nghẽn
            if (Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0)
            {
                return;
            }

            try
            {
                var packet = _packetProvider();
                if (packet == null)
                {
                    HeartbeatPulse?.Invoke(false, "No telemetry data");
                    return;
                }

                // Gán thông tin trạm nếu chưa có
                if (string.IsNullOrEmpty(packet.NodeName) || packet.NodeName == "UNKNOWN_NODE")
                {
                    packet.NodeName = NodeName;
                }
                if (string.IsNullOrEmpty(packet.NodeType))
                {
                    packet.NodeType = NodeType;
                }
                packet.TimestampUtc = DateTime.UtcNow;

                string targetUrl = ServerUrl.TrimEnd('/');
                if (!targetUrl.EndsWith("/api/telemetry", StringComparison.OrdinalIgnoreCase))
                {
                    targetUrl += "/api/telemetry";
                }

                string json = JsonSerializer.Serialize(packet, JsonOptions);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var response = await SharedHttpClient.PostAsync(targetUrl, content).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    HeartbeatPulse?.Invoke(true, null);
                }
                else
                {
                    HeartbeatPulse?.Invoke(false, $"HTTP {(int)response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                // Bắt mọi ngoại lệ (Connection refused, timeout...) tuyệt đối không làm ảnh hưởng app
                HeartbeatPulse?.Invoke(false, ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _inFlight, 0);
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
