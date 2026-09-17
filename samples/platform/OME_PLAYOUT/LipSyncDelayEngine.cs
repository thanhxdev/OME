using System;
using System.Threading;

namespace OME_PLAYOUT
{
    /// <summary>
    /// Broadcast-grade Lip-Sync & Delay Engine providing millisecond-accurate video and audio delay compensation (0 ms to 2000 ms).
    /// Prevents tearing and frame jitter through dual circular ring buffers (VRAM/RAM).
    /// </summary>
    public sealed class LipSyncDelayEngine : IDisposable
    {
        private const int MaxDelayMs = 2000;
        private const int NominalFps = 60;
        private const int MaxVideoRingFrames = (MaxDelayMs * NominalFps / 1000) + 10; // ~130 frames buffer (~2.16 sec)
        private const int AudioSampleRate = 48000;
        private const int AudioBytesPerSec = AudioSampleRate * 2 * 2; // 48000 * 2ch * 2bytes = 192,000 bytes/sec
        private const int MaxAudioRingBytes = AudioBytesPerSec * 3; // 3 seconds buffer = 576,000 bytes

        private readonly object _videoLock = new();
        private readonly object _audioLock = new();

        // Video Frame Ring Buffer
        private struct VideoFrameSlot
        {
            public byte[] Data;
            public int Width;
            public int Height;
            public long TimestampTicks;
            public long FrameId;
        }

        private readonly VideoFrameSlot[] _videoRing = new VideoFrameSlot[MaxVideoRingFrames];
        private int _videoWriteIndex = 0;
        private long _videoFrameCounter = 0;

        // Audio PCM Ring Buffer
        private readonly byte[] _audioRing = new byte[MaxAudioRingBytes];
        private int _audioWriteIndex = 0;
        private long _totalAudioBytesWritten = 0;

        // Current Delay Parameters
        public double VideoDelayMs { get; set; } = 0.0;
        public double AudioDelayMs { get; set; } = 0.0;
        public bool IsLinked { get; set; } = false;

        public LipSyncDelayEngine()
        {
            for (int i = 0; i < MaxVideoRingFrames; i++)
            {
                _videoRing[i].Data = new byte[1920 * 1080 * 4];
            }
        }

        /// <summary>
        /// Pushes a new incoming video frame into the circular delay ring buffer.
        /// </summary>
        public void PushVideoFrame(byte[] bgraBytes, int width, int height)
        {
            if (bgraBytes == null || bgraBytes.Length < width * height * 4) return;

            lock (_videoLock)
            {
                int nextIndex = (_videoWriteIndex + 1) % MaxVideoRingFrames;
                int frameSize = width * height * 4;

                if (_videoRing[nextIndex].Data.Length < frameSize)
                {
                    _videoRing[nextIndex].Data = new byte[frameSize];
                }

                Buffer.BlockCopy(bgraBytes, 0, _videoRing[nextIndex].Data, 0, frameSize);
                _videoRing[nextIndex].Width = width;
                _videoRing[nextIndex].Height = height;
                _videoRing[nextIndex].TimestampTicks = DateTime.UtcNow.Ticks;
                _videoRing[nextIndex].FrameId = ++_videoFrameCounter;

                _videoWriteIndex = nextIndex;
            }
        }

        /// <summary>
        /// Retrieves the delayed video frame according to <see cref="VideoDelayMs"/>.
        /// </summary>
        public bool TryGetDelayedVideoFrame(out byte[] frameData, out int width, out int height)
        {
            lock (_videoLock)
            {
                frameData = Array.Empty<byte>();
                width = 0;
                height = 0;

                if (_videoFrameCounter == 0) return false;

                if (VideoDelayMs <= 0.5)
                {
                    // Zero delay: immediate pass-through
                    var current = _videoRing[_videoWriteIndex];
                    frameData = current.Data;
                    width = current.Width;
                    height = current.Height;
                    return width > 0 && height > 0;
                }

                // Calculate required frames delay (assuming ~60fps)
                int framesDelay = (int)Math.Round(VideoDelayMs * 60.0 / 1000.0);
                framesDelay = Math.Clamp(framesDelay, 0, MaxVideoRingFrames - 1);

                int readIndex = (_videoWriteIndex - framesDelay + MaxVideoRingFrames) % MaxVideoRingFrames;
                var delayed = _videoRing[readIndex];

                if (delayed.Width > 0 && delayed.Height > 0)
                {
                    frameData = delayed.Data;
                    width = delayed.Width;
                    height = delayed.Height;
                    return true;
                }

                // Fallback to current if ring hasn't filled yet
                var fallback = _videoRing[_videoWriteIndex];
                frameData = fallback.Data;
                width = fallback.Width;
                height = fallback.Height;
                return width > 0 && height > 0;
            }
        }

        /// <summary>
        /// Pushes newly received PCM 48kHz Stereo audio bytes into the circular delay buffer.
        /// </summary>
        public void PushAudioPcm(byte[] pcmBytes, int length)
        {
            if (pcmBytes == null || length <= 0) return;

            lock (_audioLock)
            {
                int toWrite = Math.Min(length, MaxAudioRingBytes);
                int firstChunk = Math.Min(toWrite, MaxAudioRingBytes - _audioWriteIndex);
                Buffer.BlockCopy(pcmBytes, 0, _audioRing, _audioWriteIndex, firstChunk);

                if (firstChunk < toWrite)
                {
                    int secondChunk = toWrite - firstChunk;
                    Buffer.BlockCopy(pcmBytes, firstChunk, _audioRing, 0, secondChunk);
                    _audioWriteIndex = secondChunk;
                }
                else
                {
                    _audioWriteIndex = (_audioWriteIndex + firstChunk) % MaxAudioRingBytes;
                }

                _totalAudioBytesWritten += toWrite;
            }
        }

        /// <summary>
        /// Retrieves the delayed audio chunk matching the requested length.
        /// </summary>
        public bool TryGetDelayedAudioPcm(byte[] destBuffer, int length)
        {
            if (destBuffer == null || length <= 0) return false;

            lock (_audioLock)
            {
                if (_totalAudioBytesWritten < length) return false;

                if (AudioDelayMs <= 0.5)
                {
                    // Zero delay: take latest written chunk
                    int readIndex = (_audioWriteIndex - length + MaxAudioRingBytes) % MaxAudioRingBytes;
                    CopyFromRing(readIndex, destBuffer, length);
                    return true;
                }

                // Calculate audio bytes delay: ms * (bytesPerSec / 1000)
                int bytesDelay = (int)Math.Round(AudioDelayMs * (AudioBytesPerSec / 1000.0));
                // Align to 4-byte sample frame boundary (16-bit stereo = 4 bytes)
                bytesDelay = (bytesDelay / 4) * 4;
                bytesDelay = Math.Clamp(bytesDelay, 0, MaxAudioRingBytes - length - 4);

                int delayedIndex = (_audioWriteIndex - bytesDelay - length + MaxAudioRingBytes) % MaxAudioRingBytes;
                CopyFromRing(delayedIndex, destBuffer, length);
                return true;
            }
        }

        private void CopyFromRing(int startIndex, byte[] dest, int length)
        {
            int firstChunk = Math.Min(length, MaxAudioRingBytes - startIndex);
            Buffer.BlockCopy(_audioRing, startIndex, dest, 0, firstChunk);

            if (firstChunk < length)
            {
                int secondChunk = length - firstChunk;
                Buffer.BlockCopy(_audioRing, 0, dest, firstChunk, secondChunk);
            }
        }

        public void SetLinkedDelay(double delayMs)
        {
            VideoDelayMs = delayMs;
            AudioDelayMs = delayMs;
        }

        public void Reset()
        {
            lock (_videoLock)
            {
                _videoWriteIndex = 0;
                _videoFrameCounter = 0;
            }
            lock (_audioLock)
            {
                _audioWriteIndex = 0;
                _totalAudioBytesWritten = 0;
                Array.Clear(_audioRing, 0, _audioRing.Length);
            }
        }

        public void Dispose()
        {
            Reset();
        }
    }
}
