using System.Diagnostics;

namespace OpenMedia.Platform
{
    /// <summary>
    /// Provides seamless, gapless playlist playback with pre-decoding,
    /// looping, shuffling, and schedule support.
    /// </summary>
    /// <example>
    /// <code>
    /// var playlist = new MediaPlaylist();
    /// playlist.Add("video1.mp4");
    /// playlist.Add("video2.mp4");
    /// playlist.LoopMode = MediaPlaylist.LoopModeType.All;
    /// playlist.AttachPreview(videoView);
    /// await playlist.PlayAsync();
    /// </code>
    /// </example>
    public sealed class MediaPlaylist : IDisposable
    {
        private readonly List<PlaylistItem> _items = new();
        private readonly List<ScheduledItem> _scheduledItems = new();
        private int _currentIndex = -1;
        private bool _isPlaying;
        private bool _disposed;

        private MediaPlayer? _currentPlayer;
        private MediaPlayer? _preloadedPlayer;
        private CancellationTokenSource? _playbackCts;
        private object? _previewControl;

        // ─── Nested Types ───────────────────────────────────────────

        /// <summary>
        /// Playback loop mode.
        /// </summary>
        public enum LoopModeType
        {
            /// <summary>No looping — stop at end.</summary>
            None,
            /// <summary>Loop the current item.</summary>
            Single,
            /// <summary>Loop the entire playlist.</summary>
            All
        }

        /// <summary>
        /// Represents a single item in the playlist.
        /// </summary>
        public sealed class PlaylistItem
        {
            /// <summary>Media source URI.</summary>
            public string Uri { get; init; } = string.Empty;
            /// <summary>Zero-based index in the playlist.</summary>
            public int Index { get; internal set; }
        }

        /// <summary>
        /// Represents a scheduled playlist item that plays at a specific time.
        /// </summary>
        public sealed class ScheduledItem
        {
            /// <summary>Media source URI.</summary>
            public string Uri { get; init; } = string.Empty;
            /// <summary>Scheduled playback time.</summary>
            public DateTime PlayAt { get; init; }
            /// <summary>Whether this item has been played.</summary>
            public bool HasPlayed { get; internal set; }
        }

        // ─── Properties ─────────────────────────────────────────────

        /// <summary>Gets the playlist items.</summary>
        public IReadOnlyList<PlaylistItem> Items => _items;

        /// <summary>Gets or sets the loop mode.</summary>
        public LoopModeType LoopMode { get; set; } = LoopModeType.None;

        /// <summary>Gets or sets whether shuffle is enabled.</summary>
        public bool ShuffleEnabled { get; set; }

        /// <summary>
        /// Gets or sets the crossfade duration for gapless transitions.
        /// Set to <see cref="TimeSpan.Zero"/> to disable crossfade.
        /// </summary>
        public TimeSpan CrossfadeDuration { get; set; } = TimeSpan.Zero;

        /// <summary>Gets the currently playing item, or <c>null</c>.</summary>
        public PlaylistItem? CurrentItem => _currentIndex >= 0 && _currentIndex < _items.Count
            ? _items[_currentIndex] : null;

        /// <summary>Gets whether the playlist is currently playing.</summary>
        public bool IsPlaying => _isPlaying;

        // ─── Events ─────────────────────────────────────────────────

        /// <summary>Raised when the current item changes.</summary>
        public event EventHandler<(PlaylistItem? current, PlaylistItem? previous)>? ItemChanged;

        /// <summary>Raised when the entire playlist has finished playing.</summary>
        public event EventHandler? PlaylistCompleted;

        // ─── Collection Management ──────────────────────────────────

        /// <summary>Adds a media source to the end of the playlist.</summary>
        public void Add(string uri)
        {
            ThrowIfDisposed();
            _items.Add(new PlaylistItem { Uri = uri, Index = _items.Count });
        }

        /// <summary>Inserts a media source at the specified position.</summary>
        public void Insert(int index, string uri)
        {
            ThrowIfDisposed();
            _items.Insert(index, new PlaylistItem { Uri = uri, Index = index });
            ReindexItems();
        }

        /// <summary>Removes the item at the specified position.</summary>
        public void Remove(int index)
        {
            ThrowIfDisposed();
            if (index >= 0 && index < _items.Count)
            {
                _items.RemoveAt(index);
                ReindexItems();
            }
        }

        /// <summary>Clears all items.</summary>
        public void Clear()
        {
            ThrowIfDisposed();
            _items.Clear();
            _scheduledItems.Clear();
            _currentIndex = -1;
        }

        // ─── Schedule ───────────────────────────────────────────────

        /// <summary>
        /// Schedules a media item to play at a specific date/time.
        /// </summary>
        /// <param name="uri">Media source URI.</param>
        /// <param name="playAt">The date/time to start playing this item.</param>
        public void ScheduleItem(string uri, DateTime playAt)
        {
            ThrowIfDisposed();
            _scheduledItems.Add(new ScheduledItem { Uri = uri, PlayAt = playAt });
            _scheduledItems.Sort((a, b) => a.PlayAt.CompareTo(b.PlayAt));
            Trace.WriteLine($"[MediaPlaylist] Scheduled '{uri}' at {playAt:HH:mm:ss}");
        }

        /// <summary>Gets the list of scheduled items.</summary>
        public IReadOnlyList<ScheduledItem> ScheduledItems => _scheduledItems;

        // ─── Playback Controls ──────────────────────────────────────

        /// <summary>Starts playback from the beginning or current position.</summary>
        public async Task PlayAsync()
        {
            ThrowIfDisposed();
            if (_items.Count == 0) return;
            if (_currentIndex < 0) _currentIndex = ShuffleEnabled ? GetRandomIndex() : 0;

            _isPlaying = true;
            _playbackCts = new CancellationTokenSource();

            await PlayCurrentItemAsync();

            // Start pre-decode and schedule monitoring
            _ = MonitorPlaybackAsync(_playbackCts.Token);
        }

        /// <summary>Advances to the next item.</summary>
        public async Task NextAsync()
        {
            ThrowIfDisposed();
            if (_items.Count == 0) return;

            var prev = CurrentItem;
            _currentIndex = GetNextIndex();

            if (_currentIndex < 0)
            {
                _isPlaying = false;
                PlaylistCompleted?.Invoke(this, EventArgs.Empty);
                return;
            }

            ItemChanged?.Invoke(this, (CurrentItem, prev));

            if (_isPlaying)
            {
                // Use pre-loaded player if available for gapless transition
                if (_preloadedPlayer != null)
                {
                    _currentPlayer?.Dispose();
                    _currentPlayer = _preloadedPlayer;
                    _preloadedPlayer = null;
                    await _currentPlayer.PlayAsync();
                }
                else
                {
                    await PlayCurrentItemAsync();
                }
            }
        }

        /// <summary>Returns to the previous item.</summary>
        public async Task PreviousAsync()
        {
            ThrowIfDisposed();
            if (_items.Count == 0) return;

            var prev = CurrentItem;
            _currentIndex = _currentIndex > 0 ? _currentIndex - 1 : _items.Count - 1;
            ItemChanged?.Invoke(this, (CurrentItem, prev));

            if (_isPlaying)
            {
                _preloadedPlayer?.Dispose();
                _preloadedPlayer = null;
                await PlayCurrentItemAsync();
            }
        }

        /// <summary>Stops playlist playback.</summary>
        public async Task StopAsync()
        {
            ThrowIfDisposed();
            _isPlaying = false;
            _playbackCts?.Cancel();

            if (_currentPlayer != null)
            {
                await _currentPlayer.StopAsync();
                _currentPlayer.Dispose();
                _currentPlayer = null;
            }

            _preloadedPlayer?.Dispose();
            _preloadedPlayer = null;

            Trace.WriteLine("[MediaPlaylist] Stopped.");
        }

        /// <summary>Attaches a preview control for playlist output.</summary>
        public void AttachPreview(object previewControl)
        {
            ThrowIfDisposed();
            _previewControl = previewControl;
            _currentPlayer?.AttachPreview(previewControl);
            Trace.WriteLine("[MediaPlaylist] Preview attached.");
        }

        // ─── Private Helpers ────────────────────────────────────────

        private async Task PlayCurrentItemAsync()
        {
            if (CurrentItem == null) return;

            _currentPlayer?.Dispose();
            _currentPlayer = new MediaPlayer(CurrentItem.Uri);

            if (_previewControl != null)
                _currentPlayer.AttachPreview(_previewControl);

            await _currentPlayer.PlayAsync();
            Trace.WriteLine($"[MediaPlaylist] Playing item {_currentIndex}: {CurrentItem.Uri}");
        }

        /// <summary>
        /// Monitors playback for pre-decoding the next item and handling
        /// gapless transitions and scheduled items.
        /// </summary>
        private async Task MonitorPlaybackAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _isPlaying)
            {
                await Task.Delay(500, ct);
                if (ct.IsCancellationRequested) break;

                // Check scheduled items
                await CheckScheduledItemsAsync();

                // Pre-decode next item when approaching end of current
                if (_currentPlayer != null && _preloadedPlayer == null)
                {
                    var remaining = _currentPlayer.Duration - _currentPlayer.Position;
                    var preloadThreshold = CrossfadeDuration > TimeSpan.Zero
                        ? CrossfadeDuration + TimeSpan.FromSeconds(1)
                        : TimeSpan.FromSeconds(2);

                    if (remaining > TimeSpan.Zero && remaining <= preloadThreshold)
                    {
                        await PreloadNextItemAsync();
                    }
                }

                // Auto-advance when current item ends
                if (_currentPlayer?.State == PlaybackState.Stopped ||
                    (_currentPlayer?.Duration > TimeSpan.Zero &&
                     _currentPlayer?.Position >= _currentPlayer?.Duration))
                {
                    await NextAsync();
                }
            }
        }

        private async Task PreloadNextItemAsync()
        {
            var nextIndex = GetNextIndex();
            if (nextIndex < 0 || nextIndex >= _items.Count) return;

            var nextUri = _items[nextIndex].Uri;
            _preloadedPlayer = new MediaPlayer();
            await _preloadedPlayer.OpenAsync(nextUri);
            Trace.WriteLine($"[MediaPlaylist] Pre-loaded next item: {nextUri}");
        }

        private async Task CheckScheduledItemsAsync()
        {
            var now = DateTime.Now;
            foreach (var item in _scheduledItems)
            {
                if (!item.HasPlayed && now >= item.PlayAt)
                {
                    item.HasPlayed = true;
                    Trace.WriteLine($"[MediaPlaylist] Scheduled item triggered: {item.Uri}");

                    // Insert at current position and play
                    var insertIndex = _currentIndex + 1;
                    if (insertIndex > _items.Count) insertIndex = _items.Count;
                    Insert(insertIndex, item.Uri);
                    await NextAsync();
                    break;
                }
            }
        }

        private int GetNextIndex()
        {
            if (_items.Count == 0) return -1;

            if (LoopMode == LoopModeType.Single) return _currentIndex;

            if (ShuffleEnabled) return GetRandomIndex();

            var next = _currentIndex + 1;
            if (next >= _items.Count)
            {
                return LoopMode == LoopModeType.All ? 0 : -1;
            }
            return next;
        }

        private int GetRandomIndex()
        {
            if (_items.Count <= 1) return 0;
            int next;
            do { next = Random.Shared.Next(_items.Count); }
            while (next == _currentIndex);
            return next;
        }

        private void ReindexItems()
        {
            for (int i = 0; i < _items.Count; i++)
                _items[i].Index = i;
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

            _playbackCts?.Cancel();
            _playbackCts?.Dispose();
            _currentPlayer?.Dispose();
            _preloadedPlayer?.Dispose();
            _items.Clear();
            _scheduledItems.Clear();
        }
    }
}
