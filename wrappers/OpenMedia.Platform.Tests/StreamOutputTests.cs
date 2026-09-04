using OpenMedia.Platform.Models;

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
            Assert.Equal("Listener", output.Configuration["mode"]);
        }

        [Fact]
        public void SRT_DefaultMode_IsCaller()
        {
            var output = StreamOutput.SRT("192.168.1.1", 9000);

            Assert.Equal("Caller", output.Configuration["mode"]);
        }

        [Fact]
        public void SRT_WithFullConfig_SerializesAllParameters()
        {
            var config = new SRTStreamConfig
            {
                Host = "10.0.0.50",
                Port = 9800,
                Mode = SRTMode.Caller,
                StreamId = "live/program/cam1",
                LatencyMs = 200,
                AutoLatency = false,
                EncryptionEnabled = true,
                Passphrase = "SecretPassphrase123",
                KeyLength = 32,
                VideoCodec = "H.265 / HEVC",
                BitrateKbps = 15000,
                HardwareEncoder = "NVIDIA NVENC",
                RateControl = "CBR",
                EncoderPreset = "Low-Latency",
                UltraLowLatency = true,
                GopSeconds = 1.0,
                BFrames = 0,
                NtpSyncEnabled = true,
                NtpServer = "time.cloudflare.com",
                AudioChannels = 16,
                AudioSampleRate = 48000,
                AudioBitrateKbps = 384
            };

            var output = StreamOutput.SRT(config);

            Assert.Equal("10.0.0.50", output.Configuration["host"]);
            Assert.Equal(9800, output.Configuration["port"]);
            Assert.Equal("Caller", output.Configuration["mode"]);
            Assert.Equal("live/program/cam1", output.Configuration["streamId"]);
            Assert.Equal(200, output.Configuration["latency"]);
            Assert.Equal(true, output.Configuration["encryption"]);
            Assert.Equal("SecretPassphrase123", output.Configuration["passphrase"]);
            Assert.Equal(32, output.Configuration["pbkeylen"]);
            Assert.Equal("H.265 / HEVC", output.Configuration["videoCodec"]);
            Assert.Equal(15000, output.Configuration["bitrateKbps"]);
            Assert.Equal(true, output.Configuration["ultraLowLatency"]);
            Assert.Equal(true, output.Configuration["ntpSync"]);
            Assert.Equal(16, output.Configuration["audioChannels"]);
        }

        [Fact]
        public void SRTStreamConfig_ToSrtUri_GeneratesValidUri()
        {
            var config = new SRTStreamConfig
            {
                Host = "192.168.1.100",
                Port = 9000,
                Mode = SRTMode.Caller,
                LatencyMs = 150,
                EncryptionEnabled = true,
                Passphrase = "testpassphrase",
                KeyLength = 32,
                StreamId = "stream/cam1"
            };

            string uri = config.ToSrtUri();

            Assert.StartsWith("srt://192.168.1.100:9000?mode=caller", uri);
            Assert.Contains("latency=150", uri);
            Assert.Contains("passphrase=testpassphrase", uri);
            Assert.Contains("pbkeylen=32", uri);
            Assert.Contains("streamid=stream%2Fcam1", uri);
        }

        [Fact]
        public async Task SRTStreamSession_StartAndStop_LifecycleWorks()
        {
            var config = new SRTStreamConfig
            {
                Host = "127.0.0.1",
                Port = 9000,
                Mode = SRTMode.Listener
            };

            using var session = new SRTStreamSession(config);
            Assert.False(session.IsRunning);

            bool started = await session.StartTransmissionAsync();
            Assert.True(started);
            Assert.True(session.IsRunning);
            Assert.True(session.Statistics.IsConnected);

            await session.StopAsync();
            Assert.False(session.IsRunning);
            Assert.False(session.Statistics.IsConnected);
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

