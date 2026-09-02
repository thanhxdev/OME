using System;

namespace SRT_DECODE
{
    /// <summary>
    /// Professional Broadcast Jitter Ring Buffer for 48kHz 16-bit stereo PCM audio.
    /// Features:
    /// - Strict 4-byte frame alignment (16-bit stereo)
    /// - Adaptive Jitter Pre-Roll Watermark to prevent ping-pong starvation oscillations
    /// - Hermite S-curve Micro Fade-Out (3.0ms) on buffer underrun/packet stall
    /// - Hermite S-curve Micro Fade-In (3.0ms) on stream resumption
    /// - Boundary sample continuity ramp-down on sudden packet drop (Zero Pop / DC Clicks)
    /// </summary>
    public sealed class AudioRingBuffer
    {
        private const int FrameAlignment = 4; // 2 channels * 2 bytes per sample (16-bit)
        private const int FadeFrames = 144;   // 3.0ms at 48kHz (144 frames * 4 bytes = 576 bytes)

        // Pre-computed Hermite S-curve (3t^2 - 2t^3) lookup tables for zero-CPU, zero-derivative ramping
        private static readonly float[] FadeInLut = new float[FadeFrames];
        private static readonly float[] FadeOutLut = new float[FadeFrames];

        static AudioRingBuffer()
        {
            for (int i = 0; i < FadeFrames; i++)
            {
                float t = (float)i / (FadeFrames - 1);
                float s = t * t * (3.0f - (2.0f * t)); // Zero derivative at t=0 and t=1
                FadeInLut[i] = s;
                FadeOutLut[i] = 1.0f - s;
            }
        }

        private readonly byte[] _buffer;
        private readonly int _capacity;
        private readonly int _preRollBytes;
        private int _readPos;
        private int _writePos;
        private int _count;
        private readonly object _lock = new();

        // Audio DSP concealment state
        private bool _isBuffering = true;
        private bool _wasInSilence = true;
        private short _lastSampleL = 0;
        private short _lastSampleR = 0;

        public int Capacity => _capacity;
        public bool IsBuffering => _isBuffering;

        public int AvailableBytes
        {
            get
            {
                lock (_lock)
                {
                    return _count;
                }
            }
        }

        public AudioRingBuffer(int capacityBytes = 96000, int preRollMs = 50)
        {
            // Ensure capacity is aligned to 4 bytes
            _capacity = capacityBytes - (capacityBytes % FrameAlignment);
            if (_capacity <= 0) _capacity = 96000;
            _buffer = new byte[_capacity];

            // 50ms default pre-roll threshold (48000 * 4 * 0.05 = 9600 bytes)
            int preRoll = (preRollMs * 48000 * FrameAlignment) / 1000;
            preRoll -= (preRoll % FrameAlignment);
            _preRollBytes = Math.Clamp(preRoll, 1920, _capacity / 2); // 10ms to 50% capacity
        }

        /// <summary>
        /// Writes PCM audio bytes into the ring buffer. If writing exceeds capacity,
        /// advances read position to discard oldest frames and prevent audio delay drift.
        /// </summary>
        public void Write(byte[] data, int offset, int count)
        {
            if (data == null || count <= 0) return;

            int alignedCount = count - (count % FrameAlignment);
            if (alignedCount <= 0) return;

            lock (_lock)
            {
                // If incoming chunk is larger than capacity, only take the latest capacity bytes
                if (alignedCount >= _capacity)
                {
                    offset += (alignedCount - _capacity);
                    alignedCount = _capacity;
                    _readPos = 0;
                    _writePos = 0;
                    _count = 0;
                }

                // If buffer would overflow, discard oldest frames
                int overflow = (_count + alignedCount) - _capacity;
                if (overflow > 0)
                {
                    overflow += (FrameAlignment - (overflow % FrameAlignment)) % FrameAlignment;
                    _readPos = (_readPos + overflow) % _capacity;
                    _count -= overflow;
                    if (_count < 0) _count = 0;
                }

                // Write into ring buffer
                int firstChunk = Math.Min(alignedCount, _capacity - _writePos);
                Buffer.BlockCopy(data, offset, _buffer, _writePos, firstChunk);

                int secondChunk = alignedCount - firstChunk;
                if (secondChunk > 0)
                {
                    Buffer.BlockCopy(data, offset + firstChunk, _buffer, 0, secondChunk);
                }

                _writePos = (_writePos + alignedCount) % _capacity;
                _count += alignedCount;
            }
        }

        /// <summary>
        /// Reads PCM audio bytes with built-in Smooth Ramping Audio Concealment.
        /// Automatically handles pre-roll buffering, micro fade-out on underruns,
        /// micro fade-in on recovery, and boundary continuity to eliminate all pops/clicks.
        /// </summary>
        public int Read(byte[] destination, int offset, int count, bool padSilence = true)
        {
            if (destination == null || count <= 0) return 0;

            int alignedCount = count - (count % FrameAlignment);
            if (alignedCount <= 0) return 0;

            int toRead = 0;

            lock (_lock)
            {
                // Jitter Pre-roll Hysteresis: If currently buffering, hold until healthy margin is reached
                if (_isBuffering)
                {
                    if (_count >= _preRollBytes)
                    {
                        _isBuffering = false;
                    }
                }

                if (!_isBuffering)
                {
                    toRead = Math.Min(alignedCount, _count);
                    if (toRead > 0)
                    {
                        toRead -= (toRead % FrameAlignment);
                        int firstChunk = Math.Min(toRead, _capacity - _readPos);
                        Buffer.BlockCopy(_buffer, _readPos, destination, offset, firstChunk);

                        int secondChunk = toRead - firstChunk;
                        if (secondChunk > 0)
                        {
                            Buffer.BlockCopy(_buffer, 0, destination, offset + firstChunk, secondChunk);
                        }

                        _readPos = (_readPos + toRead) % _capacity;
                        _count -= toRead;

                        // If reading drained the buffer completely, enter buffering mode to avoid ping-pong starvation
                        if (_count == 0)
                        {
                            _isBuffering = true;
                        }
                    }
                    else
                    {
                        _isBuffering = true;
                    }
                }
            }

            // ─── AUDIO ERROR CONCEALMENT (PLC) & SMOOTH RAMPING ────────────

            if (toRead >= alignedCount)
            {
                // Healthy continuous playback
                if (_wasInSilence)
                {
                    // Resuming from silence/buffering: Micro Fade-In on the first 3ms (144 frames)
                    ApplyFadeIn(destination, offset, toRead);
                    _wasInSilence = false;
                }

                // Record the last stereo sample of this block for boundary continuity
                int lastFramePos = offset + toRead - FrameAlignment;
                _lastSampleL = BitConverter.ToInt16(destination, lastFramePos);
                _lastSampleR = BitConverter.ToInt16(destination, lastFramePos + 2);

                return toRead;
            }

            if (toRead > 0 && toRead < alignedCount)
            {
                // Partial underrun: Buffer ran out mid-block!
                if (_wasInSilence)
                {
                    ApplyFadeIn(destination, offset, toRead);
                }

                // Smoothly fade-out the tail of the available data to zero (no 90° cliff!)
                ApplyFadeOut(destination, offset + toRead, toRead);

                if (padSilence)
                {
                    Array.Clear(destination, offset + toRead, alignedCount - toRead);
                }

                _lastSampleL = 0;
                _lastSampleR = 0;
                _wasInSilence = true;
                return toRead;
            }

            // toRead == 0 (Starvation / Underrun)
            if (!_wasInSilence && (_lastSampleL != 0 || _lastSampleR != 0))
            {
                // Audio cut off abruptly right at the block boundary!
                // Ramp down smoothly from the last sample to 0 over 3ms instead of jumping to silence
                RampDownFromLastSample(destination, offset, alignedCount);
                _lastSampleL = 0;
                _lastSampleR = 0;
                _wasInSilence = true;
            }
            else if (padSilence)
            {
                Array.Clear(destination, offset, alignedCount);
                _wasInSilence = true;
            }

            return 0;
        }

        /// <summary>
        /// Ramps down the last samples of the chunk to zero using the Hermite S-curve.
        /// </summary>
        private static void ApplyFadeOut(byte[] destination, int endOffset, int availableBytes)
        {
            int frames = Math.Min(FadeFrames, availableBytes / FrameAlignment);
            int startOffset = endOffset - (frames * FrameAlignment);

            for (int i = 0; i < frames; i++)
            {
                float factor = FadeOutLut[i];
                int pos = startOffset + (i * FrameAlignment);

                short rawL = BitConverter.ToInt16(destination, pos);
                short rawR = BitConverter.ToInt16(destination, pos + 2);

                short newL = (short)(rawL * factor);
                short newR = (short)(rawR * factor);

                destination[pos] = (byte)(newL & 0xFF);
                destination[pos + 1] = (byte)((newL >> 8) & 0xFF);
                destination[pos + 2] = (byte)(newR & 0xFF);
                destination[pos + 3] = (byte)((newR >> 8) & 0xFF);
            }
        }

        /// <summary>
        /// Ramps up the first samples of the chunk from zero using the Hermite S-curve.
        /// </summary>
        private static void ApplyFadeIn(byte[] destination, int offset, int totalBytes)
        {
            int frames = Math.Min(FadeFrames, totalBytes / FrameAlignment);

            for (int i = 0; i < frames; i++)
            {
                float factor = FadeInLut[i];
                int pos = offset + (i * FrameAlignment);

                short rawL = BitConverter.ToInt16(destination, pos);
                short rawR = BitConverter.ToInt16(destination, pos + 2);

                short newL = (short)(rawL * factor);
                short newR = (short)(rawR * factor);

                destination[pos] = (byte)(newL & 0xFF);
                destination[pos + 1] = (byte)((newL >> 8) & 0xFF);
                destination[pos + 2] = (byte)(newR & 0xFF);
                destination[pos + 3] = (byte)((newR >> 8) & 0xFF);
            }
        }

        /// <summary>
        /// Synthesizes a smooth decay from the previous block's non-zero tail samples to zero.
        /// </summary>
        private void RampDownFromLastSample(byte[] destination, int offset, int totalBytes)
        {
            int frames = Math.Min(FadeFrames, totalBytes / FrameAlignment);

            for (int i = 0; i < frames; i++)
            {
                float factor = FadeOutLut[i];
                int pos = offset + (i * FrameAlignment);

                short newL = (short)(_lastSampleL * factor);
                short newR = (short)(_lastSampleR * factor);

                destination[pos] = (byte)(newL & 0xFF);
                destination[pos + 1] = (byte)((newL >> 8) & 0xFF);
                destination[pos + 2] = (byte)(newR & 0xFF);
                destination[pos + 3] = (byte)((newR >> 8) & 0xFF);
            }

            int rampBytes = frames * FrameAlignment;
            if (totalBytes > rampBytes)
            {
                Array.Clear(destination, offset + rampBytes, totalBytes - rampBytes);
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _readPos = 0;
                _writePos = 0;
                _count = 0;
                _isBuffering = true;
                _wasInSilence = true;
                _lastSampleL = 0;
                _lastSampleR = 0;
            }
        }
    }
}
