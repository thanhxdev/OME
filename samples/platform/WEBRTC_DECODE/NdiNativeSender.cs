using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace WEBRTC_DECODE
{
    #region NDI Native Interop Structs

    [StructLayout(LayoutKind.Sequential)]
    public struct NDIlib_send_create_t
    {
        public IntPtr p_ndi_name;
        public IntPtr p_groups;
        [MarshalAs(UnmanagedType.I1)]
        public bool clock_video;
        [MarshalAs(UnmanagedType.I1)]
        public bool clock_audio;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NDIlib_video_frame_v2_t
    {
        public int xres;
        public int yres;
        public uint FourCC;
        public int frame_rate_N;
        public int frame_rate_D;
        public float picture_aspect_ratio;
        public int frame_format_type; // 1 = Progressive
        public long timecode;
        public IntPtr p_data;
        public int line_stride_in_bytes;
        public IntPtr p_metadata;
        public long timestamp;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NDIlib_audio_frame_v2_t
    {
        public int sample_rate;
        public int no_channels;
        public int no_samples;
        public long timecode;
        public IntPtr p_data;
        public int channel_stride_in_bytes;
        public IntPtr p_metadata;
        public long timestamp;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NDIlib_source_t
    {
        public IntPtr p_ndi_name;
        public IntPtr p_url_address;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NDIlib_find_create_t
    {
        [MarshalAs(UnmanagedType.I1)]
        public bool show_local_sources;
        public IntPtr p_groups;
        public IntPtr p_extra_ips;
    }

    internal static class NdiNativeApi
    {
        public const string DllName = "Processing.NDI.Lib.x64.dll";
        private static bool _isInitialized = false;
        private static readonly object _initLock = new();

        static NdiNativeApi()
        {
            try
            {
                NativeLibrary.SetDllImportResolver(typeof(NdiNativeApi).Assembly, ResolveNdiDll);
            }
            catch { }
        }

        private static IntPtr ResolveNdiDll(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (libraryName.Equals(DllName, StringComparison.OrdinalIgnoreCase) ||
                libraryName.Equals("Processing.NDI.Lib.x64", StringComparison.OrdinalIgnoreCase))
            {
                // 1. Check environment variables
                string? v6Dir = Environment.GetEnvironmentVariable("NDI_RUNTIME_DIR_V6");
                if (!string.IsNullOrEmpty(v6Dir))
                {
                    string candidate = Path.Combine(v6Dir, DllName);
                    if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out IntPtr h1))
                        return h1;
                }

                string? v5Dir = Environment.GetEnvironmentVariable("NDI_RUNTIME_DIR_V5");
                if (!string.IsNullOrEmpty(v5Dir))
                {
                    string candidate = Path.Combine(v5Dir, DllName);
                    if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out IntPtr h2))
                        return h2;
                }

                string? redistDir = Environment.GetEnvironmentVariable("NDILIB_REDIST_DIR");
                if (!string.IsNullOrEmpty(redistDir))
                {
                    string candidate = Path.Combine(redistDir, DllName);
                    if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out IntPtr h3))
                        return h3;
                }

                // 2. Standard Program Files locations
                string[] standardPaths =
                {
                    @"C:\Program Files\NDI\NDI 6 Tools\Runtime\Processing.NDI.Lib.x64.dll",
                    @"C:\Program Files\NDI\NDI 5 Tools\Runtime\Processing.NDI.Lib.x64.dll",
                    @"C:\Program Files\NDI\Runtime\Processing.NDI.Lib.x64.dll",
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DllName)
                };

                foreach (var path in standardPaths)
                {
                    if (File.Exists(path) && NativeLibrary.TryLoad(path, out IntPtr handle))
                    {
                        return handle;
                    }
                }
            }

            return IntPtr.Zero;
        }

        public static bool EnsureInitialized()
        {
            lock (_initLock)
            {
                if (_isInitialized) return true;
                try
                {
                    _isInitialized = NDIlib_initialize();
                    return _isInitialized;
                }
                catch
                {
                    return false;
                }
            }
        }

        [DllImport(DllName, EntryPoint = "NDIlib_initialize", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool NDIlib_initialize();

        [DllImport(DllName, EntryPoint = "NDIlib_destroy", CallingConvention = CallingConvention.Cdecl)]
        public static extern void NDIlib_destroy();

        [DllImport(DllName, EntryPoint = "NDIlib_send_create", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr NDIlib_send_create(ref NDIlib_send_create_t p_create_settings);

        [DllImport(DllName, EntryPoint = "NDIlib_send_destroy", CallingConvention = CallingConvention.Cdecl)]
        public static extern void NDIlib_send_destroy(IntPtr p_instance);

        [DllImport(DllName, EntryPoint = "NDIlib_send_send_video_v2", CallingConvention = CallingConvention.Cdecl)]
        public static extern void NDIlib_send_send_video_v2(IntPtr p_instance, ref NDIlib_video_frame_v2_t p_video_data);

        [DllImport(DllName, EntryPoint = "NDIlib_send_send_audio_v2", CallingConvention = CallingConvention.Cdecl)]
        public static extern void NDIlib_send_send_audio_v2(IntPtr p_instance, ref NDIlib_audio_frame_v2_t p_audio_data);

        [DllImport(DllName, EntryPoint = "NDIlib_find_create_v2", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr NDIlib_find_create_v2(ref NDIlib_find_create_t p_create_settings);

        [DllImport(DllName, EntryPoint = "NDIlib_find_destroy", CallingConvention = CallingConvention.Cdecl)]
        public static extern void NDIlib_find_destroy(IntPtr p_instance);

        [DllImport(DllName, EntryPoint = "NDIlib_find_wait_for_sources", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool NDIlib_find_wait_for_sources(IntPtr p_instance, uint timeout_in_ms);

        [DllImport(DllName, EntryPoint = "NDIlib_find_get_current_sources", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr NDIlib_find_get_current_sources(IntPtr p_instance, ref uint p_no_sources);
    }

    #endregion

    /// <summary>
    /// Utility scanner for discovering active NDI sources on LAN using NDIlib_find
    /// </summary>
    public static class NdiFinderScanner
    {
        public static async Task<System.Collections.Generic.List<string>> ScanSourcesAsync(uint timeoutMs = 1500)
        {
            return await Task.Run(() =>
            {
                var list = new System.Collections.Generic.List<string>();
                if (!NdiNativeApi.EnsureInitialized()) return list;

                var settings = new NDIlib_find_create_t
                {
                    show_local_sources = true,
                    p_groups = IntPtr.Zero,
                    p_extra_ips = IntPtr.Zero
                };

                IntPtr pFind = IntPtr.Zero;
                try
                {
                    pFind = NdiNativeApi.NDIlib_find_create_v2(ref settings);
                    if (pFind == IntPtr.Zero) return list;

                    // Wait for mDNS / NDI announcements
                    NdiNativeApi.NDIlib_find_wait_for_sources(pFind, timeoutMs);

                    uint noSources = 0;
                    IntPtr pSources = NdiNativeApi.NDIlib_find_get_current_sources(pFind, ref noSources);
                    if (pSources != IntPtr.Zero && noSources > 0)
                    {
                        int structSize = Marshal.SizeOf<NDIlib_source_t>();
                        for (int i = 0; i < noSources; i++)
                        {
                            IntPtr itemPtr = IntPtr.Add(pSources, i * structSize);
                            var src = Marshal.PtrToStructure<NDIlib_source_t>(itemPtr);
                            string? name = Marshal.PtrToStringUTF8(src.p_ndi_name);
                            if (!string.IsNullOrEmpty(name) && !list.Contains(name))
                            {
                                list.Add(name);
                            }
                        }
                    }
                }
                catch { }
                finally
                {
                    if (pFind != IntPtr.Zero)
                    {
                        try { NdiNativeApi.NDIlib_find_destroy(pFind); } catch { }
                    }
                }

                return list;
            }).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Broadcast NDI Native Sender for Master Program or ISO feeds.
    /// Emits progressive BGRA video frames and 48kHz Stereo PCM audio onto local LAN.
    /// </summary>
    public sealed class NdiNativeSender : IDisposable
    {
        // NDIlib_FourCC_video_type_BGRA = 'B' | ('G'<<8) | ('R'<<16) | ('A'<<24) = 0x41524742
        private const uint FOURCC_BGRA = 0x41524742;

        private IntPtr _sendInstance = IntPtr.Zero;
        private readonly object _sendLock = new();
        private bool _isDisposed;

        // Reusable audio buffer for converting 16-bit PCM to planar float
        private float[] _audioFloatBuffer = new float[9600 * 2];
        private IntPtr _unmanagedAudioPtr = IntPtr.Zero;
        private int _unmanagedAudioCapacity = 0;

        public string StreamName { get; }
        public bool IsRunning => _sendInstance != IntPtr.Zero;

        public event Action<string, string>? LogEmitted;

        public NdiNativeSender(string streamName)
        {
            StreamName = streamName ?? "OME_STUDIO_PROGRAM";
        }

        public bool Start()
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(NdiNativeSender));
            if (IsRunning) return true;

            if (!NdiNativeApi.EnsureInitialized())
            {
                LogEmitted?.Invoke("[WARN]", $"Không thể khởi tạo NDI Sender '{StreamName}': Chưa cài đặt Processing.NDI.Lib (NDI 6/5 Tools).");
                return false;
            }

            IntPtr pStreamName = Marshal.StringToCoTaskMemUTF8(StreamName);
            try
            {
                var settings = new NDIlib_send_create_t
                {
                    p_ndi_name = pStreamName,
                    p_groups = IntPtr.Zero,
                    clock_video = true,
                    clock_audio = true
                };

                _sendInstance = NdiNativeApi.NDIlib_send_create(ref settings);
                if (_sendInstance == IntPtr.Zero)
                {
                    LogEmitted?.Invoke("[ERROR]", $"Không thể tạo NDI Sender instance cho: '{StreamName}'");
                    return false;
                }

                LogEmitted?.Invoke("[INFO]", $"✅ [NDI] Đã phát sóng luồng mạng NDI: '{StreamName}'");
                return true;
            }
            catch (Exception ex)
            {
                LogEmitted?.Invoke("[ERROR]", $"Lỗi khởi chạy NDI Sender '{StreamName}': {ex.Message}");
                return false;
            }
            finally
            {
                Marshal.FreeCoTaskMem(pStreamName);
            }
        }

        public void SendVideoFrame(byte[] bgraBytes, int width, int height, double fps = 59.94)
        {
            if (!IsRunning || bgraBytes == null || bgraBytes.Length == 0 || width <= 0 || height <= 0) return;

            lock (_sendLock)
            {
                if (_sendInstance == IntPtr.Zero) return;

                int stride = width * 4;
                int frameSize = height * stride;
                if (bgraBytes.Length < frameSize) return;

                int fpsNum = (int)Math.Round(fps * 1000.0);
                int fpsDen = 1000;
                if (Math.Abs(fps - 59.94) < 0.05)
                {
                    fpsNum = 60000;
                    fpsDen = 1001;
                }
                else if (Math.Abs(fps - 29.97) < 0.05)
                {
                    fpsNum = 30000;
                    fpsDen = 1001;
                }

                GCHandle pin = GCHandle.Alloc(bgraBytes, GCHandleType.Pinned);
                try
                {
                    var videoFrame = new NDIlib_video_frame_v2_t
                    {
                        xres = width,
                        yres = height,
                        FourCC = FOURCC_BGRA,
                        frame_rate_N = fpsNum,
                        frame_rate_D = fpsDen,
                        picture_aspect_ratio = (float)width / height,
                        frame_format_type = 1, // Progressive
                        timecode = 0,
                        p_data = pin.AddrOfPinnedObject(),
                        line_stride_in_bytes = stride,
                        p_metadata = IntPtr.Zero,
                        timestamp = 0
                    };

                    NdiNativeApi.NDIlib_send_send_video_v2(_sendInstance, ref videoFrame);
                }
                finally
                {
                    pin.Free();
                }
            }
        }

        public void SendAudioFrame(byte[] pcmBytes, int byteCount, int sampleRate = 48000, int channels = 2)
        {
            if (!IsRunning || pcmBytes == null || byteCount <= 0 || channels <= 0) return;

            lock (_sendLock)
            {
                if (_sendInstance == IntPtr.Zero) return;

                int bytesPerSample = 2; // 16-bit
                int totalSamples = byteCount / (bytesPerSample * channels);
                if (totalSamples <= 0) return;

                int totalFloats = totalSamples * channels;
                if (_audioFloatBuffer.Length < totalFloats)
                {
                    _audioFloatBuffer = new float[totalFloats * 2];
                }

                // Convert 16-bit interleaved PCM to planar float format for NDI audio v2
                // Planar: Channel 0 (L) [sample 0..N-1], followed by Channel 1 (R) [sample 0..N-1]
                int chStrideInBytes = totalSamples * sizeof(float);
                int neededBytes = chStrideInBytes * channels;

                if (_unmanagedAudioPtr == IntPtr.Zero || _unmanagedAudioCapacity < neededBytes)
                {
                    if (_unmanagedAudioPtr != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(_unmanagedAudioPtr);
                    }
                    _unmanagedAudioCapacity = neededBytes * 2;
                    _unmanagedAudioPtr = Marshal.AllocHGlobal(_unmanagedAudioCapacity);
                }

                for (int s = 0; s < totalSamples; s++)
                {
                    int pcmIdx = s * 4;
                    short sL = (short)(pcmBytes[pcmIdx] | (pcmBytes[pcmIdx + 1] << 8));
                    short sR = (short)(pcmBytes[pcmIdx + 2] | (pcmBytes[pcmIdx + 3] << 8));

                    // Channel 0 (Left)
                    _audioFloatBuffer[s] = sL / 32768.0f;
                    // Channel 1 (Right)
                    _audioFloatBuffer[totalSamples + s] = sR / 32768.0f;
                }

                Marshal.Copy(_audioFloatBuffer, 0, _unmanagedAudioPtr, totalSamples * channels);

                var audioFrame = new NDIlib_audio_frame_v2_t
                {
                    sample_rate = sampleRate,
                    no_channels = channels,
                    no_samples = totalSamples,
                    timecode = 0,
                    p_data = _unmanagedAudioPtr,
                    channel_stride_in_bytes = chStrideInBytes,
                    p_metadata = IntPtr.Zero,
                    timestamp = 0
                };

                NdiNativeApi.NDIlib_send_send_audio_v2(_sendInstance, ref audioFrame);
            }
        }

        public void Stop()
        {
            lock (_sendLock)
            {
                if (_sendInstance != IntPtr.Zero)
                {
                    IntPtr temp = _sendInstance;
                    _sendInstance = IntPtr.Zero;
                    try
                    {
                        NdiNativeApi.NDIlib_send_destroy(temp);
                        LogEmitted?.Invoke("[INFO]", $"Đã dừng NDI Sender: '{StreamName}'");
                    }
                    catch { }
                }

                if (_unmanagedAudioPtr != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_unmanagedAudioPtr);
                    _unmanagedAudioPtr = IntPtr.Zero;
                    _unmanagedAudioCapacity = 0;
                }
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            Stop();
        }
    }
}
