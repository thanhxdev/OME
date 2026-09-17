using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace SRT_DECODE
{
    /// <summary>
    /// Gói tin khung hình video kèm siêu dữ liệu thời gian phục vụ đồng bộ pha phát hình.
    /// </summary>
    public sealed class SynchronizedVideoFrame
    {
        public int ChannelIndex { get; set; }
        public byte[] FrameBytes { get; set; } = Array.Empty<byte>();
        public int Width { get; set; }
        public int Height { get; set; }
        public DateTime OriginUtcTime { get; set; }
        public DateTime IngestUtcTime { get; set; }
        public DateTime ScheduledPlayoutUtc { get; set; }
        public double RttMs { get; set; }

        // ─── Media PTS & Duration Metadata (Medialooks-Grade) ─────────────
        public long MediaPts { get; set; }
        public long MediaDuration { get; set; }
        public int TimebaseDen { get; set; } = 90000;
        public double MediaPtsSeconds => TimebaseDen > 0 ? (double)MediaPts / TimebaseDen : 0.0;
        public double FrameDurationMs => TimebaseDen > 0 && MediaDuration > 0 ? (MediaDuration * 1000.0) / TimebaseDen : 16.68;
    }

    /// <summary>
    /// Bộ đệm đồng bộ pha khung hình video đa camera chuẩn truyền hình (Broadcast Playout Alignment Engine).
    /// Khi bật Multi-Camera Master Synchronization, engine sẽ giữ các khung hình trong hàng đợi jitter
    /// theo cửa sổ trễ chung (TargetSyncWindowMs) để đảm bảo mọi camera xuất hình chính xác tại cùng một thời khắc UTC.
    /// </summary>
    public sealed class VideoPlayoutAlignmentEngine : IDisposable
    {
        public const int MaxChannels = 10;
        public const int MaxQueuePerChannel = 60; // Tối đa 60 frames (~1-2 giây buffer)

        private readonly ConcurrentQueue<SynchronizedVideoFrame>[] _channelQueues = new ConcurrentQueue<SynchronizedVideoFrame>[MaxChannels];
        private readonly NtpSyncEngine _syncEngine;
        private readonly MasterClockProvider _masterClock = MasterClockProvider.Instance;

        [DllImport("winmm.dll")]
        private static extern uint timeBeginPeriod(uint uMilliseconds);

        [DllImport("winmm.dll")]
        private static extern uint timeEndPeriod(uint uMilliseconds);

        private readonly double[] _channelNextPlayoutMs = new double[MaxChannels];
        private readonly double[] _channelNominalDurationMs = new double[MaxChannels];
        private readonly Stopwatch _playoutClock = Stopwatch.StartNew();

        private bool _isEnabled = false;
        private int _targetSyncWindowMs = 350;
        private Thread? _playoutThread;
        private CancellationTokenSource? _cts;
        private bool _isRunning = false;
        private bool _isDisposed = false;

        // Lưu frame gần nhất để lặp lại khi mạng bị nghẽn (repeat/freeze frame) tránh giật đen
        private readonly SynchronizedVideoFrame?[] _lastDispatchedFrames = new SynchronizedVideoFrame?[MaxChannels];

        /// <summary>
        /// Bắn ra khi một khung hình đã đến đúng thời điểm xuất xưởng đồng bộ UTC.
        /// Arguments: (channelIndex, frameBytes, width, height)
        /// </summary>
        public event Action<int, byte[], int, int>? FrameReadyForPlayout;

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled == value) return;
                _isEnabled = value;
                FlushAllQueues();
            }
        }

        public int TargetSyncWindowMs
        {
            get => _targetSyncWindowMs;
            set => _targetSyncWindowMs = Math.Clamp(value, 50, 2000);
        }

        public VideoPlayoutAlignmentEngine(NtpSyncEngine syncEngine)
        {
            _syncEngine = syncEngine ?? throw new ArgumentNullException(nameof(syncEngine));
            for (int i = 0; i < MaxChannels; i++)
            {
                _channelQueues[i] = new ConcurrentQueue<SynchronizedVideoFrame>();
                _channelNominalDurationMs[i] = 40.0; // Mặc định chuẩn 25 FPS
            }

            StartPlayoutLoop();
        }

        private void StartPlayoutLoop()
        {
            try { timeBeginPeriod(1); } catch { }
            _cts = new CancellationTokenSource();
            _isRunning = true;
            _playoutThread = new Thread(PlayoutWorkerLoop)
            {
                Name = "BroadcastVideoPlayoutSyncThread",
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal
            };
            _playoutThread.Start();
        }

        /// <summary>
        /// Nạp khung hình video giải mã mới kèm mốc thời gian PTS 90kHz vào bộ đệm đồng bộ.
        /// </summary>
        public void EnqueueFrameWithPts(int channelIndex, byte[] frameBytes, int width, int height, long pts, long duration, double rttMs)
        {
            if (_isDisposed || channelIndex < 0 || channelIndex >= MaxChannels || frameBytes == null) return;

            DateTime nowUtc = _masterClock.CurrentUtcTime;

            // Tính toán thời lượng khung hình danh định (default 40.0ms cho 25 FPS)
            double frameDurationMs = (duration > 0 && duration <= 90000)
                ? (duration * 1000.0) / 90000.0
                : 40.0;
            if (frameDurationMs < 5.0 || frameDurationMs > 100.0) frameDurationMs = 40.0;

            _channelNominalDurationMs[channelIndex] = frameDurationMs;

            // ═══ SỬA: Dùng PTS delta thay vì RTT estimation ═══
            // Giữ originUtc = nowUtc (thời điểm nhận frame thực tế)
            // Scheduled playout = dựa trên PTS spacing, KHÔNG dựa trên RTT biến động
            DateTime originUtc = nowUtc;

            var packet = new SynchronizedVideoFrame
            {
                ChannelIndex = channelIndex,
                FrameBytes = frameBytes,
                Width = width,
                Height = height,
                OriginUtcTime = originUtc,
                IngestUtcTime = nowUtc,
                ScheduledPlayoutUtc = originUtc, // Không dùng nữa, playout clock quyết định
                RttMs = rttMs,
                MediaPts = pts,
                MediaDuration = duration > 0 ? duration : (long)(frameDurationMs * 90),
                TimebaseDen = 90000
            };

            var queue = _channelQueues[channelIndex];

            // Khởi tạo mốc xuất xưởng nếu chưa có mốc phát (cold start) hoặc bị nghẽn gián đoạn mạng thực sự (> 2.5 frames).
            // TUYỆT ĐỐI KHÔNG dùng queue.IsEmpty: trong nhịp 25fps bình thường hàng đợi thường xuyên về 0 giữa 2 frame,
            // nếu reset mốc phát mỗi khi queue rỗng sẽ làm hình ảnh bị phạt hoãn +80ms lặp đi lặp lại (chạy-dừng chu kỳ).
            double currentClockMs = _playoutClock.Elapsed.TotalMilliseconds;
            bool isStarved = _channelNextPlayoutMs[channelIndex] <= 0 ||
                             (currentClockMs - _channelNextPlayoutMs[channelIndex] > frameDurationMs * 2.5);

            if (isStarved)
            {
                double targetBufferMs = _isEnabled ? _targetSyncWindowMs : Math.Max(60.0, frameDurationMs * 2.0);
                _channelNextPlayoutMs[channelIndex] = currentClockMs + targetBufferMs;
            }

            // Nếu hàng đợi bị dồn ứ quá giới hạn cho phép, loại bỏ bớt frame cũ nhất
            while (queue.Count >= MaxQueuePerChannel && queue.TryDequeue(out _))
            {
                _syncEngine.RegisterDroppedFrame(channelIndex);
            }

            queue.Enqueue(packet);
        }

        /// <summary>
        /// Nạp khung hình video giải mã mới vào bộ đệm đồng bộ (Tương thích ngược).
        /// </summary>
        public void EnqueueFrame(int channelIndex, byte[] frameBytes, int width, int height, double rttMs)
        {
            EnqueueFrameWithPts(channelIndex, frameBytes, width, height, 0, 3600, rttMs);
        }

        /// <summary>
        /// Vòng lặp xuất hình độ chính xác cao điều phối thời gian phát của 10 camera (Isochronous Playout Clock).
        /// </summary>
        private void PlayoutWorkerLoop()
        {
            var token = _cts?.Token ?? CancellationToken.None;

            while (!token.IsCancellationRequested && _isRunning)
            {
                try
                {
                    double currentMs = _playoutClock.Elapsed.TotalMilliseconds;
                    DateTime nowUtc = _masterClock.CurrentUtcTime;

                    for (int ch = 0; ch < MaxChannels; ch++)
                    {
                        var queue = _channelQueues[ch];
                        if (queue.IsEmpty) continue;

                        double nextDueMs = _channelNextPlayoutMs[ch];
                        if (nextDueMs <= 0)
                        {
                            _channelNextPlayoutMs[ch] = currentMs;
                            nextDueMs = currentMs;
                        }

                        // Kiểm tra xem đã đến thời điểm phát khung hình tiếp theo chưa
                        if (currentMs >= nextDueMs)
                        {
                            if (queue.TryDequeue(out var frameToDispatch))
                            {
                                double frameDur = frameToDispatch.FrameDurationMs;
                                if (frameDur < 5.0 || frameDur > 100.0)
                                {
                                    frameDur = _channelNominalDurationMs[ch] > 5.0 ? _channelNominalDurationMs[ch] : 40.0;
                                }

                                // Bước tới nhịp tiếp theo đúng chu kỳ đẳng thời
                                _channelNextPlayoutMs[ch] += frameDur;

                                // Nếu đồng hồ bị trễ quá xa (ví dụ máy bị sleep hoặc lag luồng), căn chỉnh lại mốc hiện tại
                                if (currentMs - _channelNextPlayoutMs[ch] > 150.0)
                                {
                                    _channelNextPlayoutMs[ch] = currentMs;
                                }

                                // ═══ SỬA: Smooth progressive drop ═══
                                int targetFrames = _isEnabled ? Math.Max(2, (int)Math.Round(_targetSyncWindowMs / frameDur)) : 2;
                                int queueDepth = queue.Count;

                                if (queueDepth > targetFrames + 6)
                                {
                                    // Queue overflow nghiêm trọng: drop 1 frame + tăng tốc mạnh
                                    if (queue.TryDequeue(out var staleFrame))
                                    {
                                        _syncEngine.RegisterDroppedFrame(ch);
                                        frameToDispatch = staleFrame; // Hiển thị frame mới nhất bị drop
                                    }
                                    _channelNextPlayoutMs[ch] -= frameDur * 0.3; // Tăng tốc 30%
                                }
                                else if (queueDepth > targetFrames + 3)
                                {
                                    // Queue dồn ứ nhẹ: tăng tốc 15%
                                    _channelNextPlayoutMs[ch] -= frameDur * 0.15;
                                }
                                else if (queueDepth > targetFrames)
                                {
                                    // Hơi nhanh: tăng tốc nhẹ 1ms
                                    _channelNextPlayoutMs[ch] -= 1.0;
                                }
                                else if (queueDepth < targetFrames - 1)
                                {
                                    // Hơi chậm: giảm tốc nhẹ 1ms
                                    _channelNextPlayoutMs[ch] += 1.0;
                                }

                                _lastDispatchedFrames[ch] = frameToDispatch;

                                if (_isEnabled)
                                {
                                    double actualLatency = (nowUtc - frameToDispatch.OriginUtcTime).TotalMilliseconds;
                                    double drift = actualLatency - _targetSyncWindowMs;
                                    double bufferFill = Math.Clamp((queue.Count / (double)targetFrames) * 100.0, 0.0, 150.0);
                                    _syncEngine.UpdateChannelSyncMetrics(ch, frameToDispatch.OriginUtcTime, actualLatency, drift, bufferFill, frameToDispatch.MediaPts);
                                }

                                FrameReadyForPlayout?.Invoke(ch, frameToDispatch.FrameBytes, frameToDispatch.Width, frameToDispatch.Height);
                            }
                        }
                    }

                    // Tần số kiểm tra độ phân giải cao 1ms (timeBeginPeriod(1))
                    Thread.Sleep(1);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[PlayoutSyncEngine] Loop Error: {ex.Message}");
                    Thread.Sleep(5);
                }
            }
        }

        public void FlushAllQueues()
        {
            for (int i = 0; i < MaxChannels; i++)
            {
                _channelNextPlayoutMs[i] = 0;
                while (_channelQueues[i].TryDequeue(out var frame))
                {
                    FrameReadyForPlayout?.Invoke(frame.ChannelIndex, frame.FrameBytes, frame.Width, frame.Height);
                }
            }
        }

        /// <summary>
        /// Clears all queued frames and resets playout timing for the specified channel.
        /// Call when a channel disconnects or is stopped to avoid stale video freeze.
        /// </summary>
        public void ClearChannel(int channelIndex)
        {
            if (channelIndex < 0 || channelIndex >= MaxChannels) return;
            _channelNextPlayoutMs[channelIndex] = 0;
            _lastDispatchedFrames[channelIndex] = null;
            while (_channelQueues[channelIndex].TryDequeue(out _)) { }
        }

        public int GetQueueDepth(int channelIndex)
        {
            if (channelIndex < 0 || channelIndex >= MaxChannels) return 0;
            return _channelQueues[channelIndex].Count;
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _isRunning = false;

            try
            {
                _cts?.Cancel();
                _playoutThread?.Join(200);
                _cts?.Dispose();
                timeEndPeriod(1);
            }
            catch { }

            FlushAllQueues();
        }
    }
}
