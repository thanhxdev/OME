using System;
using OpenMedia.SDK.SafeHandles;

namespace OpenMedia.SDK
{
    public class SRTEngine : IDisposable
    {
        private readonly SafeSrtEngineHandle _handle;
        private bool _disposed = false;

        public SafeSrtEngineHandle SafeHandle => _handle;
        public IntPtr Handle => _handle.DangerousGetHandle();

        public SRTEngine()
        {
            _handle = NativeBridge.ome_srt_engine_create();
            if (_handle.IsInvalid)
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
                if (disposing)
                {
                    _handle.Dispose();
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

    public class NDIEngine : IDisposable
    {
        private readonly SafeNdiEngineHandle _handle;
        private bool _disposed = false;

        public SafeNdiEngineHandle SafeHandle => _handle;
        public IntPtr Handle => _handle.DangerousGetHandle();

        public NDIEngine()
        {
            _handle = NativeBridge.ome_ndi_engine_create();
            if (_handle.IsInvalid)
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
                if (disposing)
                {
                    _handle.Dispose();
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

    public class WebRTCEngine : IDisposable
    {
        private readonly SafeWebRtcEngineHandle _handle;
        private bool _disposed = false;

        public SafeWebRtcEngineHandle SafeHandle => _handle;
        public IntPtr Handle => _handle.DangerousGetHandle();

        public WebRTCEngine()
        {
            _handle = NativeBridge.ome_webrtc_engine_create();
            if (_handle.IsInvalid)
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
                if (disposing)
                {
                    _handle.Dispose();
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

    public class CGEngine : IDisposable
    {
        private readonly SafeCgEngineHandle _handle;
        private bool _disposed = false;

        public SafeCgEngineHandle SafeHandle => _handle;
        public IntPtr Handle => _handle.DangerousGetHandle();

        public CGEngine()
        {
            _handle = NativeBridge.ome_cg_engine_create();
            if (_handle.IsInvalid)
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
                if (disposing)
                {
                    _handle.Dispose();
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
