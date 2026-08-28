using System;
using Xunit;
using OpenMedia.Core.NET;
using OpenMedia.SDK;

namespace OpenMedia.Core.NET.Tests
{
    public class MemoryLeakTest
    {
        [Fact]
        public void Test_FrameAllocation_MemoryLeak()
        {
            // Trigger initial GC to set a baseline
            GC.Collect();
            GC.WaitForPendingFinalizers();
            long initialMemory = GC.GetTotalMemory(true);

            int numIterations = 10000;

            for (int i = 0; i < numIterations; i++)
            {
                // Create MediaFrame, it allocates native memory (~3MB for 1080p NV12)
                using (var frame = MediaFrame.CreateVideo(1920, 1080, PixelFormat.NV12))
                {
                    Assert.NotNull(frame);
                    Assert.NotEqual(IntPtr.Zero, frame.Handle);
                    
                    // Verify data access works
                    var planeInfo = frame.GetVideoPlane(0);
                    Assert.NotEqual(IntPtr.Zero, planeInfo.data);
                    Assert.True(planeInfo.stride > 0);
                } // frame.Dispose() is called here
            }

            // Force GC to clean up any remaining managed objects
            GC.Collect();
            GC.WaitForPendingFinalizers();
            long finalMemory = GC.GetTotalMemory(true);

            long diff = finalMemory - initialMemory;
            
            // Allow up to 2MB difference for .NET runtime overhead, but Native leak would be much larger (1920x1080x1.5 bytes * 10000 = 30GB)
            Assert.True(diff < 2 * 1024 * 1024, $"Memory leak detected! Increased by {diff} bytes");
        }
    }
}
