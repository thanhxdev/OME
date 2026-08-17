using System;

namespace OpenMedia.SDK
{
    public class SRTEngine : IDisposable
    {
        private IntPtr _handle;
        private bool _disposed = false;

        public IntPtr Handle => _handle;

        public SRTEngine()
        {
            _handle = NativeBridge.ome_srt_engine_create();
            if (_handle == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create SRTEngine.");
        }

        public bool Initialize()
        {
            return NativeBridge.ome_srt_engine_init(_handle);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (_handle != IntPtr.Zero)
                {
                    NativeBridge.ome_srt_engine_destroy(_handle);
                    _handle = IntPtr.Zero;
                }
                _disposed = true;
            }
        }

        ~SRTEngine()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }

    public class NDIEngine : IDisposable
    {
        private IntPtr _handle;
        private bool _disposed = false;

        public IntPtr Handle => _handle;

        public NDIEngine()
        {
            _handle = NativeBridge.ome_ndi_engine_create();
            if (_handle == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create NDIEngine.");
        }

        public bool Initialize()
        {
            return NativeBridge.ome_ndi_engine_init(_handle);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (_handle != IntPtr.Zero)
                {
                    NativeBridge.ome_ndi_engine_destroy(_handle);
                    _handle = IntPtr.Zero;
                }
                _disposed = true;
            }
        }

        ~NDIEngine()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }

    public class WebRTCEngine : IDisposable
    {
        private IntPtr _handle;
        private bool _disposed = false;

        public IntPtr Handle => _handle;

        public WebRTCEngine()
        {
            _handle = NativeBridge.ome_webrtc_engine_create();
            if (_handle == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create WebRTCEngine.");
        }

        public bool Initialize()
        {
            return NativeBridge.ome_webrtc_engine_init(_handle);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (_handle != IntPtr.Zero)
                {
                    NativeBridge.ome_webrtc_engine_destroy(_handle);
                    _handle = IntPtr.Zero;
                }
                _disposed = true;
            }
        }

        ~WebRTCEngine()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }

    public class CGEngine : IDisposable
    {
        private IntPtr _handle;
        private bool _disposed = false;

        public IntPtr Handle => _handle;

        public CGEngine()
        {
            _handle = NativeBridge.ome_cg_engine_create();
            if (_handle == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create CGEngine.");
        }

        public bool LoadTemplate(string templateData)
        {
            return NativeBridge.ome_cg_engine_load_template(_handle, templateData);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (_handle != IntPtr.Zero)
                {
                    NativeBridge.ome_cg_engine_destroy(_handle);
                    _handle = IntPtr.Zero;
                }
                _disposed = true;
            }
        }

        ~CGEngine()
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
