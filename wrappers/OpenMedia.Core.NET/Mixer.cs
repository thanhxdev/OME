using System;

namespace OpenMedia.SDK
{
    public class Mixer : IDisposable
    {
        private IntPtr _handle;
        private bool _disposed = false;

        public IntPtr Handle => _handle;

        public Mixer()
        {
            _handle = NativeBridge.ome_mixer_create();
            if (_handle == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create Mixer.");
        }

        public bool AddInput(FileSource source, int layerIndex)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            return NativeBridge.ome_mixer_add_input(_handle, source.Handle, layerIndex);
        }

        public bool SetLUT(string lutPath, float intensity)
        {
            return NativeBridge.ome_mixer_set_lut(_handle, lutPath, intensity);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (_handle != IntPtr.Zero)
                {
                    NativeBridge.ome_mixer_destroy(_handle);
                    _handle = IntPtr.Zero;
                }
                _disposed = true;
            }
        }

        ~Mixer()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }

    public class AudioMixer : IDisposable
    {
        private IntPtr _handle;
        private bool _disposed = false;

        public IntPtr Handle => _handle;

        public AudioMixer()
        {
            _handle = NativeBridge.ome_audio_mixer_create();
            if (_handle == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create AudioMixer.");
        }

        public bool SetChannelVolume(int channel, float volume)
        {
            return NativeBridge.ome_audio_mixer_set_channel_volume(_handle, channel, volume);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (_handle != IntPtr.Zero)
                {
                    NativeBridge.ome_audio_mixer_destroy(_handle);
                    _handle = IntPtr.Zero;
                }
                _disposed = true;
            }
        }

        ~AudioMixer()
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
