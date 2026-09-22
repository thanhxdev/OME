using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace OME_PLAYOUT
{
    public enum NdiFrameType
    {
        None = 0,
        Video = 1,
        Audio = 2,
        Metadata = 3,
        Error = 4,
        StatusChange = 100
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NDIlib_recv_create_v3_t
    {
        public NDIlib_source_t source_to_connect_to;
        public int color_format; // 0 = BGRX_BGRA
        public int bandwidth;    // 100 = Highest, 0 = Lowest
        [MarshalAs(UnmanagedType.I1)]
        public bool allow_video_fields;
        public IntPtr p_ndi_recv_name;
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

    internal static class NdiRecvNativeApi
    {
        public const string DllName = "Processing.NDI.Lib.x64.dll";

        [DllImport(DllName, EntryPoint = "NDIlib_recv_create_v3", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr NDIlib_recv_create_v3(ref NDIlib_recv_create_v3_t p_create_settings);

        [DllImport(DllName, EntryPoint = "NDIlib_recv_destroy", CallingConvention = CallingConvention.Cdecl)]
        public static extern void NDIlib_recv_destroy(IntPtr p_instance);

        [DllImport(DllName, EntryPoint = "NDIlib_recv_capture_v3", CallingConvention = CallingConvention.Cdecl)]
        public static extern NdiFrameType NDIlib_recv_capture_v3(
            IntPtr p_instance,
            out NDIlib_video_frame_v2_t p_video_data,
            out NDIlib_audio_frame_v3_t p_audio_data,
            IntPtr p_metadata_data,
            uint timeout_in_ms);

        [DllImport(DllName, EntryPoint = "NDIlib_recv_free_video_v2", CallingConvention = CallingConvention.Cdecl)]
        public static extern void NDIlib_recv_free_video_v2(IntPtr p_instance, ref NDIlib_video_frame_v2_t p_video_data);

        [DllImport(DllName, EntryPoint = "NDIlib_recv_free_audio_v3", CallingConvention = CallingConvention.Cdecl)]
        public static extern void NDIlib_recv_free_audio_v3(IntPtr p_instance, ref NDIlib_audio_frame_v3_t p_audio_data);
    }

    /// <summary>
    /// Native NDI Receiver for consuming live LAN NDI streams in OME_PLAYOUT.
    /// Captures BGRA video frames for live preview and on-air / cue ingest.
    /// </summary>
    public sealed class NdiNativeReceiver : IDisposable
    {
        private IntPtr _recvInstance = IntPtr.Zero;
        private readonly object _lock = new();
        private CancellationTokenSource? _cts;
        private Task? _captureTask;
        private bool _isDisposed;

        public string SourceName { get; }
        public bool IsRunning => _recvInstance != IntPtr.Zero;

        public delegate void VideoFrameHandler(byte[] bgraData, int width, int height, int stride, double fps);
        public event VideoFrameHandler? VideoFrameReceived;
        public event Action<string, string>? LogEmitted;

        public NdiNativeReceiver(string sourceName)
        {
            SourceName = sourceName ?? throw new ArgumentNullException(nameof(sourceName));
        }

        public bool Start()
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(NdiNativeReceiver));
            if (IsRunning) return true;

            if (!NdiNativeApi.EnsureInitialized())
            {
                LogEmitted?.Invoke("[WARN]", $"Không thể nạp NDI Runtime cho Receiver '{SourceName}'.");
                return false;
            }

            IntPtr pName = Marshal.StringToCoTaskMemUTF8(SourceName);
            IntPtr pRecvName = Marshal.StringToCoTaskMemUTF8("OME_PLAYOUT_PREVIEW_RECV");

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
                    color_format = 0, // BGRX_BGRA
                    bandwidth = 100,  // Highest
                    allow_video_fields = false,
                    p_ndi_recv_name = pRecvName
                };

                _recvInstance = NdiRecvNativeApi.NDIlib_recv_create_v3(ref createSettings);
                if (_recvInstance == IntPtr.Zero)
                {
                    LogEmitted?.Invoke("[ERROR]", $"Không thể tạo NDI Receiver cho: {SourceName}");
                    return false;
                }

                _cts = new CancellationTokenSource();
                var token = _cts.Token;
                _captureTask = Task.Run(() => CaptureLoop(token), token);

                LogEmitted?.Invoke("[INFO]", $"✅ [NDI-RECV] Đang thu nhận luồng NDI: {SourceName}");
                return true;
            }
            catch (Exception ex)
            {
                LogEmitted?.Invoke("[ERROR]", $"Lỗi khởi tạo NDI Receiver: {ex.Message}");
                return false;
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

                    NdiFrameType frameType = NdiRecvNativeApi.NDIlib_recv_capture_v3(
                        recv,
                        out videoFrame,
                        out audioFrame,
                        IntPtr.Zero,
                        50);

                    switch (frameType)
                    {
                        case NdiFrameType.Video:
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
                                NdiRecvNativeApi.NDIlib_recv_free_video_v2(recv, ref videoFrame);
                            }
                            break;

                        case NdiFrameType.Audio:
                            NdiRecvNativeApi.NDIlib_recv_free_audio_v3(recv, ref audioFrame);
                            break;

                        default:
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[NdiNativeReceiver] Capture exception: {ex.Message}");
                }
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                _cts?.Cancel();
                if (_recvInstance != IntPtr.Zero)
                {
                    IntPtr temp = _recvInstance;
                    _recvInstance = IntPtr.Zero;
                    try
                    {
                        NdiRecvNativeApi.NDIlib_recv_destroy(temp);
                        LogEmitted?.Invoke("[INFO]", $"Đã dừng NDI Receiver: {SourceName}");
                    }
                    catch { }
                }
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            Stop();
            _cts?.Dispose();
        }
    }
}
