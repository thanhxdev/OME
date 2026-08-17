using System;
using OpenMedia.Core;

namespace OpenMedia.NDI
{
    public class NDISource : IDisposable
    {
        private IntPtr _handle;
        private bool _disposed;

        public NDISource(string sourceName)
        {
            _handle = NativeInterop.ome_ndi_source_create(sourceName);
            if (_handle == IntPtr.Zero)
                throw new Exception($"Failed to create NDI Source for {sourceName}");
        }

        public void Start()
        {
            if (_handle != IntPtr.Zero)
                NativeInterop.ome_ndi_source_start(_handle);
        }

        public void Stop()
        {
            if (_handle != IntPtr.Zero)
                NativeInterop.ome_ndi_source_stop(_handle);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                if (_handle != IntPtr.Zero)
                {
                    Stop();
                    NativeInterop.ome_ndi_source_destroy(_handle);
                    _handle = IntPtr.Zero;
                }
                _disposed = true;
                GC.SuppressFinalize(this);
            }
        }
        
        ~NDISource()
        {
            Dispose();
        }
    }
}
