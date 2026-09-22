using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace OME_PLAYOUT
{
    /// <summary>
    /// Rock-solid, low-latency Windows audio output device using Win32 waveOut (winmm.dll).
    /// Provides real-time local monitoring on PC Speakers / Headphones for OME_PLAYOUT.
    /// Non-blocking, zero GC allocation, ring buffer playback.
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

        private const int BufferCount = 4;
        private const int BufferDurationMs = 20;
        private const int BufferSizeBytes = (SampleRate * BytesPerSample * BufferDurationMs) / 1000; // 3840 bytes (20ms @ 48kHz stereo)

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

        [DllImport("winmm.dll")]
        private static extern uint timeBeginPeriod(uint uMilliseconds);

        [DllImport("winmm.dll")]
        private static extern uint timeEndPeriod(uint uMilliseconds);

        private IntPtr _hWaveOut = IntPtr.Zero;
        private readonly IntPtr[] _nativeHdrPtrs = new IntPtr[BufferCount];
        private readonly IntPtr[] _nativeBufferPtrs = new IntPtr[BufferCount];
        private readonly bool[] _bufferInUse = new bool[BufferCount];
        private readonly byte[] _silenceBuffer = new byte[BufferSizeBytes];

        private readonly ConcurrentQueue<byte[]> _audioQueue = new();
        private byte[]? _carryOverChunk;
        private int _carryOverOffset;
        private volatile bool _isPreRolling = true;
        private const int PreRollChunkCount = 2; // ~20ms initial buffer before starting hardware playback

        private readonly Thread? _playbackThread;
        private readonly AutoResetEvent _wakeEvent = new(false);
        private volatile bool _isRunning;
        private bool _disposed;
        private readonly object _lock = new();

        public bool IsMuted { get; set; } = false;
        public double Volume { get; set; } = 1.0;

        public bool IsOpen => _hWaveOut != IntPtr.Zero;

        public AudioOutputDevice()
        {
            try
            {
                timeBeginPeriod(1);

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

                int hdrSize = Marshal.SizeOf<WaveHdr>();
                for (int i = 0; i < BufferCount; i++)
                {
                    _nativeBufferPtrs[i] = Marshal.AllocHGlobal(BufferSizeBytes);
                    _nativeHdrPtrs[i] = Marshal.AllocHGlobal(hdrSize);
                    _bufferInUse[i] = false;

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
                    Name = "Playout_AudioPlaybackThread",
                    IsBackground = true,
                    Priority = ThreadPriority.Highest
                };
                _playbackThread.Start();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[AudioOutputDevice] Initialization error: {ex.Message}");
            }
        }

        public void PlayPcm(byte[] pcmData, int offset, int count)
        {
            if (!_isRunning || _hWaveOut == IntPtr.Zero || pcmData == null || count <= 0) return;
            if (IsMuted) return;

            while (_audioQueue.Count > 35)
            {
                _audioQueue.TryDequeue(out _);
            }

            int alignedCount = count - (count % BytesPerSample);
            if (alignedCount <= 0) return;

            byte[] buffer = new byte[alignedCount];
            double vol = Volume;
            if (Math.Abs(vol - 1.0) > 0.01)
            {
                float v = (float)Math.Clamp(vol, 0.0, 2.0);
                for (int i = 0; i < alignedCount; i += 2)
                {
                    short sample = BitConverter.ToInt16(pcmData, offset + i);
                    short scaled = (short)Math.Clamp(sample * v, short.MinValue, short.MaxValue);
                    buffer[i] = (byte)(scaled & 0xFF);
                    buffer[i + 1] = (byte)((scaled >> 8) & 0xFF);
                }
            }
            else
            {
                Buffer.BlockCopy(pcmData, offset, buffer, 0, alignedCount);
            }

            _audioQueue.Enqueue(buffer);

            if (_isPreRolling && _audioQueue.Count >= PreRollChunkCount)
            {
                _isPreRolling = false;
            }

            _wakeEvent.Set();
        }

        private void PlaybackLoop()
        {
            byte[] activePcmAccumulator = new byte[BufferSizeBytes];
            int bufferIndex = 0;
            int hdrSize = Marshal.SizeOf<WaveHdr>();
            int accumOffset = 0;

            while (_isRunning)
            {
                try
                {
                    IntPtr hWaveOut = _hWaveOut;
                    if (hWaveOut == IntPtr.Zero)
                    {
                        Thread.Sleep(20);
                        continue;
                    }

                    if (_bufferInUse[bufferIndex])
                    {
                        IntPtr hdrPtr = _nativeHdrPtrs[bufferIndex];
                        var checkHdr = Marshal.PtrToStructure<WaveHdr>(hdrPtr);
                        if ((checkHdr.dwFlags & WHDR_DONE) == 0)
                        {
                            _wakeEvent.WaitOne(4);
                            if (!_isRunning) break;
                            continue;
                        }

                        lock (_lock)
                        {
                            if (_hWaveOut != IntPtr.Zero && _hWaveOut == hWaveOut)
                            {
                                try { waveOutUnprepareHeader(hWaveOut, hdrPtr, hdrSize); } catch { }
                                _bufferInUse[bufferIndex] = false;
                            }
                        }
                    }

                    if (_isPreRolling)
                    {
                        if (_audioQueue.Count < PreRollChunkCount)
                        {
                            _wakeEvent.WaitOne(10);
                            if (!_isRunning) break;
                            continue;
                        }
                        _isPreRolling = false;
                    }

                    if (_audioQueue.IsEmpty && _carryOverChunk == null)
                    {
                        _isPreRolling = true;
                        _wakeEvent.WaitOne(15);
                        if (!_isRunning) break;
                        continue;
                    }

                    accumOffset = 0;
                    while (accumOffset < BufferSizeBytes)
                    {
                        if (_carryOverChunk != null)
                        {
                            int available = _carryOverChunk.Length - _carryOverOffset;
                            int toCopy = Math.Min(available, BufferSizeBytes - accumOffset);
                            Buffer.BlockCopy(_carryOverChunk, _carryOverOffset, activePcmAccumulator, accumOffset, toCopy);
                            accumOffset += toCopy;
                            _carryOverOffset += toCopy;

                            if (_carryOverOffset >= _carryOverChunk.Length)
                            {
                                _carryOverChunk = null;
                                _carryOverOffset = 0;
                            }
                            continue;
                        }

                        if (_audioQueue.TryDequeue(out byte[]? chunk) && chunk != null && chunk.Length > 0)
                        {
                            int toCopy = Math.Min(chunk.Length, BufferSizeBytes - accumOffset);
                            Buffer.BlockCopy(chunk, 0, activePcmAccumulator, accumOffset, toCopy);
                            accumOffset += toCopy;

                            if (toCopy < chunk.Length)
                            {
                                _carryOverChunk = chunk;
                                _carryOverOffset = toCopy;
                            }
                        }
                        else
                        {
                            if (accumOffset > 0 && _wakeEvent.WaitOne(4))
                            {
                                continue;
                            }

                            Array.Clear(activePcmAccumulator, accumOffset, BufferSizeBytes - accumOffset);
                            _isPreRolling = true;
                            break;
                        }
                    }

                    lock (_lock)
                    {
                        if (_hWaveOut == IntPtr.Zero || _hWaveOut != hWaveOut || !_isRunning) break;

                        IntPtr pBuffer = _nativeBufferPtrs[bufferIndex];
                        IntPtr pHdr = _nativeHdrPtrs[bufferIndex];

                        Marshal.Copy(activePcmAccumulator, 0, pBuffer, BufferSizeBytes);

                        var hdr = new WaveHdr
                        {
                            lpData = pBuffer,
                            dwBufferLength = (uint)BufferSizeBytes,
                            dwFlags = 0
                        };
                        Marshal.StructureToPtr(hdr, pHdr, false);

                        int prep = waveOutPrepareHeader(hWaveOut, pHdr, hdrSize);
                        if (prep == 0)
                        {
                            int wr = waveOutWrite(hWaveOut, pHdr, hdrSize);
                            if (wr == 0)
                            {
                                _bufferInUse[bufferIndex] = true;
                                bufferIndex = (bufferIndex + 1) % BufferCount;
                            }
                            else
                            {
                                waveOutUnprepareHeader(hWaveOut, pHdr, hdrSize);
                            }
                        }
                    }
                }
                catch
                {
                    Thread.Sleep(10);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _isRunning = false;
            _wakeEvent.Set();

            if (_playbackThread != null && _playbackThread.IsAlive)
            {
                _playbackThread.Join(150);
            }

            lock (_lock)
            {
                if (_hWaveOut != IntPtr.Zero)
                {
                    try
                    {
                        waveOutReset(_hWaveOut);
                        int hdrSize = Marshal.SizeOf<WaveHdr>();
                        for (int i = 0; i < BufferCount; i++)
                        {
                            if (_bufferInUse[i] && _nativeHdrPtrs[i] != IntPtr.Zero)
                            {
                                waveOutUnprepareHeader(_hWaveOut, _nativeHdrPtrs[i], hdrSize);
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

            timeEndPeriod(1);
        }
    }
}
