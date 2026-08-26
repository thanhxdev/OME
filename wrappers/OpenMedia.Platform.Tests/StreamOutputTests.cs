namespace OpenMedia.Platform.Tests
{
    public class StreamOutputTests
    {
        [Fact]
        public void RTMP_CreatesCorrectConfiguration()
        {
            var output = StreamOutput.RTMP("rtmp://test.com/live", StreamQuality.High1080p);

            Assert.Equal("rtmp://test.com/live", output.Configuration["url"]);
            Assert.Equal(StreamQuality.High1080p, output.Configuration["quality"]);
        }

        [Fact]
        public void RTMP_DefaultQuality_IsHigh1080p()
        {
            var output = StreamOutput.RTMP("rtmp://test.com/live");

            Assert.Equal(StreamQuality.High1080p, output.Configuration["quality"]);
        }

        [Fact]
        public void SRT_CreatesCorrectConfiguration()
        {
            var output = StreamOutput.SRT("192.168.1.1", 9000, SRTMode.Listener);

            Assert.Equal("192.168.1.1", output.Configuration["host"]);
            Assert.Equal(9000, output.Configuration["port"]);
            Assert.Equal(SRTMode.Listener, output.Configuration["mode"]);
        }

        [Fact]
        public void SRT_DefaultMode_IsCaller()
        {
            var output = StreamOutput.SRT("192.168.1.1", 9000);

            Assert.Equal(SRTMode.Caller, output.Configuration["mode"]);
        }

        [Fact]
        public void NDI_CreatesCorrectConfiguration()
        {
            var output = StreamOutput.NDI("My NDI Source");

            Assert.Equal("My NDI Source", output.Configuration["streamName"]);
        }

        [Fact]
        public void File_CreatesCorrectConfiguration()
        {
            var output = StreamOutput.File(@"C:\output\recording.mp4", RecordFormat.MP4);

            Assert.Equal(@"C:\output\recording.mp4", output.Configuration["path"]);
            Assert.Equal(RecordFormat.MP4, output.Configuration["format"]);
        }

        [Fact]
        public void File_DefaultFormat_IsMP4()
        {
            var output = StreamOutput.File("output.mp4");

            Assert.Equal(RecordFormat.MP4, output.Configuration["format"]);
        }

        [Fact]
        public void WebRTC_CreatesCorrectConfiguration()
        {
            var output = StreamOutput.WebRTC("wss://signaling.example.com");

            Assert.Equal("wss://signaling.example.com", output.Configuration["signalingUri"]);
        }

        [Fact]
        public void Dispose_CalledTwice_NoException()
        {
            var output = StreamOutput.RTMP("rtmp://test.com/live");
            output.Dispose();
            output.Dispose(); // Should not throw
        }

        [Fact]
        public void AllFactoryMethods_ReturnNonNullOutput()
        {
            Assert.NotNull(StreamOutput.RTMP("url"));
            Assert.NotNull(StreamOutput.SRT("host", 9000));
            Assert.NotNull(StreamOutput.NDI("name"));
            Assert.NotNull(StreamOutput.File("path"));
            Assert.NotNull(StreamOutput.WebRTC("uri"));
        }
    }
}
