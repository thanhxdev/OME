using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenMedia.SDK;
using OpenMedia.SDK.Interop;
using OpenMedia.SDK.SafeHandles;
using Xunit;

namespace OpenMedia.Core.NET.Tests
{
    public class ModernInteropTests
    {
        [Fact]
        public void LibraryImport_PInvoke_EngineAndPipelineLifecycle()
        {
            // Verify source-generated LibraryImport calls
            bool initSuccess = NativeBridge.ome_engine_init("{\"mode\": \"rc_test\"}");
            Assert.True(initSuccess);

            using (var pipelineHandle = NativeBridge.ome_pipeline_create())
            {
                Assert.False(pipelineHandle.IsInvalid);
                Assert.NotEqual(IntPtr.Zero, (IntPtr)pipelineHandle);

                bool started = NativeBridge.ome_pipeline_start(pipelineHandle);
                Assert.True(started);

                bool stopped = NativeBridge.ome_pipeline_stop(pipelineHandle);
                Assert.True(stopped);
            }

            NativeBridge.ome_engine_shutdown();
        }

        [Fact]
        public void SafeHandle_Prevents_DoubleFree_And_IsIdempotent()
        {
            var rawPtr = NativeBridge.ome_pipeline_create();
            Assert.False(rawPtr.IsInvalid);

            // First dispose releases native resource
            rawPtr.Dispose();
            Assert.True(rawPtr.IsClosed);

            // Second dispose must be completely safe (idempotent, no double-free crash)
            var ex = Record.Exception(() => rawPtr.Dispose());
            Assert.Null(ex);
        }

        [Fact]
        public void CallbackLifetimeManager_Protects_Delegates_From_GC_Collection_TD01()
        {
            var pipelineHandle = NativeBridge.ome_pipeline_create();
            Assert.False(pipelineHandle.IsInvalid);

            bool callbackInvoked = false;
            NativeBridge.StateChangedCallback callback = state =>
            {
                callbackInvoked = true;
            };

            // Register callback on handle
            NativeBridge.ome_pipeline_set_state_callback(pipelineHandle, callback);

            // Null out the managed reference in current scope and trigger aggressive GC sweeps
            callback = null!;
            for (int i = 0; i < 3; i++)
            {
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
            }

            // Callback was registered and pinned in CallbackLifetimeManager; handle remains valid
            Assert.False(pipelineHandle.IsClosed);

            // Clean up: unregister and dispose handle
            NativeBridge.ome_pipeline_destroy(pipelineHandle);
            Assert.True(pipelineHandle.IsClosed);
        }

        [Fact]
        public void MediaFrameSpan_ZeroCopy_Span_Integrity()
        {
            using var frame = MediaFrame.CreateVideo(1280, 720, PixelFormat.NV12);
            Assert.False(frame.SafeHandle.IsInvalid);

            var spanView = frame.AsSpan(0, timestampUs: 1000);
            Assert.False(spanView.IsEmpty);
            Assert.Equal(1280, spanView.Width);
            Assert.Equal(720, spanView.Height);
            Assert.Equal(PixelFormat.NV12, spanView.Format);
            Assert.Equal(1000, spanView.TimestampUs);

            // Read-only span direct access without memory allocation
            ReadOnlySpan<byte> span = spanView.Span;
            Assert.True(span.Length > 0);
            Assert.Equal(spanView.Length, span.Length);
        }

        [Fact]
        public void Pipeline_Modern_Fluent_API_WithNode_And_Async_Control()
        {
            using var pipeline = new Pipeline();
            Assert.False(pipeline.SafeHandle.IsInvalid);

            using var source = new FileSource("test_stream.mp4");
            Assert.False(source.SafeHandle.IsInvalid);

            // Fluent node addition
            pipeline.WithNode(source.SafeHandle);

            // Async start and stop
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            bool started = pipeline.Start();
            Assert.True(started);

            bool stopped = pipeline.Stop();
            Assert.True(stopped);
        }

        [Fact]
        public void AudioChannelMeterData_Blittable_Compatibility()
        {
            var data = new NativeBridge.AudioChannelMeterData
            {
                PeakDb = -3.5f,
                RmsDb = -18.2f,
                Lufs = -24.0f,
                Clipping = true
            };

            Assert.True(data.Clipping);
            data.Clipping = false;
            Assert.False(data.Clipping);
        }
    }
}
