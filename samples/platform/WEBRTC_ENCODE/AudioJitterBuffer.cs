using System;

namespace WEBRTC_ENCODE
{
    /// <summary>
    /// Broadcast-grade Elastic Audio Jitter Buffer & Ring Buffer.
    /// Eliminates stuttering/choppiness caused by asynchronous pipe and network I/O bursts.
    /// Provides smooth 48kHz 16-bit stereo PCM delivery (3840 bytes = 20ms @ 48kHz stereo) to WebRTC RTP sender.
    /// </summary>
    public sealed class AudioJitterBuffer
    {
        private readonly byte[] _buffer;
        private readonly int _capacity;
        private readonly object _lock = new();

        private int _writePos = 0;
        private int _readPos = 0;
        private int _available = 0;

        private readonly int _preRollTargetBytes;
        private bool _isPreRolling = true;

        /// <summary>
        /// Capacity in bytes. Default 192,000 bytes = 1.0 second of 48kHz 16-bit stereo PCM (192 KB/s).
        /// PreRoll: 7,680 bytes = 40ms (2 frames @ 20ms) to absorb pipe/network jitter without noticeable delay.
        /// </summary>
        public AudioJitterBuffer(int capacityBytes = 192000, int preRollBytes = 7680)
        {
            _capacity = capacityBytes;
            _buffer = new byte[_capacity];
            _preRollTargetBytes = preRollBytes;
            _isPreRolling = true;
        }

        public int AvailableBytes
        {
            get
            {
                lock (_lock) return _available;
            }
        }

        public bool IsPreRolling
        {
            get
            {
                lock (_lock) return _isPreRolling;
            }
        }

        public void Reset()
        {
            lock (_lock)
            {
                _writePos = 0;
                _readPos = 0;
                _available = 0;
                _isPreRolling = true;
            }
        }

        public void Write(byte[] data, int offset, int count)
        {
            if (data == null || count <= 0) return;

            lock (_lock)
            {
                // If writing more than capacity, only take the last 'capacity' bytes
                if (count > _capacity)
                {
                    offset += count - _capacity;
                    count = _capacity;
                }

                // If buffer would overflow, drop oldest bytes to prevent unbounded latency
                int overflow = (_available + count) - _capacity;
                if (overflow > 0)
                {
                    _readPos = (_readPos + overflow) % _capacity;
                    _available -= overflow;
                }

                // Write into circular ring buffer
                int firstChunk = Math.Min(count, _capacity - _writePos);
                Buffer.BlockCopy(data, offset, _buffer, _writePos, firstChunk);

                int secondChunk = count - firstChunk;
                if (secondChunk > 0)
                {
                    Buffer.BlockCopy(data, offset + firstChunk, _buffer, 0, secondChunk);
                }

                _writePos = (_writePos + count) % _capacity;
                _available += count;

                // Check if pre-roll requirement is satisfied
                if (_isPreRolling && _available >= _preRollTargetBytes)
                {
                    _isPreRolling = false;
                }
            }
        }

        /// <summary>
        /// Reads exactly count bytes.
        /// If buffer is pre-rolling or has insufficient data, returns false (and fills dest with silence).
        /// </summary>
        public bool ReadFrame(byte[] dest, int count)
        {
            if (dest == null || count <= 0) return false;

            lock (_lock)
            {
                if (_isPreRolling || _available < count)
                {
                    Array.Clear(dest, 0, count);
                    return false;
                }

                int firstChunk = Math.Min(count, _capacity - _readPos);
                Buffer.BlockCopy(_buffer, _readPos, dest, 0, firstChunk);

                int secondChunk = count - firstChunk;
                if (secondChunk > 0)
                {
                    Buffer.BlockCopy(_buffer, 0, dest, firstChunk, secondChunk);
                }

                _readPos = (_readPos + count) % _capacity;
                _available -= count;
                return true;
            }
        }
    }
}
