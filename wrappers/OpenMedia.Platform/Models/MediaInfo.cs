namespace OpenMedia.Platform
{
    /// <summary>
    /// Contains metadata about a loaded media source.
    /// </summary>
    public sealed class MediaInfo
    {
        /// <summary>Total duration of the media.</summary>
        public TimeSpan Duration { get; init; }

        /// <summary>Video codec name (e.g., "h264", "hevc").</summary>
        public string VideoCodec { get; init; } = string.Empty;

        /// <summary>Audio codec name (e.g., "aac", "opus").</summary>
        public string AudioCodec { get; init; } = string.Empty;

        /// <summary>Video width in pixels.</summary>
        public int Width { get; init; }

        /// <summary>Video height in pixels.</summary>
        public int Height { get; init; }

        /// <summary>Video frame rate (fps).</summary>
        public double FrameRate { get; init; }

        /// <summary>Number of audio channels.</summary>
        public int AudioChannels { get; init; }

        /// <summary>Audio sample rate in Hz.</summary>
        public int AudioSampleRate { get; init; }

        /// <summary>Overall bitrate in kbps.</summary>
        public long BitrateKbps { get; init; }

        /// <summary>Source URI or file path.</summary>
        public string SourceUri { get; init; } = string.Empty;
    }
}
