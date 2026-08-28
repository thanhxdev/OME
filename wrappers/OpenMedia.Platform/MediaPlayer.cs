using System.Diagnostics;
using OpenMedia.SDK;
using OpenMedia.Platform.Controls.Wpf;
using OpenMedia.Platform.Internal;

namespace OpenMedia.Platform
{
    /// <summary>
    /// High-level media player that encapsulates the complete playback lifecycle
    /// (file, network stream, device) in a simple, async-first API.
    /// <para>
    /// Provides a "3-line" developer experience:
    /// <code>
    /// var player = new MediaPlayer("C:\\Videos\\sample.mp4");
    /// player.AttachPreview(videoView);
    /// await player.PlayAsync();
    /// </code>
    /// </para>
    /// </summary>
    public sealed class MediaPlayer : IDisposable
    {
        private uint _pipelineId;
        private uint _sourceId = 1;
        private string? _sourceUri;
        private bool _pipelineCreated;
        private bool _sourceOpened;
        private bool _disposed;

        private PlaybackState _state = PlaybackState.Idle;
        private MediaInfo? _information;
        private double _volume = 1.0;
        private bool _isMuted = false;

        private IVideoView? _attachedView;
        private WpfD3D11Renderer? _renderer;
        private CancellationTokenSource? _positionCts;
        private SynchronizationContext? _syncContext;
        private long _suppressPositionUntilTick = 0;

        // ─── Properties ─────────────────────────────────────────────

        /// <summary>
        /// Current playback position. Setting this value seeks to the specified position.
        /// </summary>
        public TimeSpan Position { get; set; }

        /// <summary>
        /// Total duration of the loaded media. Available after <see cref="OpenAsync"/>.
        /// </summary>
        public TimeSpan Duration => _information?.Duration ?? TimeSpan.Zero;

        /// <summary>
        /// Playback volume (0.0 = mute, 1.0 = full volume).
        /// </summary>
        public double Volume
        {
            get => _volume;
            set
            {
                var clamped = Math.Clamp(value, 0.0, 1.0);
                if (Math.Abs(_volume - clamped) > 0.0001)
                {
                    _volume = clamped;
                    if (_pipelineCreated && OpenMediaRuntime.IsConnected)
                    {
                        _ = UpdateServerAudioPropertiesAsync();
                    }
                }
            }
        }

        /// <summary>
        /// Gets or sets whether audio is muted.
        /// </summary>
        public bool IsMuted
        {
            get => _isMuted;
            set
            {
                if (_isMuted != value)
                {
                    _isMuted = value;
                    if (_pipelineCreated && OpenMediaRuntime.IsConnected)
                    {
                        _ = UpdateServerAudioPropertiesAsync();
                    }
                }
            }
        }

        private async Task UpdateServerAudioPropertiesAsync()
        {
            if (!_pipelineCreated || !OpenMediaRuntime.IsConnected) return;

            try
            {
                var payload = IPCCommandBuilder.SetLayerProperties(_pipelineId, 0, _isMuted, _volume);
                await OpenMediaRuntime.SendCommandAsync(CommandType.SetLayerProperties, payload);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[MediaPlayer] Failed to update audio properties: {ex.Message}");
            }
        }

        private int _videoDelayMs = 0;
        private int _audioDelayMs = 0;
        private int _masterDelayMs = 0;

        /// <summary>
        /// Current Video Delay in milliseconds.
        /// </summary>
        public int VideoDelayMs => _videoDelayMs;

        public void SetVideoDelayMs(int delayMs)
        {
            if (_videoDelayMs != delayMs)
            {
                _videoDelayMs = delayMs;
                _ = UpdateServerDelayAsync();
            }
        }

        /// <summary>
        /// Current Audio Delay in milliseconds.
        /// </summary>
        public int AudioDelayMs => _audioDelayMs;

        public void SetAudioDelayMs(int delayMs)
        {
            if (_audioDelayMs != delayMs)
            {
                _audioDelayMs = delayMs;
                _ = UpdateServerDelayAsync();
            }
        }

        /// <summary>
        /// Current Master Delay (A/V) in milliseconds.
        /// </summary>
        public int MasterDelayMs => _masterDelayMs;

        public void SetMasterDelayMs(int delayMs)
        {
            if (_masterDelayMs != delayMs)
            {
                _masterDelayMs = delayMs;
                _ = UpdateServerDelayAsync();
            }
        }

        private async Task UpdateServerDelayAsync()
        {
            if (!_pipelineCreated || !OpenMediaRuntime.IsConnected) return;

            try
            {
                var payload = IPCCommandBuilder.SetAVDelay(_pipelineId, _videoDelayMs, _audioDelayMs, _masterDelayMs);
                await OpenMediaRuntime.SendCommandAsync(CommandType.SetAVDelay, payload);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[MediaPlayer] Failed to update AV Delay: {ex.Message}");
            }
        }



        /// <summary>
        /// Current playback state.
        /// </summary>
        public PlaybackState State
        {
            get => _state;
            private set
            {
                if (_state == value) return;
                _state = value;
                RaiseEvent(() => StateChanged?.Invoke(this, value));
            }
        }

        /// <summary>
        /// Metadata about the loaded media. <c>null</c> until media is opened.
        /// </summary>
        public MediaInfo? Information => _information;

        // ─── Events ─────────────────────────────────────────────────

        /// <summary>Raised when the playback state changes.</summary>
        public event EventHandler<PlaybackState>? StateChanged;

        /// <summary>Raised periodically (~100ms) with the current playback position.</summary>
        public event EventHandler<TimeSpan>? PositionChanged;

        /// <summary>Raised when a playback error occurs.</summary>
        public event EventHandler<MediaErrorEventArgs>? ErrorOccurred;

        /// <summary>Raised when the media reaches its end.</summary>
        public event EventHandler? EndOfMedia;

        // ─── Constructors ───────────────────────────────────────────

        /// <summary>
        /// Creates an empty <see cref="MediaPlayer"/>. Call <see cref="OpenAsync"/> to load media.
        /// </summary>
        public MediaPlayer()
        {
            _syncContext = SynchronizationContext.Current;
        }

        /// <summary>
        /// Creates a <see cref="MediaPlayer"/> and immediately opens the specified source.
        /// </summary>
        /// <param name="sourceUri">Path or URL to the media file.</param>
        public MediaPlayer(string sourceUri) : this()
        {
            _sourceUri = sourceUri;
            // Auto-open is deferred to first PlayAsync or explicit OpenAsync call
        }

        // ─── Core Actions ───────────────────────────────────────────

        /// <summary>
        /// Opens a media source, creating the server-side pipeline.
        /// </summary>
        /// <param name="sourceUri">Path or URL to the media file.</param>
        /// <returns>A task that completes when the media is loaded and ready.</returns>
        public async Task OpenAsync(string sourceUri)
        {
            ThrowIfDisposed();
            EnsureRuntimeConnected();

            if (_pipelineCreated)
            {
                await StopAsync();
                try
                {
                    var destroyPayload = IPCCommandBuilder.PipelineControl(_pipelineId);
                    await OpenMediaRuntime.SendCommandAsync(CommandType.DestroyPipeline, destroyPayload);
                }
                catch { }
                _pipelineCreated = false;
                _sourceOpened = false;
            }

            _sourceUri = sourceUri;
            State = PlaybackState.Opening;

            try
            {
                // 1. Create pipeline on server
                var createPayload = IPCCommandBuilder.CreatePipeline($"Player_{GetHashCode()}", 1920, 1080, 60.0);
                var createResponse = await OpenMediaRuntime.SendCommandAsync(CommandType.CreatePipeline, createPayload);
                var pipelineId = IPCCommandBuilder.ParsePipelineId(createResponse);

                if (pipelineId == null)
                {
                    State = PlaybackState.Error;
                    RaiseError(1, "Failed to create pipeline on server.");
                    return;
                }

                _pipelineId = pipelineId.Value;
                _pipelineCreated = true;

                // 2. Open source on server
                var openPayload = IPCCommandBuilder.OpenSource(_pipelineId, _sourceId, sourceUri);
                var openResponse = await OpenMediaRuntime.SendCommandAsync(CommandType.OpenSource, openPayload);

                if (openResponse == null)
                {
                    State = PlaybackState.Error;
                    RaiseError(2, $"Failed to open source: {sourceUri}");
                    return;
                }

                _sourceOpened = true;

                // 3. Query source info
                var infoPayload = IPCCommandBuilder.GetSourceInfo(_pipelineId, _sourceId);
                var infoResponse = await OpenMediaRuntime.SendCommandAsync(CommandType.GetSourceInfo, infoPayload);
                _information = IPCCommandBuilder.ParseSourceInfo(infoResponse, sourceUri);

                State = PlaybackState.Ready;
                await UpdateServerAudioPropertiesAsync();
                Trace.WriteLine($"[MediaPlayer] Opened: {sourceUri} ({_information?.Duration})");
            }
            catch (Exception ex)
            {
                State = PlaybackState.Error;
                RaiseError(-1, ex.Message);
            }
        }

        /// <summary>
        /// Starts or resumes playback.
        /// If a source URI was provided in the constructor but not yet opened, it will be opened first.
        /// </summary>
        public async Task PlayAsync()
        {
            ThrowIfDisposed();

            // Auto-open if constructor was given a URI
            if (!_sourceOpened && !string.IsNullOrEmpty(_sourceUri))
            {
                await OpenAsync(_sourceUri);
                if (State == PlaybackState.Error) return;
            }

            if (State != PlaybackState.Ready && State != PlaybackState.Paused && State != PlaybackState.Stopped) return;

            try
            {
                if (State == PlaybackState.Stopped || Position >= Duration)
                {
                    await SeekAsync(TimeSpan.Zero);
                }

                var payload = IPCCommandBuilder.PipelineControl(_pipelineId);
                var cmd = State == PlaybackState.Paused ? CommandType.ResumePipeline : CommandType.StartPipeline;
                await OpenMediaRuntime.SendCommandAsync(cmd, payload);
                State = PlaybackState.Playing;
                StartPositionTracking();

                // Set up shared texture for preview if view is attached
                if (_attachedView != null)
                {
                    await SetupSharedTextureAsync();
                }
            }
            catch (Exception ex)
            {
                State = PlaybackState.Error;
                RaiseError(-1, ex.Message);
            }
        }

        /// <summary>
        /// Pauses playback.
        /// </summary>
        public async Task PauseAsync()
        {
            ThrowIfDisposed();
            if (State != PlaybackState.Playing) return;

            try
            {
                var payload = IPCCommandBuilder.PipelineControl(_pipelineId);
                await OpenMediaRuntime.SendCommandAsync(CommandType.PausePipeline, payload);
                State = PlaybackState.Paused;
                StopPositionTracking();
            }
            catch (Exception ex)
            {
                RaiseError(-1, ex.Message);
            }
        }

        /// <summary>
        /// Stops playback and resets position to the beginning.
        /// </summary>
        public async Task StopAsync()
        {
            ThrowIfDisposed();
            if (State != PlaybackState.Playing && State != PlaybackState.Paused && State != PlaybackState.Stopped) return;

            try
            {
                var payload = IPCCommandBuilder.PipelineControl(_pipelineId);
                await OpenMediaRuntime.SendCommandAsync(CommandType.StopPipeline, payload);

                if (_pipelineCreated)
                {
                    var seekPayload = IPCCommandBuilder.SeekSource(_pipelineId, _sourceId, 0);
                    await OpenMediaRuntime.SendCommandAsync(CommandType.SeekSource, seekPayload);
                }

                Position = TimeSpan.Zero;
                State = PlaybackState.Stopped;
                StopPositionTracking();
                RaiseEvent(() => PositionChanged?.Invoke(this, TimeSpan.Zero));
            }
            catch (Exception ex)
            {
                RaiseError(-1, ex.Message);
            }
        }

        /// <summary>
        /// Seeks to the specified position. If position exceeds media duration, resets position to start and stops playback.
        /// </summary>
        /// <param name="position">The target position.</param>
        public async Task SeekAsync(TimeSpan position)
        {
            ThrowIfDisposed();

            if (position < TimeSpan.Zero)
            {
                position = TimeSpan.Zero;
            }

            if (Duration > TimeSpan.Zero && position >= Duration)
            {
                await HandleEndOfMediaAsync();
                return;
            }

            Position = position;
            _suppressPositionUntilTick = Environment.TickCount64 + 400;

            if (_pipelineCreated)
            {
                var payload = IPCCommandBuilder.SeekSource(_pipelineId, _sourceId, (ulong)position.TotalMilliseconds);
                await OpenMediaRuntime.SendCommandAsync(CommandType.SeekSource, payload);
            }
            RaiseEvent(() => PositionChanged?.Invoke(this, position));
        }

        // ─── Preview Binding ────────────────────────────────────────

        /// <summary>
        /// Attaches a WPF preview control to display video output.
        /// The control must be an <see cref="OpenMediaVideoView"/> or implement <see cref="IVideoView"/>.
        /// </summary>
        /// <param name="previewControl">The WPF control to attach.</param>
        /// <example>
        /// <code>
        /// player.AttachPreview(videoView); // videoView is an OpenMediaVideoView in XAML
        /// </code>
        /// </example>
        public void AttachPreview(object previewControl)
        {
            ThrowIfDisposed();

            if (previewControl is IVideoView videoView)
            {
                _attachedView = videoView;
                Trace.WriteLine("[MediaPlayer] Preview attached (IVideoView).");
            }
            else if (previewControl is OpenMediaVideoView wpfView)
            {
                _attachedView = wpfView;
                Trace.WriteLine("[MediaPlayer] Preview attached (OpenMediaVideoView).");
            }
            else
            {
                throw new ArgumentException(
                    $"Unsupported preview control type: {previewControl.GetType().Name}. " +
                    "Use OpenMediaVideoView (WPF) or a control implementing IVideoView.",
                    nameof(previewControl));
            }
        }

        /// <summary>
        /// Detaches the current preview control.
        /// </summary>
        public void DetachPreview()
        {
            _attachedView?.Detach();
            _attachedView = null;
            _renderer?.Dispose();
            _renderer = null;
        }

        // ─── Private Helpers ────────────────────────────────────────

        private async Task SetupSharedTextureAsync()
        {
            if (_attachedView == null) return;

            var payload = await OpenMediaRuntime.RequestSharedTextureAsync();
            if (payload.HasValue)
            {
                var p = payload.Value;
                _attachedView.Attach((IntPtr)p.NtHandle0, (int)p.Width, (int)p.Height);
                Trace.WriteLine($"[MediaPlayer] Shared texture attached: {p.Width}x{p.Height}");
            }
        }

        private async Task<double> GetServerPositionMsAsync()
        {
            if (!_pipelineCreated || !OpenMediaRuntime.IsConnected) return -1.0;
            try
            {
                var payload = IPCCommandBuilder.GetPipelineState(_pipelineId);
                var response = await OpenMediaRuntime.SendCommandAsync(CommandType.GetPipelineState, payload);
                return IPCCommandBuilder.ParsePipelineStatePosition(response);
            }
            catch
            {
                return -1.0;
            }
        }

        private void StartPositionTracking()
        {
            StopPositionTracking();
            _positionCts = new CancellationTokenSource();
            var ct = _positionCts.Token;

            _ = Task.Run(async () =>
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(100, ct);
                    if (ct.IsCancellationRequested) break;

                    if (State == PlaybackState.Playing)
                    {
                        if (Environment.TickCount64 < _suppressPositionUntilTick)
                        {
                            // In seek grace period: smoothly advance position estimate from seek target
                            Position = Position.Add(TimeSpan.FromMilliseconds(100));
                        }
                        else
                        {
                            double serverPosMs = await GetServerPositionMsAsync();
                            if (serverPosMs >= 0 && Environment.TickCount64 >= _suppressPositionUntilTick)
                            {
                                Position = TimeSpan.FromMilliseconds(serverPosMs);
                            }
                            else
                            {
                                Position = Position.Add(TimeSpan.FromMilliseconds(100));
                            }
                        }
                    }

                    if (Duration > TimeSpan.Zero && Position >= Duration)
                    {
                        _ = HandleEndOfMediaAsync();
                        break;
                    }

                    RaiseEvent(() => PositionChanged?.Invoke(this, Position));
                }
            }, ct);
        }

        private async Task HandleEndOfMediaAsync()
        {
            await StopAsync();
            RaiseEvent(() => EndOfMedia?.Invoke(this, EventArgs.Empty));
        }

        private void StopPositionTracking()
        {
            _positionCts?.Cancel();
            _positionCts?.Dispose();
            _positionCts = null;
        }

        private void RaiseError(int code, string message)
        {
            RaiseEvent(() => ErrorOccurred?.Invoke(this,
                new MediaErrorEventArgs(code, message, _sourceUri ?? "")));
        }

        private void RaiseEvent(Action action)
        {
            if (_syncContext != null)
                _syncContext.Post(_ => action(), null);
            else
                action();
        }

        private static void EnsureRuntimeConnected()
        {
            if (!OpenMediaRuntime.IsConnected)
            {
                throw new InvalidOperationException(
                    "OpenMediaRuntime is not initialized. Call OpenMediaRuntime.InitializeAsync() first, " +
                    "or ensure the server is running.");
            }
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        // ─── IDisposable ────────────────────────────────────────────

        /// <summary>
        /// Releases all resources: stops playback, destroys the server-side pipeline,
        /// and releases shared textures.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            StopPositionTracking();
            DetachPreview();

            // Destroy pipeline on server
            if (_pipelineCreated)
            {
                try
                {
                    if (OpenMediaRuntime.IsConnected)
                    {
                        var payload = IPCCommandBuilder.PipelineControl(_pipelineId);
                        var task = OpenMediaRuntime.SendCommandAsync(CommandType.DestroyPipeline, payload);
                        task.Wait(300);
                    }
                }
                catch { }
                _pipelineCreated = false;
            }

            _state = PlaybackState.Idle;
        }
    }
}
