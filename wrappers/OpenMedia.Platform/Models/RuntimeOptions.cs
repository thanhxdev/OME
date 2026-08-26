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
    }
}
