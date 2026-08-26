namespace OpenMedia.Platform.Controls.WinUI
{
    /// <summary>
    /// WinUI 3 video preview control using <c>SwapChainPanel</c>.
    /// Implements <see cref="IVideoView"/> for compatibility with
    /// <see cref="MediaPlayer.AttachPreview"/> and <see cref="VideoMixer.AttachPreview"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Phase C — Stub Implementation.</b>
    /// This class provides the <see cref="IVideoView"/> contract for WinUI 3 apps.
    /// Full SwapChainPanel rendering will be implemented when WinUI 3 preview support
    /// is prioritized. The WPF path via <see cref="Controls.Wpf.OpenMediaVideoView"/>
    /// is fully functional.
    /// </para>
    /// <para>
    /// To use WinUI 3 preview, add the <c>Microsoft.WindowsAppSDK</c> NuGet package
    /// and reference this control in your WinUI 3 XAML.
    /// </para>
    /// </remarks>
    public class WinUIVideoView : IVideoView
    {
        private IntPtr _sharedTextureHandle;
        private int _width;
        private int _height;
        private bool _isAttached;

        /// <summary>
        /// Gets whether a shared texture is currently attached.
        /// </summary>
        public bool IsAttached => _isAttached;

        /// <summary>
        /// Gets the current texture width.
        /// </summary>
        public int Width => _width;

        /// <summary>
        /// Gets the current texture height.
        /// </summary>
        public int Height => _height;

        /// <summary>
        /// Attaches the view to a shared D3D11 texture.
        /// </summary>
        /// <param name="sharedTextureHandle">NT handle to the DXGI shared texture.</param>
        /// <param name="width">Texture width in pixels.</param>
        /// <param name="height">Texture height in pixels.</param>
        /// <remarks>
        /// TODO (Phase C): Implement SwapChainPanel setup:
        /// 1. Create DXGI SwapChain for composition
        /// 2. Open shared texture via ID3D11Device1.OpenSharedResource1
        /// 3. Copy shared texture to swap chain back buffer each frame
        /// 4. Present via SwapChainPanel.
        /// </remarks>
        public void Attach(IntPtr sharedTextureHandle, int width, int height)
        {
            _sharedTextureHandle = sharedTextureHandle;
            _width = width;
            _height = height;
            _isAttached = true;

            System.Diagnostics.Trace.WriteLine(
                $"[WinUIVideoView] Attached shared texture: {width}x{height} (stub — full rendering not yet implemented)");
        }

        /// <summary>
        /// Detaches the view, releasing the shared texture reference.
        /// </summary>
        public void Detach()
        {
            _sharedTextureHandle = IntPtr.Zero;
            _isAttached = false;

            System.Diagnostics.Trace.WriteLine("[WinUIVideoView] Detached.");
        }

        /// <summary>
        /// Notifies the view that the source texture has been resized.
        /// </summary>
        public void Resize(int width, int height)
        {
            _width = width;
            _height = height;

            System.Diagnostics.Trace.WriteLine($"[WinUIVideoView] Resized to {width}x{height}");
        }
    }
}
