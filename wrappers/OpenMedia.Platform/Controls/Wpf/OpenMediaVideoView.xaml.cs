using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace OpenMedia.Platform.Controls.Wpf
{
    /// <summary>
    /// A WPF control that displays video preview from the OpenMedia engine
    /// using D3D11 shared textures with zero-copy GPU rendering.
    /// <para>
    /// Usage in XAML:
    /// <code>
    /// &lt;om:OpenMediaVideoView x:Name="VideoView" /&gt;
    /// </code>
    /// </para>
    /// </summary>
    public partial class OpenMediaVideoView : System.Windows.Controls.UserControl, IVideoView
    {
        private WpfD3D11Renderer? _renderer;
        private D3DImage? _d3dImage;
        private bool _isAttached;

        /// <summary>
        /// Indicates whether a video source is currently attached and rendering.
        /// </summary>
        public static readonly DependencyProperty IsPlayingProperty =
            DependencyProperty.Register(nameof(IsPlaying), typeof(bool), typeof(OpenMediaVideoView),
                new PropertyMetadata(false));

        /// <summary>
        /// Gets whether the view is currently displaying video frames.
        /// </summary>
        public bool IsPlaying
        {
            get => (bool)GetValue(IsPlayingProperty);
            private set => SetValue(IsPlayingProperty, value);
        }

        /// <summary>
        /// Initializes a new instance of <see cref="OpenMediaVideoView"/>.
        /// </summary>
        public OpenMediaVideoView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _renderer = new WpfD3D11Renderer();
            if (!_renderer.Initialize())
            {
                Trace.WriteLine("[OpenMediaVideoView] Failed to initialize renderer.");
                return;
            }

            // Create D3DImage in code-behind and set as Image source
            _d3dImage = new D3DImage();
            VideoImage.Source = _d3dImage;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            Detach();
            _renderer?.Dispose();
            _renderer = null;
        }

        /// <inheritdoc />
        public void Attach(IntPtr sharedTextureHandle, int width, int height)
        {
            if (_renderer == null || _d3dImage == null || _isAttached) return;

            if (_renderer.OpenSharedTexture(sharedTextureHandle, width, height))
            {
                // Set up D3DImage backbuffer
                if (_renderer.D3D9SurfacePtr != IntPtr.Zero)
                {
                    _d3dImage.Lock();
                    _d3dImage.SetBackBuffer(D3DResourceType.IDirect3DSurface9, _renderer.D3D9SurfacePtr);
                    _d3dImage.Unlock();
                }

                CompositionTarget.Rendering += OnRendering;
                _isAttached = true;
                IsPlaying = true;
                Trace.WriteLine($"[OpenMediaVideoView] Attached: {width}x{height}");
            }
        }

        /// <inheritdoc />
        public void Detach()
        {
            if (!_isAttached) return;

            CompositionTarget.Rendering -= OnRendering;
            _isAttached = false;
            IsPlaying = false;
            Trace.WriteLine("[OpenMediaVideoView] Detached.");
        }

        /// <inheritdoc />
        public void Resize(int width, int height)
        {
            Trace.WriteLine($"[OpenMediaVideoView] Resize requested: {width}x{height}");
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            _renderer?.RenderFrame();
        }
    }
}
