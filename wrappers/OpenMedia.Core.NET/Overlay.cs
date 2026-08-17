using System;

namespace OpenMedia.SDK
{
    public class ClockOverlay : IDisposable
    {
        private IntPtr _handle;
        private bool _disposed = false;

        public IntPtr Handle => _handle;

        public ClockOverlay()
        {
            _handle = NativeBridge.ome_clock_overlay_create();
            if (_handle == IntPtr.Zero)
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
                if (_handle != IntPtr.Zero)
                {
                    NativeBridge.ome_overlay_destroy(_handle);
                    _handle = IntPtr.Zero;
                }
                _disposed = true;
            }
        }

        ~ClockOverlay()
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
