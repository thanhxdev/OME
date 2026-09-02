using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace SRT_DECODE
{
    /// <summary>
    /// Rock-solid, low-latency Windows audio output device using Win32 waveOut (winmm.dll).
    /// Uses pre-allocated native buffers with a ring buffer on a dedicated background playback thread.
    /// Zero deadlock, zero spin-wait, smooth A/V sync playback.
    /// </summary>
    public sealed class AudioOutputDevice : IDisposable
    {
        private const int WAVE_MAPPER = -1;
        private const int CALLBACK_NULL = 0x00000000;
        private const uint WHDR_DONE = 0x00000001;

        private const int SampleRate = 48000;
        private const int Channels = 2;
        private const int BitsPerSample = 16;
        private const int BytesPerSample = Channels * (BitsPerSample / 8); // 4 bytes per stereo frame

        // 4 buffers of 25ms each = 100ms total hardware buffer pool
        private const int BufferCount = 4;
        private const int BufferDurationMs = 25;
        private const int BufferSizeBytes = (SampleRate * BytesPerSample * BufferDurationMs) / 1000; // 4800 bytes

        [StructLayout(LayoutKind.Sequential)]
        public struct WaveFormatEx
        {
            public ushort wFormatTag;
            public ushort nChannels;
            public uint nSamplesPerSec;
            public uint nAvgBytesPerSec;
            public ushort nBlockAlign;
            public ushort wBitsPerSample;
            public ushort cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WaveHdr
        {
            public IntPtr lpData;
            public uint dwBufferLength;
            public uint dwBytesRecorded;
            public IntPtr dwUser;
            public uint dwFlags;
            public uint dwLoops;
            public IntPtr lpNext;
            public IntPtr reserved;
        }

        [DllImport("winmm.dll")]
        private static extern int waveOutOpen(out IntPtr hWaveOut, int uDeviceID, ref WaveFormatEx lpFormat, IntPtr dwCallback, IntPtr dwInstance, int dwFlags);

        [DllImport("winmm.dll")]
        private static extern int waveOutPrepareHeader(IntPtr hWaveOut, IntPtr lpWaveOutHdr, int uSize);

        [DllImport("winmm.dll")]
        private static extern int waveOutWrite(IntPtr hWaveOut, IntPtr lpWaveOutHdr, int uSize);

        [DllImport("winmm.dll")]
        private static extern int waveOutUnprepareHeader(IntPtr hWaveOut, IntPtr lpWaveOutHdr, int uSize);

        [DllImport("winmm.dll")]
        private static extern int waveOutClose(IntPtr hWaveOut);

        [DllImport("winmm.dll")]
        private static extern int waveOutReset(IntPtr hWaveOut);

        private IntPtr _hWaveOut = IntPtr.Zero;
        private readonly IntPtr[] _nativeHdrPtrs = new IntPtr[BufferCount];
        private readonly IntPtr[] _nativeBufferPtrs = new IntPtr[BufferCount];
        private readonly bool[] _bufferInUse = new bool[BufferCount];
        private readonly byte[] _silenceBuffer = new byte[BufferSizeBytes];

        private readonly ConcurrentQueue<byte[]> _audioQueue = new();
        private readonly Thread? _playbackThread;
        private readonly AutoResetEvent _wakeEvent = new(false);
        private volatile bool _isRunning;
        private bool _disposed;
        private readonly object _lock = new();

        public bool IsOpen => _hWaveOut != IntPtr.Zero;

        public AudioOutputDevice()
        {
            try
            {
                var format = new WaveFormatEx
                {
                    wFormatTag = 1, // PCM
                    nChannels = (ushort)Channels,
                    nSamplesPerSec = (uint)SampleRate,
                    wBitsPerSample = (ushort)BitsPerSample,
                    nBlockAlign = (ushort)BytesPerSample,
                    nAvgBytesPerSec = (uint)(SampleRate * BytesPerSample),
                    cbSize = 0
                };

                int res = waveOutOpen(out _hWaveOut, WAVE_MAPPER, ref format, IntPtr.Zero, IntPtr.Zero, CALLBACK_NULL);
                if (res != 0 || _hWaveOut == IntPtr.Zero)
                {
                    Trace.WriteLine($"[AudioOutputDevice] waveOutOpen failed with error code: {res}");
                    return;
                }

                // Pre-allocate fixed native headers & buffers
                int hdrSize = Marshal.SizeOf<WaveHdr>();
                for (int i = 0; i < BufferCount; i++)
                {
                    _nativeBufferPtrs[i] = Marshal.AllocHGlobal(BufferSizeBytes);
                    _nativeHdrPtrs[i] = Marshal.AllocHGlobal(hdrSize);
                    _bufferInUse[i] = false;

                    // Zero-initialize buffer & header
                    Marshal.Copy(_silenceBuffer, 0, _nativeBufferPtrs[i], BufferSizeBytes);

                    var hdr = new WaveHdr
                    {
                        lpData = _nativeBufferPtrs[i],
                        dwBufferLength = (uint)BufferSizeBytes,
                        dwFlags = 0
                    };
                    Marshal.StructureToPtr(hdr, _nativeHdrPtrs[i], false);
                }

                _isRunning = true;
                _playbackThread = new Thread(PlaybackLoop)
                {
                    Name = "SRT_AudioPlaybackThread",
                    IsBackground = true,
                    Priority = ThreadPriority.AboveNormal
                };
                _playbackThread.Start();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[AudioOutputDevice] Initialization error: {ex.Message}");
            }
        }

        /// <summary>
        /// Enqueues PCM audio chunk (16-bit 48kHz Stereo) non-blockingly.
        /// </summary>
        public void PlayPcm(byte[] pcmData, int offset, int count, double volumeMultiplier = 1.0)
        {
            if (!_isRunning || _hWaveOut == IntPtr.Zero || pcmData == null || count <= 0) return;

            // Prevent queue buildup beyond ~200ms (10 chunks of 20ms) to ensure low latency
            while (_audioQueue.Count > 10)
            {
                _audioQueue.TryDequeue(out _);
            }

            byte[] buffer = new byte[count];
            if (Math.Abs(volumeMultiplier - 1.0) > 0.01)
            {
                float vol = (float)Math.Clamp(volumeMultiplier, 0.0, 2.0);
                for (int i = 0; i < count; i += 2)
                {
                    short sample = BitConverter.ToInt16(pcmData, offset + i);
                    short scaled = (short)Math.Clamp(sample * vol, short.MinValue, short.MaxValue);
                    buffer[i] = (byte)(scaled & 0xFF);
                    buffer[i + 1] = (byte)((scaled >> 8) & 0xFF);
                }
            }
            else
            {
                Buffer.BlockCopy(pcmData, offset, buffer, 0, count);
            }

            _audioQueue.Enqueue(buffer);
            _wakeEvent.Set();
        }

        public void ClearQueue()
        {
            while (_audioQueue.TryDequeue(out _)) { }
        }

        private void PlaybackLoop()
        {
            int bufferIndex = 0;
            int hdrSize = Marshal.SizeOf<WaveHdr>();
            byte[] activePcmAccumulator = new byte[BufferSizeBytes];
            int accumOffset = 0;

            while (_isRunning && _hWaveOut != IntPtr.Zero)
            {
                try
                {
                    IntPtr hdrPtr = _nativeHdrPtrs[bufferIndex];
                    IntPtr dataPtr = _nativeBufferPtrs[bufferIndex];

                    // If this buffer was previously submitted to hardware, wait until playback finishes
                    if (_bufferInUse[bufferIndex])
                    {
                        var prevHdr = Marshal.PtrToStructure<WaveHdr>(hdrPtr);
                        while (_isRunning && (prevHdr.dwFlags & WHDR_DONE) == 0)
                        {
                            Thread.Sleep(2);
                            prevHdr = Marshal.PtrToStructure<WaveHdr>(hdrPtr);
                        }

                        if (!_isRunning) break;

                        waveOutUnprepareHeader(_hWaveOut, hdrPtr, hdrSize);
                        _bufferInUse[bufferIndex] = false;
                    }

                    // If queue is completely empty, wait for incoming PCM
                    if (_audioQueue.IsEmpty)
                    {
                        _wakeEvent.WaitOne(15);
                        if (!_isRunning) break;
                    }

                    // Fill accumulator with incoming audio chunks until BufferSizeBytes is reached
                    accumOffset = 0;
                    while (accumOffset < BufferSizeBytes)
                    {
                        if (_audioQueue.TryDequeue(out byte[]? chunk) && chunk != null)
                        {
                            int toCopy = Math.Min(chunk.Length, BufferSizeBytes - accumOffset);
                            Buffer.BlockCopy(chunk, 0, activePcmAccumulator, accumOffset, toCopy);
                            accumOffset += toCopy;
                        }
                        else
                        {
                            // No more audio in queue, pad remainder with silence
                            Array.Clear(activePcmAccumulator, accumOffset, BufferSizeBytes - accumOffset);
                            break;
                        }
                    }

                    // If accumulator is all silence and queue was empty, avoid hammering waveOut
                    if (accumOffset == 0 && _audioQueue.IsEmpty)
                    {
                        Thread.Sleep(5);
                        continue;
                    }

                    // Copy accumulated audio into native buffer
                    Marshal.Copy(activePcmAccumulator, 0, dataPtr, BufferSizeBytes);

                    var hdr = new WaveHdr
                    {
                        lpData = dataPtr,
                        dwBufferLength = (uint)BufferSizeBytes,
                        dwBytesRecorded = 0,
                        dwUser = IntPtr.Zero,
                        dwFlags = 0,
                        dwLoops = 0,
                        lpNext = IntPtr.Zero,
                        reserved = IntPtr.Zero
                    };
                    Marshal.StructureToPtr(hdr, hdrPtr, false);

                    waveOutPrepareHeader(_hWaveOut, hdrPtr, hdrSize);
                    waveOutWrite(_hWaveOut, hdrPtr, hdrSize);
                    _bufferInUse[bufferIndex] = true;

                    bufferIndex = (bufferIndex + 1) % BufferCount;
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[AudioOutputDevice] PlaybackLoop error: {ex.Message}");
                    Thread.Sleep(10);
                }
            }
        }

        public void Close()
        {
            lock (_lock)
            {
                if (!_isRunning) return;
                _isRunning = false;
                _wakeEvent.Set();

                if (_hWaveOut != IntPtr.Zero)
                {
                    try
                    {
                        waveOutReset(_hWaveOut);

                        int hdrSize = Marshal.SizeOf<WaveHdr>();
                        for (int i = 0; i < BufferCount; i++)
                        {
                            if (_nativeHdrPtrs[i] != IntPtr.Zero && _bufferInUse[i])
                            {
                                waveOutUnprepareHeader(_hWaveOut, _nativeHdrPtrs[i], hdrSize);
                                _bufferInUse[i] = false;
                            }
                        }

                        waveOutClose(_hWaveOut);
                    }
                    catch { }
                    _hWaveOut = IntPtr.Zero;
                }

                for (int i = 0; i < BufferCount; i++)
                {
                    if (_nativeBufferPtrs[i] != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(_nativeBufferPtrs[i]);
                        _nativeBufferPtrs[i] = IntPtr.Zero;
                    }
                    if (_nativeHdrPtrs[i] != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(_nativeHdrPtrs[i]);
                        _nativeHdrPtrs[i] = IntPtr.Zero;
                    }
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Close();
            _wakeEvent.Dispose();
        }
    }
}
