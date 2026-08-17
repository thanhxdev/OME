using System;
using Xunit;
using OpenMedia.Core.NET;

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
    }
}
