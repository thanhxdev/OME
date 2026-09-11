using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WEBRTC_DECODE
{
    /// <summary>
    /// Decoder WebRTC Signaling Client.
    /// Connects to ws://localhost:3000/ws with role="decoder" and senderId="studio-decoder-1".
    /// Broadcasts vision switcher Tally updates (on-air red, preview green, off) to encoders.
    /// Receives health_report from encoders to display live status in studio UI.
    /// </summary>
    public sealed class WebRtcSignalingClient : IDisposable
    {
        private ClientWebSocket? _ws;
        private CancellationTokenSource? _cts;
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private bool _isDisposed;

        public string ServerUrl { get; set; } = "ws://127.0.0.1:3000/ws";
        public string SessionId { get; set; } = "broadcast-01";
        public string SenderId { get; set; } = "studio-decoder-1";
        public bool IsConnected => _ws != null && _ws.State == WebSocketState.Open;

        private bool _shouldReconnect = true;

        public event Action<bool>? ConnectionStateChanged;
        public event Action<string, string>? EncoderHealthReceived; // cameraId, healthJson
        public event Action<string, string, bool, int, int, string>? CameraStreamPublished; // cameraId, cameraName, isSinglePort, videoPort, audioPort, codec
        public event Action<string, string>? CameraMetaUpdated; // cameraId, cameraName
        public event Action<string, byte[]>? IntercomAudioReceived; // from, pcmData

        public async Task ConnectAsync()
        {
            if (IsConnected) return;
            _shouldReconnect = true;

            try
            {
                _cts = new CancellationTokenSource();
                _ws = new ClientWebSocket();

                await _ws.ConnectAsync(new Uri(ServerUrl), _cts.Token);
                ConnectionStateChanged?.Invoke(true);

                // Send standard join_session message
                var joinMsg = new
                {
                    type = "join_session",
                    sessionId = SessionId,
                    senderId = SenderId,
                    role = "decoder",
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    machineId = Environment.MachineName
                };

                await SendJsonAsync(joinMsg);

                // Start listening loop
                _ = Task.Run(() => ReceiveLoopAsync(_cts.Token));
            }
            catch (Exception)
            {
                ConnectionStateChanged?.Invoke(false);
                ScheduleReconnect();
            }
        }

        private void ScheduleReconnect()
        {
            if (!_shouldReconnect || _isDisposed) return;
            Task.Run(async () =>
            {
                await Task.Delay(2500).ConfigureAwait(false);
                if (_shouldReconnect && !_isDisposed && !IsConnected)
                {
                    await ConnectAsync().ConfigureAwait(false);
                }
            });
        }

        public async Task SendTallyUpdateAsync(string cameraId, string tallyState)
        {
            if (!IsConnected) return;

            // state: "on-air" (PGM - Red), "preview" (PVW - Green), "off" (Grey)
            var tallyMsg = new
            {
                type = "tally_update",
                senderId = SenderId,
                role = "decoder",
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                payload = new
                {
                    cameraId = cameraId,
                    tally = tallyState
                }
            };

            await SendJsonAsync(tallyMsg);
        }

        public async Task RequestKeyframeAsync(string cameraId)
        {
            if (!IsConnected) return;

            var pliMsg = new
            {
                type = "request_keyframe",
                senderId = SenderId,
                role = "decoder",
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                payload = new
                {
                    cameraId = cameraId
                }
            };

            await SendJsonAsync(pliMsg);
        }

        public async Task SendDecoderReadyAsync(string cameraId, int videoPort, int audioPort, bool isSinglePort, CancellationToken token = default)
        {
            if (!IsConnected) return;

            var msg = new
            {
                type = "decoder_ready",
                sessionId = SessionId,
                senderId = SenderId,
                cameraId = cameraId,
                videoPort = videoPort,
                audioPort = audioPort,
                isSinglePort = isSinglePort,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            await SendJsonAsync(msg, token).ConfigureAwait(false);
        }

        public async Task SendIntercomAudioAsync(string to, byte[] audioData, string codec = "opus", CancellationToken token = default)
        {
            if (!IsConnected || audioData == null || audioData.Length == 0) return;

            var msg = new
            {
                type = "intercom_audio",
                sessionId = SessionId,
                senderId = SenderId,
                from = "Director",
                to = to, // "all" or specific camera (e.g. "cam-01")
                codec = codec,
                audioData = Convert.ToBase64String(audioData),
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            await SendJsonAsync(msg, token).ConfigureAwait(false);
        }

        public async Task SendIntercomStateAsync(string to, bool active, string from = "Director", CancellationToken token = default)
        {
            if (!IsConnected) return;

            var msg = new
            {
                type = "intercom_state",
                sessionId = SessionId,
                senderId = SenderId,
                from = from,
                to = to, // "all" or specific camera
                active = active,
                state = active ? "enable" : "disable",
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            await SendJsonAsync(msg, token).ConfigureAwait(false);
        }

        private async Task SendJsonAsync(object obj, CancellationToken token = default)
        {
            if (_ws == null || _ws.State != WebSocketState.Open) return;

            await _sendLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (_ws == null || _ws.State != WebSocketState.Open) return;
                string json = JsonSerializer.Serialize(obj);
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token).ConfigureAwait(false);
            }
            catch { }
            finally
            {
                _sendLock.Release();
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            var buffer = new byte[8192];

            while (!token.IsCancellationRequested && _ws != null && _ws.State == WebSocketState.Open)
            {
                try
                {
                    using var ms = new System.IO.MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close) break;
                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close) break;

                    string text = Encoding.UTF8.GetString(ms.ToArray());
                    ProcessMessage(text);
                }
                catch
                {
                    break;
                }
            }

            ConnectionStateChanged?.Invoke(false);
            ScheduleReconnect();
        }

        private void ProcessMessage(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("type", out var typeProp)) return;
                string type = typeProp.GetString() ?? "";

                if (type == "health_report")
                {
                    string camId = "";
                    if (root.TryGetProperty("sourceId", out var src)) camId = src.GetString() ?? "";
                    if (string.IsNullOrEmpty(camId) && root.TryGetProperty("cameraId", out var c)) camId = c.GetString() ?? "";
                    if (string.IsNullOrEmpty(camId) && root.TryGetProperty("senderId", out var s)) camId = s.GetString() ?? "";

                    string camName = root.TryGetProperty("cameraName", out var cn) ? cn.GetString() ?? "" : (root.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? "" : "");
                    if (string.IsNullOrEmpty(camName) && root.TryGetProperty("streamMetrics", out var sm))
                    {
                        if (sm.TryGetProperty("cameraName", out var scn)) camName = scn.GetString() ?? "";
                        if (string.IsNullOrEmpty(camName) && sm.TryGetProperty("displayName", out var sdn)) camName = sdn.GetString() ?? "";
                    }

                    if (!string.IsNullOrEmpty(camId) && !string.IsNullOrEmpty(camName))
                    {
                        CameraMetaUpdated?.Invoke(camId, camName);
                    }
                    if (!string.IsNullOrEmpty(camId))
                    {
                        EncoderHealthReceived?.Invoke(camId, json);
                    }
                }
                else if (type == "stream_published")
                {
                    string camId = root.TryGetProperty("cameraId", out var c) ? c.GetString() ?? "" : "";
                    string camName = root.TryGetProperty("cameraName", out var cn) ? cn.GetString() ?? "" : (root.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? "" : (root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : ""));
                    bool isSinglePort = root.TryGetProperty("isSinglePort", out var isp) && isp.GetBoolean();
                    int videoPort = root.TryGetProperty("videoPort", out var vp) ? vp.GetInt32() : 0;
                    int audioPort = root.TryGetProperty("audioPort", out var ap) ? ap.GetInt32() : (isSinglePort ? videoPort : videoPort + 2);
                    string codec = root.TryGetProperty("codec", out var cd) ? cd.GetString() ?? "h264" : "h264";

                    if (!string.IsNullOrEmpty(camId))
                    {
                        CameraStreamPublished?.Invoke(camId, camName, isSinglePort, videoPort, audioPort, codec);
                        if (!string.IsNullOrEmpty(camName))
                        {
                            CameraMetaUpdated?.Invoke(camId, camName);
                        }
                    }
                }
                else if (type == "camera_meta_update")
                {
                    string camId = root.TryGetProperty("cameraId", out var c) ? c.GetString() ?? "" : "";
                    string camName = root.TryGetProperty("cameraName", out var cn) ? cn.GetString() ?? "" : (root.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? "" : (root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : ""));
                    if (!string.IsNullOrEmpty(camId) && !string.IsNullOrEmpty(camName))
                    {
                        CameraMetaUpdated?.Invoke(camId, camName);
                    }
                }
                else if (type == "presence_update")
                {
                    if (root.TryGetProperty("client", out var clientProp))
                    {
                        string camId = clientProp.TryGetProperty("cameraId", out var c) ? c.GetString() ?? "" : "";
                        string camName = clientProp.TryGetProperty("cameraName", out var cn) ? cn.GetString() ?? "" : (clientProp.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? "" : (clientProp.TryGetProperty("name", out var n) ? n.GetString() ?? "" : ""));
                        if (!string.IsNullOrEmpty(camId) && !string.IsNullOrEmpty(camName))
                        {
                            CameraMetaUpdated?.Invoke(camId, camName);
                        }
                    }
                }
                else if (type == "intercom_audio")
                {
                    string from = root.TryGetProperty("from", out var f) ? f.GetString() ?? "encoder" : "encoder";
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
                else if (type == "joined_session" && root.TryGetProperty("cameras", out var camsProp) && camsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var camElem in camsProp.EnumerateArray())
                    {
                        bool online = camElem.TryGetProperty("online", out var onl) && onl.GetBoolean();
                        string camId = camElem.TryGetProperty("id", out var idp) ? idp.GetString() ?? "" : "";
                        string camName = camElem.TryGetProperty("name", out var np) ? np.GetString() ?? "" : (camElem.TryGetProperty("cameraName", out var cnp) ? cnp.GetString() ?? "" : (camElem.TryGetProperty("displayName", out var dnp) ? dnp.GetString() ?? "" : ""));
                        string codec = camElem.TryGetProperty("codec", out var cd) ? cd.GetString() ?? "h264" : "h264";
                        if (!string.IsNullOrEmpty(camId) && !string.IsNullOrEmpty(camName))
                        {
                            CameraMetaUpdated?.Invoke(camId, camName);
                        }
                        if (online && !string.IsNullOrEmpty(camId))
                        {
                            CameraStreamPublished?.Invoke(camId, camName, true, 0, 0, codec);
                        }
                    }
                }
            }
            catch { }
        }

        public async Task DisconnectAsync()
        {
            try
            {
                _shouldReconnect = false;
                _cts?.Cancel();
                if (_ws != null && _ws.State == WebSocketState.Open)
                {
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                }
                _ws?.Dispose();
                _ws = null;
            }
            catch { }
            finally
            {
                ConnectionStateChanged?.Invoke(false);
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _ = DisconnectAsync();
            _cts?.Dispose();
            _sendLock.Dispose();
        }
    }
}
