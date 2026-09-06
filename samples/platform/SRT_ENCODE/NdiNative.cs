using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace SRT_ENCODE
{
    #region NDI Native Types and Enums

    public enum NDIlib_frame_type_e
    {
        None = 0,
        Video = 1,
        Audio = 2,
        Metadata = 3,
        Error = 4,
        StatusChange = 100
    }

    public enum NDIlib_recv_color_format_e
    {
        BGRX_BGRA = 0,
        UYVY_BGRA = 1,
        RGBX_RGBA = 2,
        UYVY_RGBA = 3,
        Fastest = 100,
        Best = 101
    }

    public enum NDIlib_recv_bandwidth_e
    {
        MetadataOnly = -10,
        AudioOnly = 10,
        Lowest = 0,
        Highest = 100
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

    [StructLayout(LayoutKind.Sequential)]
    public struct NDIlib_recv_create_v3_t
    {
        public NDIlib_source_t source_to_connect_to;
        public int color_format;
        public int bandwidth;
        [MarshalAs(UnmanagedType.I1)]
        public bool allow_video_fields;
        public IntPtr p_ndi_recv_name;
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
        public int frame_format_type;
        public long timecode;
        public IntPtr p_data;
        public int line_stride_in_bytes;
        public IntPtr p_metadata;
        public long timestamp;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NDIlib_audio_frame_v3_t
    {
        public int sample_rate;
        public int no_channels;
        public int no_samples;
        public long timecode;
        public uint FourCC;
        public IntPtr p_data;
        public int channel_stride_in_bytes;
        public IntPtr p_metadata;
        public long timestamp;
    }

    #endregion

    #region NDI Native Interop API

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
            catch
            {
                // In case already set
            }
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
                    {
                        return h1;
                    }
                }

                string? v5Dir = Environment.GetEnvironmentVariable("NDI_RUNTIME_DIR_V5");
                if (!string.IsNullOrEmpty(v5Dir))
                {
                    string candidate = Path.Combine(v5Dir, DllName);
                    if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out IntPtr h2))
                    {
                        return h2;
                    }
                }

                string? redistDir = Environment.GetEnvironmentVariable("NDILIB_REDIST_DIR");
                if (!string.IsNullOrEmpty(redistDir))
                {
                    string candidate = Path.Combine(redistDir, DllName);
                    if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out IntPtr h3))
                    {
                        return h3;
                    }
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

        [DllImport(DllName, EntryPoint = "NDIlib_find_create_v2", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr NDIlib_find_create_v2(ref NDIlib_find_create_t p_create_settings);

        [DllImport(DllName, EntryPoint = "NDIlib_find_destroy", CallingConvention = CallingConvention.Cdecl)]
        public static extern void NDIlib_find_destroy(IntPtr p_instance);

        [DllImport(DllName, EntryPoint = "NDIlib_find_wait_for_sources", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool NDIlib_find_wait_for_sources(IntPtr p_instance, uint timeout_in_ms);

        [DllImport(DllName, EntryPoint = "NDIlib_find_get_current_sources", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr NDIlib_find_get_current_sources(IntPtr p_instance, out uint p_no_sources);

        [DllImport(DllName, EntryPoint = "NDIlib_recv_create_v3", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr NDIlib_recv_create_v3(ref NDIlib_recv_create_v3_t p_create_settings);

        [DllImport(DllName, EntryPoint = "NDIlib_recv_destroy", CallingConvention = CallingConvention.Cdecl)]
        public static extern void NDIlib_recv_destroy(IntPtr p_instance);

        [DllImport(DllName, EntryPoint = "NDIlib_recv_capture_v3", CallingConvention = CallingConvention.Cdecl)]
        public static extern NDIlib_frame_type_e NDIlib_recv_capture_v3(
            IntPtr p_instance,
            out NDIlib_video_frame_v2_t p_video_data,
            out NDIlib_audio_frame_v3_t p_audio_data,
            IntPtr p_metadata,
            uint timeout_in_ms);

        [DllImport(DllName, EntryPoint = "NDIlib_recv_free_video_v2", CallingConvention = CallingConvention.Cdecl)]
        public static extern void NDIlib_recv_free_video_v2(IntPtr p_instance, ref NDIlib_video_frame_v2_t p_video_data);

        [DllImport(DllName, EntryPoint = "NDIlib_recv_free_audio_v3", CallingConvention = CallingConvention.Cdecl)]
        public static extern void NDIlib_recv_free_audio_v3(IntPtr p_instance, ref NDIlib_audio_frame_v3_t p_audio_data);
    }

    #endregion

    #region NdiNativeFinder

    public static class NdiNativeFinder
    {
        public static Task<List<string>> FindSourcesAsync(int timeoutMs = 800)
        {
            return Task.Run(() =>
            {
                var result = new List<string>();
                if (!NdiNativeApi.EnsureInitialized())
                {
                    return result;
                }

                var settings = new NDIlib_find_create_t
                {
                    show_local_sources = true,
                    p_groups = IntPtr.Zero,
                    p_extra_ips = IntPtr.Zero
                };

                IntPtr finder = IntPtr.Zero;
                try
                {
                    finder = NdiNativeApi.NDIlib_find_create_v2(ref settings);
                    if (finder == IntPtr.Zero) return result;

                    // Wait for mDNS response packets from LAN
                    NdiNativeApi.NDIlib_find_wait_for_sources(finder, (uint)Math.Max(200, timeoutMs));

                    IntPtr sourcesPtr = NdiNativeApi.NDIlib_find_get_current_sources(finder, out uint count);
                    if (sourcesPtr != IntPtr.Zero && count > 0)
                    {
                        int structSize = Marshal.SizeOf<NDIlib_source_t>();
                        for (int i = 0; i < count; i++)
                        {
                            IntPtr currentPtr = IntPtr.Add(sourcesPtr, i * structSize);
                            var source = Marshal.PtrToStructure<NDIlib_source_t>(currentPtr);
                            string? name = Marshal.PtrToStringUTF8(source.p_ndi_name);
                            if (!string.IsNullOrWhiteSpace(name) && !result.Contains(name))
                            {
                                result.Add(name);
                            }
                        }
                    }
                }
                catch
                {
                    // Graceful fallback
                }
                finally
                {
                    if (finder != IntPtr.Zero)
                    {
                        NdiNativeApi.NDIlib_find_destroy(finder);
                    }
                }

                return result;
            });
        }
    }

    #endregion

    #region NdiReceiver

    public sealed class NdiReceiver : IDisposable
    {
        private IntPtr _recvInstance = IntPtr.Zero;
        private CancellationTokenSource? _cts;
        private Task? _captureTask;
        private bool _isDisposed;

        public string SourceName { get; }
        public bool IsRunning => _recvInstance != IntPtr.Zero && _cts != null && !_cts.IsCancellationRequested;

        public event Action<byte[], int, int, int, double>? VideoFrameReceived;
        public event Action<float[], int, int>? AudioSamplesReceived;
        public event Action<string, string>? LogRequested;

        public NdiReceiver(string sourceName)
        {
            SourceName = sourceName ?? throw new ArgumentNullException(nameof(sourceName));
        }

        public bool Start()
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(NdiReceiver));
            if (IsRunning) return true;

            if (!NdiNativeApi.EnsureInitialized())
            {
                LogRequested?.Invoke("[ERROR]", "Không thể nạp hoặc khởi tạo Processing.NDI.Lib runtime.");
                return false;
            }

            IntPtr pName = Marshal.StringToCoTaskMemUTF8(SourceName);
            IntPtr pRecvName = Marshal.StringToCoTaskMemUTF8("OME SRT NDI Receiver");

            try
            {
                var src = new NDIlib_source_t
                {
                    p_ndi_name = pName,
                    p_url_address = IntPtr.Zero
                };

                var createSettings = new NDIlib_recv_create_v3_t
                {
                    source_to_connect_to = src,
                    color_format = (int)NDIlib_recv_color_format_e.BGRX_BGRA,
                    bandwidth = (int)NDIlib_recv_bandwidth_e.Highest,
                    allow_video_fields = false,
                    p_ndi_recv_name = pRecvName
                };

                _recvInstance = NdiNativeApi.NDIlib_recv_create_v3(ref createSettings);
                if (_recvInstance == IntPtr.Zero)
                {
                    LogRequested?.Invoke("[ERROR]", $"Không thể tạo NDI Receiver instance cho nguồn: {SourceName}");
                    return false;
                }

                _cts = new CancellationTokenSource();
                var token = _cts.Token;
                _captureTask = Task.Run(() => CaptureLoop(token), token);

                LogRequested?.Invoke("[INFO]", $"✅ [NDI] Đã khởi tạo NDI Receiver cho: {SourceName}");
                return true;
            }
            finally
            {
                Marshal.FreeCoTaskMem(pName);
                Marshal.FreeCoTaskMem(pRecvName);
            }
        }

        private void CaptureLoop(CancellationToken token)
        {
            IntPtr recv = _recvInstance;
            if (recv == IntPtr.Zero) return;

            while (!token.IsCancellationRequested && !_isDisposed)
            {
                try
                {
                    NDIlib_video_frame_v2_t videoFrame;
                    NDIlib_audio_frame_v3_t audioFrame;

                    NDIlib_frame_type_e frameType = NdiNativeApi.NDIlib_recv_capture_v3(
                        recv,
                        out videoFrame,
                        out audioFrame,
                        IntPtr.Zero,
                        50);

                    switch (frameType)
                    {
                        case NDIlib_frame_type_e.Video:
                            try
                            {
                                int width = videoFrame.xres;
                                int height = videoFrame.yres;
                                int stride = videoFrame.line_stride_in_bytes;
                                if (stride <= 0) stride = width * 4;

                                int dataSize = height * stride;
                                if (videoFrame.p_data != IntPtr.Zero && dataSize > 0)
                                {
                                    byte[] managedBuffer = new byte[dataSize];
                                    Marshal.Copy(videoFrame.p_data, managedBuffer, 0, dataSize);

                                    double fps = 59.94;
                                    if (videoFrame.frame_rate_D > 0)
                                    {
                                        fps = (double)videoFrame.frame_rate_N / videoFrame.frame_rate_D;
                                    }

                                    VideoFrameReceived?.Invoke(managedBuffer, width, height, stride, fps);
                                }
                            }
                            finally
                            {
                                NdiNativeApi.NDIlib_recv_free_video_v2(recv, ref videoFrame);
                            }
                            break;

                        case NDIlib_frame_type_e.Audio:
                            try
                            {
                                int samples = audioFrame.no_samples;
                                int channels = audioFrame.no_channels;
                                int sampleRate = audioFrame.sample_rate;
                                int totalFloats = samples * channels;

                                if (audioFrame.p_data != IntPtr.Zero && totalFloats > 0)
                                {
                                    float[] floatBuffer = new float[totalFloats];
                                    Marshal.Copy(audioFrame.p_data, floatBuffer, 0, totalFloats);

                                    AudioSamplesReceived?.Invoke(floatBuffer, channels, sampleRate);
                                }
                            }
                            finally
                            {
                                NdiNativeApi.NDIlib_recv_free_audio_v3(recv, ref audioFrame);
                            }
                            break;

                        case NDIlib_frame_type_e.None:
                            // Timeout expired with no new frames, loop again
                            break;

                        case NDIlib_frame_type_e.StatusChange:
                            // Connection status changed
                            break;

                        default:
                            break;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogRequested?.Invoke("[WARN]", $"NDI Capture loop warning: {ex.Message}");
                    Thread.Sleep(10);
                }
            }
        }

        public void Stop()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                try
                {
                    _captureTask?.Wait(250);
                }
                catch { }
                _cts.Dispose();
                _cts = null;
            }

            if (_recvInstance != IntPtr.Zero)
            {
                IntPtr temp = _recvInstance;
                _recvInstance = IntPtr.Zero;
                try
                {
                    NdiNativeApi.NDIlib_recv_destroy(temp);
                }
                catch { }
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            Stop();
        }
    }

    #endregion
}
