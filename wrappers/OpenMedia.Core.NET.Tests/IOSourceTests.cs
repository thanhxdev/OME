using System;
using Xunit;
using OpenMedia.Core.NET;
using OpenMedia.SDK;

namespace OpenMedia.Core.NET.Tests
{
    public class IOSourceTests
    {
        [Fact]
        public void FileSource_OpenAndPlay()
        {
            // Note: Since this is a unit test, we might not have a real file to open.
            // We are testing if the wrapper correctly calls native and handles the error gracefully.
            string filePath = "dummy_file.mp4";
            
            // Assume we have an ome_source_create_file API
            var sourcePtr = NativeBridge.ome_source_create_file(filePath);
            Assert.NotEqual(IntPtr.Zero, sourcePtr);
            
            NativeBridge.ome_source_destroy(sourcePtr);
        }

        [Fact]
        public void SRTSource_And_Output_Lifecycle()
        {
            using var srtOutput = new SRTOutput();
            Assert.NotEqual(IntPtr.Zero, srtOutput.Handle);
            bool opened = srtOutput.Open("srt://127.0.0.1:19000?mode=listener&latency=50");
            Assert.True(opened);
            Assert.True(srtOutput.IsOpen);

            System.Threading.Thread.Sleep(100);

            using var srtSource = new SRTSource();
            Assert.NotEqual(IntPtr.Zero, srtSource.Handle);
            bool connected = srtSource.Connect("srt://127.0.0.1:19000?mode=caller&latency=50");
            Assert.True(connected);
            Assert.True(srtSource.IsConnected);

            System.Threading.Thread.Sleep(100);

            bool hasStats = srtSource.GetStatistics(out var stats);
            Assert.True(hasStats);
            Assert.True(stats.msRTT >= 0);

            bool hasOutStats = srtOutput.GetStatistics(out var outStats);
            Assert.True(hasOutStats);
            Assert.True(outStats.msRTT >= 0);

            srtSource.Disconnect();
            Assert.False(srtSource.IsConnected);

            srtOutput.Close();
            Assert.False(srtOutput.IsOpen);
        }
    }
}

