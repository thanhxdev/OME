namespace OpenMedia.Platform
{
    /// <summary>
    /// Configuration options for <see cref="OpenMediaRuntime.InitializeAsync"/>.
    /// All properties are optional — sensible defaults are applied.
    /// </summary>
    public sealed class RuntimeOptions
    {
        /// <summary>
        /// Explicit path to <c>OpenMediaServer.exe</c>.
        /// When set, bypasses the automatic discovery chain.
        /// </summary>
        public string? ServerPath { get; init; }

        /// <summary>
        /// Named pipe name for IPC communication.
        /// Default: <c>"OpenMediaSDK"</c>.
        /// </summary>
        public string PipeName { get; init; } = "OpenMediaSDK";

        /// <summary>
        /// Whether to automatically launch the server process if it's not running.
        /// Default: <c>true</c>.
        /// </summary>
        public bool AutoLaunch { get; init; } = true;

        /// <summary>
        /// Timeout in milliseconds for the IPC connection attempt.
        /// Default: <c>5000</c> (5 seconds).
        /// </summary>
        public int ConnectionTimeout { get; init; } = 5000;

        /// <summary>
        /// Enables automated self-healing and reconnection when the server terminates or disconnects.
        /// Default: <c>true</c>.
        /// </summary>
        public bool AutoReconnect { get; init; } = true;

        /// <summary>
        /// Heartbeat watchdog sensitivity threshold in milliseconds.
        /// If server fails to respond within this duration, recovery is triggered.
        /// Default: <c>1000</c> (1 second).
        /// </summary>
        public int WatchdogTimeoutMs { get; init; } = 1000;

        /// <summary>
        /// Maximum number of automated reconnection attempts before giving up.
        /// Default: <c>10</c>.
        /// </summary>
        public int MaxReconnectAttempts { get; init; } = 10;
    }
}
