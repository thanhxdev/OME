using System;
using OpenMedia.SDK.SafeHandles;

namespace OpenMedia.SDK
{
    public class Mixer : IDisposable
    {
        private readonly SafeVideoMixerHandle _handle;
        private bool _disposed = false;

        public SafeVideoMixerHandle SafeHandle => _handle;
        public IntPtr Handle => _handle.DangerousGetHandle();

        public Mixer()
        {
            _handle = NativeBridge.ome_mixer_create();
            if (_handle.IsInvalid)
                throw new InvalidOperationException("Failed to create Mixer.");
        }

        public bool AddInput(FileSource source, int layerIndex)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            return AddInput(source.Handle, layerIndex);
        }

        public bool AddInput(SafeMediaSourceHandle source, int layerIndex)
        {
            if (source == null || source.IsInvalid) return false;
            return NativeBridge.ome_mixer_add_input(_handle, source.DangerousGetHandle(), layerIndex);
        }

        public bool AddInput(IntPtr source, int layerIndex)
        {
            return NativeBridge.ome_mixer_add_input(_handle, source, layerIndex);
        }

        public bool SetLUT(string lutPath, float intensity)
        {
            return NativeBridge.ome_mixer_set_lut(_handle, lutPath, intensity);
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

    public class AudioMixer : IDisposable
    {
        private readonly SafeAudioMixerHandle _handle;
        private bool _disposed = false;

        public SafeAudioMixerHandle SafeHandle => _handle;
        public IntPtr Handle => _handle.DangerousGetHandle();

        public AudioMixer()
        {
            _handle = NativeBridge.ome_audio_mixer_create();
            if (_handle.IsInvalid)
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
