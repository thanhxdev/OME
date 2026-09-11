using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace WEBRTC_ENCODE
{
    public sealed class AudioOutputDevice : IDisposable
    {
        private const int WAVE_MAPPER = -1;
        private const int CALLBACK_NULL = 0x00000000;
        private const uint WHDR_DONE = 0x00000001;

        private const int SampleRate = 48000;
        private const int Channels = 2;
        private const int BitsPerSample = 16;
        private const int BytesPerSample = Channels * (BitsPerSample / 8); // 4 bytes

        // 6 buffers of 20ms each = 120ms hardware buffer pool
        private const int BufferCount = 6;
        private const int BufferDurationMs = 20;
        private const int BufferSizeBytes = (SampleRate * BytesPerSample * BufferDurationMs) / 1000; // 3840 bytes

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

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct WaveOutCaps
        {
            public ushort wMid;
            public ushort wPid;
            public uint vDriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szPname;
            public uint dwFormats;
            public ushort wChannels;
            public ushort wReserved1;
            public uint dwSupport;
        }

        [DllImport("winmm.dll", EntryPoint = "waveOutGetNumDevs")]
        private static extern int WaveOutGetNumDevs();

        [DllImport("winmm.dll", EntryPoint = "waveOutGetDevCapsW", CharSet = CharSet.Unicode)]
        private static extern int WaveOutGetDevCaps(IntPtr uDeviceID, out WaveOutCaps pwoc, int cbwoc);

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
        private int _currentDeviceId = WAVE_MAPPER;
        public int CurrentDeviceId => _currentDeviceId;

        private readonly IntPtr[] _nativeHdrPtrs = new IntPtr[BufferCount];
        private readonly IntPtr[] _nativeBufferPtrs = new IntPtr[BufferCount];
        private readonly bool[] _bufferInUse = new bool[BufferCount];

        private readonly ConcurrentQueue<byte[]> _audioQueue = new();
        private byte[]? _carryOverChunk;
        private int _carryOverOffset;
        private volatile bool _isPreRolling = true;
        private const int PreRollChunkCount = 2; // ~40ms initial buffer

        private readonly Thread? _playbackThread;
        private readonly AutoResetEvent _wakeEvent = new(false);
        private volatile bool _isRunning;
        private bool _disposed;
        private readonly object _lock = new();

        public bool IsOpen => _hWaveOut != IntPtr.Zero;

        public static System.Collections.Generic.List<AudioDeviceInfo> GetOutputDevices()
        {
            var list = new System.Collections.Generic.List<AudioDeviceInfo>();
            list.Add(new AudioDeviceInfo(WAVE_MAPPER, "Mặc định hệ thống (Default Audio Device)"));
            int count = WaveOutGetNumDevs();
            for (int i = 0; i < count; i++)
            {
                if (WaveOutGetDevCaps((IntPtr)i, out var caps, Marshal.SizeOf<WaveOutCaps>()) == 0)
                {
                    string name = string.IsNullOrWhiteSpace(caps.szPname) ? $"Speaker/Headphone {i}" : caps.szPname.Trim();
                    list.Add(new AudioDeviceInfo(i, name));
                }
            }
            return list;
        }

        public bool ChangeDevice(int deviceId)
        {
            lock (_lock)
            {
                try
                {
                    if (_hWaveOut != IntPtr.Zero)
                    {
                        IntPtr oldHw = _hWaveOut;
                        _hWaveOut = IntPtr.Zero; // Pause writes during reset
                        waveOutReset(oldHw);
                        int hdrSize = Marshal.SizeOf<WaveHdr>();
                        for (int i = 0; i < BufferCount; i++)
                        {
                            if (_nativeHdrPtrs[i] != IntPtr.Zero)
                            {
                                waveOutUnprepareHeader(oldHw, _nativeHdrPtrs[i], hdrSize);
                                _bufferInUse[i] = false;
                            }
                        }
                        waveOutClose(oldHw);
                    }

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

                    int res = waveOutOpen(out _hWaveOut, deviceId, ref format, IntPtr.Zero, IntPtr.Zero, CALLBACK_NULL);
                    if (res != 0 || _hWaveOut == IntPtr.Zero)
                    {
                        Trace.WriteLine($"[AudioOutputDevice] ChangeDevice {deviceId} failed ({res}), falling back to WAVE_MAPPER");
                        res = waveOutOpen(out _hWaveOut, WAVE_MAPPER, ref format, IntPtr.Zero, IntPtr.Zero, CALLBACK_NULL);
                        _currentDeviceId = WAVE_MAPPER;
                    }
                    else
                    {
                        _currentDeviceId = deviceId;
                    }

                    _wakeEvent.Set();
                    return res == 0;
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[AudioOutputDevice] ChangeDevice error: {ex.Message}");
                    return false;
                }
            }
        }

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
                    Name = "WebRTC_AudioMonitorThread",
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

        public void PlayPcm(byte[] pcmData, int offset, int count, double volumeMultiplier = 1.0)
        {
            if (!_isRunning || _hWaveOut == IntPtr.Zero || pcmData == null || count <= 0) return;

            while (_audioQueue.Count > 30)
            {
                _audioQueue.TryDequeue(out _);
            }

            int alignedCount = count - (count % BytesPerSample);
            if (alignedCount <= 0) return;

            byte[] buffer = new byte[alignedCount];
            if (Math.Abs(volumeMultiplier - 1.0) > 0.01)
            {
                float vol = (float)Math.Clamp(volumeMultiplier, 0.0, 2.0);
                for (int i = 0; i < alignedCount; i += 2)
                {
                    short sample = (short)(pcmData[offset + i] | (pcmData[offset + i + 1] << 8));
                    short scaled = (short)Math.Clamp((int)(sample * vol), short.MinValue, short.MaxValue);
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
            int bufferIdx = 0;
            byte[] accumulator = new byte[BufferSizeBytes];
            int hdrSize = Marshal.SizeOf<WaveHdr>();

            while (_isRunning)
            {
                try
                {
                    IntPtr hWaveOut;
                    lock (_lock)
                    {
                        hWaveOut = _hWaveOut;
                    }

                    if (hWaveOut == IntPtr.Zero)
                    {
                        Thread.Sleep(10);
                        continue;
                    }

                    // 1. Wait for pre-roll to fill before sending audio to hardware
                    if (_isPreRolling)
                    {
                        if (_audioQueue.Count < PreRollChunkCount)
                        {
                            _wakeEvent.WaitOne(10);
                            continue;
                        }
                        _isPreRolling = false;
                    }

                    // 2. Collect exactly BufferSizeBytes (3840 bytes)
                    int filledBytes = 0;

                    if (_carryOverChunk != null)
                    {
                        int bytesLeftInCarry = _carryOverChunk.Length - _carryOverOffset;
                        int toCopy = Math.Min(bytesLeftInCarry, BufferSizeBytes);
                        Buffer.BlockCopy(_carryOverChunk, _carryOverOffset, accumulator, 0, toCopy);
                        filledBytes += toCopy;
                        _carryOverOffset += toCopy;

                        if (_carryOverOffset >= _carryOverChunk.Length)
                        {
                            _carryOverChunk = null;
                            _carryOverOffset = 0;
                        }
                    }

                    while (filledBytes < BufferSizeBytes && _audioQueue.TryDequeue(out byte[]? chunk))
                    {
                        if (chunk == null || chunk.Length == 0) continue;

                        int needed = BufferSizeBytes - filledBytes;
                        if (chunk.Length <= needed)
                        {
                            Buffer.BlockCopy(chunk, 0, accumulator, filledBytes, chunk.Length);
                            filledBytes += chunk.Length;
                        }
                        else
                        {
                            Buffer.BlockCopy(chunk, 0, accumulator, filledBytes, needed);
                            filledBytes += needed;

                            _carryOverChunk = chunk;
                            _carryOverOffset = needed;
                            break;
                        }
                    }

                    // If queue is temporarily starved, wait for data — NEVER pad with silence mid-stream!
                    if (filledBytes < BufferSizeBytes)
                    {
                        if (_audioQueue.IsEmpty)
                        {
                            if (filledBytes > 0)
                            {
                                byte[] partial = new byte[filledBytes];
                                Buffer.BlockCopy(accumulator, 0, partial, 0, filledBytes);
                                _carryOverChunk = partial;
                                _carryOverOffset = 0;
                            }
                            _wakeEvent.WaitOne(5);
                            continue;
                        }
                    }

                    // 3. Ensure hardware buffer is free before writing
                    IntPtr hdrPtr = _nativeHdrPtrs[bufferIdx];
                    if (_bufferInUse[bufferIdx])
                    {
                        while (_isRunning)
                        {
                            var currentHdr = Marshal.PtrToStructure<WaveHdr>(hdrPtr);
                            if ((currentHdr.dwFlags & WHDR_DONE) != 0)
                            {
                                waveOutUnprepareHeader(hWaveOut, hdrPtr, hdrSize);
                                _bufferInUse[bufferIdx] = false;
                                break;
                            }
                            Thread.Sleep(1);
                        }

                        if (!_isRunning) break;
                    }

                    // 4. Copy accumulator to unmanaged memory and write to waveOut
                    Marshal.Copy(accumulator, 0, _nativeBufferPtrs[bufferIdx], BufferSizeBytes);

                    var newHdr = new WaveHdr
                    {
                        lpData = _nativeBufferPtrs[bufferIdx],
                        dwBufferLength = (uint)BufferSizeBytes,
                        dwFlags = 0
                    };
                    Marshal.StructureToPtr(newHdr, hdrPtr, false);

                    waveOutPrepareHeader(hWaveOut, hdrPtr, hdrSize);
                    int writeRes = waveOutWrite(hWaveOut, hdrPtr, hdrSize);
                    if (writeRes == 0)
                    {
                        _bufferInUse[bufferIdx] = true;
                    }
                    else
                    {
                        waveOutUnprepareHeader(hWaveOut, hdrPtr, hdrSize);
                        _bufferInUse[bufferIdx] = false;
                    }

                    bufferIdx = (bufferIdx + 1) % BufferCount;
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[AudioOutputDevice] PlaybackLoop exception: {ex.Message}");
                    _wakeEvent.WaitOne(10);
                }
            }
        }

        public void Stop()
        {
            _isRunning = false;
            _wakeEvent.Set();

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
                                _bufferInUse[i] = false;
                            }
                        }
                        waveOutClose(_hWaveOut);
                    }
                    catch { }
                    _hWaveOut = IntPtr.Zero;
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            timeEndPeriod(1);
        }
    }
}
