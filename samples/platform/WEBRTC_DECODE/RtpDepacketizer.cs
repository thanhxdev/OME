using System;
using System.IO;

namespace WEBRTC_DECODE
{
    /// <summary>
    /// Broadcast-grade C# RTP Depacketizer supporting RFC 6184 (H.264), RFC 7798 (H.265), and RFC 7587 (Opus).
    /// Reassembles fragmented RTP packets (FU-A / FU) into raw Annex B NAL streams and Opus audio frames.
    /// </summary>
    public sealed class RtpDepacketizer
    {
        private static readonly byte[] AnnexBStartCode = { 0x00, 0x00, 0x00, 0x01 };

        private readonly MemoryStream _videoReassemblyStream = new(1024 * 1024);
        private bool _isAssemblingVideo = false;
        private uint _currentVideoTimestamp = 0;
        private ushort _lastVideoSeq = 0;
        private bool _hasReceivedFirstVideoPacket = false;
        private uint _lastRtpTimestamp = 0;
        private long _lastArrivalTicks = 0;
        private double _jitterEstimate = 0.0;

        public event Action<byte[], int>? VideoNalAssembled;
        public event Action<byte[], int>? OpusAudioFrameReady;
        public event Action<ushort>? SequenceGapDetected;

        public ulong TotalPacketsProcessed { get; private set; }
        public ulong TotalNalsProduced { get; private set; }
        public ulong SequenceGaps { get; private set; }
        public double EstimatedJitterMs => _jitterEstimate;

        public void ProcessVideoRtpPacket(byte[] packet, int length)
        {
            if (packet == null || length < 12) return;
            TotalPacketsProcessed++;

            // RTP Header parsing
            byte payloadType = (byte)(packet[1] & 0x7F);
            ushort seqNum = (ushort)((packet[2] << 8) | packet[3]);
            uint timestamp = (uint)((packet[4] << 24) | (packet[5] << 16) | (packet[6] << 8) | packet[7]);

            // RFC 3550 RTP Interarrival Jitter calculation (90kHz video clock)
            long nowTicks = DateTime.UtcNow.Ticks;
            if (_hasReceivedFirstVideoPacket && _lastArrivalTicks > 0)
            {
                double transitDiffMs = ((nowTicks - _lastArrivalTicks) / 10000.0) - ((double)(timestamp - _lastRtpTimestamp) / 90.0);
                double d = Math.Abs(transitDiffMs);
                if (d < 500) // Ignore clock resets/wrap
                {
                    _jitterEstimate += (d - _jitterEstimate) / 16.0;
                }
            }
            _lastRtpTimestamp = timestamp;
            _lastArrivalTicks = nowTicks;

            // Sequence gap detection
            if (_hasReceivedFirstVideoPacket)
            {
                ushort expectedSeq = (ushort)(_lastVideoSeq + 1);
                if (seqNum != expectedSeq)
                {
                    int gap = (ushort)(seqNum - expectedSeq);
                    if (gap > 0 && gap < 1000)
                    {
                        SequenceGaps++;
                        SequenceGapDetected?.Invoke(expectedSeq);
                    }
                }
            }
            _hasReceivedFirstVideoPacket = true;
            _lastVideoSeq = seqNum;

            int payloadOffset = 12;
            int payloadLength = length - 12;
            if (payloadLength <= 0) return;

            if (payloadType == 97) // H.265 / HEVC
            {
                DepacketizeH265(packet, payloadOffset, payloadLength, timestamp);
            }
            else // H.264 / AVC (default PT 96)
            {
                DepacketizeH264(packet, payloadOffset, payloadLength, timestamp);
            }
        }

        private void DepacketizeH264(byte[] packet, int offset, int length, uint timestamp)
        {
            byte firstByte = packet[offset];
            byte nalType = (byte)(firstByte & 0x1F);

            if (nalType >= 1 && nalType <= 23)
            {
                // Single NAL Unit Packet
                EmitSingleNal(packet, offset, length);
            }
            else if (nalType == 28)
            {
                // FU-A Fragmentation Unit (RFC 6184)
                if (length < 2) return;

                byte indicator = packet[offset];
                byte fuHeader = packet[offset + 1];
                bool isStart = (fuHeader & 0x80) != 0;
                bool isEnd = (fuHeader & 0x40) != 0;
                byte originalNalType = (byte)(fuHeader & 0x1F);

                byte reconstructedNalHeader = (byte)((indicator & 0xE0) | originalNalType);

                int dataOffset = offset + 2;
                int dataLen = length - 2;

                if (isStart)
                {
                    _videoReassemblyStream.SetLength(0);
                    _videoReassemblyStream.Write(AnnexBStartCode, 0, 4);
                    _videoReassemblyStream.WriteByte(reconstructedNalHeader);
                    if (dataLen > 0)
                    {
                        _videoReassemblyStream.Write(packet, dataOffset, dataLen);
                    }
                    _isAssemblingVideo = true;
                    _currentVideoTimestamp = timestamp;
                }
                else if (_isAssemblingVideo)
                {
                    if (dataLen > 0)
                    {
                        _videoReassemblyStream.Write(packet, dataOffset, dataLen);
                    }

                    if (isEnd)
                    {
                        byte[] nalBytes = _videoReassemblyStream.ToArray();
                        _videoReassemblyStream.SetLength(0);
                        _isAssemblingVideo = false;
                        TotalNalsProduced++;
                        VideoNalAssembled?.Invoke(nalBytes, nalBytes.Length);
                    }
                }
            }
            else if (nalType == 24)
            {
                // STAP-A Aggregation Packet
                int pos = offset + 1;
                int end = offset + length;
                while (pos + 2 <= end)
                {
                    int nalSize = (packet[pos] << 8) | packet[pos + 1];
                    pos += 2;
                    if (pos + nalSize <= end && nalSize > 0)
                    {
                        EmitSingleNal(packet, pos, nalSize);
                        pos += nalSize;
                    }
                    else break;
                }
            }
        }

        private void DepacketizeH265(byte[] packet, int offset, int length, uint timestamp)
        {
            if (length < 2) return;

            byte header0 = packet[offset];
            byte header1 = packet[offset + 1];
            byte nalType = (byte)((header0 >> 1) & 0x3F);

            if (nalType != 49)
            {
                // Single NAL Packet
                EmitSingleNal(packet, offset, length);
            }
            else
            {
                // FU Fragmentation Unit (Type 49, RFC 7798)
                if (length < 3) return;

                byte fuHeader = packet[offset + 2];
                bool isStart = (fuHeader & 0x80) != 0;
                bool isEnd = (fuHeader & 0x40) != 0;
                byte originalNalType = (byte)(fuHeader & 0x3F);

                byte reconstructed0 = (byte)((header0 & 0x81) | (originalNalType << 1));
                byte reconstructed1 = header1;

                int dataOffset = offset + 3;
                int dataLen = length - 3;

                if (isStart)
                {
                    _videoReassemblyStream.SetLength(0);
                    _videoReassemblyStream.Write(AnnexBStartCode, 0, 4);
                    _videoReassemblyStream.WriteByte(reconstructed0);
                    _videoReassemblyStream.WriteByte(reconstructed1);
                    if (dataLen > 0)
                    {
                        _videoReassemblyStream.Write(packet, dataOffset, dataLen);
                    }
                    _isAssemblingVideo = true;
                    _currentVideoTimestamp = timestamp;
                }
                else if (_isAssemblingVideo)
                {
                    if (dataLen > 0)
                    {
                        _videoReassemblyStream.Write(packet, dataOffset, dataLen);
                    }

                    if (isEnd)
                    {
                        byte[] nalBytes = _videoReassemblyStream.ToArray();
                        _videoReassemblyStream.SetLength(0);
                        _isAssemblingVideo = false;
                        TotalNalsProduced++;
                        VideoNalAssembled?.Invoke(nalBytes, nalBytes.Length);
                    }
                }
            }
        }

        private void EmitSingleNal(byte[] packet, int offset, int length)
        {
            byte[] nalWithStartCode = new byte[4 + length];
            Buffer.BlockCopy(AnnexBStartCode, 0, nalWithStartCode, 0, 4);
            Buffer.BlockCopy(packet, offset, nalWithStartCode, 4, length);
            TotalNalsProduced++;
            VideoNalAssembled?.Invoke(nalWithStartCode, nalWithStartCode.Length);
        }

        public void ProcessAudioRtpPacket(byte[] packet, int length)
        {
            if (packet == null || length <= 12) return;

            // Extract raw Opus audio frame directly from RTP payload
            int payloadOffset = 12;
            int payloadLen = length - 12;

            byte[] opusFrame = new byte[payloadLen];
            Buffer.BlockCopy(packet, payloadOffset, opusFrame, 0, payloadLen);
            OpusAudioFrameReady?.Invoke(opusFrame, payloadLen);
        }

        public void Reset()
        {
            _videoReassemblyStream.SetLength(0);
            _isAssemblingVideo = false;
            _hasReceivedFirstVideoPacket = false;
        }
    }
}
