using System;
using OpenMedia.SDK.SafeHandles;

namespace OpenMedia.SDK
{
    public class ClockOverlay : IDisposable
    {
        private readonly SafeOverlayHandle _handle;
        private bool _disposed = false;

        public SafeOverlayHandle SafeHandle => _handle;
        public IntPtr Handle => _handle.DangerousGetHandle();

        public ClockOverlay()
        {
            _handle = NativeBridge.ome_clock_overlay_create();
            if (_handle.IsInvalid)
                throw new InvalidOperationException("Failed to create ClockOverlay.");
        }

        public void SetFormat(string format)
        {
            NativeBridge.ome_clock_overlay_set_format(_handle, format);
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
