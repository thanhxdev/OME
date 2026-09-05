namespace OpenMedia.Platform
{
    /// <summary>
    /// Defines the contract for a video preview surface that can receive
    /// and display D3D11 shared textures from the media engine.
    /// Implemented by <c>OpenMediaVideoView</c> (WPF) and <c>WinUIVideoView</c> (WinUI 3).
    /// </summary>
    public interface IVideoView
    {
        /// <summary>
        /// Attaches the view to a shared D3D11 texture.
        /// </summary>
        /// <param name="sharedTextureHandle">NT handle to the DXGI shared texture.</param>
        /// <param name="width">Texture width in pixels.</param>
        /// <param name="height">Texture height in pixels.</param>
        void Attach(IntPtr sharedTextureHandle, int width, int height);

        /// <summary>
        /// Attaches the view to a double-buffered shared D3D11 texture pair.
        /// </summary>
        /// <param name="sharedTextureHandle0">NT handle to the first DXGI shared texture.</param>
        /// <param name="sharedTextureHandle1">NT handle to the second DXGI shared texture.</param>
        /// <param name="width">Texture width in pixels.</param>
        /// <param name="height">Texture height in pixels.</param>
        void Attach(IntPtr sharedTextureHandle0, IntPtr sharedTextureHandle1, int width, int height)
        {
            Attach(sharedTextureHandle0, width, height);
        }

        /// <summary>
        /// Detaches the view, releasing the shared texture reference.
        /// </summary>
        void Detach();

        /// <summary>
        /// Notifies the view that the source texture has been resized.
        /// </summary>
        void Resize(int width, int height);
    }
}
