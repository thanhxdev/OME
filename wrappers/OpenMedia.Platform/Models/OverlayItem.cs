namespace OpenMedia.Platform.Models
{
    /// <summary>
    /// Defines the position of an overlay element on the video surface.
    /// </summary>
    public enum OverlayPosition
    {
        /// <summary>Top-left corner.</summary>
        TopLeft,
        /// <summary>Top-right corner.</summary>
        TopRight,
        /// <summary>Bottom-left corner.</summary>
        BottomLeft,
        /// <summary>Bottom-right corner.</summary>
        BottomRight,
        /// <summary>Center of the video.</summary>
        Center,
        /// <summary>Custom position defined by X and Y coordinates.</summary>
        Custom
    }

    /// <summary>
    /// Base class for overlay items rendered on top of video output.
    /// </summary>
    public abstract class OverlayItem
    {
        /// <summary>Overlay position on the video surface.</summary>
        public OverlayPosition Position { get; internal set; } = OverlayPosition.TopLeft;

        /// <summary>Custom X coordinate (used when Position is Custom).</summary>
        public double X { get; internal set; }

        /// <summary>Custom Y coordinate (used when Position is Custom).</summary>
        public double Y { get; internal set; }

        /// <summary>Opacity (0.0 = transparent, 1.0 = fully opaque).</summary>
        public double Opacity { get; internal set; } = 1.0;

        /// <summary>Whether the overlay is currently visible.</summary>
        public bool IsVisible { get; set; } = true;
    }

    /// <summary>
    /// Text overlay rendered on the video surface.
    /// </summary>
    public sealed class TextOverlay : OverlayItem
    {
        /// <summary>Text content.</summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>Font family name.</summary>
        public string FontFamily { get; internal set; } = "Arial";

        /// <summary>Font size in points.</summary>
        public double FontSize { get; internal set; } = 16;

        /// <summary>Text color as ARGB hex string (e.g., "#FFFFFFFF").</summary>
        public string Color { get; internal set; } = "#FFFFFFFF";
    }

    /// <summary>
    /// Image overlay rendered on the video surface.
    /// </summary>
    public sealed class ImageOverlay : OverlayItem
    {
        /// <summary>Path to the image file.</summary>
        public string ImagePath { get; set; } = string.Empty;

        /// <summary>Image width in pixels. 0 = original size.</summary>
        public int Width { get; internal set; }

        /// <summary>Image height in pixels. 0 = original size.</summary>
        public int Height { get; internal set; }
    }

    /// <summary>
    /// Clock overlay displaying current time on the video surface.
    /// </summary>
    public sealed class ClockOverlay : OverlayItem
    {
        /// <summary>Time format string (e.g., "HH:mm:ss").</summary>
        public string Format { get; set; } = "HH:mm:ss";

        /// <summary>Font family name.</summary>
        public string FontFamily { get; internal set; } = "Arial";

        /// <summary>Font size in points.</summary>
        public double FontSize { get; internal set; } = 16;

        /// <summary>Text color as ARGB hex string.</summary>
        public string Color { get; internal set; } = "#FFFFFFFF";
    }
}
