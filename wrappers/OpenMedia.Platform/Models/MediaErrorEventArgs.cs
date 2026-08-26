namespace OpenMedia.Platform
{
    /// <summary>
    /// Provides data for the <see cref="MediaPlayer.ErrorOccurred"/> event.
    /// </summary>
    public sealed class MediaErrorEventArgs : EventArgs
    {
        /// <summary>Numeric error code from the media engine.</summary>
        public int ErrorCode { get; }

        /// <summary>Human-readable error description.</summary>
        public string Message { get; }

        /// <summary>The source URI or component that caused the error.</summary>
        public string Source { get; }

        /// <summary>
        /// Initializes a new instance of <see cref="MediaErrorEventArgs"/>.
        /// </summary>
        public MediaErrorEventArgs(int errorCode, string message, string source = "")
        {
            ErrorCode = errorCode;
            Message = message;
            Source = source;
        }
    }
}
