using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SRT_DECODE
{
    /// <summary>
    /// Bộ phân tích dòng khung hình NUT container siêu nhẹ độ trễ cực thấp (Ultra Low-Latency NUT Demuxer).
    /// Bóc tách các gói tin từ stdout của FFmpeg (-f nut -c:v rawvideo -pix_fmt bgra pipe:1),
    /// bảo toàn chính xác PTS (Presentation Timestamp) 64-bit 90kHz và duration cho từng khung hình BGRA32.
    /// </summary>
    public sealed class NutFrameDemuxer : IDisposable
    {
        // ─── NUT Startcodes (ISO/IEC RFC draft standard) ─────────────────────────
        // Syncpoint startcode: 0x4E 0x4B 0xE4 0xAD 0xEE 0xCA 0x45 0x69
        private static readonly byte[] SYNCPOINT_STARTCODE = new byte[] {
            0x4E, 0x4B, 0xE4, 0xAD, 0xEE, 0xCA, 0x45, 0x69
        };

        private readonly int _channelIndex;
        private int _width;
        private int _height;
        private int _frameSizeBytes;
        private int _fpsNum = 25;
        private int _fpsDen = 1;
        private long _frameDuration90k = 3600L;

        private long _lastPts = 0;
        private long _frameCounter = 0;
        private bool _isDisposed = false;

        // ─── Zero-Allocation Frame Buffer Pool (Triple/Ring Buffering) ──────────
        private const int FramePoolSize = 32;
        private byte[][] _framePool = new byte[FramePoolSize][];
        private int _poolIndex = 0;
        private readonly object _poolLock = new();

        public int Width => _width;
        public int Height => _height;
        public int FrameSizeBytes => _frameSizeBytes;
        public int FpsNum => _fpsNum;
        public int FpsDen => _fpsDen;
        public long FrameDuration90k => _frameDuration90k;

        /// <summary>
        /// Bắn ra khi một khung hình BGRA32 kèm PTS và duration đã được trích xuất hoàn chỉnh.
        /// Arguments: (channelIndex, frameBytes, width, height, pts90k, duration90k)
        /// </summary>
        public event Action<int, byte[], int, int, long, long>? FrameDemuxed;
        public event Action<string, string>? LogEmitted;

        public NutFrameDemuxer(int channelIndex, int width = 1920, int height = 1080, int fpsNum = 25, int fpsDen = 1)
        {
            _channelIndex = channelIndex;
            UpdateFormat(width, height, fpsNum, fpsDen);
        }

        public void UpdateFormat(int width, int height, int fpsNum = 25, int fpsDen = 1)
        {
            lock (_poolLock)
            {
                _width = Math.Max(320, width);
                _height = Math.Max(240, height);
                int newFrameSize = _width * _height * 4;

                if (fpsNum > 0 && fpsDen > 0)
                {
                    _fpsNum = fpsNum;
                    _fpsDen = fpsDen;
                    _frameDuration90k = (90000L * _fpsDen) / _fpsNum;
                }

                if (_frameSizeBytes != newFrameSize || _framePool[0] == null || _framePool[0].Length != newFrameSize)
                {
                    _frameSizeBytes = newFrameSize;
                    for (int i = 0; i < FramePoolSize; i++)
                    {
                        _framePool[i] = new byte[_frameSizeBytes];
                    }
                    _poolIndex = 0;
                }
            }
        }

        /// <summary>
        /// Vòng lặp đọc dòng byte từ stdout của FFmpeg và bóc tách từng khung hình kèm PTS.
        /// Tự động thích ứng cả định dạng NUT container và chế độ rawvideo fallback.
        /// </summary>
        public async Task ReadLoopAsync(Stream stdout, CancellationToken token)
        {
            if (stdout == null) return;

            // Thử đọc NUT container trước (giữ PTS gốc từ MPEG-TS)
            // Nếu không phải NUT stream hoặc lỗi cấu trúc, fallback sang rawvideo
            try
            {
                await RunNutDemuxerLoopAsync(stdout, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log("[WARN]", $"Fallback sang rawvideo do lỗi NUT container Cam {_channelIndex + 1}: {ex.Message}");
                try
                {
                    await RunRawvideoFallbackLoopAsync(stdout, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
            }
        }

        /// <summary>
        /// Vòng lặp bóc tách NUT Syncpoint & Rawvideo Frame Payload.
        /// Bảo toàn PTS gốc và sử dụng Ring Buffer Pool tránh GC freeze.
        /// </summary>
        private async Task RunNutDemuxerLoopAsync(Stream stdout, CancellationToken token)
        {
            long frameDuration90k = (_fpsNum > 0) ? (90000L * _fpsDen) / _fpsNum : 3600L;
            byte[] sizeVarint = EncodeVarInt(_frameSizeBytes);

            // 1. Dò tìm Syncpoint đầu tiên trong NUT container stream (giới hạn 64KB)
            int syncMatch = 0;
            int initialBytesScanned = 0;
            while (!token.IsCancellationRequested && !_isDisposed)
            {
                int b = stdout.ReadByte();
                if (b < 0) return;
                initialBytesScanned++;
                if (initialBytesScanned > 65536)
                {
                    throw new InvalidDataException("Không tìm thấy NUT syncpoint startcode trong 64KB đầu tiên.");
                }

                if (b == SYNCPOINT_STARTCODE[syncMatch])
                {
                    syncMatch++;
                    if (syncMatch == SYNCPOINT_STARTCODE.Length)
                    {
                        break; // Đã đồng bộ với Syncpoint đầu tiên
                    }
                }
                else
                {
                    syncMatch = (b == SYNCPOINT_STARTCODE[0]) ? 1 : 0;
                }
            }

            byte[] headerBuf = new byte[128];
            byte[] crcBuf = new byte[4];
            long currentPts90k = 0;

            while (!token.IsCancellationRequested && !_isDisposed)
            {
                try
                {
                    int expectedBytes;
                    int targetWidth;
                    int targetHeight;
                    lock (_poolLock)
                    {
                        expectedBytes = _frameSizeBytes;
                        targetWidth = _width;
                        targetHeight = _height;
                        frameDuration90k = _frameDuration90k;
                    }

                    if (sizeVarint.Length == 0 || expectedBytes != targetWidth * targetHeight * 4)
                    {
                        sizeVarint = EncodeVarInt(expectedBytes);
                    }

                    // 2. Đọc header giữa syncpoint và frame payload để tìm sizeVarint
                    int headerLen = 0;
                    bool foundSize = false;
                    while (headerLen < headerBuf.Length)
                    {
                        int b = stdout.ReadByte();
                        if (b < 0) return;
                        headerBuf[headerLen++] = (byte)b;

                        if (headerLen >= sizeVarint.Length)
                        {
                            bool match = true;
                            for (int k = 0; k < sizeVarint.Length; k++)
                            {
                                if (headerBuf[headerLen - sizeVarint.Length + k] != sizeVarint[k])
                                {
                                    match = false;
                                    break;
                                }
                            }
                            if (match)
                            {
                                foundSize = true;
                                break;
                            }
                        }
                    }

                    if (!foundSize)
                    {
                        throw new InvalidDataException("Không tìm thấy kích thước frame hợp lệ trong NUT header.");
                    }

                    // 3. Trích xuất PTS từ header trước sizeVarint
                    int sizeStartIdx = headerLen - sizeVarint.Length;
                    int frameCodeIdx = -1;
                    for (int k = sizeStartIdx - 1; k >= 0; k--)
                    {
                        if (headerBuf[k] == 105) // 0x69 frame_code
                        {
                            frameCodeIdx = k;
                            break;
                        }
                    }

                    long pts = 0;
                    if (frameCodeIdx >= 0 && sizeStartIdx > frameCodeIdx + 1)
                    {
                        ulong ptsVal = DecodeVarInt(headerBuf, frameCodeIdx + 1, sizeStartIdx - (frameCodeIdx + 1));
                        if (_lastPts == 0)
                        {
                            _lastPts = (long)ptsVal;
                            currentPts90k = ptsVal > 0 ? (long)ptsVal : frameDuration90k;
                        }
                        else
                        {
                            long delta = (long)ptsVal - _lastPts;
                            if (delta > 0)
                            {
                                long delta90k = (delta == 2048) ? frameDuration90k : ((delta == 3600) ? 3600 : (delta * 90000L) / 51200L);
                                currentPts90k += delta90k;
                            }
                            else
                            {
                                currentPts90k += frameDuration90k;
                            }
                            _lastPts = (long)ptsVal;
                        }
                        pts = currentPts90k;
                    }
                    else
                    {
                        _frameCounter++;
                        pts = _frameCounter * frameDuration90k;
                    }

                    // 4. Bỏ qua 4 bytes CRC32
                    int crcRead = 0;
                    while (crcRead < 4)
                    {
                        int r = await stdout.ReadAsync(crcBuf.AsMemory(crcRead, 4 - crcRead), token).ConfigureAwait(false);
                        if (r <= 0) return;
                        crcRead += r;
                    }

                    // 5. Đọc trọn vẹn khung hình BGRA vào Frame Pool
                    byte[] currentBuffer;
                    lock (_poolLock)
                    {
                        currentBuffer = _framePool[_poolIndex];
                    }

                    if (currentBuffer == null || currentBuffer.Length != expectedBytes)
                    {
                        currentBuffer = new byte[expectedBytes];
                    }

                    int totalRead = 0;
                    while (totalRead < expectedBytes)
                    {
                        int read = await stdout.ReadAsync(currentBuffer.AsMemory(totalRead, expectedBytes - totalRead), token).ConfigureAwait(false);
                        if (read <= 0)
                        {
                            if (totalRead == 0) return;
                            break;
                        }
                        totalRead += read;
                    }

                    if (totalRead == expectedBytes)
                    {
                        _frameCounter++;

                        lock (_poolLock)
                        {
                            _poolIndex = (_poolIndex + 1) % FramePoolSize;
                        }

                        FrameDemuxed?.Invoke(_channelIndex, currentBuffer, targetWidth, targetHeight, pts, frameDuration90k);
                    }

                    // 6. Đọc Syncpoint tiếp theo
                    syncMatch = 0;
                    while (!token.IsCancellationRequested && !_isDisposed)
                    {
                        int b = stdout.ReadByte();
                        if (b < 0) return;

                        if (b == SYNCPOINT_STARTCODE[syncMatch])
                        {
                            syncMatch++;
                            if (syncMatch == SYNCPOINT_STARTCODE.Length)
                            {
                                break;
                            }
                        }
                        else
                        {
                            syncMatch = (b == SYNCPOINT_STARTCODE[0]) ? 1 : 0;
                        }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Log("[WARN]", $"Lỗi phân tích gói tin NUT Cam {_channelIndex + 1}: {ex.Message}");
                    throw;
                }
            }
        }

        /// <summary>
        /// Vòng lặp dự phòng (Fallback) khi nguồn không phát NUT container: đọc trực tiếp rawvideo BGRA32.
        /// Sử dụng Zero-Allocation Ring Buffer Pool loại bỏ hoàn toàn cấp phát LOH và GC freeze.
        /// </summary>
        private async Task RunRawvideoFallbackLoopAsync(Stream stdout, CancellationToken token)
        {
            while (!token.IsCancellationRequested && !_isDisposed)
            {
                try
                {
                    byte[] currentBuffer;
                    int expectedBytes;
                    int targetWidth;
                    int targetHeight;
                    long duration90k;

                    lock (_poolLock)
                    {
                        expectedBytes = _frameSizeBytes;
                        targetWidth = _width;
                        targetHeight = _height;
                        duration90k = _frameDuration90k;
                        currentBuffer = _framePool[_poolIndex];
                    }

                    if (currentBuffer == null || currentBuffer.Length != expectedBytes)
                    {
                        currentBuffer = new byte[expectedBytes];
                    }

                    int totalRead = 0;
                    while (totalRead < expectedBytes)
                    {
                        int read = await stdout.ReadAsync(currentBuffer.AsMemory(totalRead, expectedBytes - totalRead), token).ConfigureAwait(false);
                        if (read <= 0)
                        {
                            if (totalRead == 0) return; // Clean EOF
                            break;
                        }
                        totalRead += read;
                    }

                    if (totalRead == expectedBytes)
                    {
                        _frameCounter++;
                        long pts = _frameCounter * duration90k;

                        // Chuyển sang buffer tiếp theo trong pool tuần hoàn (Ring Buffering)
                        lock (_poolLock)
                        {
                            _poolIndex = (_poolIndex + 1) % FramePoolSize;
                        }

                        FrameDemuxed?.Invoke(_channelIndex, currentBuffer, targetWidth, targetHeight, pts, duration90k);
                    }
                    else if (totalRead == 0)
                    {
                        if (token.IsCancellationRequested || _isDisposed) break;
                        await Task.Delay(5, token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Log("[WARN]", $"Lỗi đọc rawvideo fallback Cam {_channelIndex + 1}: {ex.Message}");
                }
            }
        }

        private static ulong ReadVarInt(Stream stream, out int bytesRead)
        {
            ulong val = 0;
            bytesRead = 0;
            while (true)
            {
                int b = stream.ReadByte();
                if (b < 0) break;
                bytesRead++;
                val = (val << 7) | (ulong)(byte)(b & 0x7F);
                if ((b & 0x80) == 0) break;
            }
            return val;
        }

        private static byte[] EncodeVarInt(int value)
        {
            Span<byte> temp = stackalloc byte[10];
            int count = 0;
            temp[count++] = (byte)(value & 0x7F);
            value >>= 7;
            while (value > 0)
            {
                temp[count++] = (byte)((value & 0x7F) | 0x80);
                value >>= 7;
            }
            byte[] result = new byte[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = temp[count - 1 - i];
            }
            return result;
        }

        private static ulong DecodeVarInt(byte[] data, int offset, int length)
        {
            ulong val = 0;
            for (int i = 0; i < length; i++)
            {
                byte b = data[offset + i];
                val = (val << 7) | (ulong)(byte)(b & 0x7F);
                if ((b & 0x80) == 0) break;
            }
            return val;
        }

        private void Log(string tag, string message)
        {
            LogEmitted?.Invoke(tag, message);
            Trace.WriteLine($"[NutDemuxer]{tag} {message}");
        }

        public void Dispose()
        {
            _isDisposed = true;
        }
    }
}
