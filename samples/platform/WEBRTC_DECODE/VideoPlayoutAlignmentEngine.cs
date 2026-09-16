using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace WEBRTC_DECODE
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
                if (!_isEnabled)
                {
                    // Khi tắt sync, giải phóng ngay toàn bộ frame đang đệm để xuất tức thì
                    FlushAllQueues();
                }
            }
        }

        private int _videoLipSyncDelayMs = 0;
        public int VideoLipSyncDelayMs
        {
            get => _videoLipSyncDelayMs;
            set
            {
                if (_videoLipSyncDelayMs == value) return;
                _videoLipSyncDelayMs = Math.Clamp(value, 0, 300);
                if (_videoLipSyncDelayMs == 0 && !_isEnabled)
                {
                    FlushAllQueues();
                }
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
            }

            StartPlayoutLoop();
        }

        private void StartPlayoutLoop()
        {
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
        /// Nạp khung hình video giải mã mới vào bộ đệm đồng bộ.
        /// </summary>
        public void EnqueueFrame(int channelIndex, byte[] frameBytes, int width, int height, double rttMs)
        {
            if (_isDisposed || channelIndex < 0 || channelIndex >= MaxChannels || frameBytes == null) return;

            // Nếu không bật Master Sync và không có Video Lip-Sync Delay: Xuất hình trực tiếp (Free-Run Passthrough)
            if (!_isEnabled && _videoLipSyncDelayMs == 0)
            {
                FrameReadyForPlayout?.Invoke(channelIndex, frameBytes, width, height);
                return;
            }

            DateTime nowUtc = _masterClock.CurrentUtcTime;

            DateTime originUtc;
            DateTime scheduledPlayoutUtc;

            if (_isEnabled)
            {
                // Ước lượng độ trễ truyền dẫn mạng từ SRT RTT (1 chiều = RTT / 2, tối thiểu 30ms)
                double transitLatencyMs = Math.Max(30.0, rttMs / 2.0);
                originUtc = nowUtc.AddMilliseconds(-transitLatencyMs);
                scheduledPlayoutUtc = originUtc.AddMilliseconds(_targetSyncWindowMs);
            }
            else
            {
                // Bù trễ video cho Lip-Sync khi Audio chạy chậm hơn
                originUtc = nowUtc;
                scheduledPlayoutUtc = nowUtc.AddMilliseconds(_videoLipSyncDelayMs);
            }

            var packet = new SynchronizedVideoFrame
            {
                ChannelIndex = channelIndex,
                FrameBytes = frameBytes,
                Width = width,
                Height = height,
                OriginUtcTime = originUtc,
                IngestUtcTime = nowUtc,
                ScheduledPlayoutUtc = scheduledPlayoutUtc,
                RttMs = rttMs
            };

            var queue = _channelQueues[channelIndex];

            // Nếu hàng đợi bị dồn ứ quá giới hạn cho phép (camera bị lag dồn nén), bỏ bớt frame cũ
            while (queue.Count >= MaxQueuePerChannel && queue.TryDequeue(out _))
            {
                _syncEngine.RegisterDroppedFrame(channelIndex);
            }

            queue.Enqueue(packet);
        }

        /// <summary>
        /// Vòng lặp xuất hình độ chính xác cao điều phối thời gian phát của 10 camera.
        /// </summary>
        private void PlayoutWorkerLoop()
        {
            var token = _cts?.Token ?? CancellationToken.None;

            while (!token.IsCancellationRequested && _isRunning)
            {
                try
                {
                    if (!_isEnabled && _videoLipSyncDelayMs == 0)
                    {
                        Thread.Sleep(10);
                        continue;
                    }

                    DateTime nowUtc = _masterClock.CurrentUtcTime;

                    for (int ch = 0; ch < MaxChannels; ch++)
                    {
                        var queue = _channelQueues[ch];
                        if (queue.IsEmpty) continue;

                        if (queue.TryPeek(out var headFrame))
                        {
                            // Kiểm tra xem đã đến mốc thời gian xuất hình của frame này chưa
                            if (headFrame.ScheduledPlayoutUtc <= nowUtc)
                            {
                                SynchronizedVideoFrame? frameToDispatch = null;

                                // Lấy frame ra
                                if (queue.TryDequeue(out var dequeuedFrame))
                                {
                                    frameToDispatch = dequeuedFrame;

                                    // Smooth Catch-up: Nếu có nhiều frame quá hạn nằm phía sau (trễ > 60ms),
                                    // drop các frame trung gian cũ để bắt kịp thời gian thực
                                    while (queue.TryPeek(out var nextFrame) && nextFrame.ScheduledPlayoutUtc <= nowUtc.AddMilliseconds(-40))
                                    {
                                        if (queue.TryDequeue(out var staleFrame))
                                        {
                                            _syncEngine.RegisterDroppedFrame(ch);
                                            frameToDispatch = staleFrame; // Chọn frame mới nhất
                                        }
                                    }
                                }

                                if (frameToDispatch != null)
                                {
                                    _lastDispatchedFrames[ch] = frameToDispatch;

                                    if (_isEnabled)
                                    {
                                        // Tính độ trễ thực tế và độ lệch pha drift
                                        double actualLatency = (nowUtc - frameToDispatch.OriginUtcTime).TotalMilliseconds;
                                        double drift = actualLatency - _targetSyncWindowMs;

                                        // Cập nhật số liệu đồng bộ vào NtpSyncEngine
                                        double bufferFill = Math.Clamp((queue.Count / (double)Math.Max(1, _targetSyncWindowMs / 16.6)) * 100.0, 0.0, 150.0);
                                        _syncEngine.UpdateChannelSyncMetrics(ch, frameToDispatch.OriginUtcTime, actualLatency, drift, bufferFill);
                                    }

                                    // Bắn tín hiệu xuất hình đồng bộ
                                    FrameReadyForPlayout?.Invoke(ch, frameToDispatch.FrameBytes, frameToDispatch.Width, frameToDispatch.Height);
                                }
                            }
                        }
                    }

                    // Tần số kiểm tra ~200Hz (5ms) để đáp ứng xuất hình 60fps mượt mà
                    Thread.Sleep(5);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[PlayoutSyncEngine] Loop Error: {ex.Message}");
                    Thread.Sleep(10);
                }
            }
        }

        public void FlushAllQueues()
        {
            for (int i = 0; i < MaxChannels; i++)
            {
                while (_channelQueues[i].TryDequeue(out var frame))
                {
                    // Phát ngay các frame đang đọng ra màn hình khi tắt sync
                    FrameReadyForPlayout?.Invoke(frame.ChannelIndex, frame.FrameBytes, frame.Width, frame.Height);
                }
            }
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
            }
            catch { }

            FlushAllQueues();
        }
    }
}
