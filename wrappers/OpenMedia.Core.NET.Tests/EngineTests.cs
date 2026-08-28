using System;
using Xunit;
using OpenMedia.Core.NET;
using OpenMedia.SDK;

namespace OpenMedia.Core.NET.Tests
{
    public class EngineTests
    {
        [Fact]
        public void Engine_Lifecycle_InitializeAndShutdown()
        {
            var config = "{\"mode\": \"test\"}";
            var result = NativeBridge.ome_engine_init(config);
            Assert.True(result);

            NativeBridge.ome_engine_shutdown();
        }

        [Fact]
        public void Pipeline_Construction_Succeeds()
        {
            var pipelinePtr = NativeBridge.ome_pipeline_create();
            Assert.NotEqual(IntPtr.Zero, pipelinePtr);

            var result = NativeBridge.ome_pipeline_start(pipelinePtr);
            Assert.True(result);

            NativeBridge.ome_pipeline_stop(pipelinePtr);
            NativeBridge.ome_pipeline_destroy(pipelinePtr);
        }
    }
}
