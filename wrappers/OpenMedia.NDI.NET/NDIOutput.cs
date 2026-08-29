using System;

namespace OpenMedia.NDI
{
    public class NDIOutput : IDisposable
    {
        private IntPtr _handle;
        private bool _disposed;

        public NDIOutput(string sourceName)
        {
            _handle = NativeInterop.ome_ndi_output_create(sourceName);
            if (_handle == IntPtr.Zero)
                throw new Exception($"Failed to create NDI Output for {sourceName}");
        }

        public bool SendFrame(IntPtr frameHandle)
        {
            if (_handle != IntPtr.Zero && frameHandle != IntPtr.Zero)
                return NativeInterop.ome_ndi_output_send_frame(_handle, frameHandle);
            return false;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                if (_handle != IntPtr.Zero)
                {
                    NativeInterop.ome_ndi_output_destroy(_handle);
                    _handle = IntPtr.Zero;
                }
                _disposed = true;
                GC.SuppressFinalize(this);
            }
        }
        
        ~NDIOutput()
        {
            Dispose();
        }
    }
}
