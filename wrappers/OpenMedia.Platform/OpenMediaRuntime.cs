using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OpenMedia.SDK;
using OpenMedia.Platform.Internal;

namespace OpenMedia.Platform
{
    /// <summary>
    /// Manages the lifecycle of the OpenMedia engine — server discovery,
    /// process launch, IPC connection, Watchdog heartbeat monitoring, and automated State Replay self-healing.
    /// <para>
    /// This is the entry point for all OpenMedia.Platform operations.
    /// Call <see cref="InitializeAsync"/> before using any other Platform class.
    /// </para>
    /// </summary>
    /// <example>
    /// <code>
    /// await OpenMediaRuntime.InitializeAsync();
    /// // ... use MediaPlayer, VideoMixer, etc. ...
    /// OpenMediaRuntime.Shutdown();
    /// </code>
    /// </example>
    public static class OpenMediaRuntime
    {
        private static IPCClient? _ipcClient;
        private static Process? _serverProcess;
        private static CancellationTokenSource? _heartbeatCts;
        private static Version? _engineVersion;
        private static RuntimeOptions _currentOptions = new();
        private static int _isRecovering = 0;
        private static readonly object _lock = new();

        /// <summary>
        /// Indicates whether the runtime is currently connected to the server.
        /// </summary>
        public static bool IsConnected => _ipcClient?.IsConnected == true;

        /// <summary>
        /// The version of the connected media engine. <c>null</c> if not connected.
        /// </summary>
        public static Version? EngineVersion => _engineVersion;

        /// <summary>
        /// The Process ID of the active OpenMediaServer instance. <c>null</c> if not launched as child process.
        /// </summary>
        public static int? ServerProcessId => _serverProcess != null && !_serverProcess.HasExited ? _serverProcess.Id : null;

        /// <summary>
        /// Raised when the server process disconnects unexpectedly.
        /// Events are dispatched on the captured <see cref="SynchronizationContext"/>.
        /// </summary>
        public static event EventHandler? ServerDisconnected;

        /// <summary>
        /// Raised when the runtime successfully reconnects to the server and reconstitutes the pipeline graph.
        /// </summary>
        public static event EventHandler? ServerReconnected;

        /// <summary>
        /// Fired when operational self-healing and watchdog messages are emitted.
        /// </summary>
        public static event Action<string>? ResilienceLogEmitted;

        /// <summary>
        /// Accesses the active State Replay Engine singleton for tracking and reconstituting media state.
        /// </summary>
        public static StateReplayEngine StateReplay => StateReplayEngine.Instance;

        /// <summary>
        /// The underlying IPC client. Exposed for internal use by Platform classes.
        /// </summary>
        internal static IPCClient? IPC => _ipcClient;

        /// <summary>
        /// Initializes the OpenMedia runtime: discovers the server, establishes
        /// an IPC connection, and starts heartbeat monitoring.
        /// </summary>
        /// <param name="options">Optional configuration. When <c>null</c>, defaults are used.</param>
        /// <returns><c>true</c> if initialization succeeded; <c>false</c> otherwise.</returns>
        /// <exception cref="System.IO.FileNotFoundException">
        /// Thrown when the server executable cannot be found and <paramref name="options"/>
        /// does not provide an explicit path.
        /// </exception>
        public static async Task<bool> InitializeAsync(RuntimeOptions? options = null)
        {
            _currentOptions = options ?? new RuntimeOptions();

            lock (_lock)
            {
                if (_ipcClient?.IsConnected == true)
                    return true; // Already initialized
            }

            string effectivePipeName = _currentOptions.PipeName;
            if (effectivePipeName == "OpenMediaSDK")
            {
                effectivePipeName = $"OpenMediaSDK_{Process.GetCurrentProcess().Id}";
            }

            _ipcClient = new IPCClient();

            // Try connecting to already-running server first
            bool connected = await _ipcClient.ConnectAsync(effectivePipeName, 1000);

            if (!connected && _currentOptions.AutoLaunch)
            {
                // Discover and launch server
                var serverPath = ServerDiscovery.Discover(_currentOptions.ServerPath);
                if (serverPath == null)
                {
                    Trace.WriteLine("[OpenMedia.Platform] Server not found. InitializeAsync returning false.");
                    return false;
                }

                _serverProcess = new Process();
                _serverProcess.StartInfo.FileName = serverPath;

                string? serverDir = Path.GetDirectoryName(serverPath);
                if (!string.IsNullOrEmpty(serverDir))
                {
                    _serverProcess.StartInfo.WorkingDirectory = serverDir;
                    string existingPath = Environment.GetEnvironmentVariable("PATH") ?? "";
                    if (!existingPath.Contains(serverDir, StringComparison.OrdinalIgnoreCase))
                    {
                        _serverProcess.StartInfo.EnvironmentVariables["PATH"] = serverDir + ";" + existingPath;
                    }
                }

                _serverProcess.StartInfo.Arguments = $"--pipe-name \\\\.\\pipe\\{effectivePipeName}";
                _serverProcess.StartInfo.UseShellExecute = false;
                _serverProcess.StartInfo.CreateNoWindow = true;
                _serverProcess.EnableRaisingEvents = true;
                _serverProcess.Exited += OnServerProcessExited;

                try
                {
                    _serverProcess.Start();
                    Trace.WriteLine($"[OpenMedia.Platform] Launched server: {serverPath} (PID: {_serverProcess.Id}) with pipe {effectivePipeName}");
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[OpenMedia.Platform] Failed to launch server: {ex.Message}");
                    return false;
                }

                // Retry connection with longer timeout
                connected = await _ipcClient.ConnectAsync(effectivePipeName, _currentOptions.ConnectionTimeout);
            }

            if (!connected)
            {
                Trace.WriteLine("[OpenMedia.Platform] Failed to connect via IPC.");
                return false;
            }

            // Perform handshake
            try
            {
                var handshakePayload = IPCCommandBuilder.Handshake();
                var response = await _ipcClient.SendAndReceiveAsync(CommandType.Handshake, handshakePayload);
                if (!IPCCommandBuilder.ParseHandshake(response))
                {
                    Trace.WriteLine("[OpenMedia.Platform] Handshake failed.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[OpenMedia.Platform] Handshake exception: {ex.Message}");
                return false;
            }

            _engineVersion = new Version(2, 0, 0);

            // Start watchdog heartbeat monitor
            StartHeartbeat();

            Trace.WriteLine("[OpenMedia.Platform] Runtime initialized successfully (V2.0-Final).");
            return true;
        }

        /// <summary>
        /// Manually triggers State Replay to reconstitute all active pipelines and media graphs.
        /// </summary>
        public static async Task<ReplayReport> ReplayStateAsync()
        {
            return await StateReplayEngine.Instance.ReplayAsync(_ipcClient);
        }

        /// <summary>
        /// Shuts down the runtime, closing the IPC connection and releasing all resources.
        /// </summary>
        public static void Shutdown()
        {
            StopHeartbeat();

            if (_ipcClient != null)
            {
                try
                {
                    var task = _ipcClient.ShutdownServerAsync();
                    task.Wait(500);
                }
                catch { }

                try
                {
                    _ipcClient.Dispose();
                }
                catch { }
                _ipcClient = null;
            }

            if (_serverProcess != null)
            {
                try
                {
                    _serverProcess.Exited -= OnServerProcessExited;
                    if (!_serverProcess.HasExited)
                    {
                        if (!_serverProcess.WaitForExit(300))
                        {
                            _serverProcess.Kill();
                        }
                    }
                }
                catch { }

                try
                {
                    _serverProcess.Dispose();
                }
                catch { }
                _serverProcess = null;
            }

            _engineVersion = null;
            Trace.WriteLine("[OpenMedia.Platform] Runtime shut down.");
        }

        /// <summary>
        /// Sends a command to the server and returns the response.
        /// Used internally by Platform classes.
        /// </summary>
        internal static async Task<byte[]?> SendCommandAsync(CommandType command, byte[]? payload = null)
        {
            if (_ipcClient == null || !_ipcClient.IsConnected)
                throw new InvalidOperationException("OpenMediaRuntime is not initialized. Call InitializeAsync() first.");

            return await _ipcClient.SendAndReceiveAsync(command, payload);
        }

        /// <summary>
        /// Requests a shared D3D11 texture from the server.
        /// Used internally by preview renderers.
        /// </summary>
        internal static async Task<ShareD3D11TexturePayload?> RequestSharedTextureAsync()
        {
            if (_ipcClient == null || !_ipcClient.IsConnected)
                return null;

            return await _ipcClient.RequestSharedTextureAsync();
        }

        private static void StartHeartbeat()
        {
            StopHeartbeat();

            _heartbeatCts = new CancellationTokenSource();
            var ct = _heartbeatCts.Token;
            var syncContext = SynchronizationContext.Current;
            int timeoutMs = Math.Max(500, _currentOptions.WatchdogTimeoutMs);

            _ = Task.Run(async () =>
            {
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(timeoutMs, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    if (ct.IsCancellationRequested) break;

                    if (_ipcClient == null || !_ipcClient.IsConnected)
                    {
                        _ = HandleDisconnectAndRecoverAsync(syncContext);
                        break;
                    }

                    try
                    {
                        var response = await _ipcClient.SendAndReceiveAsync(CommandType.GetStatus);
                        if (response == null)
                        {
                            _ = HandleDisconnectAndRecoverAsync(syncContext);
                            break;
                        }
                    }
                    catch
                    {
                        _ = HandleDisconnectAndRecoverAsync(syncContext);
                        break;
                    }
                }
            }, ct);
        }

        private static void StopHeartbeat()
        {
            _heartbeatCts?.Cancel();
            _heartbeatCts?.Dispose();
            _heartbeatCts = null;
        }

        private static void OnServerProcessExited(object? sender, EventArgs e)
        {
            Trace.WriteLine("[OpenMedia.Platform] Server process exited.");
            _ = HandleDisconnectAndRecoverAsync(SynchronizationContext.Current);
        }

        private static async Task HandleDisconnectAndRecoverAsync(SynchronizationContext? syncContext)
        {
            if (Interlocked.CompareExchange(ref _isRecovering, 1, 0) != 0)
                return; // Recovery already in progress

            try
            {
                StopHeartbeat();
                RaiseServerDisconnected(syncContext);

                if (!_currentOptions.AutoReconnect)
                {
                    ResilienceLogEmitted?.Invoke("[Resilience] AutoReconnect disabled. Manual intervention required.");
                    return;
                }

                ResilienceLogEmitted?.Invoke("[Watchdog] Server disconnect detected. Initiating automated self-healing & state reconstitution...");

                bool reconnected = false;
                for (int attempt = 1; attempt <= _currentOptions.MaxReconnectAttempts; attempt++)
                {
                    ResilienceLogEmitted?.Invoke($"[Watchdog] Reconnection attempt {attempt}/{_currentOptions.MaxReconnectAttempts}...");
                    try
                    {
                        // Clean up existing IPC instance
                        _ipcClient?.Dispose();
                        _ipcClient = null;

                        // Reinitialize runtime
                        bool ok = await InitializeAsync(_currentOptions);
                        if (ok && _ipcClient?.IsConnected == true)
                        {
                            reconnected = true;
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        ResilienceLogEmitted?.Invoke($"[Watchdog] Attempt {attempt} failed: {ex.Message}");
                    }

                    int delay = Math.Min(100 * (1 << Math.Min(attempt, 5)), 2000);
                    await Task.Delay(delay);
                }

                if (reconnected)
                {
                    ResilienceLogEmitted?.Invoke("[StateReplay] Replaying active pipeline graph to reconstituted server...");
                    var report = await StateReplayEngine.Instance.ReplayAsync(_ipcClient);
                    if (report.Succeeded)
                    {
                        ResilienceLogEmitted?.Invoke($"[StateReplay] ✅ Pipeline graph restored in {report.ElapsedMs}ms (Players: {report.ReplayedPlayers}, Mixers: {report.ReplayedMixers}, Outputs: {report.ReplayedOutputs})");
                    }
                    else
                    {
                        ResilienceLogEmitted?.Invoke($"[StateReplay] ⚠️ Pipeline replay completed with note: {report.ErrorMessage}");
                    }

                    RaiseServerReconnected(syncContext);
                }
                else
                {
                    ResilienceLogEmitted?.Invoke("[Watchdog] ❌ All reconnection attempts exhausted.");
                }
            }
            finally
            {
                Interlocked.Exchange(ref _isRecovering, 0);
            }
        }

        private static void RaiseServerDisconnected(SynchronizationContext? syncContext)
        {
            if (syncContext != null)
            {
                syncContext.Post(_ => ServerDisconnected?.Invoke(null, EventArgs.Empty), null);
            }
            else
            {
                ServerDisconnected?.Invoke(null, EventArgs.Empty);
            }
        }

        private static void RaiseServerReconnected(SynchronizationContext? syncContext)
        {
            if (syncContext != null)
            {
                syncContext.Post(_ => ServerReconnected?.Invoke(null, EventArgs.Empty), null);
            }
            else
            {
                ServerReconnected?.Invoke(null, EventArgs.Empty);
            }
        }
    }
}
