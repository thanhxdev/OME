using System;
using OpenMedia.SDK.SafeHandles;

namespace OpenMedia.SDK
{
    /// <summary>
    /// Managed wrapper for native MediaFrame with SafeHandle lifecycle and zero-copy Span support.
    /// </summary>
    public class MediaFrame : IDisposable
    {
        private readonly SafeMediaFrameHandle _safeHandle;
        private bool _disposed = false;

        public SafeMediaFrameHandle SafeHandle => _safeHandle;

        public IntPtr Handle => _safeHandle.DangerousGetHandle();

        public static MediaFrame CreateVideo(int width, int height, PixelFormat format)
        {
            var handle = NativeBridge.ome_media_frame_create_video(width, height, (int)format);
            if (handle.IsInvalid)
            {
                NativeHelper.CheckError(false, "Failed to create MediaFrame.");
            }
            return new MediaFrame(handle);
        }

        public MediaFrame(SafeMediaFrameHandle handle)
        {
            _safeHandle = handle ?? throw new ArgumentNullException(nameof(handle));
        }

        internal MediaFrame(IntPtr handle) : this(new SafeMediaFrameHandle(handle, ownsHandle: true))
        {
        }

        public (IntPtr data, int stride) GetVideoPlane(int plane)
        {
            bool success = NativeBridge.ome_media_frame_get_data(_safeHandle, plane, out IntPtr data, out int stride);
            NativeHelper.CheckError(success, "Failed to get video plane data.");
            return (data, stride);
        }

        public (int width, int height, PixelFormat format) GetVideoInfo()
        {
            bool success = NativeBridge.ome_media_frame_get_video_info(_safeHandle, out int width, out int height, out int format);
            NativeHelper.CheckError(success, "Failed to get video info.");
            return (width, height, (PixelFormat)format);
        }

        /// <summary>
        /// Exposes zero-copy memory access to the native frame plane as a MediaFrameSpan.
        /// </summary>
        /// <param name="plane">Plane index (0 for packed or luma Y plane).</param>
        /// <param name="timestampUs">Optional presentation timestamp in microseconds.</param>
        public MediaFrameSpan AsSpan(int plane = 0, long timestampUs = 0)
        {
            var (width, height, format) = GetVideoInfo();
            var (data, stride) = GetVideoPlane(plane);

            // Estimate plane byte length based on format and dimensions
            int planeHeight = height;
            if ((format == PixelFormat.NV12 || format == PixelFormat.YUV420P) && plane > 0)
            {
                planeHeight = height / 2;
            }
            int byteLength = stride * planeHeight;

            return new MediaFrameSpan(
                dataPointer: data,
                length: byteLength,
                stride: stride,
                width: width,
                height: height,
                format: format,
                timestampUs: timestampUs,
                frameHandle: _safeHandle);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _safeHandle.Dispose();
                }
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
