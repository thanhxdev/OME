using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WEBRTC_DECODE
{
    /// <summary>
    /// Multi-Stream WebRTC RTP Receiver Engine managing up to 10 concurrent camera streams.
    /// Acts as drop-in replacement for SRT MultiStreamReceiverEngine, connecting with SFU and RTP sockets.
    /// </summary>
    public sealed class MultiStreamRtpReceiver : IDisposable, IAsyncDisposable
    {
        public const int MaxChannels = 10;
        private readonly ReceiverChannelState[] _channels = new ReceiverChannelState[MaxChannels];
        private readonly ChannelVideoDecoder?[] _decoders = new ChannelVideoDecoder?[MaxChannels];
        private readonly ChannelAudioDecoder?[] _audioDecoders = new ChannelAudioDecoder?[MaxChannels];
        private readonly RtpDepacketizer[] _depacketizers = new RtpDepacketizer[MaxChannels];

        private readonly UdpClient?[] _videoSockets = new UdpClient?[MaxChannels];
        private readonly UdpClient?[] _audioSockets = new UdpClient?[MaxChannels];
        private readonly CancellationTokenSource?[] _channelCts = new CancellationTokenSource?[MaxChannels];
        private readonly Task?[] _receiveTasks = new Task?[MaxChannels * 2];
        private readonly bool[] _firstVideoFrameDecoded = new bool[MaxChannels];

        private readonly HttpClient _httpClient = new();
        private readonly NtpSyncEngine _syncEngine;
        private bool _isDisposed;

        public event Action<int, ReceiverChannelState>? ChannelUpdated;
        public event Action<string, string>? LogEmitted;
        public event Action<int, string>? ChannelError;
        public event Action<int, byte[], int, int>? FrameReady;
        public event Action<int, byte[], int>? AudioPcmReady;
        public event Action<int>? AudioAlignmentRequested; // channelIndex
        public event Action<int>? AudioResetRequested;     // channelIndex
        public event Action<int, int, int, bool>? ChannelSocketsBound; // channelIndex, videoPort, audioPort, isSinglePort
        public event Action<int, string>? CameraDisplayNameReceived; // channelIndex, cameraDisplayName

        public ReceiverChannelState[] Channels => _channels;
        public RtpDepacketizer[] Depacketizers => _depacketizers;
        public ChannelVideoDecoder?[] Decoders => _decoders;
        public string SfuRestUrl { get; set; } = "http://127.0.0.1:4000";
        public int BaseVideoPort { get; set; } = 10000;
        public int BaseAudioPort { get; set; } = 10002;

        public MultiStreamRtpReceiver(NtpSyncEngine syncEngine)
        {
            _syncEngine = syncEngine;

            for (int i = 0; i < MaxChannels; i++)
            {
                int ch = i;
                int videoPort = BaseVideoPort + (ch * 4);
                int audioPort = BaseAudioPort + (ch * 4);

                _channels[i] = new ReceiverChannelState
                {
                    ChannelIndex = ch,
                    Name = $"CAM {ch + 1}",
                    VideoPort = videoPort,
                    AudioPort = audioPort,
                    SfuUrl = SfuRestUrl,
                    StatusMessage = "Standby / Idle"
                };

                _depacketizers[i] = new RtpDepacketizer();
                _depacketizers[i].VideoNalAssembled += (nal, len) =>
                {
                    _decoders[ch]?.FeedData(nal, len);
                };
                _depacketizers[i].OpusAudioFrameReady += (opus, len) =>
                {
                    _audioDecoders[ch]?.FeedOpusPacket(opus);
                };
            }
        }

        public async Task StartAllAsync(int activeCount)
        {
            int count = Math.Min(activeCount, MaxChannels);
            for (int i = 0; i < count; i++)
            {
                await StartChannelAsync(i).ConfigureAwait(false);
            }
        }

        public async Task StopAllAsync(int activeCount = MaxChannels)
        {
            int count = Math.Min(activeCount, MaxChannels);
            for (int i = 0; i < count; i++)
            {
                await StopChannelAsync(i).ConfigureAwait(false);
            }
        }

        public async Task StartChannelAsync(int index)
        {
            if (index < 0 || index >= MaxChannels) return;
            var ch = _channels[index];
            if (ch.IsRunning) return;

            try
            {
                ch.IsRunning = true;
                ch.StatusMessage = "Connecting UDP Sockets...";
                ChannelUpdated?.Invoke(index, ch);

                _firstVideoFrameDecoded[index] = false;
                _depacketizers[index]?.Reset();
                AudioResetRequested?.Invoke(index);

                _channelCts[index] = new CancellationTokenSource();
                var token = _channelCts[index]!.Token;

                // 1. Initialize Decoders with live FPS calculation
                long frameCount = 0;
                var lastFpsTime = DateTime.UtcNow;

                _decoders[index] = new ChannelVideoDecoder(index, 1920, 1080, "h264");
                _decoders[index]!.FrameDecoded += (c, bgra, w, h) =>
                {
                    ch.IsConnected = true;
                    if (!_firstVideoFrameDecoded[c])
                    {
                        _firstVideoFrameDecoded[c] = true;
                        AudioAlignmentRequested?.Invoke(c);
                    }
                    frameCount++;
                    var now = DateTime.UtcNow;
                    double elapsed = (now - lastFpsTime).TotalSeconds;
                    if (elapsed >= 1.0)
                    {
                        ch.CurrentFps = frameCount / elapsed;
                        frameCount = 0;
                        lastFpsTime = now;
                    }
                    ch.StatusMessage = $"Streaming (1080p @ {ch.CurrentFps:F0}fps)";
                    FrameReady?.Invoke(c, bgra, w, h);
                };
                _decoders[index]!.Start();

                _audioDecoders[index] = new ChannelAudioDecoder(index);
                _audioDecoders[index]!.PcmAudioReceived += (c, pcm, len) =>
                {
                    AudioPcmReady?.Invoke(c, pcm, len);
                };
                _audioDecoders[index]!.Start();

                // 2. Open UDP Sockets with configured ports (Direct Ingest from ENCODE or SFU)
                bool isSinglePort = ch.IsSinglePortMode || ch.VideoPort == ch.AudioPort;
                int targetVideoPort = ch.VideoPort > 0 ? ch.VideoPort : (10000 + (index * 4));
                int targetAudioPort = isSinglePort ? targetVideoPort : (ch.AudioPort > 0 ? ch.AudioPort : targetVideoPort + 2);

                _videoSockets[index]?.Close();
                var vSock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                vSock.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                try
                {
                    vSock.Bind(new IPEndPoint(IPAddress.Any, targetVideoPort));
                    ch.VideoPort = targetVideoPort;
                }
                catch (Exception ex)
                {
                    LogEmitted?.Invoke("[WARN]", $"Không thể bind cổng Video {targetVideoPort} ({ex.Message}), mở cổng ngẫu nhiên...");
                    vSock.Bind(new IPEndPoint(IPAddress.Any, 0));
                    ch.VideoPort = ((IPEndPoint)vSock.LocalEndPoint!).Port;
                }

                _videoSockets[index] = new UdpClient { Client = vSock };
                _videoSockets[index]!.Client.ReceiveBufferSize = 4 * 1024 * 1024;
                _receiveTasks[index * 2] = Task.Run(() => VideoReceiveLoop(index, _videoSockets[index]!, token), token);

                // Start RTT & Loss Prober for this channel
                _ = Task.Run(() => RttProbeLoop(index, token), token);

                if (!isSinglePort)
                {
                    // Open separate Audio UDP Socket for 2-Port Mode
                    _audioSockets[index]?.Close();
                    var aSock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    aSock.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                    try
                    {
                        aSock.Bind(new IPEndPoint(IPAddress.Any, targetAudioPort));
                        ch.AudioPort = targetAudioPort;
                    }
                    catch (Exception ex)
                    {
                        LogEmitted?.Invoke("[WARN]", $"Không thể bind cổng Audio {targetAudioPort} ({ex.Message}), mở cổng ngẫu nhiên...");
                        aSock.Bind(new IPEndPoint(IPAddress.Any, 0));
                        ch.AudioPort = ((IPEndPoint)aSock.LocalEndPoint!).Port;
                    }

                    _audioSockets[index] = new UdpClient { Client = aSock };
                    _audioSockets[index]!.Client.ReceiveBufferSize = 512 * 1024;
                    _receiveTasks[(index * 2) + 1] = Task.Run(() => AudioReceiveLoop(index, _audioSockets[index]!, token), token);
                }
                else
                {
                    ch.AudioPort = ch.VideoPort;
                    _audioSockets[index]?.Close();
                    _audioSockets[index] = null;
                    _receiveTasks[(index * 2) + 1] = null;
                }

                // Notify that channel sockets are ready with bound ports
                ChannelSocketsBound?.Invoke(index, ch.VideoPort, ch.AudioPort, isSinglePort);

                // 3. REST Subscribe to SFU
                await SubscribeSfuAsync(index, ch).ConfigureAwait(false);

                ch.StatusMessage = isSinglePort ? $"Listening 1-Port (:{ch.VideoPort})" : $"Listening 2-Port (:{ch.VideoPort}/:{ch.AudioPort})";
                ChannelUpdated?.Invoke(index, ch);

                string modeDesc = isSinglePort ? $"1 Port duy nhất (Muxed BUNDLE UDP {ch.VideoPort})" : $"2 Ports (Video UDP {ch.VideoPort}, Audio UDP {ch.AudioPort})";
                LogEmitted?.Invoke("[WEBRTC]", $"Khởi chạy luồng RTP Ingest {ch.Name} [{modeDesc}] thành công.");
            }
            catch (Exception ex)
            {
                ch.IsRunning = false;
                ch.IsConnected = false;
                ch.StatusMessage = $"Error: {ex.Message}";
                ChannelUpdated?.Invoke(index, ch);
                ChannelError?.Invoke(index, ex.Message);
                LogEmitted?.Invoke("[ERROR]", $"Lỗi khởi chạy {ch.Name}: {ex.Message}");
            }
        }

        public async Task StopChannelAsync(int index)
        {
            if (index < 0 || index >= MaxChannels) return;
            var ch = _channels[index];
            if (!ch.IsRunning) return;

            try
            {
                ch.IsRunning = false;
                ch.IsConnected = false;
                ch.StatusMessage = "Stopping...";
                ChannelUpdated?.Invoke(index, ch);

                _firstVideoFrameDecoded[index] = false;
                _depacketizers[index]?.Reset();
                AudioResetRequested?.Invoke(index);

                _channelCts[index]?.Cancel();

                // Unsubscribe from SFU
                await UnsubscribeSfuAsync(index, ch).ConfigureAwait(false);

                _videoSockets[index]?.Close();
                _audioSockets[index]?.Close();

                _decoders[index]?.Stop();
                _decoders[index]?.Dispose();
                _decoders[index] = null;

                _audioDecoders[index]?.Stop();
                _audioDecoders[index]?.Dispose();
                _audioDecoders[index] = null;

                ch.StatusMessage = "Standby / Idle";
                ChannelUpdated?.Invoke(index, ch);
                LogEmitted?.Invoke("[WEBRTC]", $"Đã dừng luồng RTP Ingest {ch.Name}.");
            }
            catch (Exception ex)
            {
                LogEmitted?.Invoke("[WARN]", $"Lỗi khi dừng {ch.Name}: {ex.Message}");
            }
        }

        private async Task SubscribeSfuAsync(int index, ReceiverChannelState ch)
        {
            try
            {
                string cameraId = $"cam-{index + 1:D2}";
                var payload = new
                {
                    id = $"studio-decoder-1-cam-{index + 1:D2}",
                    ip = "127.0.0.1",
                    videoPort = ch.VideoPort,
                    audioPort = ch.AudioPort
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                string url = $"{SfuRestUrl.TrimEnd('/')}/api/streams/{cameraId}/subscribe";

                var response = await _httpClient.PostAsync(url, content).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    LogEmitted?.Invoke("[SFU]", $"Subscribed thành công {cameraId} tại SFU {url}");
                }
            }
            catch (Exception ex)
            {
                LogEmitted?.Invoke("[SFU-WARN]", $"Chưa kết nối được SFU ({ex.Message}), vẫn tiếp tục lắng nghe UDP trực tiếp.");
            }
        }

        private async Task UnsubscribeSfuAsync(int index, ReceiverChannelState ch)
        {
            try
            {
                string cameraId = $"cam-{index + 1:D2}";
                string url = $"{SfuRestUrl.TrimEnd('/')}/api/streams/{cameraId}/subscribers/studio-decoder-1-cam-{index + 1:D2}";
                await _httpClient.DeleteAsync(url).ConfigureAwait(false);
            }
            catch { }
        }

        private void VideoReceiveLoop(int chIdx, UdpClient client, CancellationToken token)
        {
            IPEndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);
            long bytesInPeriod = 0;
            var sw = Stopwatch.StartNew();
            var ch = _channels[chIdx];

            while (!token.IsCancellationRequested && !_isDisposed)
            {
                try
                {
                    byte[] packet = client.Receive(ref remoteEp);
                    if (packet.Length < 12) continue;

                    bytesInPeriod += packet.Length;
                    ch.TotalBytesReceived += (ulong)packet.Length;
                    ch.IsConnected = true;

                    // Check if RTCP packet (RFC 3550: V=2, PT in 200..206)
                    byte firstByte = packet[0];
                    byte ptOrType = packet[1];
                    if ((firstByte >> 6) == 2 && ptOrType >= 200 && ptOrType <= 206)
                    {
                        ProcessIncomingRtcp(chIdx, packet, packet.Length);
                        continue;
                    }

                    byte payloadType = (byte)(packet[1] & 0x7F);
                    if (payloadType == 111)
                    {
                        // Demux Opus Audio packet received on single port (Muxed BUNDLE)
                        _depacketizers[chIdx].ProcessAudioRtpPacket(packet, packet.Length);
                    }
                    else
                    {
                        // Video packet (H.264 PT 96 / H.265 PT 97)
                        _depacketizers[chIdx].ProcessVideoRtpPacket(packet, packet.Length);
                    }

                    if (sw.ElapsedMilliseconds >= 1000)
                    {
                        double mbps = (bytesInPeriod * 8.0) / (sw.ElapsedMilliseconds * 1000.0);
                        ch.CurrentBitrateKbps = mbps * 1000.0;
                        bytesInPeriod = 0;
                        sw.Restart();
                    }
                }
                catch (SocketException) { break; }
                catch (Exception) { }
            }
        }

        private void ProcessIncomingRtcp(int chIdx, byte[] packet, int length)
        {
            try
            {
                int offset = 0;
                while (offset + 4 <= length)
                {
                    int version = packet[offset] >> 6;
                    if (version != 2) break;
                    byte pt = packet[offset + 1];
                    int words = (packet[offset + 2] << 8) | packet[offset + 3];
                    int packetLen = (words + 1) * 4;
                    if (packetLen <= 0 || offset + packetLen > length) break;

                    if (pt == 202) // RTCP SDES
                    {
                        int sdesOffset = offset + 4;
                        int sdesEnd = offset + packetLen;
                        if (sdesOffset + 4 <= sdesEnd)
                        {
                            sdesOffset += 4; // Skip SSRC (4 bytes)
                            while (sdesOffset < sdesEnd)
                            {
                                byte itemType = packet[sdesOffset++];
                                if (itemType == 0) break; // End of list
                                if (sdesOffset >= sdesEnd) break;
                                byte itemLen = packet[sdesOffset++];
                                if (sdesOffset + itemLen > sdesEnd) break;

                                if (itemType == 2) // SDES NAME (Camera Display Name)
                                {
                                    string displayName = Encoding.UTF8.GetString(packet, sdesOffset, itemLen).Trim();
                                    if (!string.IsNullOrWhiteSpace(displayName))
                                    {
                                        CameraDisplayNameReceived?.Invoke(chIdx, displayName);
                                    }
                                }
                                sdesOffset += itemLen;
                            }
                        }
                    }

                    offset += packetLen;
                }
            }
            catch { }
        }

        private async Task RttProbeLoop(int chIdx, CancellationToken token)
        {
            var ch = _channels[chIdx];
            using var pinger = new System.Net.NetworkInformation.Ping();

            while (!token.IsCancellationRequested && !_isDisposed && ch.IsRunning)
            {
                try
                {
                    string hostToPing = "127.0.0.1";
                    if (!string.IsNullOrEmpty(SfuRestUrl) && Uri.TryCreate(SfuRestUrl, UriKind.Absolute, out var uri))
                    {
                        hostToPing = uri.Host;
                    }

                    if (hostToPing == "localhost" || hostToPing == "127.0.0.1" || hostToPing == "::1")
                    {
                        ch.CurrentRttMs = 1.0;
                    }
                    else
                    {
                        var reply = await pinger.SendPingAsync(hostToPing, 800).ConfigureAwait(false);
                        if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                        {
                            ch.CurrentRttMs = Math.Max(0.5, reply.RoundtripTime);
                        }
                    }
                }
                catch
                {
                    if (ch.CurrentRttMs <= 0) ch.CurrentRttMs = 1.0;
                }

                // Sync WebRTC Depacketizer Metrics (Loss, Jitter, Gaps)
                var depack = _depacketizers[chIdx];
                if (depack != null)
                {
                    ch.CurrentJitterMs = depack.EstimatedJitterMs;
                    ch.SequenceGaps = depack.SequenceGaps;
                    if (depack.TotalPacketsProcessed > 0)
                    {
                        double totalExpected = depack.TotalPacketsProcessed + depack.SequenceGaps;
                        ch.CurrentPacketLoss = Math.Min(100.0, ((double)depack.SequenceGaps / totalExpected) * 100.0);
                    }
                    else
                    {
                        ch.CurrentPacketLoss = 0.0;
                    }
                }

                // Overall QoS Health %
                double score = 100.0;
                if (ch.CurrentPacketLoss > 0.0) score -= Math.Min(40.0, ch.CurrentPacketLoss * 10.0);
                if (ch.CurrentRttMs > 40.0) score -= Math.Min(30.0, (ch.CurrentRttMs - 40.0) * 0.3);
                if (ch.CurrentJitterMs > 10.0) score -= Math.Min(20.0, (ch.CurrentJitterMs - 10.0) * 0.5);
                ch.BufferHealthPercent = Math.Clamp(score, 0.0, 100.0);

                try
                {
                    await Task.Delay(1000, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }
            }
        }

        private void AudioReceiveLoop(int chIdx, UdpClient client, CancellationToken token)
        {
            IPEndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);
            var ch = _channels[chIdx];
            while (!token.IsCancellationRequested && !_isDisposed)
            {
                try
                {
                    byte[] packet = client.Receive(ref remoteEp);
                    if (packet.Length < 12) continue;

                    ch.TotalBytesReceived += (ulong)packet.Length;
                    ch.IsConnected = true;
                    _depacketizers[chIdx].ProcessAudioRtpPacket(packet, packet.Length);
                }
                catch (SocketException) { break; }
                catch (Exception) { }
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _ = StopAllAsync();
            _httpClient.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            await StopAllAsync().ConfigureAwait(false);
            _httpClient.Dispose();
        }
    }
}
