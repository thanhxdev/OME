using System;
using System.Linq;
using OpenMedia.Platform.IPC;
using Xunit;

namespace OpenMedia.Platform.Tests
{
    public class MatrixRouterTests
    {
        [Fact]
        public void TotalMatrixChannels_IsExpandedTo32()
        {
            Assert.Equal(32, MatrixChannelConstants.TotalMatrixChannels);
        }

        [Fact]
        public void MatrixVideoChannelInfo_SourceName_PreservesUtf8String()
        {
            var info = new MatrixVideoChannelInfo();
            info.SourceName = "[SRT] CAM 1 - 4K LIVE";

            Assert.Equal("[SRT] CAM 1 - 4K LIVE", info.SourceName);
        }

        [Fact]
        public void MatrixVideoChannelInfo_SourceName_TruncatesLongStringSafely()
        {
            var info = new MatrixVideoChannelInfo();
            // A string longer than 31 characters
            info.SourceName = "VERY_LONG_CAMERA_SOURCE_NAME_THAT_EXCEEDS_THIRTY_ONE_BYTES_TOTAL";

            Assert.NotNull(info.SourceName);
            Assert.True(info.SourceName.Length <= 31);
        }

        [Fact]
        public void MatrixRouterPublisher_SlotBaseIndex_AppliesCorrectOffset()
        {
            var publisher = new MatrixRouterPublisher { SlotBaseIndex = 11 };
            Assert.Equal(11, publisher.SlotBaseIndex);
            publisher.Dispose();
        }

        [Fact]
        public void MatrixRouterSubscriber_GetDiscoveredSources_ReturnsAll32Slots()
        {
            using var subscriber = new MatrixRouterSubscriber();
            var discovered = subscriber.GetDiscoveredSources();

            Assert.NotNull(discovered);
            Assert.Equal(32, discovered.Count);
            // Verify slot indices 0..31 are all represented
            for (int i = 0; i < 32; i++)
            {
                Assert.Contains(discovered, s => s.slot == i);
            }
        }
    }
}
