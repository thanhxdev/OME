using System;
using Xunit;
using OpenMedia.Core.NET;

namespace OpenMedia.Core.NET.Tests
{
    public class IntegrationTests
    {
        [Fact]
        public void FullPipeline_ConstructAndRun()
        {
            var pipeline = NativeBridge.ome_pipeline_create();
            Assert.NotEqual(IntPtr.Zero, pipeline);

            var source = NativeBridge.ome_source_create_file("test.mp4");

            // Assume ome_pipeline_add_source(pipeline, source) exists
            // NativeBridge.ome_pipeline_add_source(pipeline, source);

            var result = NativeBridge.ome_pipeline_start(pipeline);
            Assert.True(result);

            System.Threading.Thread.Sleep(100);

            NativeBridge.ome_pipeline_stop(pipeline);
            NativeBridge.ome_source_destroy(source);
            NativeBridge.ome_pipeline_destroy(pipeline);
        }
    }
}
