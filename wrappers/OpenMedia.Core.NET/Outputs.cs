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

    public class SRTOutput : IDisposable
    {
        private IntPtr _handle;
        private bool _disposed = false;
        private string _openedUri = string.Empty;

        public IntPtr Handle => _handle;
        public string OpenedUri => _openedUri;
        public bool IsOpen { get; private set; }

        public SRTOutput()
        {
            _handle = NativeBridge.ome_srt_output_create();
            if (_handle == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create SRTOutput.");
        }

        public SRTOutput(string uri) : this()
        {
            if (!string.IsNullOrEmpty(uri))
            {
                Open(uri);
            }
        }

        public bool Open(string uri)
        {
            if (_handle == IntPtr.Zero || string.IsNullOrEmpty(uri)) return false;
            bool success = NativeBridge.ome_srt_output_open(_handle, uri);
            if (success)
            {
                _openedUri = uri;
                IsOpen = true;
            }
            return success;
        }

        public void Close()
        {
            if (_handle != IntPtr.Zero && IsOpen)
            {
                NativeBridge.ome_srt_output_close(_handle);
                IsOpen = false;
            }
        }

        public bool IsConnected
        {
            get
            {
                if (_handle == IntPtr.Zero || !IsOpen) return false;
                return NativeBridge.ome_srt_output_is_connected(_handle);
            }
        }

        public bool Send(byte[] data)
        {
            if (_handle == IntPtr.Zero || !IsOpen || data == null || data.Length == 0) return false;
            return NativeBridge.ome_srt_output_send(_handle, data, data.Length);
        }

        public bool GetStatistics(out NativeBridge.SRTNativeStats stats)
        {
            stats = default;
            if (_handle == IntPtr.Zero || !IsOpen) return false;
            return NativeBridge.ome_srt_output_get_stats(_handle, out stats);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (_handle != IntPtr.Zero)
                {
                    Close();
                    NativeBridge.ome_srt_output_destroy(_handle);
                    _handle = IntPtr.Zero;
                }
                _disposed = true;
            }
        }

        ~SRTOutput()
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

