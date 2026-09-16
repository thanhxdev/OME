using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace SRT_ENCODE
{
    /// <summary>
    /// Cung cấp đồng hồ UTC Master chuẩn độ chính xác cao (Microsecond Precision).
    /// Neo mốc thời gian vào máy chủ NTP qua giao thức SNTP RFC 5905 và nội suy
    /// bằng bộ đếm hiệu năng cao của phần cứng CPU (QueryPerformanceCounter / Stopwatch.GetTimestamp).
    /// Loại bỏ triệt để độ trễ bước nhảy (15.6ms quantum jitter) của Windows OS Clock.
    /// </summary>
    public sealed class MasterClockProvider : IDisposable
    {
        private static readonly Lazy<MasterClockProvider> _instance = new(() => new MasterClockProvider());
        public static MasterClockProvider Instance => _instance.Value;

        private readonly object _syncLock = new();
        private DateTime _baseUtcTime = DateTime.UtcNow;
        private long _baseStopwatchTicks = Stopwatch.GetTimestamp();
        private bool _isSynced = false;
        private Timer? _autoSyncTimer;
        private bool _disposed = false;

        public bool IsSynced => _isSynced;
        public double LastOffsetMs { get; private set; } = 0.0;
        public double LastRttMs { get; private set; } = 0.0;
        public int Stratum { get; private set; } = 0;
        public string CurrentServer { get; private set; } = "time.google.com";

        public event Action<NtpSyncResult>? SyncStatusChanged;

        public MasterClockProvider()
        {
        }

        /// <summary>
        /// Lấy thời gian UTC hiện tại với độ chính xác cao được nội suy từ QPC.
        /// </summary>
        public DateTime CurrentUtcTime
        {
            get
            {
                lock (_syncLock)
                {
                    if (!_isSynced)
                    {
                        return DateTime.UtcNow;
                    }

                    long elapsedTicks = Stopwatch.GetTimestamp() - _baseStopwatchTicks;
                    double elapsedSeconds = (double)elapsedTicks / Stopwatch.Frequency;
                    return _baseUtcTime.AddSeconds(elapsedSeconds);
                }
            }
        }

        /// <summary>
        /// Khởi tạo và đồng bộ với máy chủ NTP.
        /// </summary>
        public async Task<NtpSyncResult> SyncWithServerAsync(string server = "time.google.com", int timeoutMs = 3000)
        {
            if (string.IsNullOrWhiteSpace(server)) server = "time.google.com";
            CurrentServer = server;

            var result = await NtpClient.QueryTimeAsync(server, timeoutMs).ConfigureAwait(false);
            if (result.Success)
            {
                ApplyNtpResult(result);
            }
            else
            {
                SyncStatusChanged?.Invoke(result);
            }

            return result;
        }

        /// <summary>
        /// Kích hoạt cơ chế tự động đồng bộ định kỳ chạy ngầm (mặc định mỗi 30s) để giữ nhịp không trôi dạt.
        /// </summary>
        public void StartPeriodicSync(string server = "time.google.com", int intervalSeconds = 30)
        {
            CurrentServer = string.IsNullOrWhiteSpace(server) ? "time.google.com" : server;
            _autoSyncTimer?.Dispose();
            _autoSyncTimer = new Timer(async _ =>
            {
                try
                {
                    await SyncWithServerAsync(CurrentServer, 3000).ConfigureAwait(false);
                }
                catch { }
            }, null, 500, intervalSeconds * 1000);
        }

        public void StopPeriodicSync()
        {
            _autoSyncTimer?.Dispose();
            _autoSyncTimer = null;
        }

        /// <summary>
        /// Nạp kết quả NTP trực tiếp để neo mốc QPC.
        /// </summary>
        public void ApplyNtpResult(NtpSyncResult result)
        {
            if (result == null || !result.Success) return;

            lock (_syncLock)
            {
                _baseUtcTime = result.ServerUtcTime;
                _baseStopwatchTicks = result.ReceiveStopwatchTicks > 0 ? result.ReceiveStopwatchTicks : Stopwatch.GetTimestamp();
                LastOffsetMs = result.OffsetMs;
                LastRttMs = result.RoundTripDelayMs;
                Stratum = result.Stratum;
                _isSynced = true;
            }

            SyncStatusChanged?.Invoke(result);
        }

        /// <summary>
        /// Định dạng chuỗi UTC chuẩn mili-giây: HH:mm:ss.fff
        /// </summary>
        public string GetFormattedUtc()
        {
            return CurrentUtcTime.ToString("HH:mm:ss.fff");
        }

        /// <summary>
        /// Định dạng SMPTE Broadcast Timecode chuẩn: HH:mm:ss:FF
        /// (FF là frame index từ 00 đến fps-1, ví dụ 00-24 với 25fps)
        /// Giúp mắt thường nhận diện sự đồng bộ từng khung hình tuyệt đối giữa 2 màn hình.
        /// </summary>
        public string GetFormattedSmpteTimecode(int fps = 25)
        {
            DateTime time = CurrentUtcTime;
            int frame = (int)((time.Millisecond / 1000.0) * fps);
            if (frame >= fps) frame = fps - 1;
            return $"{time:HH\\:mm\\:ss}:{frame:D2}";
        }

        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod", SetLastError = true)]
        private static extern uint TimeBeginPeriod(uint uMilliseconds);

        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod", SetLastError = true)]
        private static extern uint TimeEndPeriod(uint uMilliseconds);

        static MasterClockProvider()
        {
            try
            {
                // Kích hoạt độ phân giải hẹn giờ 1ms cho toàn bộ hệ thống Windows
                TimeBeginPeriod(1);
            }
            catch { }
        }

        /// <summary>
        /// Chờ đến đúng mốc thời gian đích với độ chính xác cao (Sub-millisecond Broadcast Precision),
        /// kết hợp giữa Task.Delay thô (1ms OS quantum) và Thread.SpinWait tinh vi nhằm triệt tiêu micro-jitter.
        /// </summary>
        public static async Task PreciseWaitUntilAsync(Stopwatch stopwatch, double targetTimeMs, CancellationToken token = default)
        {
            while (!token.IsCancellationRequested)
            {
                double remainingMs = targetTimeMs - stopwatch.Elapsed.TotalMilliseconds;
                if (remainingMs <= 0.05)
                {
                    break;
                }

                if (remainingMs > 2.0)
                {
                    // Với timeBeginPeriod(1), Task.Delay(1) thức dậy trong khoảng 1-2ms
                    await Task.Delay(1, token).ConfigureAwait(false);
                }
                else if (remainingMs > 0.5)
                {
                    Thread.Yield();
                }
                else
                {
                    // Đạt độ chính xác vi mô trong 0.5ms cuối
                    Thread.SpinWait(100);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _autoSyncTimer?.Dispose();
            try
            {
                TimeEndPeriod(1);
            }
            catch { }
        }
    }
}
