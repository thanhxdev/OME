namespace OpenMedia.Platform
{
    /// <summary>
    /// Specifies the container format for file recording via <see cref="StreamOutput.File"/>.
    /// </summary>
    public enum RecordFormat
    {
        /// <summary>MPEG-4 container (.mp4).</summary>
        MP4 = 0,

        /// <summary>Matroska container (.mkv).</summary>
        MKV,

        /// <summary>QuickTime container (.mov).</summary>
        MOV,

        /// <summary>MPEG Transport Stream (.ts).</summary>
        TS
    }
}
