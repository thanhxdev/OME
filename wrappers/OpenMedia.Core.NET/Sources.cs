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

    public class SRTSource : IDisposable
    {
        private IntPtr _handle;
        private bool _disposed = false;
        private string _connectedUri = string.Empty;

        public IntPtr Handle => _handle;
        public string ConnectedUri => _connectedUri;
        public bool IsConnected { get; private set; }

        public SRTSource()
        {
            _handle = NativeBridge.ome_srt_source_create();
            if (_handle == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create SRTSource.");
        }

        public SRTSource(string uri) : this()
        {
            if (!string.IsNullOrEmpty(uri))
            {
                Connect(uri);
            }
        }

        public bool Connect(string uri)
        {
            if (_handle == IntPtr.Zero || string.IsNullOrEmpty(uri)) return false;
            bool success = NativeBridge.ome_srt_source_connect(_handle, uri);
            if (success)
            {
                _connectedUri = uri;
                IsConnected = true;
            }
            return success;
        }

        public void Disconnect()
        {
            if (_handle != IntPtr.Zero && IsConnected)
            {
                NativeBridge.ome_srt_source_disconnect(_handle);
                IsConnected = false;
            }
        }

        public bool IsActiveConnected
        {
            get
            {
                if (_handle == IntPtr.Zero || !IsConnected) return false;
                return NativeBridge.ome_srt_source_is_connected(_handle);
            }
        }

        public int Receive(byte[] buffer)
        {
            if (_handle == IntPtr.Zero || !IsConnected || buffer == null || buffer.Length == 0) return -1;
            return NativeBridge.ome_srt_source_receive(_handle, buffer, buffer.Length);
        }

        public bool GetStatistics(out NativeBridge.SRTNativeStats stats)
        {
            stats = default;
            if (_handle == IntPtr.Zero || !IsConnected) return false;
            return NativeBridge.ome_srt_source_get_stats(_handle, out stats);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (_handle != IntPtr.Zero)
                {
                    Disconnect();
                    NativeBridge.ome_srt_source_destroy(_handle);
                    _handle = IntPtr.Zero;
                }
                _disposed = true;
            }
        }

        ~SRTSource()
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

