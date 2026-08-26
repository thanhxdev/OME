namespace OpenMedia.Platform
{
    /// <summary>
    /// Specifies the transition effect used when switching between video layers in a <see cref="VideoMixer"/>.
    /// </summary>
    public enum TransitionType
    {
        /// <summary>Instant cut — no transition effect.</summary>
        Cut = 0,

        /// <summary>Cross-dissolve between two sources.</summary>
        Dissolve,

        /// <summary>Wipe transition with directional reveal.</summary>
        Wipe,

        /// <summary>Fade to/from black.</summary>
        Fade
    }
}
