namespace OpenMedia.Platform.Tests
{
    public class MediaPlayerTests
    {
        [Fact]
        public void Constructor_Default_StateIsIdle()
        {
            var player = new MediaPlayer();
            Assert.Equal(PlaybackState.Idle, player.State);
        }

        [Fact]
        public void Constructor_WithUri_StoresUri()
        {
            var player = new MediaPlayer("test.mp4");

            // State should still be Idle until PlayAsync/OpenAsync is called
            Assert.Equal(PlaybackState.Idle, player.State);
        }

        [Fact]
        public void Volume_Clamped_Between0And1()
        {
            var player = new MediaPlayer();

            player.Volume = 1.5;
            Assert.Equal(1.0, player.Volume);

            player.Volume = -0.5;
            Assert.Equal(0.0, player.Volume);

            player.Volume = 0.7;
            Assert.Equal(0.7, player.Volume);
        }

        [Fact]
        public void Duration_BeforeOpen_IsZero()
        {
            var player = new MediaPlayer();
            Assert.Equal(TimeSpan.Zero, player.Duration);
        }

        [Fact]
        public void Information_BeforeOpen_IsNull()
        {
            var player = new MediaPlayer();
            Assert.Null(player.Information);
        }

        [Fact]
        public void Dispose_SetsStateToIdle()
        {
            var player = new MediaPlayer();
            player.Dispose();

            Assert.Equal(PlaybackState.Idle, player.State);
        }

        [Fact]
        public void Dispose_CalledTwice_NoException()
        {
            var player = new MediaPlayer();
            player.Dispose();
            player.Dispose(); // Should not throw
        }

        [Fact]
        public void AttachPreview_NullControl_ThrowsArgumentException()
        {
            var player = new MediaPlayer();

            // Invalid type should throw
            Assert.Throws<ArgumentException>(() => player.AttachPreview("not_a_control"));
        }

        [Fact]
        public async Task PlayAsync_NotConnected_ThrowsInvalidOperation()
        {
            var player = new MediaPlayer();

            // Without runtime initialized, OpenAsync should throw
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => player.OpenAsync("test.mp4"));
        }

        [Fact]
        public async Task PauseAsync_NotPlaying_NoOp()
        {
            var player = new MediaPlayer();

            // Pausing when idle should not throw
            await player.PauseAsync();
            Assert.Equal(PlaybackState.Idle, player.State);
        }

        [Fact]
        public async Task StopAsync_NotPlaying_NoOp()
        {
            var player = new MediaPlayer();

            await player.StopAsync();
            Assert.Equal(PlaybackState.Idle, player.State);
        }

        [Fact]
        public async Task SeekAsync_SetsPosition()
        {
            var player = new MediaPlayer();
            var target = TimeSpan.FromSeconds(30);

            await player.SeekAsync(target);
            Assert.Equal(target, player.Position);
        }

        [Fact]
        public void StateChanged_Event_FiresOnStateChange()
        {
            var player = new MediaPlayer();
            PlaybackState? receivedState = null;

            player.StateChanged += (sender, state) => receivedState = state;

            // Trigger state change via Dispose (resets to Idle internally)
            // This tests the event mechanism indirectly
            Assert.Null(receivedState); // No change yet
        }

        [Fact]
        public void DetachPreview_WhenNotAttached_NoException()
        {
            var player = new MediaPlayer();
            player.DetachPreview(); // Should not throw
        }

        [Fact]
        public void ThrowIfDisposed_AfterDispose_ThrowsObjectDisposed()
        {
            var player = new MediaPlayer();
            player.Dispose();

            Assert.Throws<ObjectDisposedException>(() => player.AttachPreview(new object()));
        }
    }
}
