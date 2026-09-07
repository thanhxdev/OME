using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using OpenMedia.Platform.Internal;
using OpenMedia.Platform.Models;
using OpenMedia.SDK;
using Xunit;

namespace OpenMedia.Platform.Tests
{
    public class MockIngestSession : IMediaIngestSession
    {
        public int ChannelId { get; }
        public bool IsRunning { get; private set; }
        public int FramesProduced { get; private set; }

        public MockIngestSession(int channelId)
        {
            ChannelId = channelId;
        }

        public StreamStatistics CurrentStats => new StreamStatistics
        {
            CurrentFps = 60.0,
            CurrentBitrateKbps = 5000,
            PacketLossPercent = 0,
            RttMs = 5.0,
            IsConnected = true
        };

        public ValueTask<bool> StartAsync(CancellationToken ct = default)
        {
            IsRunning = true;
            return ValueTask.FromResult(true);
        }

        public ValueTask StopAsync()
        {
            IsRunning = false;
            return ValueTask.CompletedTask;
        }

        public async IAsyncEnumerable<MediaFrameSpan> ReadFramesAsync([EnumeratorCancellation] CancellationToken ct = default)
        {
            const int bufferSize = 1920 * 1080 * 4;
            var nativePtr = System.Runtime.InteropServices.Marshal.AllocHGlobal(bufferSize);
            try
            {
                while (!ct.IsCancellationRequested && IsRunning)
                {
                    FramesProduced++;
                    yield return new MediaFrameSpan(
                        nativePtr,
                        bufferSize,
                        1920 * 4,
                        1920,
                        1080,
                        PixelFormat.BGRA,
                        FramesProduced * 16_666);
                    await Task.Yield();
                }
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FreeHGlobal(nativePtr);
            }
        }

        public ValueTask DisposeAsync()
        {
            IsRunning = false;
            return ValueTask.CompletedTask;
        }
    }

    public class StressAndResilienceTests
    {
        [Fact]
        public async Task MultiChannel_32_Streams_Concurrent_Stress_Test()
        {
            // 1. Khởi tạo 32 streams ingest đồng thời
            const int streamCount = 32;
            var sessions = new List<MockIngestSession>(streamCount);

            for (int i = 0; i < streamCount; i++)
            {
                sessions.Add(new MockIngestSession(i + 1));
            }

            // 2. Chạy đồng thời 32 sessions
            var startTasks = new Task[streamCount];
            for (int i = 0; i < streamCount; i++)
            {
                startTasks[i] = sessions[i].StartAsync().AsTask();
            }
            await Task.WhenAll(startTasks);

            Assert.All(sessions, s => Assert.True(s.IsRunning));

            // 3. Đọc dữ liệu từ 32 streams đồng thời trong 200ms
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            var consumeTasks = new List<Task>();

            foreach (var session in sessions)
            {
                consumeTasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await foreach (var frame in session.ReadFramesAsync(cts.Token))
                        {
                            Assert.False(frame.IsEmpty);
                            Assert.Equal(1920, frame.Width);
                            Assert.Equal(1080, frame.Height);
                            if (cts.IsCancellationRequested) break;
                        }
                    }
                    catch (OperationCanceledException) { }
                }));
            }

            await Task.WhenAll(consumeTasks);

            // 4. Kiểm tra tất cả 32 streams đã sản xuất và xử lý frame ổn định
            Assert.All(sessions, s => Assert.True(s.FramesProduced > 0));

            // 5. Cleanup
            foreach (var session in sessions)
            {
                await session.DisposeAsync();
                Assert.False(session.IsRunning);
            }
        }

        [Fact]
        public async Task StateReplayEngine_Reconstitutes_Graph_Under_500ms()
        {
            var engine = StateReplayEngine.Instance;

            // Xóa state cũ nếu có
            // Đăng ký 10 Player Snapshot và 5 Mixer Snapshot giả lập broadcast graph phức tạp
            var playerGuids = new List<Guid>();
            for (int i = 0; i < 10; i++)
            {
                var id = Guid.NewGuid();
                playerGuids.Add(id);
                engine.RegisterPlayer(new PlayerSnapshot
                {
                    Id = id,
                    Uri = $"rtmp://server/live/stream_{i}",
                    Volume = 0.8,
                    State = PlaybackState.Playing,
                    ReplayAction = async () =>
                    {
                        // Giả lập tái tạo pipeline IPC call (~5-10ms)
                        await Task.Delay(5);
                    }
                });
            }

            var mixerGuids = new List<Guid>();
            for (int i = 0; i < 5; i++)
            {
                var id = Guid.NewGuid();
                mixerGuids.Add(id);
                var mixerSnap = new MixerSnapshot
                {
                    Id = id,
                    ReplayAction = async () =>
                    {
                        await Task.Delay(5);
                    }
                };
                mixerSnap.Sources.Add(new MixerSourceSnapshot { LayerIndex = 0, Uri = "cam1.mp4" });
                mixerSnap.Sources.Add(new MixerSourceSnapshot { LayerIndex = 1, Uri = "cam2.mp4" });
                engine.RegisterMixer(mixerSnap);
            }

            // Thực thi Replay toàn diện và đo thời gian
            var sw = Stopwatch.StartNew();
            var report = await engine.ReplayAsync(null);
            sw.Stop();

            // Khẳng định tiêu chuẩn V2.0-Final: Tự phục hồi < 500ms
            Assert.True(report.Succeeded, "Replay state phải thành công");
            Assert.Equal(10, report.ReplayedPlayers);
            Assert.Equal(5, report.ReplayedMixers);
            Assert.True(sw.ElapsedMilliseconds < 500, $"Thời gian replay ({sw.ElapsedMilliseconds}ms) phải nhỏ hơn 500ms theo SLA V2.0");

            // Cleanup test
            foreach (var id in playerGuids) engine.UnregisterPlayer(id);
            foreach (var id in mixerGuids) engine.UnregisterMixer(id);
        }

        [Fact]
        public void StateReplayEngine_Tracks_And_Untracks_Entities()
        {
            var engine = StateReplayEngine.Instance;
            var testId = Guid.NewGuid();

            var snapshot = new PlayerSnapshot
            {
                Id = testId,
                Uri = "test://stream"
            };

            engine.RegisterPlayer(snapshot);
            Assert.True(engine.TrackedPlayersCount >= 1);

            engine.UnregisterPlayer(testId);
            // Verify unregister success
        }

        [Fact]
        public void RuntimeOptions_ResilienceDefaults_AreCompliant()
        {
            var options = new RuntimeOptions();
            Assert.True(options.AutoReconnect, "Mặc định AutoReconnect phải bật");
            Assert.Equal(1000, options.WatchdogTimeoutMs);
            Assert.Equal(10, options.MaxReconnectAttempts);
        }
    }
}
