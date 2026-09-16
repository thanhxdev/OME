using System;
using OpenMedia.Platform.Models;
using Xunit;

namespace OpenMedia.Platform.Tests
{
    public class MediaStreamInfoTests
    {
        [Fact]
        public void BroadcastFrameRates_SnapToRational_AccuratelyIdentifiesNTSC()
        {
            var (num59, den59) = BroadcastFrameRates.SnapToRational(59.94);
            Assert.Equal(60000, num59);
            Assert.Equal(1001, den59);

            var (num29, den29) = BroadcastFrameRates.SnapToRational(29.97);
            Assert.Equal(30000, num29);
            Assert.Equal(1001, den29);

            var (num23, den23) = BroadcastFrameRates.SnapToRational(23.976);
            Assert.Equal(24000, num23);
            Assert.Equal(1001, den23);
        }

        [Fact]
        public void BroadcastFrameRates_SnapToRational_HandlesIntegerFrequencies()
        {
            var (num60, den60) = BroadcastFrameRates.SnapToRational(60.0);
            Assert.Equal(60, num60);
            Assert.Equal(1, den60);

            var (num50, den50) = BroadcastFrameRates.SnapToRational(50.0);
            Assert.Equal(50, num50);
            Assert.Equal(1, den50);

            var (num25, den25) = BroadcastFrameRates.SnapToRational(25.0);
            Assert.Equal(25, num25);
            Assert.Equal(1, den25);
        }

        [Fact]
        public void BroadcastFrameRates_FormatFfmpeg_ProducesExactStrings()
        {
            Assert.Equal("60000/1001", BroadcastFrameRates.FormatFfmpeg(59.94));
            Assert.Equal("30000/1001", BroadcastFrameRates.FormatFfmpeg(29.97));
            Assert.Equal("60", BroadcastFrameRates.FormatFfmpeg(60.0));
            Assert.Equal("50", BroadcastFrameRates.FormatFfmpeg(50.0));
        }

        [Fact]
        public void BroadcastFrameRates_CalculateFrameDuration90k_Matches90kHzTimebase()
        {
            // 59.94 fps -> 90000 * 1001 / 60000 = 1501.5 -> integer division is 1501
            long dur59 = BroadcastFrameRates.CalculateFrameDuration90k(60000, 1001);
            Assert.Equal(1501, dur59);

            // 60.0 fps -> 90000 * 1 / 60 = 1500
            long dur60 = BroadcastFrameRates.CalculateFrameDuration90k(60, 1);
            Assert.Equal(1500, dur60);

            // 50.0 fps -> 90000 * 1 / 50 = 1800
            long dur50 = BroadcastFrameRates.CalculateFrameDuration90k(50, 1);
            Assert.Equal(1800, dur50);
        }

        [Fact]
        public void MediaStreamInfo_SerializeToStreamId_GeneratesValidUriAndCanRoundtrip()
        {
            var original = new MediaStreamInfo
            {
                Width = 1920,
                Height = 1080,
                FrameRateNum = 60000,
                FrameRateDen = 1001,
                VideoCodec = "h264",
                PixelFormat = "nv12",
                AudioSampleRate = 48000,
                AudioChannels = 2,
                BitrateKbps = 8000
            };

            string streamId = original.SerializeToStreamId("live/multicam1");
            Assert.StartsWith("live/multicam1?fmt=omi", streamId);
            Assert.Contains("v=h264", streamId);
            Assert.Contains("w=1920", streamId);
            Assert.Contains("h=1080", streamId);
            Assert.Contains("fps=60000/1001", streamId);
            Assert.Contains("br=8000", streamId);

            bool success = MediaStreamInfo.TryParseFromStreamId(streamId, out var parsed, out string basePath);
            Assert.True(success);
            Assert.NotNull(parsed);
            Assert.Equal("live/multicam1", basePath);
            Assert.Equal(1920, parsed.Width);
            Assert.Equal(1080, parsed.Height);
            Assert.Equal(60000, parsed.FrameRateNum);
            Assert.Equal(1001, parsed.FrameRateDen);
            Assert.InRange(parsed.FrameRateDouble, 59.939, 59.941);
            Assert.Equal("h264", parsed.VideoCodec);
            Assert.Equal(8000, parsed.BitrateKbps);
        }

        [Fact]
        public void MediaStreamInfo_TryParseFromStreamId_GracefullyHandlesLegacyNonOmiStreamId()
        {
            bool success = MediaStreamInfo.TryParseFromStreamId("live/obs_feed_cam2", out var parsed, out string basePath);
            Assert.False(success);
            Assert.NotNull(parsed);
            Assert.Equal("live/obs_feed_cam2", basePath);
        }

        [Fact]
        public void MediaStreamInfo_TryParseFromStreamId_HandlesEmptyOrNull()
        {
            Assert.False(MediaStreamInfo.TryParseFromStreamId(null, out var parsed1, out _));
            Assert.NotNull(parsed1);

            Assert.False(MediaStreamInfo.TryParseFromStreamId(string.Empty, out var parsed2, out _));
            Assert.NotNull(parsed2);
        }

        [Fact]
        public void MediaStreamInfo_JsonSerialization_RoundtripsAccurately()
        {
            var original = new MediaStreamInfo
            {
                Width = 3840,
                Height = 2160,
                FrameRateNum = 50,
                FrameRateDen = 1,
                VideoCodec = "hevc",
                BitrateKbps = 25000
            };

            string json = original.SerializeJson();
            var restored = MediaStreamInfo.DeserializeJson(json);

            Assert.NotNull(restored);
            Assert.Equal(3840, restored.Width);
            Assert.Equal(2160, restored.Height);
            Assert.Equal(50, restored.FrameRateNum);
            Assert.Equal(1, restored.FrameRateDen);
            Assert.Equal("hevc", restored.VideoCodec);
            Assert.Equal(25000, restored.BitrateKbps);
        }
    }
}
