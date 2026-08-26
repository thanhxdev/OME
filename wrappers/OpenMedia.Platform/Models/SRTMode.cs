namespace OpenMedia.Platform
{
    /// <summary>
    /// Specifies the SRT connection mode for <see cref="StreamOutput.SRT"/>.
    /// </summary>
    public enum SRTMode
    {
        /// <summary>Active caller — initiates connection to a listener.</summary>
        Caller = 0,

        /// <summary>Passive listener — waits for incoming connections.</summary>
        Listener,

        /// <summary>Both sides connect simultaneously (peer-to-peer).</summary>
        Rendezvous
    }
}
