using System;

namespace OpenMedia.SDK
{
    public class FileSource : IDisposable
    {
        private IntPtr _handle;
        private bool _disposed = false;

        public IntPtr Handle => _handle;

        public FileSource(string uri)
        {
            _handle = NativeBridge.ome_source_create_file(uri);
            if (_handle == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create FileSource.");
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (_handle != IntPtr.Zero)
                {
                    NativeBridge.ome_source_destroy(_handle);
                    _handle = IntPtr.Zero;
                }
                _disposed = true;
            }
        }

        ~FileSource()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }

    public class Playlist : IDisposable
    {
        private IntPtr _handle;
        private bool _disposed = false;

        public IntPtr Handle => _handle;

        public Playlist()
        {
            _handle = NativeBridge.ome_playlist_create();
            if (_handle == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create Playlist.");
        }

        public bool AddItem(string uri)
        {
            return NativeBridge.ome_playlist_add_item(_handle, uri);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (_handle != IntPtr.Zero)
                {
                    NativeBridge.ome_playlist_destroy(_handle);
                    _handle = IntPtr.Zero;
                }
                _disposed = true;
            }
        }

        ~Playlist()
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
