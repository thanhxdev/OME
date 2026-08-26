namespace OpenMedia.Platform
{
    /// <summary>
    /// Represents the playback state of a <see cref="MediaPlayer"/>.
    /// </summary>
    public enum PlaybackState
    {
        /// <summary>No media loaded.</summary>
        Idle = 0,

        /// <summary>Media is being opened and analyzed.</summary>
        Opening,

        /// <summary>Media is loaded and ready to play.</summary>
        Ready,

        /// <summary>Media is currently playing.</summary>
        Playing,

        /// <summary>Playback is paused.</summary>
        Paused,

        /// <summary>Playback has been stopped.</summary>
        Stopped,

        /// <summary>An error occurred during playback.</summary>
        Error
    }
}
