using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WEBRTC_ENCODE
{
    public enum VideoRtpCodec
    {
        H264 = 96,
        H265 = 97
    }

    /// <summary>
    /// Broadcast-grade C# RTP Stream Sender for WebRTC SFU Ingress.
    /// Implements RFC 3550 (RTP), RFC 6184 (H.264), RFC 7798 (H.265), RFC 7587 (Opus), and RFC 4585 (RTCP NACK/PLI).
    /// </summary>
    public sealed class RtpStreamSender : IDisposable
    {
        private const int MaxMtu = 1200; // Safe MTU payload size to prevent UDP fragmentation
        private const int RtpHeaderLength = 12;

        private Socket? _videoSocket;
        private Socket? _audioSocket;
        private IPEndPoint? _sfuVideoEndpoint;
        private IPEndPoint? _sfuAudioEndpoint;

        private uint _videoSsrc;
        private uint _audioSsrc;
        private ushort _videoSeqNum;
        private ushort _audioSeqNum;
        private uint _videoTimestamp;
        private uint _audioTimestamp;

        private VideoRtpCodec _videoCodec = VideoRtpCodec.H264;
        private bool _isRunning;
        private bool _disposed;

        // Retransmission Packet Cache (Circular buffer for ARQ NACK retransmission)
        private const int CacheSize = 1024;
        private readonly byte[][] _packetCache = new byte[CacheSize][];
        private readonly ushort[] _cacheSeq = new ushort[CacheSize];
        private readonly object _cacheLock = new();

        // Annex B Stream NAL Accumulator to prevent truncated NAL slices across pipe reads
        private readonly MemoryStream _nalAccumulator = new(256 * 1024);
        private readonly object _videoStreamLock = new();

        // Telemetry
        private ulong _videoPacketsSent;
        private ulong _videoBytesSent;
        private ulong _audioPacketsSent;
        private ulong _audioBytesSent;
        private int _nackCount;
        private int _pliCount;
        private double _currentBitrateKbps;
        private DateTime _lastBitrateCalc = DateTime.UtcNow;
        private ulong _lastTotalBytes = 0;

        // Advanced Real-Time Network Quality Telemetry
        private double _currentRttMs = 1.0;
        private double _currentLossPercent = 0.0;
        private double _videoBitrateKbps = 0.0;
        private double _audioBitrateKbps = 0.0;
        private double _jitterMs = 0.0;
        private ulong _lastVideoBytes = 0;
        private ulong _lastAudioBytes = 0;
        private DateTime _lastRtcpReportTime = DateTime.MinValue;
        private DateTime _lastNonZeroBitrateTime = DateTime.UtcNow;
        private CancellationTokenSource? _telemetryCts;

        private string _host = "127.0.0.1";
        private int _videoPort = 10000;
        private int _audioPort = 10002;
        private bool _isSinglePortMode = false;

        public string Host
        {
            get => _host;
            set
            {
                _host = string.IsNullOrWhiteSpace(value) ? "127.0.0.1" : value.Trim();
                UpdateEndpoints();
            }
        }

        public int VideoPort
        {
            get => _videoPort;
            set
            {
                _videoPort = value;
                if (_isSinglePortMode)
                {
                    _audioPort = _videoPort;
                }
                UpdateEndpoints();
            }
        }

        public int AudioPort
        {
            get => _audioPort;
            set
            {
                _audioPort = value;
                UpdateEndpoints();
            }
        }

        public bool IsSinglePortMode
        {
            get => _isSinglePortMode;
            set
            {
                _isSinglePortMode = value;
                if (_isSinglePortMode)
                {
                    _audioPort = _videoPort;
                }
                else if (_audioPort == _videoPort)
                {
                    _audioPort = _videoPort + 2;
                }
                UpdateEndpoints();
            }
        }

        public bool EnableEchoCancellation { get; set; } = true;
        public VideoRtpCodec VideoCodec => _videoCodec;
        public bool IsRunning => _isRunning;

        public double CurrentBitrateKbps => _currentBitrateKbps;
        public double VideoBitrateKbps => _videoBitrateKbps;
        public double AudioBitrateKbps => _audioBitrateKbps;
        public double CurrentRttMs => _currentRttMs;
        public double CurrentLossPercent => _currentLossPercent;
        public double JitterMs => _jitterMs;
        public ulong TotalPacketsSent => _videoPacketsSent + _audioPacketsSent;
        public ulong TotalBytesSent => _videoBytesSent + _audioBytesSent;
        public ulong VideoPacketsSent => _videoPacketsSent;
        public ulong AudioPacketsSent => _audioPacketsSent;
        public ulong VideoBytesSent => _videoBytesSent;
        public ulong AudioBytesSent => _audioBytesSent;
        public int NackCount => _nackCount;
        public int PliCount => _pliCount;
        public string CameraDisplayName { get; set; } = string.Empty;
        public string CameraId { get; set; } = string.Empty;

        public event Action? KeyframeRequested;
        public event Action<string, string>? LogEmitted;

        public RtpStreamSender()
        {
            var rnd = new Random();
            _videoSsrc = (uint)rnd.Next(100000, 999999);
            _audioSsrc = (uint)rnd.Next(100000, 999999);
            _videoSeqNum = (ushort)rnd.Next(0, 32767);
            _audioSeqNum = (ushort)rnd.Next(0, 32767);
            UpdateEndpoints();
        }

        private void UpdateEndpoints()
        {
            try
            {
                if (IPAddress.TryParse(_host, out var ip))
                {
                    _sfuVideoEndpoint = new IPEndPoint(ip, _videoPort);
                    _sfuAudioEndpoint = new IPEndPoint(ip, _audioPort);
                }
            }
            catch { }
        }

        public void ConfigureCamera(int cameraIndex, string host = "127.0.0.1", VideoRtpCodec codec = VideoRtpCodec.H264, bool singlePortMode = false)
        {
            // Port mapping: cam-01: 10000/10002, cam-02: 10004/10006 ...
            _host = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();
            _isSinglePortMode = singlePortMode;
            _videoPort = 10000 + (cameraIndex * 4);
            _audioPort = _isSinglePortMode ? _videoPort : _videoPort + 2;
            _videoCodec = codec;
            UpdateEndpoints();
        }

        public bool Start()
        {
            if (IsSinglePortMode)
            {
                _audioPort = _videoPort;
            }
            else if (_audioPort == _videoPort)
            {
                _audioPort = _videoPort + 2;
            }

            UpdateEndpoints();

            if (_isRunning)
            {
                string runningModeDesc = IsSinglePortMode ? "1-Port Muxed BUNDLE" : "2-Port Split A/V";
                Log("[RTP]", $"Cập nhật RTP Sender ({runningModeDesc}) tới SFU tại {Host} - Video: {VideoPort} ({_videoCodec}), Audio: {AudioPort} (Opus)");
                return true;
            }

            try
            {
                _videoSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
                {
                    SendBufferSize = 2 * 1024 * 1024,
                    ReceiveBufferSize = 512 * 1024
                };

                // Bind to ephemeral port to receive incoming RTCP feedback (RR, NACK, PLI)
                try
                {
                    _videoSocket.Bind(new IPEndPoint(IPAddress.Any, 0));
                }
                catch { }

                _audioSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
                {
                    SendBufferSize = 512 * 1024,
                    ReceiveBufferSize = 256 * 1024
                };

                _isRunning = true;
                _lastBitrateCalc = DateTime.UtcNow;
                _lastTotalBytes = 0;
                _lastVideoBytes = 0;
                _lastAudioBytes = 0;
                _videoBitrateKbps = 0;
                _audioBitrateKbps = 0;
                _currentBitrateKbps = 0;
                _currentLossPercent = 0;

                _telemetryCts?.Cancel();
                _telemetryCts?.Dispose();
                _telemetryCts = new CancellationTokenSource();
                var token = _telemetryCts.Token;

                // Start active RTT network prober & RTCP receiver
                _ = Task.Run(() => RttProbeLoopAsync(token), token);
                _ = Task.Run(() => RtcpReceiveLoopAsync(token), token);

                string modeDesc = IsSinglePortMode ? "1-Port Muxed BUNDLE" : "2-Port Split A/V";
                string aecDesc = EnableEchoCancellation ? "Bật (AEC Loopback Guard)" : "Tắt";
                Log("[RTP]", $"Khởi động RTP Sender ({modeDesc}, Echo Guard: {aecDesc}) tới SFU tại {Host} - Video: {VideoPort} ({_videoCodec}), Audio: {AudioPort} (Opus)");
                return true;
            }
            catch (Exception ex)
            {
                Log("[ERROR]", $"Không thể khởi tạo RTP Sockets: {ex.Message}");
                _isRunning = false;
                return false;
            }
        }

        public void Stop()
        {
            _isRunning = false;
            try { _telemetryCts?.Cancel(); } catch { }
            try { _telemetryCts?.Dispose(); } catch { }
            _telemetryCts = null;

            try { _videoSocket?.Close(); } catch { }
            try { _audioSocket?.Close(); } catch { }
            _videoSocket = null;
            _audioSocket = null;
            lock (_videoStreamLock)
            {
                _nalAccumulator.SetLength(0);
            }
            Log("[RTP]", "Đã dừng RTP Sender.");
        }

        public void SetCodec(VideoRtpCodec codec)
        {
            _videoCodec = codec;
            Log("[RTP]", $"Chuyển đổi RTP Video Codec: {_videoCodec} (PT {(int)_videoCodec})");
        }

        /// <summary>
        /// Sends an entire video frame composed of raw Annex B bitstream (containing one or more NAL units).
        /// Automatically segments NAL units into Single NAL or FU-A fragments and sends via UDP.
        /// </summary>
        public void SendVideoFrame(byte[] frameData, int length, uint timestampIncrement = 3000)
        {
            if (!_isRunning || _videoSocket == null || length <= 0) return;

            lock (_videoStreamLock)
            {
                _nalAccumulator.Write(frameData, 0, length);
                byte[] streamBytes = _nalAccumulator.GetBuffer();
                int streamLen = (int)_nalAccumulator.Length;

                int lastProcessedPos = 0;
                int i = 0;

                while (i < streamLen - 3)
                {
                    int startCodeLen = 0;
                    if (streamBytes[i] == 0 && streamBytes[i + 1] == 0 && streamBytes[i + 2] == 1)
                    {
                        startCodeLen = 3;
                    }
                    else if (i < streamLen - 4 && streamBytes[i] == 0 && streamBytes[i + 1] == 0 && streamBytes[i + 2] == 0 && streamBytes[i + 3] == 1)
                    {
                        startCodeLen = 4;
                    }

                    if (startCodeLen > 0)
                    {
                        int nalStart = i + startCodeLen;
                        // Search for the next start code to ensure NAL is complete
                        int nextStart = -1;
                        for (int j = nalStart; j < streamLen - 3; j++)
                        {
                            if ((streamBytes[j] == 0 && streamBytes[j + 1] == 0 && streamBytes[j + 2] == 1) ||
                                (j < streamLen - 4 && streamBytes[j] == 0 && streamBytes[j + 1] == 0 && streamBytes[j + 2] == 0 && streamBytes[j + 3] == 1))
                            {
                                nextStart = j;
                                break;
                            }
                        }

                        if (nextStart != -1)
                        {
                            int nalLength = nextStart - nalStart;
                            if (nalLength > 0)
                            {
                                if (_videoCodec == VideoRtpCodec.H265)
                                {
                                    SendH265Nal(streamBytes, nalStart, nalLength, false);
                                }
                                else
                                {
                                    SendH264Nal(streamBytes, nalStart, nalLength, false);
                                }
                            }
                            lastProcessedPos = nextStart;
                            i = nextStart;
                        }
                        else
                        {
                            // Partial NAL slice at end of buffer -> wait for next read chunk
                            break;
                        }
                    }
                    else
                    {
                        i++;
                    }
                }

                // Compact unprocessed tail
                if (lastProcessedPos > 0)
                {
                    int remaining = streamLen - lastProcessedPos;
                    if (remaining > 0)
                    {
                        Buffer.BlockCopy(streamBytes, lastProcessedPos, streamBytes, 0, remaining);
                        _nalAccumulator.SetLength(remaining);
                    }
                    else
                    {
                        _nalAccumulator.SetLength(0);
                    }
                }
                else if (streamLen > 2 * 1024 * 1024)
                {
                    // Buffer safeguard
                    _nalAccumulator.SetLength(0);
                }
            }

            unchecked
            {
                _videoTimestamp += timestampIncrement;
            }

            UpdateBitrateMetrics();
        }

        /// <summary>
        /// Packetize and send H.264 NAL (RFC 6184).
        /// </summary>
        private void SendH264Nal(byte[] buffer, int offset, int length, bool isLastNalInFrame)
        {
            if (length <= 0) return;

            if (length <= MaxMtu)
            {
                // Single NAL Unit Packet
                byte[] rtpPacket = new byte[RtpHeaderLength + length];
                BuildRtpHeader(rtpPacket, (byte)VideoRtpCodec.H264, isLastNalInFrame, _videoSeqNum++, _videoTimestamp, _videoSsrc);
                Buffer.BlockCopy(buffer, offset, rtpPacket, RtpHeaderLength, length);

                SendAndCacheVideoPacket(rtpPacket);
            }
            else
            {
                // FU-A Fragmentation Unit (NAL Type 28)
                byte nalHeader = buffer[offset];
                byte fnri = (byte)(nalHeader & 0xE0); // F and NRI bits
                byte originalType = (byte)(nalHeader & 0x1F);

                int payloadOffset = offset + 1;
                int remaining = length - 1;
                bool isFirst = true;

                while (remaining > 0)
                {
                    int chunkSize = Math.Min(remaining, MaxMtu - 2); // 2 bytes for FU indicator & header
                    bool isLastChunk = (remaining - chunkSize == 0);
                    bool marker = isLastChunk && isLastNalInFrame;

                    byte[] rtpPacket = new byte[RtpHeaderLength + 2 + chunkSize];
                    BuildRtpHeader(rtpPacket, (byte)VideoRtpCodec.H264, marker, _videoSeqNum++, _videoTimestamp, _videoSsrc);

                    // FU Indicator: [ F | NRI | Type(28) ]
                    rtpPacket[RtpHeaderLength] = (byte)(fnri | 28);

                    // FU Header: [ S | E | R | Type(original) ]
                    byte fuHeader = originalType;
                    if (isFirst) fuHeader |= 0x80; // S bit
                    if (isLastChunk) fuHeader |= 0x40; // E bit
                    rtpPacket[RtpHeaderLength + 1] = fuHeader;

                    Buffer.BlockCopy(buffer, payloadOffset, rtpPacket, RtpHeaderLength + 2, chunkSize);

                    SendAndCacheVideoPacket(rtpPacket);

                    payloadOffset += chunkSize;
                    remaining -= chunkSize;
                    isFirst = false;
                }
            }
        }

        /// <summary>
        /// Packetize and send H.265 NAL (RFC 7798).
        /// </summary>
        private void SendH265Nal(byte[] buffer, int offset, int length, bool isLastNalInFrame)
        {
            if (length < 2) return;

            if (length <= MaxMtu)
            {
                // Single NAL Packet
                byte[] rtpPacket = new byte[RtpHeaderLength + length];
                BuildRtpHeader(rtpPacket, (byte)VideoRtpCodec.H265, isLastNalInFrame, _videoSeqNum++, _videoTimestamp, _videoSsrc);
                Buffer.BlockCopy(buffer, offset, rtpPacket, RtpHeaderLength, length);

                SendAndCacheVideoPacket(rtpPacket);
            }
            else
            {
                // Fragmentation Unit (FU, Type 49)
                byte header0 = buffer[offset];
                byte header1 = buffer[offset + 1];
                byte nalType = (byte)((header0 >> 1) & 0x3F);

                // FU Indicator Payload Header (2 bytes): Type is 49
                byte fuPayloadHeader0 = (byte)((header0 & 0x81) | (49 << 1));
                byte fuPayloadHeader1 = header1;

                int payloadOffset = offset + 2;
                int remaining = length - 2;
                bool isFirst = true;

                while (remaining > 0)
                {
                    int chunkSize = Math.Min(remaining, MaxMtu - 3); // 3 bytes: 2 payload headers + 1 FU header
                    bool isLastChunk = (remaining - chunkSize == 0);
                    bool marker = isLastChunk && isLastNalInFrame;

                    byte[] rtpPacket = new byte[RtpHeaderLength + 3 + chunkSize];
                    BuildRtpHeader(rtpPacket, (byte)VideoRtpCodec.H265, marker, _videoSeqNum++, _videoTimestamp, _videoSsrc);

                    rtpPacket[RtpHeaderLength] = fuPayloadHeader0;
                    rtpPacket[RtpHeaderLength + 1] = fuPayloadHeader1;

                    // FU Header: [ S | E | Type (6 bits) ]
                    byte fuHeader = nalType;
                    if (isFirst) fuHeader |= 0x80;
                    if (isLastChunk) fuHeader |= 0x40;
                    rtpPacket[RtpHeaderLength + 2] = fuHeader;

                    Buffer.BlockCopy(buffer, payloadOffset, rtpPacket, RtpHeaderLength + 3, chunkSize);

                    SendAndCacheVideoPacket(rtpPacket);

                    payloadOffset += chunkSize;
                    remaining -= chunkSize;
                    isFirst = false;
                }
            }
        }

        /// <summary>
        /// Sends raw Opus audio frame directly over RTP (RFC 7587).
        /// </summary>
        public void SendAudioFrame(byte[] opusData, int length, uint timestampIncrement = 960) // 20ms at 48kHz = 960 samples
        {
            if (!_isRunning || _audioSocket == null || length <= 0) return;

            unchecked
            {
                _audioTimestamp += timestampIncrement;
            }

            byte[] rtpPacket = new byte[RtpHeaderLength + length];
            BuildRtpHeader(rtpPacket, 111, false, _audioSeqNum++, _audioTimestamp, _audioSsrc);
            Buffer.BlockCopy(opusData, 0, rtpPacket, RtpHeaderLength, length);

            try
            {
                _audioSocket.SendTo(rtpPacket, _sfuAudioEndpoint!);
                _audioPacketsSent++;
                _audioBytesSent += (ulong)rtpPacket.Length;
                UpdateBitrateMetrics();
            }
            catch { }
        }

        private void SendAndCacheVideoPacket(byte[] packet)
        {
            try
            {
                _videoSocket?.SendTo(packet, _sfuVideoEndpoint!);
                _videoPacketsSent++;
                _videoBytesSent += (ulong)packet.Length;
                UpdateBitrateMetrics();

                // Cache for retransmissions
                ushort seq = (ushort)((packet[2] << 8) | packet[3]);
                int cacheIdx = seq % CacheSize;
                lock (_cacheLock)
                {
                    _packetCache[cacheIdx] = packet;
                    _cacheSeq[cacheIdx] = seq;
                }
            }
            catch { }
        }

        /// <summary>
        /// Responds to RTCP NACK feedback by retransmitting cached RTP packets.
        /// </summary>
        public void HandleNack(ushort lostSeqNum)
        {
            _nackCount++;
            int cacheIdx = lostSeqNum % CacheSize;
            byte[]? packet = null;

            lock (_cacheLock)
            {
                if (_cacheSeq[cacheIdx] == lostSeqNum)
                {
                    packet = _packetCache[cacheIdx];
                }
            }

            if (packet != null && _videoSocket != null && _sfuVideoEndpoint != null)
            {
                try
                {
                    _videoSocket.SendTo(packet, _sfuVideoEndpoint);
                }
                catch { }
            }
        }

        /// <summary>
        /// Triggers instant IDR / Keyframe request from video encoder pipeline.
        /// </summary>
        public void TriggerPli()
        {
            _pliCount++;
            KeyframeRequested?.Invoke();
        }

        private static void BuildRtpHeader(byte[] buffer, byte payloadType, bool marker, ushort seqNum, uint timestamp, uint ssrc)
        {
            // Byte 0: V=2, P=0, X=0, CC=0 -> 0x80
            buffer[0] = 0x80;
            // Byte 1: M (1 bit) | PT (7 bits)
            buffer[1] = (byte)((marker ? 0x80 : 0x00) | (payloadType & 0x7F));
            // Bytes 2-3: Sequence number
            buffer[2] = (byte)(seqNum >> 8);
            buffer[3] = (byte)(seqNum & 0xFF);
            // Bytes 4-7: Timestamp
            buffer[4] = (byte)(timestamp >> 24);
            buffer[5] = (byte)(timestamp >> 16);
            buffer[6] = (byte)(timestamp >> 8);
            buffer[7] = (byte)(timestamp & 0xFF);
            // Bytes 8-11: SSRC
            buffer[8] = (byte)(ssrc >> 24);
            buffer[9] = (byte)(ssrc >> 16);
            buffer[10] = (byte)(ssrc >> 8);
            buffer[11] = (byte)(ssrc & 0xFF);
        }

        private static List<(int offset, int length)> ExtractAnnexBNals(byte[] data, int length)
        {
            var nals = new List<(int offset, int length)>();
            int i = 0;

            while (i < length - 3)
            {
                int startCodeLen = 0;
                if (data[i] == 0 && data[i + 1] == 0 && data[i + 2] == 1)
                {
                    startCodeLen = 3;
                }
                else if (i < length - 4 && data[i] == 0 && data[i + 1] == 0 && data[i + 2] == 0 && data[i + 3] == 1)
                {
                    startCodeLen = 4;
                }

                if (startCodeLen > 0)
                {
                    int nalStart = i + startCodeLen;
                    // Find next start code or end of buffer
                    int nextStart = length;
                    for (int j = nalStart; j < length - 3; j++)
                    {
                        if ((data[j] == 0 && data[j + 1] == 0 && data[j + 2] == 1) ||
                            (j < length - 4 && data[j] == 0 && data[j + 1] == 0 && data[j + 2] == 0 && data[j + 3] == 1))
                        {
                            nextStart = j;
                            break;
                        }
                    }

                    int nalLength = nextStart - nalStart;
                    if (nalLength > 0)
                    {
                        nals.Add((nalStart, nalLength));
                    }
                    i = nextStart;
                }
                else
                {
                    i++;
                }
            }

            return nals;
        }

        private void UpdateBitrateMetrics()
        {
            var now = DateTime.UtcNow;
            double elapsedSec = (now - _lastBitrateCalc).TotalSeconds;
            if (elapsedSec >= 0.5)
            {
                ulong totalV = _videoBytesSent;
                ulong totalA = _audioBytesSent;
                ulong diffV = totalV >= _lastVideoBytes ? totalV - _lastVideoBytes : 0;
                ulong diffA = totalA >= _lastAudioBytes ? totalA - _lastAudioBytes : 0;

                if (diffV > 0 || diffA > 0)
                {
                    double instantV = (diffV * 8.0) / (elapsedSec * 1000.0);
                    double instantA = (diffA * 8.0) / (elapsedSec * 1000.0);

                    _videoBitrateKbps = _videoBitrateKbps > 0 ? (_videoBitrateKbps * 0.3) + (instantV * 0.7) : instantV;
                    _audioBitrateKbps = _audioBitrateKbps > 0 ? (_audioBitrateKbps * 0.3) + (instantA * 0.7) : instantA;
                    _currentBitrateKbps = _videoBitrateKbps + _audioBitrateKbps;

                    _lastNonZeroBitrateTime = now;
                    _lastVideoBytes = totalV;
                    _lastAudioBytes = totalA;
                    _lastTotalBytes = totalV + totalA;
                    _lastBitrateCalc = now;
                }
                else
                {
                    if ((now - _lastNonZeroBitrateTime).TotalSeconds > 1.5)
                    {
                        _videoBitrateKbps = 0;
                        _audioBitrateKbps = 0;
                        _currentBitrateKbps = 0;
                    }
                    _lastBitrateCalc = now;
                }

                // If no recent RTCP Receiver Report arrived in last 3s, estimate loss from NACK count
                if ((now - _lastRtcpReportTime).TotalSeconds > 3.0)
                {
                    ulong totalPkts = TotalPacketsSent;
                    _currentLossPercent = totalPkts > 0 ? Math.Min(100.0, ((double)_nackCount / totalPkts) * 100.0) : 0.0;
                }
            }
        }

        private async Task RttProbeLoopAsync(CancellationToken token)
        {
            using var pinger = new System.Net.NetworkInformation.Ping();
            while (!token.IsCancellationRequested && _isRunning)
            {
                try
                {
                    string hostToPing = _host;
                    if (hostToPing == "localhost" || hostToPing == "127.0.0.1" || hostToPing == "::1")
                    {
                        _currentRttMs = 1.0; // Local loopback latency ~1ms
                    }
                    else
                    {
                        var reply = await pinger.SendPingAsync(hostToPing, 800).ConfigureAwait(false);
                        if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                        {
                            _currentRttMs = Math.Max(0.5, reply.RoundtripTime);
                        }
                    }
                }
                catch
                {
                    if (_currentRttMs <= 0) _currentRttMs = 15.0;
                }

                UpdateBitrateMetrics();

                if (!string.IsNullOrWhiteSpace(CameraDisplayName))
                {
                    SendRtcpSdesPacket(CameraDisplayName);
                }

                try
                {
                    await Task.Delay(1000, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }
            }
        }

        public void SendRtcpSdesPacket(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName) || _videoSocket == null || _sfuVideoEndpoint == null) return;
            try
            {
                byte[] nameBytes = Encoding.UTF8.GetBytes(displayName);
                if (nameBytes.Length > 255) Array.Resize(ref nameBytes, 255);

                // RFC 3550 Section 6.5 SDES Packet
                // Header (4 bytes): V=2, P=0, SC=1 (0x81), PT=202 (SDES), Length (in 32-bit words minus 1)
                // Chunk: SSRC (4 bytes) + Item: Type=2 (NAME, 1 byte), Length (1 byte), Value (N bytes) + END (1 byte: 0)
                int payloadLen = 4 + 1 + 1 + nameBytes.Length + 1;
                int pad = (4 - (payloadLen % 4)) % 4;
                int totalPayloadLen = payloadLen + pad;
                int totalPacketLen = 4 + totalPayloadLen;
                ushort wordsMinusOne = (ushort)((totalPacketLen / 4) - 1);

                byte[] packet = new byte[totalPacketLen];
                packet[0] = 0x81; // V=2, P=0, SC=1
                packet[1] = 202;  // RTCP SDES
                packet[2] = (byte)(wordsMinusOne >> 8);
                packet[3] = (byte)(wordsMinusOne & 0xFF);

                // SSRC
                packet[4] = (byte)(_videoSsrc >> 24);
                packet[5] = (byte)(_videoSsrc >> 16);
                packet[6] = (byte)(_videoSsrc >> 8);
                packet[7] = (byte)(_videoSsrc & 0xFF);

                // SDES item: NAME (2)
                packet[8] = 2; // RTCP_SDES_NAME
                packet[9] = (byte)nameBytes.Length;
                Buffer.BlockCopy(nameBytes, 0, packet, 10, nameBytes.Length);
                packet[10 + nameBytes.Length] = 0; // END item

                _videoSocket.SendTo(packet, _sfuVideoEndpoint);
            }
            catch { }
        }

        private async Task RtcpReceiveLoopAsync(CancellationToken token)
        {
            byte[] buffer = new byte[4096];
            while (!token.IsCancellationRequested && _isRunning && _videoSocket != null)
            {
                try
                {
                    var result = await _videoSocket.ReceiveAsync(buffer, SocketFlags.None, token).ConfigureAwait(false);
                    if (result > 0)
                    {
                        ProcessIncomingRtcp(buffer, result);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (SocketException) { }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[RTCP] Loop exception: {ex.Message}");
                }
            }
        }

        private void ProcessIncomingRtcp(byte[] buffer, int length)
        {
            if (length < 8) return;

            int offset = 0;
            while (offset <= length - 4)
            {
                byte b0 = buffer[offset];
                int version = (b0 >> 6) & 0x03;
                if (version != 2) break; // RFC 3550 RTCP version must be 2

                int count = b0 & 0x1F;
                byte pt = buffer[offset + 1];
                int pktLenWords = (buffer[offset + 2] << 8) | buffer[offset + 3];
                int pktLenBytes = (pktLenWords + 1) * 4;
                if (pktLenBytes <= 0 || offset + pktLenBytes > length) break;

                if (pt == 201) // RTCP Receiver Report (RR)
                {
                    if (pktLenBytes >= 28 && count > 0)
                    {
                        // First report block: offset + 8
                        byte fractionLost = buffer[offset + 12];
                        _currentLossPercent = (fractionLost / 256.0) * 100.0;

                        uint jitter = (uint)((buffer[offset + 20] << 24) | (buffer[offset + 21] << 16) | (buffer[offset + 22] << 8) | buffer[offset + 23]);
                        _jitterMs = jitter / 90.0; // 90kHz video clock

                        _lastRtcpReportTime = DateTime.UtcNow;
                    }
                }
                else if (pt == 205) // Transport Layer Feedback (Generic RTP NACK)
                {
                    int fmt = count;
                    if (fmt == 1 && pktLenBytes >= 12)
                    {
                        for (int pos = offset + 12; pos + 4 <= offset + pktLenBytes; pos += 4)
                        {
                            ushort pid = (ushort)((buffer[pos] << 8) | buffer[pos + 1]);
                            ushort blp = (ushort)((buffer[pos + 2] << 8) | buffer[pos + 3]);
                            HandleNack(pid);
                            for (int bit = 0; bit < 16; bit++)
                            {
                                if ((blp & (1 << bit)) != 0)
                                {
                                    HandleNack((ushort)(pid + bit + 1));
                                }
                            }
                        }
                    }
                }
                else if (pt == 206) // Payload-Specific Feedback (PLI / Picture Loss Indication)
                {
                    int fmt = count;
                    if (fmt == 1)
                    {
                        TriggerPli();
                    }
                }

                offset += pktLenBytes;
            }
        }

        private void Log(string tag, string msg)
        {
            LogEmitted?.Invoke(tag, msg);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
