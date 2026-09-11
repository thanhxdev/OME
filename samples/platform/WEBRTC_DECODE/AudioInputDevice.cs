using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace WEBRTC_DECODE
{
    public record AudioDeviceInfo(int Id, string Name);

    /// <summary>
    /// Native Windows Audio Capture using winmm.dll (waveIn)
    /// 48kHz, 16-bit, 2 channels (Opus/WebRTC standard 20ms frame = 960 samples = 3840 bytes)
    /// Used for Studio Talkback / Director Microphone input.
    /// </summary>
    public sealed class AudioInputDevice : IDisposable
    {
        private const int CALLBACK_FUNCTION = 0x00030000;
        private const int WIM_DATA = 0x3C0;
        private const int WIM_OPEN = 0x3BE;
        private const int WIM_CLOSE = 0x3BF;

        private const int SampleRate = 48000;
        private const short Channels = 2;
        private const short BitsPerSample = 16;
        private const int BufferMs = 20; // 20ms per buffer (WebRTC / Opus standard)
        private const int BufferSize = SampleRate * Channels * (BitsPerSample / 8) * BufferMs / 1000; // 3840 bytes
        private const int BufferCount = 4;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct WaveInCaps
        {
            public ushort wMid;
            public ushort wPid;
            public uint vDriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szPname;
            public uint dwFormats;
            public ushort wChannels;
            public ushort wReserved1;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WaveFormatEx
        {
            public ushort wFormatTag;
            public short nChannels;
            public int nSamplesPerSec;
            public int nAvgBytesPerSec;
            public short nBlockAlign;
            public short wBitsPerSample;
            public short cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WaveHdr
        {
            public IntPtr lpData;
            public int dwBufferLength;
            public int dwBytesRecorded;
            public IntPtr dwUser;
            public int dwFlags;
            public int dwLoops;
            public IntPtr lpNext;
            public IntPtr reserved;
        }

        private delegate void WaveInProc(IntPtr hwi, int uMsg, IntPtr dwInstance, IntPtr dwParam1, IntPtr dwParam2);

        [DllImport("winmm.dll", EntryPoint = "waveInGetNumDevs")]
        private static extern int WaveInGetNumDevs();

        [DllImport("winmm.dll", EntryPoint = "waveInGetDevCapsW", CharSet = CharSet.Unicode)]
        private static extern int WaveInGetDevCaps(IntPtr uDeviceID, out WaveInCaps pwic, int cbwic);

        [DllImport("winmm.dll", EntryPoint = "waveInOpen")]
        private static extern int WaveInOpen(out IntPtr phwi, int uDeviceID, ref WaveFormatEx pwfx, WaveInProc dwCallback, IntPtr dwInstance, int fdwOpen);

        [DllImport("winmm.dll", EntryPoint = "waveInPrepareHeader")]
        private static extern int WaveInPrepareHeader(IntPtr hwi, IntPtr pwh, int cbwh);

        [DllImport("winmm.dll", EntryPoint = "waveInUnprepareHeader")]
        private static extern int WaveInUnprepareHeader(IntPtr hwi, IntPtr pwh, int cbwh);

        [DllImport("winmm.dll", EntryPoint = "waveInAddBuffer")]
        private static extern int WaveInAddBuffer(IntPtr hwi, IntPtr pwh, int cbwh);

        [DllImport("winmm.dll", EntryPoint = "waveInStart")]
        private static extern int WaveInStart(IntPtr hwi);

        [DllImport("winmm.dll", EntryPoint = "waveInStop")]
        private static extern int WaveInStop(IntPtr hwi);

        [DllImport("winmm.dll", EntryPoint = "waveInReset")]
        private static extern int WaveInReset(IntPtr hwi);

        [DllImport("winmm.dll", EntryPoint = "waveInClose")]
        private static extern int WaveInClose(IntPtr hwi);

        private IntPtr _waveInHandle = IntPtr.Zero;
        private readonly WaveInProc _callbackDelegate;
        private readonly List<IntPtr> _headers = new();
        private readonly List<IntPtr> _buffers = new();
        private bool _isRecording;
        private readonly object _lock = new();

        public event Action<byte[]>? DataAvailable;

        public AudioInputDevice()
        {
            _callbackDelegate = WaveInCallback;
        }

        public static List<AudioDeviceInfo> GetInputDevices()
        {
            var list = new List<AudioDeviceInfo>();
            int count = WaveInGetNumDevs();
            for (int i = 0; i < count; i++)
            {
                if (WaveInGetDevCaps((IntPtr)i, out var caps, Marshal.SizeOf<WaveInCaps>()) == 0)
                {
                    list.Add(new AudioDeviceInfo(i, string.IsNullOrWhiteSpace(caps.szPname) ? $"Microphone {i}" : caps.szPname.Trim()));
                }
            }
            return list;
        }

        public bool Start(int deviceId)
        {
            lock (_lock)
            {
                if (_isRecording) return true;

                var format = new WaveFormatEx
                {
                    wFormatTag = 1, // WAVE_FORMAT_PCM
                    nChannels = Channels,
                    nSamplesPerSec = SampleRate,
                    wBitsPerSample = BitsPerSample,
                    nBlockAlign = (short)(Channels * (BitsPerSample / 8)),
                    nAvgBytesPerSec = SampleRate * Channels * (BitsPerSample / 8),
                    cbSize = 0
                };

                int res = WaveInOpen(out _waveInHandle, deviceId, ref format, _callbackDelegate, IntPtr.Zero, CALLBACK_FUNCTION);
                if (res != 0 || _waveInHandle == IntPtr.Zero)
                {
                    return false;
                }

                _headers.Clear();
                _buffers.Clear();

                for (int i = 0; i < BufferCount; i++)
                {
                    IntPtr buffer = Marshal.AllocHGlobal(BufferSize);
                    _buffers.Add(buffer);

                    var hdr = new WaveHdr
                    {
                        lpData = buffer,
                        dwBufferLength = BufferSize,
                        dwFlags = 0
                    };

                    IntPtr pHdr = Marshal.AllocHGlobal(Marshal.SizeOf<WaveHdr>());
                    Marshal.StructureToPtr(hdr, pHdr, false);
                    _headers.Add(pHdr);

                    WaveInPrepareHeader(_waveInHandle, pHdr, Marshal.SizeOf<WaveHdr>());
                    WaveInAddBuffer(_waveInHandle, pHdr, Marshal.SizeOf<WaveHdr>());
                }

                res = WaveInStart(_waveInHandle);
                _isRecording = res == 0;
                return _isRecording;
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                if (!_isRecording) return;
                _isRecording = false;

                if (_waveInHandle != IntPtr.Zero)
                {
                    WaveInReset(_waveInHandle);
                    WaveInStop(_waveInHandle);

                    foreach (var pHdr in _headers)
                    {
                        WaveInUnprepareHeader(_waveInHandle, pHdr, Marshal.SizeOf<WaveHdr>());
                        Marshal.FreeHGlobal(pHdr);
                    }
                    _headers.Clear();

                    foreach (var buf in _buffers)
                    {
                        Marshal.FreeHGlobal(buf);
                    }
                    _buffers.Clear();

                    WaveInClose(_waveInHandle);
                    _waveInHandle = IntPtr.Zero;
                }
            }
        }

        private void WaveInCallback(IntPtr hwi, int uMsg, IntPtr dwInstance, IntPtr dwParam1, IntPtr dwParam2)
        {
            if (uMsg == WIM_DATA && _isRecording && dwParam1 != IntPtr.Zero)
            {
                var hdr = Marshal.PtrToStructure<WaveHdr>(dwParam1);
                if (hdr.dwBytesRecorded > 0 && hdr.lpData != IntPtr.Zero)
                {
                    byte[] pcm = new byte[hdr.dwBytesRecorded];
                    Marshal.Copy(hdr.lpData, pcm, 0, hdr.dwBytesRecorded);
                    DataAvailable?.Invoke(pcm);
                }

                if (_isRecording && _waveInHandle != IntPtr.Zero)
                {
                    WaveInAddBuffer(_waveInHandle, dwParam1, Marshal.SizeOf<WaveHdr>());
                }
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
