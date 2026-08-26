using OpenMedia.Platform.Models;

namespace OpenMedia.Platform.Extensions
{
    /// <summary>
    /// Provides fluent overlay API for <see cref="MediaPlayer"/> and <see cref="VideoMixer"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// player.Overlay.AddText("Title").AtTopRight().WithFont("Arial", 24);
    /// player.Overlay.AddImage("logo.png").AtBottomLeft().WithOpacity(0.8);
    /// player.Overlay.AddClock().AtTopLeft();
    /// </code>
    /// </example>
    public static class OverlayExtensions
    {
        private static readonly Dictionary<int, OverlayManager> _managers = new();

        /// <summary>
        /// Gets the <see cref="OverlayManager"/> for a <see cref="MediaPlayer"/>.
        /// </summary>
        public static OverlayManager Overlay(this MediaPlayer player)
        {
            var key = player.GetHashCode();
            if (!_managers.TryGetValue(key, out var manager))
            {
                manager = new OverlayManager();
                _managers[key] = manager;
            }
            return manager;
        }

        /// <summary>
        /// Gets the <see cref="OverlayManager"/> for a <see cref="VideoMixer"/>.
        /// </summary>
        public static OverlayManager Overlay(this VideoMixer mixer)
        {
            var key = mixer.GetHashCode();
            if (!_managers.TryGetValue(key, out var manager))
            {
                manager = new OverlayManager();
                _managers[key] = manager;
            }
            return manager;
        }
    }

    /// <summary>
    /// Manages a collection of overlay items and provides fluent methods for adding them.
    /// </summary>
    public sealed class OverlayManager
    {
        private readonly List<OverlayItem> _overlays = new();

        /// <summary>Gets all current overlay items.</summary>
        public IReadOnlyList<OverlayItem> Items => _overlays;

        /// <summary>
        /// Adds a text overlay.
        /// </summary>
        /// <param name="text">The text content to display.</param>
        /// <returns>A builder for configuring the text overlay.</returns>
        public TextOverlayBuilder AddText(string text)
        {
            var overlay = new TextOverlay { Text = text };
            _overlays.Add(overlay);
            return new TextOverlayBuilder(overlay);
        }

        /// <summary>
        /// Adds an image overlay.
        /// </summary>
        /// <param name="imagePath">Path to the image file.</param>
        /// <returns>A builder for configuring the image overlay.</returns>
        public ImageOverlayBuilder AddImage(string imagePath)
        {
            var overlay = new ImageOverlay { ImagePath = imagePath };
            _overlays.Add(overlay);
            return new ImageOverlayBuilder(overlay);
        }

        /// <summary>
        /// Adds a clock overlay displaying the current time.
        /// </summary>
        /// <param name="format">Time format string. Default: "HH:mm:ss".</param>
        /// <returns>A builder for configuring the clock overlay.</returns>
        public ClockOverlayBuilder AddClock(string format = "HH:mm:ss")
        {
            var overlay = new ClockOverlay { Format = format };
            _overlays.Add(overlay);
            return new ClockOverlayBuilder(overlay);
        }

        /// <summary>Removes all overlays.</summary>
        public void Clear() => _overlays.Clear();

        /// <summary>Removes a specific overlay item.</summary>
        public void Remove(OverlayItem item) => _overlays.Remove(item);
    }

    // ─── Builders ───────────────────────────────────────────────────

    /// <summary>
    /// Base builder providing shared position and style methods.
    /// </summary>
    /// <typeparam name="TBuilder">The concrete builder type for fluent chaining.</typeparam>
    /// <typeparam name="TOverlay">The overlay item type being built.</typeparam>
    public abstract class OverlayBuilderBase<TBuilder, TOverlay>
        where TBuilder : OverlayBuilderBase<TBuilder, TOverlay>
        where TOverlay : OverlayItem
    {
        /// <summary>The overlay item being configured.</summary>
        protected TOverlay Item { get; }

        /// <summary>Creates a new builder for the specified overlay item.</summary>
        protected OverlayBuilderBase(TOverlay item) => Item = item;

        /// <summary>Positions at top-left corner.</summary>
        public TBuilder AtTopLeft() { Item.Position = OverlayPosition.TopLeft; return (TBuilder)this; }

        /// <summary>Positions at top-right corner.</summary>
        public TBuilder AtTopRight() { Item.Position = OverlayPosition.TopRight; return (TBuilder)this; }

        /// <summary>Positions at bottom-left corner.</summary>
        public TBuilder AtBottomLeft() { Item.Position = OverlayPosition.BottomLeft; return (TBuilder)this; }

        /// <summary>Positions at bottom-right corner.</summary>
        public TBuilder AtBottomRight() { Item.Position = OverlayPosition.BottomRight; return (TBuilder)this; }

        /// <summary>Positions at center.</summary>
        public TBuilder AtCenter() { Item.Position = OverlayPosition.Center; return (TBuilder)this; }

        /// <summary>Positions at custom coordinates.</summary>
        /// <param name="x">X coordinate (pixels from left).</param>
        /// <param name="y">Y coordinate (pixels from top).</param>
        public TBuilder AtCustom(double x, double y)
        {
            Item.Position = OverlayPosition.Custom;
            Item.X = x;
            Item.Y = y;
            return (TBuilder)this;
        }

        /// <summary>Sets the opacity.</summary>
        /// <param name="opacity">Opacity value (0.0 = transparent, 1.0 = opaque).</param>
        public TBuilder WithOpacity(double opacity)
        {
            Item.Opacity = Math.Clamp(opacity, 0.0, 1.0);
            return (TBuilder)this;
        }
    }

    /// <summary>
    /// Fluent builder for configuring a <see cref="TextOverlay"/>.
    /// </summary>
    public sealed class TextOverlayBuilder : OverlayBuilderBase<TextOverlayBuilder, TextOverlay>
    {
        internal TextOverlayBuilder(TextOverlay item) : base(item) { }

        /// <summary>Sets the font family and size.</summary>
        public TextOverlayBuilder WithFont(string family, double size)
        {
            Item.FontFamily = family;
            Item.FontSize = size;
            return this;
        }

        /// <summary>Sets the text color as ARGB hex.</summary>
        public TextOverlayBuilder WithColor(string argbHex)
        {
            Item.Color = argbHex;
            return this;
        }
    }

    /// <summary>
    /// Fluent builder for configuring an <see cref="ImageOverlay"/>.
    /// </summary>
    public sealed class ImageOverlayBuilder : OverlayBuilderBase<ImageOverlayBuilder, ImageOverlay>
    {
        internal ImageOverlayBuilder(ImageOverlay item) : base(item) { }

        /// <summary>Sets the image dimensions.</summary>
        public ImageOverlayBuilder WithSize(int width, int height)
        {
            Item.Width = width;
            Item.Height = height;
            return this;
        }
    }

    /// <summary>
    /// Fluent builder for configuring a <see cref="ClockOverlay"/>.
    /// </summary>
    public sealed class ClockOverlayBuilder : OverlayBuilderBase<ClockOverlayBuilder, ClockOverlay>
    {
        internal ClockOverlayBuilder(ClockOverlay item) : base(item) { }

        /// <summary>Sets the font family and size.</summary>
        public ClockOverlayBuilder WithFont(string family, double size)
        {
            Item.FontFamily = family;
            Item.FontSize = size;
            return this;
        }

        /// <summary>Sets the text color as ARGB hex.</summary>
        public ClockOverlayBuilder WithColor(string argbHex)
        {
            Item.Color = argbHex;
            return this;
        }
    }
}
