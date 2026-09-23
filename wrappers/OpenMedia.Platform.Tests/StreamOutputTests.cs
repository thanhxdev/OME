using OpenMedia.Platform.Internal;
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
        public async Task SRT_Transmission_SenderToReceiver_Roundtrip()
        {
            var recvConfig = new SRTStreamConfig
            {
                Host = "127.0.0.1",
                Port = 9876,
                Mode = SRTMode.Listener,
                LatencyMs = 120
            };
            var sendConfig = new SRTStreamConfig
            {
                Host = "127.0.0.1",
                Port = 9876,
                Mode = SRTMode.Caller,
                LatencyMs = 120
            };

            using var recvSession = new SRTStreamSession(recvConfig);
            bool recvStarted = await recvSession.ConnectReceiverAsync();
            Assert.True(recvStarted);

            using var sendSession = new SRTStreamSession(sendConfig);
            bool sendStarted = await sendSession.StartTransmissionAsync();
            Assert.True(sendStarted);

            byte[] testData = new byte[1316];
            testData[0] = 0x47;
            testData[188] = 0x47;
            bool sent = sendSession.SendData(testData, testData.Length, 120, true);
            Assert.True(sent);

            byte[] recvBuffer = new byte[2048];
            int received = -1;
            for (int i = 0; i < 50; i++)
            {
                received = recvSession.ReceiveData(recvBuffer);
                if (received > 0) break;
                await Task.Delay(20);
            }

            Assert.Equal(1316, received);
            Assert.Equal(0x47, recvBuffer[0]);

            await sendSession.StopAsync();
            await recvSession.StopAsync();
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

        [Fact]
        public async Task SRT_NativeSourceAndOutput_LoopbackTest()
        {
            var serverConfig = new SRTStreamConfig
            {
                Host = "127.0.0.1",
                Port = 9876,
                Mode = SRTMode.Listener,
                LatencyMs = 50
            };

            var clientConfig = new SRTStreamConfig
            {
                Host = "127.0.0.1",
                Port = 9876,
                Mode = SRTMode.Caller,
                LatencyMs = 50
            };

            using var serverSession = new SRTStreamSession(serverConfig);
            bool serverStarted = await serverSession.ConnectReceiverAsync();
            Assert.True(serverStarted, "Server listener failed to start");

            using var clientSession = new SRTStreamSession(clientConfig);
            bool clientStarted = await clientSession.StartTransmissionAsync();
            Assert.True(clientStarted, "Client caller failed to connect");

            // Wait for handshake
            await Task.Delay(200);

            byte[] sendData = new byte[1316];
            sendData[0] = 0x47; // MPEG-TS sync byte
            sendData[1315] = 0xAA;

            bool sent = clientSession.SendData(sendData, sendData.Length);
            Assert.True(sent, "Client SendData failed");

            byte[] recvBuffer = new byte[1316];
            int received = -1;
            for (int i = 0; i < 20; i++)
            {
                received = serverSession.ReceiveData(recvBuffer);
                if (received > 0) break;
                await Task.Delay(50);
            }

            Assert.Equal(1316, received);
            Assert.Equal(0x47, recvBuffer[0]);
            Assert.Equal(0xAA, recvBuffer[1315]);
        }

        [Fact]
        public async Task SRT_Listener_ClientReconnectOnSameSocket()
        {
            var serverConfig = new SRTStreamConfig
            {
                Host = "127.0.0.1",
                Port = 9888,
                Mode = SRTMode.Listener,
                LatencyMs = 50
            };

            var clientConfig = new SRTStreamConfig
            {
                Host = "127.0.0.1",
                Port = 9888,
                Mode = SRTMode.Caller,
                LatencyMs = 50
            };

            using var serverSession = new SRTStreamSession(serverConfig);
            bool serverStarted = await serverSession.ConnectReceiverAsync();
            Assert.True(serverStarted, "Server listener failed to start");

            // Client 1 connects
            using (var cli1 = new SRTStreamSession(clientConfig))
            {
                Assert.True(await cli1.StartTransmissionAsync(), "Client 1 connect");
                await Task.Delay(100);

                byte[] send1 = new byte[1316];
                send1[0] = 0x47;
                send1[1] = 0x11;
                Assert.True(cli1.SendData(send1, send1.Length));

                byte[] recvBuffer = new byte[1316];
                int recv = -1;
                for (int i = 0; i < 20; i++)
                {
                    recv = serverSession.ReceiveData(recvBuffer);
                    if (recv > 0) break;
                    await Task.Delay(50);
                }
                Assert.Equal(1316, recv);
                Assert.Equal(0x11, recvBuffer[1]);

                await cli1.StopAsync();
            }

            // Server detects disconnect when receiving
            byte[] dummy = new byte[1316];
            for (int i = 0; i < 20; i++)
            {
                int r = serverSession.ReceiveData(dummy);
                if (r < 0) break;
                await Task.Delay(50);
            }

            // Wait a moment for accept loop to reset client socket
            await Task.Delay(150);

            // Client 2 connects to the SAME server session without restarting server!
            using (var cli2 = new SRTStreamSession(clientConfig))
            {
                Assert.True(await cli2.StartTransmissionAsync(), "Client 2 reconnect to same server");
                await Task.Delay(100);

                byte[] send2 = new byte[1316];
                send2[0] = 0x47;
                send2[1] = 0x22;
                Assert.True(cli2.SendData(send2, send2.Length));

                byte[] recvBuffer2 = new byte[1316];
                int recv2 = -1;
                for (int i = 0; i < 30; i++)
                {
                    recv2 = serverSession.ReceiveData(recvBuffer2);
                    if (recv2 > 0) break;
                    await Task.Delay(50);
                }
                Assert.Equal(1316, recv2);
                Assert.Equal(0x22, recvBuffer2[1]);

                await cli2.StopAsync();
            }

            await serverSession.StopAsync();
        }

        [Fact]
        public async Task SRT_TwoConcurrentListeners_ClientReconnectWhileOtherStreaming()
        {
            var srv1Config = new SRTStreamConfig { Host = "127.0.0.1", Port = 9880, Mode = SRTMode.Listener, LatencyMs = 50 };
            var srv2Config = new SRTStreamConfig { Host = "127.0.0.1", Port = 9881, Mode = SRTMode.Listener, LatencyMs = 50 };

            var cli1Config = new SRTStreamConfig { Host = "127.0.0.1", Port = 9880, Mode = SRTMode.Caller, LatencyMs = 50 };
            var cli2Config = new SRTStreamConfig { Host = "127.0.0.1", Port = 9881, Mode = SRTMode.Caller, LatencyMs = 50 };

            using var srv1 = new SRTStreamSession(srv1Config);
            Assert.True(await srv1.ConnectReceiverAsync(), "Srv 1 start");

            using var srv2 = new SRTStreamSession(srv2Config);
            Assert.True(await srv2.ConnectReceiverAsync(), "Srv 2 start");

            var cli1 = new SRTStreamSession(cli1Config);
            Assert.True(await cli1.StartTransmissionAsync(), "Cli 1 start");

            var cli2 = new SRTStreamSession(cli2Config);
            Assert.True(await cli2.StartTransmissionAsync(), "Cli 2 start");

            await Task.Delay(100);

            // Both send data
            byte[] p1 = new byte[1316]; p1[0] = 0x47; p1[1] = 0x11;
            byte[] p2 = new byte[1316]; p2[0] = 0x47; p2[1] = 0x22;
            Assert.True(cli1.SendData(p1, p1.Length));
            Assert.True(cli2.SendData(p2, p2.Length));

            // Stop Client 1 only
            await cli1.StopAsync();
            cli1.Dispose();

            // Client 2 CONTINUES sending packets in background!
            using var cts = new CancellationTokenSource();
            var cli2Pumping = Task.Run(async () =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    cli2.SendData(p2, p2.Length);
                    await Task.Delay(20);
                }
            });

            // Drain srv1
            byte[] dummy = new byte[1316];
            for (int i = 0; i < 20; i++)
            {
                if (srv1.ReceiveData(dummy) < 0) break;
                await Task.Delay(50);
            }

            // SIMULATE MULTISTREAMRECEIVERENGINE TEARDOWN
            await srv1.StopAsync();
            srv1.Dispose();

            await Task.Delay(1500);

            // Recreate srv1 while cli2 is still actively sending data
            var srv1New = new SRTStreamSession(srv1Config);
            bool srv1NewStarted = await srv1New.ConnectReceiverAsync();
            Assert.True(srv1NewStarted, "Srv 1 restart while Cli 2 streaming");

            // Reconnect Client 1 while Client 2 is still actively sending data
            cli1 = new SRTStreamSession(cli1Config);
            Assert.True(await cli1.StartTransmissionAsync(), "Cli 1 reconnect while Cli 2 streaming");

            await Task.Delay(100);
            Assert.True(cli1.SendData(p1, p1.Length));

            byte[] recv1 = new byte[1316];
            int received1 = -1;
            for (int i = 0; i < 30; i++)
            {
                received1 = srv1New.ReceiveData(recv1);
                if (received1 > 0) break;
                await Task.Delay(50);
            }

            Assert.Equal(1316, received1);
            Assert.Equal(0x11, recv1[1]);

            cts.Cancel();
            await cli2Pumping;
            await cli1.StopAsync();
            cli1.Dispose();
            await cli2.StopAsync();
            cli2.Dispose();
            await srv1New.StopAsync();
            srv1New.Dispose();
            await srv2.StopAsync();
        }

        [Fact]
        public void HitlessMergeDeduplicator_NullPacketHeader_DoesNotCauseFalseDuplicateDrop()
        {
            var dedup = new HitlessMergeDeduplicator(differentialDelayMs: 30);

            // Tạo 2 gói tin 1316 bytes cùng bắt đầu bằng MPEG-TS Null Packet (0x47, 0x1F, 0xFF, 0x10...)
            // nhưng mang payload video/audio khác nhau ở các byte tiếp theo
            byte[] packet1 = new byte[1316];
            packet1[0] = 0x47; packet1[1] = 0x1F; packet1[2] = 0xFF; packet1[3] = 0x10;
            packet1[500] = 0xAA;

            byte[] packet2 = new byte[1316];
            packet2[0] = 0x47; packet2[1] = 0x1F; packet2[2] = 0xFF; packet2[3] = 0x10;
            packet2[500] = 0xBB; // payload khác packet1

            // Cả 2 gói đều từ Path 0 (Primary)
            Assert.True(dedup.PushPacket(0, packet1, packet1.Length), "Packet 1 should be accepted");
            Assert.True(dedup.PushPacket(0, packet2, packet2.Length), "Packet 2 must NOT be falsely dropped as duplicate");

            byte[] readBuffer = new byte[1316];
            int r1 = dedup.ReadMergedData(readBuffer);
            Assert.Equal(1316, r1);
            Assert.Equal(0xAA, readBuffer[500]);

            int r2 = dedup.ReadMergedData(readBuffer);
            Assert.Equal(1316, r2);
            Assert.Equal(0xBB, readBuffer[500]);
        }

        [Fact]
        public void HitlessMergeDeduplicator_DualPath_DiscardsDuplicatesCorrectly()
        {
            var dedup = new HitlessMergeDeduplicator(differentialDelayMs: 30);

            byte[] packet = new byte[1316];
            packet[0] = 0x47; packet[1] = 0x01; packet[2] = 0x00; packet[3] = 0x10;
            packet[100] = 0x42;

            // Gửi từ Path 0 (Primary)
            Assert.True(dedup.PushPacket(0, packet, packet.Length));

            // Gửi cùng gói từ Path 1 (Secondary) -> Phải bị loại bỏ do trùng lặp
            Assert.False(dedup.PushPacket(1, packet, packet.Length));
            Assert.Equal(1u, dedup.Stats.DuplicatesDropped);

            byte[] outBuf = new byte[1316];
            int read = dedup.ReadMergedData(outBuf);
            Assert.Equal(1316, read);
            Assert.Equal(0x42, outBuf[100]);

            // Sau khi đọc hết, queue phải rỗng (không có gói thừa)
            Assert.Equal(0, dedup.ReadMergedData(outBuf));
        }

        [Fact]
        public async Task HitlessMergeDeduplicator_PacketLostOnPrimary_RecoveredFromSecondary()
        {
            var dedup = new HitlessMergeDeduplicator(differentialDelayMs: 20);

            // Packet 1 gửi trên cả 2 path
            byte[] p1 = new byte[1316]; p1[0] = 0x47; p1[1] = 0x01;
            dedup.PushPacket(0, p1, p1.Length);
            dedup.PushPacket(1, p1, p1.Length);

            // Packet 2 BỊ MẤT trên Path 0, chỉ có trên Path 1
            byte[] p2 = new byte[1316]; p2[0] = 0x47; p2[1] = 0x02;
            dedup.PushPacket(1, p2, p2.Length);

            // Đợi hết differential delay (20ms) để Path 1 bù khuyết
            await Task.Delay(40);

            // Packet 3 gửi trên cả 2 path
            byte[] p3 = new byte[1316]; p3[0] = 0x47; p3[1] = 0x03;
            dedup.PushPacket(0, p3, p3.Length);
            dedup.PushPacket(1, p3, p3.Length);

            byte[] outBuf = new byte[1316];
            Assert.Equal(1316, dedup.ReadMergedData(outBuf));
            Assert.Equal(0x01, outBuf[1]); // p1

            Assert.Equal(1316, dedup.ReadMergedData(outBuf));
            Assert.Equal(0x02, outBuf[1]); // p2 recovered!

            Assert.Equal(1316, dedup.ReadMergedData(outBuf));
            Assert.Equal(0x03, outBuf[1]); // p3 in exact sequence!

            Assert.True(dedup.Stats.RecoveredFromRedundantPath >= 1);
        }
    }
}

