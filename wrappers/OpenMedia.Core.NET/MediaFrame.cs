using System;

namespace OpenMedia.SDK
{
    public class MediaFrame : IDisposable
    {
        private IntPtr _handle;
        private bool _disposed = false;

        public IntPtr Handle => _handle;

        public static MediaFrame CreateVideo(int width, int height, PixelFormat format)
        {
            IntPtr handle = NativeBridge.ome_media_frame_create_video(width, height, (int)format);
            if (handle == IntPtr.Zero)
            {
                NativeHelper.CheckError(false, "Failed to create MediaFrame.");
            }
            return new MediaFrame(handle);
        }

        internal MediaFrame(IntPtr handle)
        {
            _handle = handle;
        }

        public (IntPtr data, int stride) GetVideoPlane(int plane)
        {
            IntPtr data;
            int stride;
            bool success = NativeBridge.ome_media_frame_get_data(_handle, plane, out data, out stride);
            NativeHelper.CheckError(success, "Failed to get video plane data.");
            return (data, stride);
        }

        public (int width, int height, PixelFormat format) GetVideoInfo()
        {
            int width, height, format;
            bool success = NativeBridge.ome_media_frame_get_video_info(_handle, out width, out height, out format);
            NativeHelper.CheckError(success, "Failed to get video info.");
            return (width, height, (PixelFormat)format);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (_handle != IntPtr.Zero)
                {
                    NativeBridge.ome_media_frame_destroy(_handle);
                    _handle = IntPtr.Zero;
                }
                _disposed = true;
            }
        }

        ~MediaFrame()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
