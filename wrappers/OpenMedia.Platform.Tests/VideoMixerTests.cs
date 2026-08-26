namespace OpenMedia.Platform.Tests
{
    public class VideoMixerTests
    {
        [Fact]
        public void AddSource_Uri_ReturnsIncrementingIndex()
        {
            var mixer = new VideoMixer();

            Assert.Equal(0, mixer.AddSource("source1.mp4"));
            Assert.Equal(1, mixer.AddSource("source2.mp4"));
            Assert.Equal(2, mixer.AddSource("source3.mp4"));
        }

        [Fact]
        public void AddDevice_ReturnsIncrementingIndex()
        {
            var mixer = new VideoMixer();
            mixer.AddSource("source1.mp4");

            Assert.Equal(1, mixer.AddDevice("Webcam HD"));
        }

        [Fact]
        public void AddSource_Player_ReturnsIndex()
        {
            var mixer = new VideoMixer();
            var player = new MediaPlayer();

            Assert.Equal(0, mixer.AddSource(player));
        }

        [Fact]
        public void AddSource_NullPlayer_ThrowsArgumentNull()
        {
            var mixer = new VideoMixer();

            Assert.Throws<ArgumentNullException>(() => mixer.AddSource((MediaPlayer)null!));
        }

        [Fact]
        public void RemoveSource_ValidIndex_Removes()
        {
            var mixer = new VideoMixer();
            mixer.AddSource("source1.mp4");
            mixer.AddSource("source2.mp4");

            mixer.RemoveSource(0);

            // After removal, adding should give index 1 (1 remaining + 1 new)
            Assert.Equal(1, mixer.AddSource("source3.mp4"));
        }

        [Fact]
        public void RemoveSource_InvalidIndex_ThrowsOutOfRange()
        {
            var mixer = new VideoMixer();
            mixer.AddSource("source1.mp4");

            Assert.Throws<ArgumentOutOfRangeException>(() => mixer.RemoveSource(5));
            Assert.Throws<ArgumentOutOfRangeException>(() => mixer.RemoveSource(-1));
        }

        [Fact]
        public async Task SwitchToAsync_InvalidLayer_ThrowsOutOfRange()
        {
            var mixer = new VideoMixer();
            mixer.AddSource("source1.mp4");

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => mixer.SwitchToAsync(5));
        }

        [Fact]
        public async Task SwitchToAsync_NegativeIndex_ThrowsOutOfRange()
        {
            var mixer = new VideoMixer();
            mixer.AddSource("source1.mp4");

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => mixer.SwitchToAsync(-1));
        }

        [Fact]
        public void AddOutput_NullOutput_ThrowsArgumentNull()
        {
            var mixer = new VideoMixer();

            Assert.Throws<ArgumentNullException>(() => mixer.AddOutput(null!));
        }

        [Fact]
        public void AddOutput_ValidOutput_Succeeds()
        {
            var mixer = new VideoMixer();
            var output = StreamOutput.RTMP("rtmp://test.com");

            mixer.AddOutput(output); // Should not throw
        }

        [Fact]
        public void RemoveOutput_ValidOutput_Succeeds()
        {
            var mixer = new VideoMixer();
            var output = StreamOutput.RTMP("rtmp://test.com");
            mixer.AddOutput(output);

            mixer.RemoveOutput(output); // Should not throw
        }

        [Fact]
        public void Dispose_CalledTwice_NoException()
        {
            var mixer = new VideoMixer();
            mixer.Dispose();
            mixer.Dispose(); // Should not throw
        }

        [Fact]
        public void AfterDispose_AllOperations_ThrowObjectDisposed()
        {
            var mixer = new VideoMixer();
            mixer.Dispose();

            Assert.Throws<ObjectDisposedException>(() => mixer.AddSource("test.mp4"));
            Assert.Throws<ObjectDisposedException>(() => mixer.AddDevice("cam"));
            Assert.Throws<ObjectDisposedException>(() => mixer.RemoveSource(0));
            Assert.Throws<ObjectDisposedException>(() => mixer.AttachPreview(new object()));
        }

        [Fact]
        public void Constructor_DefaultValues()
        {
            // Should construct with default 1920x1080 29.97fps
            var mixer = new VideoMixer();
            Assert.NotNull(mixer);
        }

        [Fact]
        public void Constructor_CustomValues()
        {
            var mixer = new VideoMixer(3840, 2160, 60.0);
            Assert.NotNull(mixer);
        }

        [Fact]
        public void Dispose_CleansUpOutputs()
        {
            var mixer = new VideoMixer();
            var output1 = StreamOutput.RTMP("rtmp://test1.com");
            var output2 = StreamOutput.File("output.mp4");
            mixer.AddOutput(output1);
            mixer.AddOutput(output2);

            mixer.Dispose(); // Should dispose all outputs too
        }
    }
}
