using System;
using OpenMedia.Platform.Internal;
using OpenMedia.Platform.Models;
using Xunit;

namespace OpenMedia.Platform.Tests
{
    public class HitlessMergeDeduplicatorTests
    {
        [Fact]
        public void Deduplicator_DropsIdenticalPackets_FromPathB()
        {
            var deduplicator = new HitlessMergeDeduplicator(differentialDelayMs: 100);

            // Construct sample TS packet (188 bytes, sync byte 0x47)
            byte[] packetA = new byte[188];
            packetA[0] = 0x47; // Sync byte
            packetA[1] = 0x01; // PID high
            packetA[2] = 0x00; // PID low
            packetA[3] = 0x10; // CC = 0
            for (int i = 4; i < 188; i++) packetA[i] = (byte)(i % 255);

            byte[] packetB = (byte[])packetA.Clone();

            // Push from Path A (pathIndex 0)
            bool pushedA = deduplicator.PushPacket(0, packetA, packetA.Length);
            Assert.True(pushedA);

            // Push duplicate from Path B (pathIndex 1)
            bool pushedB = deduplicator.PushPacket(1, packetB, packetB.Length);
            Assert.False(pushedB); // Should be dropped as duplicate

            var stats = deduplicator.Stats;
            Assert.Equal(1UL, stats.PathAPackets);
            Assert.Equal(1UL, stats.PathBPackets);
            Assert.Equal(1UL, stats.DuplicatesDropped);

            // Dequeue packet
            byte[] outBuf = new byte[1024];
            int read = deduplicator.ReadMergedData(outBuf);
            Assert.Equal(188, read);

            // No second packet should be outputted because it was dropped
            int secondRead = deduplicator.ReadMergedData(outBuf);
            Assert.Equal(0, secondRead);
        }

        [Fact]
        public void Deduplicator_SeamlessRedundancy_PassesBothUniquePackets()
        {
            var deduplicator = new HitlessMergeDeduplicator(differentialDelayMs: 100);

            // Packet 1: arrives only on Path A
            byte[] p1 = new byte[188];
            p1[0] = 0x47;
            p1[3] = 0x11; // CC 1
            bool pushed1 = deduplicator.PushPacket(0, p1, p1.Length);
            Assert.True(pushed1);

            // Packet 2: arrives only on Path B
            byte[] p2 = new byte[188];
            p2[0] = 0x47;
            p2[3] = 0x12; // CC 2
            bool pushed2 = deduplicator.PushPacket(1, p2, p2.Length);
            Assert.True(pushed2);

            var stats = deduplicator.Stats;
            Assert.Equal(1UL, stats.PathAPackets);
            Assert.Equal(1UL, stats.PathBPackets);
            Assert.Equal(0UL, stats.DuplicatesDropped);

            byte[] outBuf = new byte[1024];
            int read1 = deduplicator.ReadMergedData(outBuf);
            Assert.Equal(188, read1);
            Assert.Equal(0x11, outBuf[3]);

            int read2 = deduplicator.ReadMergedData(outBuf);
            Assert.Equal(188, read2);
            Assert.Equal(0x12, outBuf[3]);
        }

        [Fact]
        public void Deduplicator_Reset_ClearsInternalQueues()
        {
            var deduplicator = new HitlessMergeDeduplicator(differentialDelayMs: 100);
            byte[] p = new byte[188];
            p[0] = 0x47;
            deduplicator.PushPacket(0, p, p.Length);
            Assert.Equal(1, deduplicator.QueueCount);

            deduplicator.Reset();
            Assert.Equal(0, deduplicator.QueueCount);

            byte[] outBuf = new byte[1024];
            Assert.Equal(0, deduplicator.ReadMergedData(outBuf));
        }

        [Fact]
        public void Deduplicator_DoesNotDrop_RepetitivePackets_FromSamePath()
        {
            var deduplicator = new HitlessMergeDeduplicator(differentialDelayMs: 100);

            // Giả lập 2 gói tin MPEG-TS PAT hoặc NULL packet hoàn toàn giống nhau byte-for-byte gửi trên cùng Path A (pathIndex 0)
            byte[] patPacket = new byte[188];
            patPacket[0] = 0x47;
            patPacket[1] = 0x00; // PID 0 (PAT)
            patPacket[2] = 0x00;
            patPacket[3] = 0x10;
            patPacket[4] = 0x00; // Pointer field

            // Gói PAT 1 gửi trên Path A
            bool pushed1 = deduplicator.PushPacket(0, patPacket, patPacket.Length);
            Assert.True(pushed1, "Gói PAT 1 trên Path A phải được chấp nhận");

            // Gói PAT 2 (nội dung giống hệt) gửi trên cùng Path A sau 100ms
            bool pushed2 = deduplicator.PushPacket(0, patPacket, patPacket.Length);
            Assert.True(pushed2, "Gói PAT 2 trên cùng Path A KHÔNG được bị drop nhầm thành duplicate");

            var stats = deduplicator.Stats;
            Assert.Equal(2UL, stats.PathAPackets);
            Assert.Equal(0UL, stats.DuplicatesDropped);
            Assert.Equal(2, deduplicator.QueueCount);
        }
    }
}
