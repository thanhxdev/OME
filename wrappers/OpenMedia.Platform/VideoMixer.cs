using System.Diagnostics;
using OpenMedia.SDK;
using OpenMedia.Platform.Internal;

namespace OpenMedia.Platform
{
    /// <summary>
    /// Multi-layer vision mixer for combining multiple video sources
    /// with transitions and routing to multiple outputs.
    /// <para>
    /// Example:
    /// <code>
    /// var mixer = new VideoMixer(1920, 1080);
    /// mixer.AddSource("camera1.mp4");
    /// mixer.AddSource("camera2.mp4");
    /// mixer.AttachPreview(videoView);
    /// mixer.AddOutput(StreamOutput.RTMP("rtmp://..."));
    /// await mixer.StartAsync();
    /// await mixer.SwitchToAsync(1, TransitionType.Dissolve, TimeSpan.FromSeconds(1));
    /// </code>
    /// </para>
    /// </summary>
    public sealed class VideoMixer : IDisposable
    {
        private readonly int _width;
        private readonly int _height;
        private readonly double _frameRate;
        private readonly List<string> _sources = new();
        private readonly List<StreamOutput> _outputs = new();
        private uint _pipelineId;
        private bool _pipelineCreated;
        private bool _disposed;

        /// <summary>
        /// Creates a new <see cref="VideoMixer"/> with the specified output resolution and frame rate.
        /// </summary>
        /// <param name="width">Output width in pixels. Default: 1920.</param>
        /// <param name="height">Output height in pixels. Default: 1080.</param>
        /// <param name="frameRate">Output frame rate. Default: 29.97.</param>
        public VideoMixer(int width = 1920, int height = 1080, double frameRate = 29.97)
        {
            _width = width;
            _height = height;
            _frameRate = frameRate;
        }

        /// <summary>
        /// Adds a file or stream source to a new layer. Returns the layer index.
        /// </summary>
        /// <param name="uri">Path or URL to the media source.</param>
        /// <returns>The zero-based layer index of the added source.</returns>
        public int AddSource(string uri)
        {
            ThrowIfDisposed();
            _sources.Add(uri);
            Trace.WriteLine($"[VideoMixer] Added source layer {_sources.Count - 1}: {uri}");
            return _sources.Count - 1;
        }

        /// <summary>
        /// Adds an already-created <see cref="MediaPlayer"/> as a source layer.
        /// </summary>
        /// <param name="player">The player to use as a source.</param>
        /// <returns>The zero-based layer index.</returns>
        public int AddSource(MediaPlayer player)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(player);
            // Use the player's source URI as a reference
            _sources.Add($"player://{player.GetHashCode()}");
            return _sources.Count - 1;
        }

        /// <summary>
        /// Adds a capture device as a source layer.
        /// </summary>
        /// <param name="deviceName">Name of the device (e.g., webcam name).</param>
        /// <returns>The zero-based layer index.</returns>
        public int AddDevice(string deviceName)
        {
            ThrowIfDisposed();
            _sources.Add($"device://{deviceName}");
            return _sources.Count - 1;
        }

        /// <summary>
        /// Removes a source from the specified layer.
        /// </summary>
        /// <param name="layerIndex">Zero-based index of the layer to remove.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="layerIndex"/> is outside the valid range.
        /// </exception>
        public void RemoveSource(int layerIndex)
        {
            ThrowIfDisposed();
            if (layerIndex < 0 || layerIndex >= _sources.Count)
                throw new ArgumentOutOfRangeException(nameof(layerIndex));

            _sources.RemoveAt(layerIndex);
            Trace.WriteLine($"[VideoMixer] Removed source at layer {layerIndex}. Remaining: {_sources.Count}");
        }

        /// <summary>
        /// Attaches a WPF preview control to display the mixer output.
        /// </summary>
        /// <param name="previewControl">An <see cref="IVideoView"/> or WPF control.</param>
        public void AttachPreview(object previewControl)
        {
            ThrowIfDisposed();
            // Preview attachment will be wired through IPC shared texture
            Trace.WriteLine("[VideoMixer] Preview attached.");
        }

        /// <summary>
        /// Adds an output destination (e.g., RTMP, SRT, file recording).
        /// </summary>
        /// <param name="output">The <see cref="StreamOutput"/> to add.</param>
        public void AddOutput(StreamOutput output)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(output);
            _outputs.Add(output);
            Trace.WriteLine($"[VideoMixer] Added output: {output.OutputType}");
        }

        /// <summary>
        /// Removes an output destination.
        /// </summary>
        /// <param name="output">The <see cref="StreamOutput"/> to remove.</param>
        public void RemoveOutput(StreamOutput output)
        {
            ThrowIfDisposed();
            _outputs.Remove(output);
        }

        /// <summary>
        /// Switches the program output to the specified layer with a transition effect.
        /// </summary>
        /// <param name="layerIndex">Zero-based index of the target layer.</param>
        /// <param name="transition">The transition type. Default: <see cref="TransitionType.Cut"/>.</param>
        /// <param name="duration">Duration of the transition. <c>null</c> for instant cut.</param>
        public async Task SwitchToAsync(int layerIndex, TransitionType transition = TransitionType.Cut, TimeSpan? duration = null)
        {
            ThrowIfDisposed();
            if (layerIndex < 0 || layerIndex >= _sources.Count)
                throw new ArgumentOutOfRangeException(nameof(layerIndex));

            Trace.WriteLine($"[VideoMixer] Switching to layer {layerIndex}, transition: {transition}, duration: {duration}");
            await Task.CompletedTask; // IPC command would go here
        }

        /// <summary>
        /// Starts the mixer, activating all sources and outputs.
        /// </summary>
        public async Task StartAsync()
        {
            ThrowIfDisposed();

            if (!_pipelineCreated)
            {
                var payload = IPCCommandBuilder.CreatePipeline(
                    $"Mixer_{GetHashCode()}",
                    (uint)_width, (uint)_height, _frameRate);
                var response = await OpenMediaRuntime.SendCommandAsync(CommandType.CreatePipeline, payload);
                var id = IPCCommandBuilder.ParsePipelineId(response);
                if (id == null) throw new InvalidOperationException("Failed to create mixer pipeline.");
                _pipelineId = id.Value;
                _pipelineCreated = true;
            }

            var startPayload = IPCCommandBuilder.PipelineControl(_pipelineId);
            await OpenMediaRuntime.SendCommandAsync(CommandType.StartPipeline, startPayload);
            Trace.WriteLine("[VideoMixer] Started.");
        }

        /// <summary>
        /// Stops the mixer and all outputs.
        /// </summary>
        public async Task StopAsync()
        {
            ThrowIfDisposed();
            if (!_pipelineCreated) return;

            var payload = IPCCommandBuilder.PipelineControl(_pipelineId);
            await OpenMediaRuntime.SendCommandAsync(CommandType.StopPipeline, payload);
            Trace.WriteLine("[VideoMixer] Stopped.");
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_pipelineCreated)
            {
                try
                {
                    var payload = IPCCommandBuilder.PipelineControl(_pipelineId);
                    OpenMediaRuntime.SendCommandAsync(CommandType.DestroyPipeline, payload)
                        .GetAwaiter().GetResult();
                }
                catch { }
            }

            foreach (var output in _outputs)
                output.Dispose();
            _outputs.Clear();
        }
    }
}
