using System;
using OpenMedia.SDK.SafeHandles;

namespace OpenMedia.SDK
{
    public class FileSource : IDisposable
    {
        private readonly SafeMediaSourceHandle _handle;
        private bool _disposed = false;

        public SafeMediaSourceHandle SafeHandle => _handle;
        public IntPtr Handle => _handle.DangerousGetHandle();

        public FileSource(string uri)
        {
            _handle = NativeBridge.ome_source_create_file(uri);
            if (_handle.IsInvalid)
                throw new InvalidOperationException("Failed to create FileSource.");
        }

        public FileSource(SafeMediaSourceHandle handle)
        {
            _handle = handle ?? throw new ArgumentNullException(nameof(handle));
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

    public class Playlist : IDisposable
    {
        private readonly SafePlaylistHandle _handle;
        private bool _disposed = false;

        public SafePlaylistHandle SafeHandle => _handle;
        public IntPtr Handle => _handle.DangerousGetHandle();

        public Playlist()
        {
            _handle = NativeBridge.ome_playlist_create();
            if (_handle.IsInvalid)
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

    public class SRTSource : IDisposable
    {
        private readonly SafeSrtSourceHandle _handle;
        private bool _disposed = false;
        private string _connectedUri = string.Empty;

        public SafeSrtSourceHandle SafeHandle => _handle;
        public IntPtr Handle => _handle.DangerousGetHandle();
        public string ConnectedUri => _connectedUri;
        public bool IsConnected { get; private set; }

        public SRTSource()
        {
            _handle = NativeBridge.ome_srt_source_create();
            if (_handle.IsInvalid)
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
            if (_handle.IsInvalid || string.IsNullOrEmpty(uri)) return false;
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
            if (!_handle.IsInvalid && IsConnected)
            {
                NativeBridge.ome_srt_source_disconnect(_handle);
                IsConnected = false;
            }
        }

        public bool IsActiveConnected
        {
            get
            {
                if (_handle.IsInvalid || !IsConnected) return false;
                return NativeBridge.ome_srt_source_is_connected(_handle);
            }
        }

        public int Receive(byte[] buffer)
        {
            if (_handle.IsInvalid || !IsConnected || buffer == null || buffer.Length == 0) return -1;
            return NativeBridge.ome_srt_source_receive(_handle, buffer, buffer.Length);
        }

        public bool GetStatistics(out NativeBridge.SRTNativeStats stats)
        {
            stats = default;
            if (_handle.IsInvalid || !IsConnected) return false;
            return NativeBridge.ome_srt_source_get_stats(_handle, out stats);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    Disconnect();
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
