using System;

namespace OpenMedia.SDK
{
    public enum EncoderType
    {
        NVENC,
        QuickSync
    }

    public class MediaEncoder : IDisposable
    {
        private IntPtr _handle;
        private bool _disposed = false;

        public IntPtr Handle => _handle;

        public MediaEncoder(EncoderType type)
        {
            if (type == EncoderType.NVENC)
                _handle = NativeBridge.ome_h264_encoder_nv_create();
            else if (type == EncoderType.QuickSync)
                _handle = NativeBridge.ome_h264_encoder_qsv_create();
            
            if (_handle == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create MediaEncoder.");
        }

        public bool Initialize()
        {
            return NativeBridge.ome_encoder_initialize(_handle);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (_handle != IntPtr.Zero)
                {
                    NativeBridge.ome_encoder_destroy(_handle);
                    _handle = IntPtr.Zero;
                }
                _disposed = true;
            }
        }

        ~MediaEncoder()
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
