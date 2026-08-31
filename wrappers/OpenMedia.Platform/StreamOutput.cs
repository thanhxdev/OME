using OpenMedia.Platform.Models;

namespace OpenMedia.Platform
{
    /// <summary>
    /// Factory class for creating output configurations with a single-line API.
    /// Each factory method returns a configured <see cref="StreamOutput"/>
    /// ready to be added to a <see cref="VideoMixer"/> or <see cref="MediaPlayer"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// var rtmp = StreamOutput.RTMP("rtmp://a.rtmp.youtube.com/live2/KEY", StreamQuality.High1080p);
    /// var file = StreamOutput.File("recording.mp4", RecordFormat.MP4);
    /// mixer.AddOutput(rtmp);
    /// mixer.AddOutput(file);
    /// </code>
    /// </example>
    public sealed class StreamOutput : IDisposable
    {
        private bool _disposed;

        /// <summary>
        /// The type of output (RTMP, SRT, NDI, File, WebRTC).
        /// </summary>
        internal StreamOutputType OutputType { get; }

        /// <summary>
        /// Configuration dictionary for the output (varies by type).
        /// </summary>
        internal IReadOnlyDictionary<string, object> Configuration { get; }

        private StreamOutput(StreamOutputType type, Dictionary<string, object> config)
        {
            OutputType = type;
            Configuration = config;
        }

        /// <summary>
        /// Creates an RTMP streaming output.
        /// </summary>
        /// <param name="url">RTMP URL (e.g., <c>rtmp://a.rtmp.youtube.com/live2/YOUR_KEY</c>).</param>
        /// <param name="quality">Output quality preset. Default: <see cref="StreamQuality.High1080p"/>.</param>
        /// <returns>A configured <see cref="StreamOutput"/>.</returns>
        public static StreamOutput RTMP(string url, StreamQuality quality = StreamQuality.High1080p)
        {
            return new StreamOutput(StreamOutputType.RTMP, new Dictionary<string, object>
            {
                ["url"] = url,
                ["quality"] = quality
            });
        }

        /// <summary>
        /// Creates an SRT streaming output with full configuration parameters.
        /// </summary>
        /// <param name="config">Comprehensive SRT stream configuration.</param>
        /// <returns>A configured <see cref="StreamOutput"/>.</returns>
        public static StreamOutput SRT(SRTStreamConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);
            return new StreamOutput(StreamOutputType.SRT, config.ToDictionary());
        }

        /// <summary>
        /// Creates an SRT streaming output with individual parameters.
        /// </summary>
        /// <param name="host">SRT host address.</param>
        /// <param name="port">SRT port.</param>
        /// <param name="mode">SRT connection mode. Default: <see cref="SRTMode.Caller"/>.</param>
        /// <param name="streamId">Optional Stream ID / channel identifier.</param>
        /// <param name="latencyMs">SRT buffer latency in milliseconds.</param>
        /// <param name="passphrase">Optional passphrase for AES encryption.</param>
        /// <returns>A configured <see cref="StreamOutput"/>.</returns>
        public static StreamOutput SRT(
            string host, 
            int port, 
            SRTMode mode = SRTMode.Caller,
            string? streamId = null,
            int latencyMs = 120,
            string? passphrase = null)
        {
            var config = new SRTStreamConfig
            {
                Host = host,
                Port = port,
                Mode = mode,
                StreamId = streamId ?? string.Empty,
                LatencyMs = latencyMs,
                EncryptionEnabled = !string.IsNullOrEmpty(passphrase),
                Passphrase = passphrase ?? string.Empty
            };
            return SRT(config);
        }

        /// <summary>
        /// Creates an NDI output.
        /// </summary>
        /// <param name="streamName">NDI stream name visible on the network.</param>
        /// <returns>A configured <see cref="StreamOutput"/>.</returns>
        public static StreamOutput NDI(string streamName)
        {
            return new StreamOutput(StreamOutputType.NDI, new Dictionary<string, object>
            {
                ["streamName"] = streamName
            });
        }

        /// <summary>
        /// Creates a file recording output.
        /// </summary>
        /// <param name="targetPath">Output file path.</param>
        /// <param name="format">Container format. Default: <see cref="RecordFormat.MP4"/>.</param>
        /// <returns>A configured <see cref="StreamOutput"/>.</returns>
        public static StreamOutput File(string targetPath, RecordFormat format = RecordFormat.MP4)
        {
            return new StreamOutput(StreamOutputType.File, new Dictionary<string, object>
            {
                ["path"] = targetPath,
                ["format"] = format
            });
        }

        /// <summary>
        /// Creates a WebRTC output.
        /// </summary>
        /// <param name="signalingServerUri">URI of the signaling server.</param>
        /// <returns>A configured <see cref="StreamOutput"/>.</returns>
        public static StreamOutput WebRTC(string signalingServerUri)
        {
            return new StreamOutput(StreamOutputType.WebRTC, new Dictionary<string, object>
            {
                ["signalingUri"] = signalingServerUri
            });
        }

        /// <summary>
        /// Converts the output configuration into an IPC command payload
        /// for transmission to the server when attached to a Mixer or Player.
        /// </summary>
        /// <param name="pipelineId">The pipeline to attach this output to.</param>
        /// <returns>Binary payload for the AddOutput IPC command.</returns>
        internal byte[] ToIPCPayload(uint pipelineId)
        {
            var b = new OpenMedia.SDK.MessageBuilder();
            b.WriteU32(pipelineId);
            b.WriteString(OutputType.ToString());

            // Serialize all configuration key-value pairs
            b.WriteU32((uint)Configuration.Count);
            foreach (var kvp in Configuration)
            {
                b.WriteString(kvp.Key);
                b.WriteString(kvp.Value?.ToString() ?? string.Empty);
            }

            return b.ToArray();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // IPC cleanup: notify server to remove this output
            System.Diagnostics.Trace.WriteLine($"[StreamOutput] Disposed: {OutputType}");
        }
    }

    /// <summary>
    /// Internal enum for identifying stream output types.
    /// </summary>
    internal enum StreamOutputType
    {
        RTMP,
        SRT,
        NDI,
        File,
        WebRTC
    }
}
