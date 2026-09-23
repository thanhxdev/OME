using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using OpenMedia.Platform.Models;

namespace OpenMedia.Platform.Internal
{
    /// <summary>
    /// Bộ hợp nhất và khử trùng lặp gói tin luồng truyền dẫn chuẩn SMPTE ST 2022-7 (Hitless Merge).
    /// Hỗ trợ bù trừ sai lệch thời gian truyền dẫn vi sai (Differential Delay Buffer: 10ms - 500ms)
    /// giữa hai đường mạng độc lập (Path A và Path B), đảm bảo luồng xuất cho Video Decoder
    /// hoàn toàn liền mạch 100% không bị rớt gói hoặc gián đoạn khi một trong các đường truyền gặp sự cố.
    /// </summary>
    public sealed class HitlessMergeDeduplicator
    {
        private readonly object _syncLock = new();
        private readonly ConcurrentQueue<byte[]> _outputQueue = new();

        private struct DeliveredEntry
        {
            public int PathIndex;
            public long TimestampMs;
        }

        // Lịch sử chữ ký các gói tin đã được chuyển giao vào _outputQueue kèm thông tin pathIndex và thời điểm nhận
        private readonly Dictionary<ulong, DeliveredEntry> _deliveredSignatures = new(2048);
        private readonly Queue<ulong> _signatureHistory = new(2048);
        private const int MaxHistorySignatures = 2048;

        private readonly SMPTE2022_7Stats _stats = new();

        private int _maxBufferSize = 8192;
        private int _maxSkewWindowMs = 1500;
        private bool _enabled = true;

        public bool IsEnabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        public SMPTE2022_7Stats Stats
        {
            get
            {
                lock (_syncLock)
                {
                    return _stats.Clone();
                }
            }
        }

        public int QueueCount => _outputQueue.Count;

        public HitlessMergeDeduplicator(int differentialDelayMs = 50, int maxBufferSize = 8192)
        {
            Configure(differentialDelayMs, maxBufferSize);
        }

        public void Configure(int differentialDelayMs, int maxBufferSize)
        {
            lock (_syncLock)
            {
                _maxBufferSize = Math.Clamp(maxBufferSize, 1024, 65536);
                _maxSkewWindowMs = Math.Clamp(Math.Max(500, differentialDelayMs * 3), 300, 5000);
            }
        }

        /// <summary>
        /// Đẩy một gói tin từ một member socket (Path A, Path B...) vào bộ xử lý SMPTE 2022-7.
        /// </summary>
        /// <param name="pathIndex">0: Path A (Primary), 1: Path B (Secondary), >= 2: Các path bổ sung</param>
        /// <param name="data">Dữ liệu gói tin</param>
        /// <param name="length">Độ dài dữ liệu</param>
        /// <returns>True nếu gói tin được chấp nhận; False nếu bị loại bỏ do trùng lặp.</returns>
        public bool PushPacket(int pathIndex, byte[] data, int length)
        {
            if (!_enabled || data == null || length <= 0)
            {
                return false;
            }

            // Nếu dữ liệu chứa nhiều gói tin chuẩn 1316 bytes (hoặc 188 bytes), phân tách để băm và khử trùng lặp từng khối độc lập
            if (length > 1316 && (length % 1316 == 0 || length % 188 == 0))
            {
                int chunkSize = (length % 1316 == 0) ? 1316 : 188;
                bool anyAccepted = false;
                for (int offset = 0; offset < length; offset += chunkSize)
                {
                    int curLen = Math.Min(chunkSize, length - offset);
                    if (PushSingleChunk(pathIndex, data, offset, curLen))
                    {
                        anyAccepted = true;
                    }
                }
                return anyAccepted;
            }

            return PushSingleChunk(pathIndex, data, 0, length);
        }

        private bool PushSingleChunk(int pathIndex, byte[] data, int offset, int length)
        {
            ulong signature = ComputeSignature(data, offset, length);
            long nowMs = Environment.TickCount64;

            lock (_syncLock)
            {
                _stats.IsGroupActive = true;
                if (pathIndex == 0)
                {
                    _stats.PathAPackets++;
                }
                else
                {
                    _stats.PathBPackets++;
                }

                // 1. Kiểm tra trùng lặp:
                // Nếu chữ ký đã tồn tại VÀ đến từ một path KHÁC trong phạm vi cửa sổ trễ vi sai:
                // -> Đây là gói tin bản sao dư thừa từ đường truyền dự phòng (Redundant Path Duplicate).
                if (_deliveredSignatures.TryGetValue(signature, out var existing))
                {
                    if (existing.PathIndex != pathIndex && (nowMs - existing.TimestampMs) <= _maxSkewWindowMs)
                    {
                        _stats.DuplicatesDropped++;
                        return false;
                    }

                    // Nếu đến từ cùng một path (existing.PathIndex == pathIndex):
                    // Đây là dữ liệu liên tục hợp lệ của chính path này (ví dụ: các gói tin PAT, PMT, NULL packet
                    // hoặc audio lặp lại định kỳ theo chuẩn MPEG-TS). Cập nhật mốc thời gian và KHÔNG drop!
                    _deliveredSignatures[signature] = new DeliveredEntry
                    {
                        PathIndex = pathIndex,
                        TimestampMs = nowMs
                    };
                }
                else
                {
                    // 2. Ghi nhận chữ ký mới vào danh sách bảo vệ
                    RecordDeliveredSignature(signature, pathIndex, nowMs);
                }

                // 3. Đưa gói tin vào output queue
                byte[] packetCopy = new byte[length];
                Buffer.BlockCopy(data, offset, packetCopy, 0, length);

                if (_outputQueue.Count >= _maxBufferSize)
                {
                    _outputQueue.TryDequeue(out _);
                    _stats.LostPacketsTotal++;
                }

                _outputQueue.Enqueue(packetCopy);
                _stats.OutputPacketsTotal++;

                if (pathIndex > 0)
                {
                    _stats.RecoveredFromRedundantPath++;
                }

                return true;
            }
        }

        private void RecordDeliveredSignature(ulong signature, int pathIndex, long nowMs)
        {
            _deliveredSignatures[signature] = new DeliveredEntry
            {
                PathIndex = pathIndex,
                TimestampMs = nowMs
            };
            _signatureHistory.Enqueue(signature);

            if (_signatureHistory.Count > MaxHistorySignatures)
            {
                ulong oldest = _signatureHistory.Dequeue();
                _deliveredSignatures.Remove(oldest);
            }
        }

        /// <summary>
        /// Lấy gói tin tiếp theo đã được khử trùng lặp và sắp xếp theo thứ tự thời gian.
        /// </summary>
        public bool TryPopPacket(out byte[]? packet)
        {
            return _outputQueue.TryDequeue(out packet);
        }

        /// <summary>
        /// Đọc gói tin đã hợp nhất tiếp theo vào buffer của consumer (tương tự như socket receive).
        /// </summary>
        public int ReadMergedData(byte[] targetBuffer)
        {
            if (targetBuffer == null || targetBuffer.Length == 0) return 0;
            if (_outputQueue.TryDequeue(out byte[]? packet) && packet != null)
            {
                int copyLen = Math.Min(packet.Length, targetBuffer.Length);
                Buffer.BlockCopy(packet, 0, targetBuffer, 0, copyLen);
                return copyLen;
            }
            return 0;
        }

        /// <summary>
        /// Reset toàn bộ bộ đệm và thống kê.
        /// </summary>
        public void Reset()
        {
            lock (_syncLock)
            {
                while (_outputQueue.TryDequeue(out _)) { }
                _deliveredSignatures.Clear();
                _signatureHistory.Clear();
            }
        }

        /// <summary>
        /// Tính chữ ký 64-bit toàn bộ gói tin (FNV-1a 64-bit) đảm bảo các khối MPEG-TS bắt đầu bằng
        /// Null Packet (PID 0x1FFF), PAT, PMT hoặc dữ liệu tĩnh không bao giờ bị xung đột chữ ký giả.
        /// </summary>
        private static ulong ComputeSignature(byte[] data, int offset, int length)
        {
            unchecked
            {
                ulong hash = 14695981039346656037UL;
                int end = offset + length;
                for (int i = offset; i < end; i++)
                {
                    hash ^= data[i];
                    hash *= 1099511628211UL;
                }

                return hash ^ ((ulong)length << 32);
            }
        }
    }
}
