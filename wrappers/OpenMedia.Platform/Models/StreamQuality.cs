namespace OpenMedia.Platform
{
    /// <summary>
    /// Predefined output quality presets for <see cref="StreamOutput"/>.
    /// </summary>
    public enum StreamQuality
    {
        /// <summary>480p (854×480), ~1.5 Mbps.</summary>
        Low480p = 0,

        /// <summary>720p (1280×720), ~3 Mbps.</summary>
        Medium720p,

        /// <summary>1080p (1920×1080), ~6 Mbps.</summary>
        High1080p,

        /// <summary>4K (3840×2160), ~20 Mbps.</summary>
        Ultra4K
    }
}
