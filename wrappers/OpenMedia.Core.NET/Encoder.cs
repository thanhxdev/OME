using System;
using OpenMedia.SDK.SafeHandles;

namespace OpenMedia.SDK
{
    public enum EncoderType
    {
        NVENC,
        QuickSync
    }

    public class MediaEncoder : IDisposable
    {
        private readonly SafeEncoderHandle _handle;
        private bool _disposed = false;

        public SafeEncoderHandle SafeHandle => _handle;
        public IntPtr Handle => _handle.DangerousGetHandle();

        public MediaEncoder(EncoderType type)
        {
            if (type == EncoderType.NVENC)
                _handle = NativeBridge.ome_h264_encoder_nv_create();
            else if (type == EncoderType.QuickSync)
                _handle = NativeBridge.ome_h264_encoder_qsv_create();
            else
                throw new ArgumentOutOfRangeException(nameof(type));
            
            if (_handle.IsInvalid)
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
