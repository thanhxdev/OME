using System;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace WEBRTC_ENCODE
{
    public sealed class PortAllocationResult
    {
        public string CameraId { get; set; } = string.Empty;
        public bool IsSinglePort { get; set; }
        public int VideoPort { get; set; }
        public int AudioPort { get; set; }
        public string SfuHost { get; set; } = "127.0.0.1";
    }

    public sealed class WebRtcSignalingClient : IAsyncDisposable, IDisposable
    {
        private ClientWebSocket? _ws;
        private CancellationTokenSource? _connectionCts;
        private Task? _receiveLoopTask;
        private Task? _healthReportTask;
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, TaskCompletionSource<PortAllocationResult>> _pendingPortAllocations = new();

        private bool _isDisposed;
        private bool _isConnected;
        private bool _shouldReconnect = true;

        public string ServerUrl { get; set; } = "ws://127.0.0.1:3000/ws";
        public string SessionId { get; set; } = "broadcast-01";
        public string CameraId { get; set; } = "cam-01";
        public string CameraDisplayName { get; set; } = "Máy quay 01";
        public string ClientId { get; private set; } = string.Empty;
        public bool IsConnected => _isConnected && _ws?.State == WebSocketState.Open;

        // Current Telemetry Feed Provider
        public Func<(double bitrateKbps, double fps, double packetLoss, double rttMs, int droppedFrames)>? TelemetryProvider { get; set; }

        public event Action? Connected;
        public event Action<string>? Disconnected;
        public event Action<string, string>? LogEmitted;
        public event Action<string, int, string, int>? CodecChangeRequested; // codec, bitrate, resolution, fps
        public event Action<string, string>? TallyStateChanged; // cameraId, state ("on-air", "preview", "off")
        public event Action<string, byte[]>? IntercomAudioReceived; // from, pcm/opus data
        public event Action<string, bool>? IntercomStateChanged; // from, isActive
        public event Action<string, string>? RecordingCommandReceived; // cameraId, action ("start", "stop")
        public event Action<string, int, int, bool>? DecoderReadyReceived; // cameraId, videoPort, audioPort, isSinglePort

        public async Task StartAsync()
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(WebRtcSignalingClient));
            _shouldReconnect = true;
            await ConnectInternalAsync().ConfigureAwait(false);
        }

        public async Task StopAsync()
        {
            _shouldReconnect = false;
            await DisconnectInternalAsync("User Requested Stop").ConfigureAwait(false);
        }

        private async Task ConnectInternalAsync()
        {
            await DisconnectInternalAsync("Reconnecting").ConfigureAwait(false);

            _connectionCts = new CancellationTokenSource();
            var token = _connectionCts.Token;

            try
            {
                Log("[SIGNALING]", $"Đang kết nối WebSocket Signaling Server tại {ServerUrl}...");
                _ws = new ClientWebSocket();
                _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);

                await _ws.ConnectAsync(new Uri(ServerUrl), token).ConfigureAwait(false);
                _isConnected = true;
                Log("[SIGNALING]", $"✅ Đã kết nối thành công tới Signaling Server! Session: {SessionId}, Cam: {CameraId}");

                // Gửi Join Session Message
                await SendJoinSessionAsync(token).ConfigureAwait(false);

                Connected?.Invoke();

                // Bắt đầu vòng lặp nhận tin nhắn
                _receiveLoopTask = Task.Run(() => ReceiveLoopAsync(token), token);

                // Bắt đầu định kỳ báo cáo Health Report
                _healthReportTask = Task.Run(() => HealthReportLoopAsync(token), token);
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                Log("[ERROR]", $"Lỗi kết nối Signaling Server: {ex.Message}");
                _isConnected = false;
                Disconnected?.Invoke(ex.Message);
                ScheduleReconnect();
            }
        }

        private async Task DisconnectInternalAsync(string reason)
        {
            _isConnected = false;
            try
            {
                _connectionCts?.Cancel();
            }
            catch { }

            if (_ws != null)
            {
                try
                {
                    if (_ws.State == WebSocketState.Open || _ws.State == WebSocketState.CloseReceived)
                    {
                        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                        await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, reason, timeoutCts.Token).ConfigureAwait(false);
                    }
                }
                catch { }
                finally
                {
                    _ws.Dispose();
                    _ws = null;
                }
            }

            _connectionCts?.Dispose();
            _connectionCts = null;
        }

        private void ScheduleReconnect()
        {
            if (!_shouldReconnect || _isDisposed) return;

            Task.Run(async () =>
            {
                Log("[SIGNALING]", "Tự động kết nối lại Signaling Server sau 3 giây...");
                await Task.Delay(3000).ConfigureAwait(false);
                if (_shouldReconnect && !_isDisposed && !IsConnected)
                {
                    await ConnectInternalAsync().ConfigureAwait(false);
                }
            });
        }

        private async Task SendJoinSessionAsync(CancellationToken token)
        {
            var joinMsg = new
            {
                type = "join_session",
                sessionId = SessionId,
                senderId = $"encoder-{CameraId}",
                role = "encoder",
                cameraId = CameraId,
                cameraName = CameraDisplayName,
                machineId = Environment.MachineName,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            await SendJsonAsync(joinMsg, token).ConfigureAwait(false);
        }

        public async Task SendHealthReportAsync(
            double bitrateKbps,
            double fps,
            double packetLoss,
            double rttMs,
            int droppedFrames,
            CancellationToken token = default)
        {
            if (!IsConnected) return;

            var streamMetrics = new
            {
                cameraId = CameraId,
                bitrate = bitrateKbps,
                fps = fps,
                packetLoss = packetLoss,
                rtt = rttMs,
                droppedFrames = droppedFrames,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            var systemMetrics = new
            {
                machineId = Environment.MachineName,
                cpu = 25, // Ước tính mức tải CPU trung bình
                gpu = 30, // GPU NVENC workload
                ram = 45,
                temperature = 52,
                networkThroughput = new
                {
                    txKbps = bitrateKbps,
                    rxKbps = 64
                },
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            var healthMsg = new
            {
                type = "health_report",
                sessionId = SessionId,
                senderId = $"encoder-{CameraId}",
                sourceId = CameraId,
                sourceType = "encoder",
                streamMetrics,
                systemMetrics,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            await SendJsonAsync(healthMsg, token).ConfigureAwait(false);
        }

        public async Task SendIntercomAudioAsync(string to, byte[] audioData, string codec = "opus", CancellationToken token = default)
        {
            if (!IsConnected || audioData == null || audioData.Length == 0) return;

            var msg = new
            {
                type = "intercom_audio",
                sessionId = SessionId,
                senderId = $"encoder-{CameraId}",
                from = CameraId,
                to = to, // "all" or specific client
                codec = codec,
                audioData = Convert.ToBase64String(audioData),
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            await SendJsonAsync(msg, token).ConfigureAwait(false);
        }

        public async Task SendCodecChangedNotificationAsync(string codec, int bitrate, string resolution, int fps, CancellationToken token = default)
        {
            if (!IsConnected) return;

            var msg = new
            {
                type = "codec_changed",
                sessionId = SessionId,
                senderId = $"encoder-{CameraId}",
                cameraId = CameraId,
                codec = codec,
                bitrate = bitrate,
                resolution = resolution,
                fps = fps,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            await SendJsonAsync(msg, token).ConfigureAwait(false);
        }

        public async Task SendRecordingStatusAsync(bool isRecording, string filename, CancellationToken token = default)
        {
            if (!IsConnected) return;

            var msg = new
            {
                type = "recording_status",
                sessionId = SessionId,
                senderId = $"encoder-{CameraId}",
                cameraId = CameraId,
                isRecording = isRecording,
                currentFilename = filename,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            await SendJsonAsync(msg, token).ConfigureAwait(false);
        }

        public async Task<PortAllocationResult> RequestPortAllocationAsync(string cameraId, bool isSinglePort, int timeoutMs = 5000)
        {
            if (!IsConnected)
            {
                Log("[SIGNALING]", "Chưa kết nối Signaling Server, đang thử kết nối trước khi xin cấp port...");
                await StartAsync().ConfigureAwait(false);
                var startWait = DateTime.UtcNow;
                while (!IsConnected && (DateTime.UtcNow - startWait).TotalMilliseconds < 3000)
                {
                    await Task.Delay(100).ConfigureAwait(false);
                }
                if (!IsConnected)
                {
                    throw new InvalidOperationException("Không thể kết nối tới Signaling Server để xin cấp port.");
                }
            }

            var tcs = new TaskCompletionSource<PortAllocationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingPortAllocations[cameraId] = tcs;

            var req = new
            {
                type = "port_allocate_request",
                sessionId = SessionId,
                senderId = $"encoder-{CameraId}",
                cameraId = cameraId,
                isSinglePort = isSinglePort,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            Log("[SIGNALING]", $"Đang gửi yêu cầu cấp Port động cho {cameraId} (Chế độ: {(isSinglePort ? "1 Port Muxed BUNDLE" : "2 Ports Split UDP")})...");
            await SendJsonAsync(req, CancellationToken.None).ConfigureAwait(false);

            using var timeoutCts = new CancellationTokenSource(timeoutMs);
            using (timeoutCts.Token.Register(() => tcs.TrySetCanceled()))
            {
                try
                {
                    var result = await tcs.Task.ConfigureAwait(false);
                    Log("[SIGNALING]", $"✅ Đã nhận Port cấp tự động từ Server: Video UDP {result.VideoPort}, Audio UDP {result.AudioPort}");
                    return result;
                }
                catch (OperationCanceledException)
                {
                    Log("[WARN]", $"Yêu cầu cấp port cho {cameraId} bị quá thời gian ({timeoutMs}ms).");
                    throw new TimeoutException($"Hết thời gian chờ cấp port từ Signaling Server ({timeoutMs}ms).");
                }
                finally
                {
                    _pendingPortAllocations.TryRemove(cameraId, out _);
                }
            }
        }

        public async Task SendStreamPublishedAsync(string cameraId, string cameraName, bool isSinglePort, int videoPort, int audioPort, string codec, int audioChannels = 2, CancellationToken token = default)
        {
            if (!IsConnected) return;

            var msg = new
            {
                type = "stream_published",
                sessionId = SessionId,
                senderId = $"encoder-{CameraId}",
                cameraId = cameraId,
                cameraName = cameraName,
                isSinglePort = isSinglePort,
                videoPort = videoPort,
                audioPort = audioPort,
                codec = codec,
                audioChannels = audioChannels,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            await SendJsonAsync(msg, token).ConfigureAwait(false);
            Log("[SIGNALING]", $"🚀 Đã phát thông báo StreamPublished: {cameraId} ({cameraName}) -> Video UDP {videoPort}, Audio UDP {audioPort} ({audioChannels}-CH)");
        }

        public async Task SendCameraMetaUpdateAsync(string cameraName, CancellationToken token = default)
        {
            if (!IsConnected) return;

            var msg = new
            {
                type = "camera_meta_update",
                sessionId = SessionId,
                senderId = $"encoder-{CameraId}",
                cameraId = CameraId,
                cameraName = cameraName,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            await SendJsonAsync(msg, token).ConfigureAwait(false);
            Log("[SIGNALING]", $"🏷️ Đã gửi cập nhật tên camera: {CameraId} -> \"{cameraName}\"");
        }

        private async Task SendJsonAsync<T>(T payload, CancellationToken token)
        {
            if (_ws == null || _ws.State != WebSocketState.Open) return;

            string json = JsonSerializer.Serialize(payload);
            byte[] bytes = Encoding.UTF8.GetBytes(json);

            await _sendLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (_ws != null && _ws.State == WebSocketState.Open)
                {
                    await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token).ConfigureAwait(false);
                }
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            byte[] buffer = new byte[65536];
            var ms = new System.IO.MemoryStream();

            try
            {
                while (!token.IsCancellationRequested && _ws != null && _ws.State == WebSocketState.Open)
                {
                    WebSocketReceiveResult result;
                    ms.SetLength(0);

                    do
                    {
                        result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            Log("[SIGNALING]", "Máy chủ yêu cầu đóng kết nối WebSocket.");
                            _isConnected = false;
                            Disconnected?.Invoke("Server Closed Connection");
                            ScheduleReconnect();
                            return;
                        }

                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    string messageJson = Encoding.UTF8.GetString(ms.ToArray());
                    ProcessIncomingMessage(messageJson);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    Log("[WARN]", $"Mất kết nối Signaling: {ex.Message}");
                    _isConnected = false;
                    Disconnected?.Invoke(ex.Message);
                    ScheduleReconnect();
                }
            }
        }

        private void ProcessIncomingMessage(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("type", out var typeProp)) return;
                string type = typeProp.GetString() ?? "";

                switch (type)
                {
                    case "joined_session":
                        if (root.TryGetProperty("clientId", out var cidProp))
                        {
                            ClientId = cidProp.GetString() ?? "";
                            Log("[SIGNALING]", $"Đã gia nhập Session: ClientId = {ClientId}");
                        }
                        break;

                    case "codec_change":
                        HandleCodecChange(root);
                        break;

                    case "tally_update":
                        HandleTallyUpdate(root);
                        break;

                    case "intercom_audio":
                        HandleIntercomAudio(root);
                        break;

                    case "intercom_state":
                        HandleIntercomState(root);
                        break;

                    case "recording_command":
                        HandleRecordingCommand(root);
                        break;

                    case "sdp_answer":
                        Log("[SIGNALING]", "Đã nhận SDP Answer từ SFU/Subscriber.");
                        break;

                    case "ice_candidate":
                        Log("[SIGNALING]", "Đã nhận ICE Candidate.");
                        break;

                    case "port_allocate_response":
                        HandlePortAllocateResponse(root);
                        break;

                    case "decoder_ready":
                        HandleDecoderReady(root);
                        break;

                    case "error":
                        string errMsg = root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "Unknown error";
                        Log("[ERROR]", $"Signaling Server trả về lỗi: {errMsg}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Log("[WARN]", $"Lỗi phân tích cú pháp gói tin signaling: {ex.Message}");
            }
        }

        private void HandleDecoderReady(JsonElement root)
        {
            string camId = root.TryGetProperty("cameraId", out var c) ? c.GetString() ?? "" : "";
            int videoPort = root.TryGetProperty("videoPort", out var vp) ? vp.GetInt32() : 0;
            int audioPort = root.TryGetProperty("audioPort", out var ap) ? ap.GetInt32() : 0;
            bool isSinglePort = root.TryGetProperty("isSinglePort", out var isp) && isp.GetBoolean();

            if (!string.IsNullOrEmpty(camId) && videoPort > 0)
            {
                Log("[SIGNALING]", $"🎯 Nhận thông báo DecoderReady: {camId} -> Video UDP {videoPort}, Audio UDP {audioPort}");
                DecoderReadyReceived?.Invoke(camId, videoPort, audioPort, isSinglePort);
            }
        }

        private void HandleCodecChange(JsonElement root)
        {
            string camId = root.TryGetProperty("cameraId", out var c) ? c.GetString() ?? "" : "";
            if (camId == CameraId || camId == "all")
            {
                string codec = root.TryGetProperty("codec", out var cd) ? cd.GetString() ?? "h264" : "h264";
                int bitrate = root.TryGetProperty("bitrate", out var br) ? br.GetInt32() : 8000;
                string resolution = root.TryGetProperty("resolution", out var res) ? res.GetString() ?? "1920x1080" : "1920x1080";
                int fps = root.TryGetProperty("fps", out var fp) ? fp.GetInt32() : 30;

                Log("[CODEC]", $"Yêu cầu đổi Codec từ xa: {codec.ToUpper()}, {bitrate} kbps, {resolution} @ {fps}fps");
                CodecChangeRequested?.Invoke(codec, bitrate, resolution, fps);
            }
        }

        private void HandleTallyUpdate(JsonElement root)
        {
            string camId = root.TryGetProperty("cameraId", out var c) ? c.GetString() ?? "" : "";
            if (camId == CameraId || camId == "all")
            {
                string state = root.TryGetProperty("state", out var s) ? s.GetString() ?? "off" : "off";
                Log("[TALLY]", $"Cập nhật Tally cho {camId}: {state.ToUpper()}");
                TallyStateChanged?.Invoke(camId, state);
            }
        }

        private void HandleIntercomAudio(JsonElement root)
        {
            string to = root.TryGetProperty("to", out var t) ? t.GetString() ?? "" : "";
            if (to == CameraId || to == "all")
            {
                string from = root.TryGetProperty("from", out var f) ? f.GetString() ?? "Director" : "Director";
                if (root.TryGetProperty("audioData", out var ad))
                {
                    string base64 = ad.GetString() ?? "";
                    if (!string.IsNullOrEmpty(base64))
                    {
                        byte[] pcm = Convert.FromBase64String(base64);
                        IntercomAudioReceived?.Invoke(from, pcm);
                    }
                }
            }
        }

        private void HandleIntercomState(JsonElement root)
        {
            string to = root.TryGetProperty("to", out var t) ? t.GetString() ?? "" : "";
            if (to == CameraId || to == "all")
            {
                string from = root.TryGetProperty("from", out var f) ? f.GetString() ?? "Director" : "Director";
                bool active = false;
                if (root.TryGetProperty("active", out var a))
                {
                    active = a.ValueKind == JsonValueKind.True || (a.ValueKind == JsonValueKind.String && a.GetString() == "true");
                }
                else if (root.TryGetProperty("state", out var s))
                {
                    string stateStr = s.GetString() ?? "";
                    active = stateStr.Equals("enable", StringComparison.OrdinalIgnoreCase) ||
                             stateStr.Equals("active", StringComparison.OrdinalIgnoreCase) ||
                             stateStr.Equals("on", StringComparison.OrdinalIgnoreCase);
                }
                Log("[INTERCOM]", $"Trạng thái Intercom từ {from}: {(active ? "ENABLE (ON-AIR)" : "DISABLE (OFF-AIR)")}");
                IntercomStateChanged?.Invoke(from, active);
            }
        }

        private void HandleRecordingCommand(JsonElement root)
        {
            string camId = root.TryGetProperty("cameraId", out var c) ? c.GetString() ?? "" : "";
            if (camId == CameraId || camId == "all")
            {
                string action = root.TryGetProperty("action", out var a) ? a.GetString() ?? "stop" : "stop";
                Log("[RECORD]", $"Nhận lệnh ghi hình ISO: {action.ToUpper()} cho {camId}");
                RecordingCommandReceived?.Invoke(camId, action);
            }
        }

        private async Task HealthReportLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && IsConnected)
            {
                try
                {
                    await Task.Delay(1000, token).ConfigureAwait(false);

                    if (TelemetryProvider != null)
                    {
                        var (bitrate, fps, loss, rtt, dropped) = TelemetryProvider.Invoke();
                        await SendHealthReportAsync(bitrate, fps, loss, rtt, dropped, token).ConfigureAwait(false);
                    }
                    else
                    {
                        await SendHealthReportAsync(8000, 30, 0, 10, 0, token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch { }
            }
        }

        private void HandlePortAllocateResponse(JsonElement root)
        {
            try
            {
                string camId = root.TryGetProperty("cameraId", out var c) ? c.GetString() ?? "" : "";
                bool isSinglePort = root.TryGetProperty("isSinglePort", out var isp) && isp.GetBoolean();
                int videoPort = root.TryGetProperty("videoPort", out var vp) ? vp.GetInt32() : 10000;
                int audioPort = root.TryGetProperty("audioPort", out var ap) ? ap.GetInt32() : (isSinglePort ? videoPort : videoPort + 2);
                string sfuHost = root.TryGetProperty("sfuHost", out var sh) ? sh.GetString() ?? "127.0.0.1" : "127.0.0.1";

                var result = new PortAllocationResult
                {
                    CameraId = camId,
                    IsSinglePort = isSinglePort,
                    VideoPort = videoPort,
                    AudioPort = audioPort,
                    SfuHost = sfuHost
                };

                if (!string.IsNullOrEmpty(camId) && _pendingPortAllocations.TryGetValue(camId, out var tcs))
                {
                    tcs.TrySetResult(result);
                }
            }
            catch (Exception ex)
            {
                Log("[WARN]", $"Lỗi xử lý phản hồi cấp port: {ex.Message}");
            }
        }

        private void Log(string tag, string msg)
        {
            LogEmitted?.Invoke(tag, msg);
        }

        public async ValueTask DisposeAsync()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _shouldReconnect = false;
            await DisconnectInternalAsync("Disposing").ConfigureAwait(false);
            _sendLock.Dispose();
        }

        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
