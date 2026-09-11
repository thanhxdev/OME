using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace WEBRTC_ENCODE
{
    public record AudioDeviceInfo(int Id, string Name);

    /// <summary>
    /// Native Windows Audio Capture using winmm.dll (waveIn)
    /// 48kHz, 16-bit, 2 channels (Opus/WebRTC standard 20ms frame = 960 samples = 3840 bytes)
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

        private int _currentSampleRate = SampleRate;
        private short _currentChannels = Channels;
        private int _currentBufferSize = BufferSize;

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

                // Test candidate formats in order of preference
                (int rate, short channels)[] candidates =
                {
                    (48000, 2),
                    (48000, 1),
                    (44100, 2),
                    (44100, 1),
                    (16000, 1)
                };

                int[] devCandidates = (deviceId >= 0)
                    ? new int[] { deviceId, -1 } // try selected device, fallback to WAVE_MAPPER
                    : new int[] { -1 };

                IntPtr openedHandle = IntPtr.Zero;
                int chosenRate = SampleRate;
                short chosenChannels = Channels;

                foreach (int dev in devCandidates)
                {
                    foreach (var (rate, channels) in candidates)
                    {
                        var format = new WaveFormatEx
                        {
                            wFormatTag = 1, // WAVE_FORMAT_PCM
                            nChannels = channels,
                            nSamplesPerSec = rate,
                            wBitsPerSample = BitsPerSample,
                            nBlockAlign = (short)(channels * (BitsPerSample / 8)),
                            nAvgBytesPerSec = rate * channels * (BitsPerSample / 8),
                            cbSize = 0
                        };

                        int res = WaveInOpen(out openedHandle, dev, ref format, _callbackDelegate, IntPtr.Zero, CALLBACK_FUNCTION);
                        if (res == 0 && openedHandle != IntPtr.Zero)
                        {
                            chosenRate = rate;
                            chosenChannels = channels;
                            break;
                        }
                    }

                    if (openedHandle != IntPtr.Zero) break;
                }

                if (openedHandle == IntPtr.Zero)
                {
                    return false;
                }

                _waveInHandle = openedHandle;
                _currentSampleRate = chosenRate;
                _currentChannels = chosenChannels;
                _currentBufferSize = chosenRate * chosenChannels * (BitsPerSample / 8) * BufferMs / 1000;
                if (_currentBufferSize <= 0) _currentBufferSize = BufferSize;

                _headers.Clear();
                _buffers.Clear();

                for (int i = 0; i < BufferCount; i++)
                {
                    IntPtr buffer = Marshal.AllocHGlobal(_currentBufferSize);
                    _buffers.Add(buffer);

                    var hdr = new WaveHdr
                    {
                        lpData = buffer,
                        dwBufferLength = _currentBufferSize,
                        dwFlags = 0
                    };

                    IntPtr pHdr = Marshal.AllocHGlobal(Marshal.SizeOf<WaveHdr>());
                    Marshal.StructureToPtr(hdr, pHdr, false);
                    _headers.Add(pHdr);

                    WaveInPrepareHeader(_waveInHandle, pHdr, Marshal.SizeOf<WaveHdr>());
                    WaveInAddBuffer(_waveInHandle, pHdr, Marshal.SizeOf<WaveHdr>());
                }

                int startRes = WaveInStart(_waveInHandle);
                _isRecording = startRes == 0;
                return _isRecording;
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                if (!_isRecording && _waveInHandle == IntPtr.Zero) return;
                _isRecording = false;

                if (_waveInHandle != IntPtr.Zero)
                {
                    try { WaveInReset(_waveInHandle); } catch { }
                    try { WaveInStop(_waveInHandle); } catch { }

                    foreach (var pHdr in _headers)
                    {
                        try { WaveInUnprepareHeader(_waveInHandle, pHdr, Marshal.SizeOf<WaveHdr>()); } catch { }
                        Marshal.FreeHGlobal(pHdr);
                    }
                    _headers.Clear();

                    foreach (var buf in _buffers)
                    {
                        Marshal.FreeHGlobal(buf);
                    }
                    _buffers.Clear();

                    try { WaveInClose(_waveInHandle); } catch { }
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
                    byte[] raw = new byte[hdr.dwBytesRecorded];
                    Marshal.Copy(hdr.lpData, raw, 0, hdr.dwBytesRecorded);

                    byte[] pcm48kStereo = ConvertTo48kStereo(raw, _currentSampleRate, _currentChannels);
                    DataAvailable?.Invoke(pcm48kStereo);
                }

                if (_isRecording && _waveInHandle != IntPtr.Zero)
                {
                    try
                    {
                        WaveInAddBuffer(_waveInHandle, dwParam1, Marshal.SizeOf<WaveHdr>());
                    }
                    catch { }
                }
            }
        }

        private static byte[] ConvertTo48kStereo(byte[] input, int inSampleRate, short inChannels)
        {
            if (input == null || input.Length == 0) return Array.Empty<byte>();

            // If already 48kHz stereo 16-bit, return copy directly
            if (inSampleRate == 48000 && inChannels == 2)
            {
                return input;
            }

            int inSampleCount = input.Length / (inChannels * 2);
            if (inSampleCount == 0) return Array.Empty<byte>();

            // 1. Convert to float stereo samples
            float[] stereoSamples = new float[inSampleCount * 2];
            for (int i = 0; i < inSampleCount; i++)
            {
                int inOffset = i * inChannels * 2;
                short s0 = (short)(input[inOffset] | (input[inOffset + 1] << 8));
                float f0 = s0 / 32768.0f;

                float f1 = f0;
                if (inChannels >= 2)
                {
                    short s1 = (short)(input[inOffset + 2] | (input[inOffset + 3] << 8));
                    f1 = s1 / 32768.0f;
                }

                stereoSamples[i * 2] = f0;
                stereoSamples[i * 2 + 1] = f1;
            }

            // 2. Resample to 48kHz if needed (e.g. 44100 or 16000)
            if (inSampleRate != 48000)
            {
                int outSampleCount = (int)Math.Round(inSampleCount * 48000.0 / inSampleRate);
                if (outSampleCount <= 0) outSampleCount = 960;

                byte[] outPcm = new byte[outSampleCount * 4];
                double ratio = (double)inSampleCount / outSampleCount;

                for (int i = 0; i < outSampleCount; i++)
                {
                    double srcIdx = i * ratio;
                    int idx0 = (int)srcIdx;
                    int idx1 = Math.Min(idx0 + 1, inSampleCount - 1);
                    double frac = srcIdx - idx0;

                    float l = (float)((1.0 - frac) * stereoSamples[idx0 * 2] + frac * stereoSamples[idx1 * 2]);
                    float r = (float)((1.0 - frac) * stereoSamples[idx0 * 2 + 1] + frac * stereoSamples[idx1 * 2 + 1]);

                    short sl = (short)Math.Clamp((int)(l * 32767.0f), short.MinValue, short.MaxValue);
                    short sr = (short)Math.Clamp((int)(r * 32767.0f), short.MinValue, short.MaxValue);

                    int outOffset = i * 4;
                    outPcm[outOffset] = (byte)(sl & 0xFF);
                    outPcm[outOffset + 1] = (byte)((sl >> 8) & 0xFF);
                    outPcm[outOffset + 2] = (byte)(sr & 0xFF);
                    outPcm[outOffset + 3] = (byte)((sr >> 8) & 0xFF);
                }

                return outPcm;
            }
            else
            {
                // Same 48kHz, mono -> stereo conversion
                byte[] outPcm = new byte[inSampleCount * 4];
                for (int i = 0; i < inSampleCount; i++)
                {
                    short sl = (short)Math.Clamp((int)(stereoSamples[i * 2] * 32767.0f), short.MinValue, short.MaxValue);
                    short sr = (short)Math.Clamp((int)(stereoSamples[i * 2 + 1] * 32767.0f), short.MinValue, short.MaxValue);

                    int outOffset = i * 4;
                    outPcm[outOffset] = (byte)(sl & 0xFF);
                    outPcm[outOffset + 1] = (byte)((sl >> 8) & 0xFF);
                    outPcm[outOffset + 2] = (byte)(sr & 0xFF);
                    outPcm[outOffset + 3] = (byte)((sr >> 8) & 0xFF);
                }
                return outPcm;
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
