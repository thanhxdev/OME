using System;

namespace OpenMedia.SDK
{
    public class RTMPOutput : IDisposable
    {
        private IntPtr _handle;
        private bool _disposed = false;

        public IntPtr Handle => _handle;

        public RTMPOutput()
        {
            _handle = NativeBridge.ome_rtmp_output_create();
            if (_handle == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create RTMPOutput.");
        }

        public bool Open(string url)
        {
            return NativeBridge.ome_rtmp_output_open(_handle, url);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (_handle != IntPtr.Zero)
                {
                    NativeBridge.ome_output_destroy(_handle);
                    _handle = IntPtr.Zero;
                }
                _disposed = true;
            }
        }

        ~RTMPOutput()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }

    public class WebRTCOutput : IDisposable
    {
        private IntPtr _handle;
        private bool _disposed = false;

        public IntPtr Handle => _handle;

        public WebRTCOutput()
        {
            _handle = NativeBridge.ome_webrtc_output_create();
            if (_handle == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create WebRTCOutput.");
        }

        public bool Open(string signalingUri)
        {
            return NativeBridge.ome_webrtc_output_open(_handle, signalingUri);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (_handle != IntPtr.Zero)
                {
                    NativeBridge.ome_output_destroy(_handle);
                    _handle = IntPtr.Zero;
                }
                _disposed = true;
            }
        }

        ~WebRTCOutput()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }

    public class CallbackOutput : IDisposable
    {
        private IntPtr _handle;
        private bool _disposed = false;
        private NativeBridge.FrameCallback _callbackDelegate; // Keep reference to prevent GC
        
        public event Action<MediaFrame> OnFrameReceived;

        public IntPtr Handle => _handle;

        public CallbackOutput()
        {
            _callbackDelegate = new NativeBridge.FrameCallback(InternalCallback);
            _handle = NativeBridge.ome_callback_output_create(_callbackDelegate, IntPtr.Zero);
            if (_handle == IntPtr.Zero)
            {
                NativeHelper.CheckError(false, "Failed to create CallbackOutput.");
            }
        }

        private void InternalCallback(IntPtr frameHandle, IntPtr userData)
        {
            if (OnFrameReceived != null && frameHandle != IntPtr.Zero)
            {
                using (var frame = new MediaFrame(frameHandle))
                {
                    OnFrameReceived.Invoke(frame);
                }
            }
            else if (frameHandle != IntPtr.Zero)
            {
                NativeBridge.ome_media_frame_destroy(frameHandle);
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (_handle != IntPtr.Zero)
                {
                    NativeBridge.ome_output_destroy(_handle);
                    _handle = IntPtr.Zero;
                }
                _disposed = true;
            }
        }

        ~CallbackOutput()
        {
            Dispose(false);
        }
    }
}
