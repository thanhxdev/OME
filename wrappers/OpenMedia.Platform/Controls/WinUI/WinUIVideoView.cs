using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace OpenMedia.Platform.Controls.WinUI
{
    /// <summary>
    /// COM interface definition for WinUI 3 / UWP <c>SwapChainPanel</c> native interop.
    /// Used to assign an <see cref="IDXGISwapChain2"/> or <see cref="IDXGISwapChain1"/> to the XAML composition tree.
    /// </summary>
    [ComImport]
    [Guid("63DE0B70-AC38-4330-AC05-59E302257530")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ISwapChainPanelNative
    {
        /// <summary>
        /// Sets the DXGI swap chain on the native SwapChainPanel.
        /// </summary>
        /// <param name="swapChain">Pointer to IDXGISwapChain.</param>
        void SetSwapChain(IntPtr swapChain);
    }

    /// <summary>
    /// WinUI 3 video preview control backend using DirectX 11 and <c>SwapChainPanel</c>.
    /// Implements <see cref="IVideoView"/> for zero-copy composition with
    /// <see cref="MediaPlayer.AttachPreview"/> and <see cref="VideoMixer.AttachPreview"/>.
    /// </summary>
    public class WinUIVideoView : IVideoView, IDisposable
    {
        private IntPtr _sharedTextureHandle;
        private IntPtr _sharedTextureHandle1;
        private int _width;
        private int _height;
        private bool _isAttached;
        private bool _disposed;
        private ulong _renderedFramesCount;

        private ID3D11Device? _d3d11Device;
        private ID3D11DeviceContext? _d3d11Context;
        private IDXGISwapChain1? _swapChain;
        private ID3D11Texture2D? _backBuffer;
        private ID3D11Texture2D? _sharedTexture;
        private object? _swapChainPanelTarget;

        private readonly object _syncLock = new();

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
        /// Gets the native DXGI swap chain instance.
        /// </summary>
        public IDXGISwapChain1? SwapChain => _swapChain;

        /// <summary>
        /// Gets the total number of frames presented via this view.
        /// </summary>
        public ulong RenderedFramesCount => _renderedFramesCount;

        /// <summary>
        /// Initializes a new instance of the <see cref="WinUIVideoView"/> class.
        /// </summary>
        public WinUIVideoView()
        {
            InitializeD3D11();
        }

        private void InitializeD3D11()
        {
            try
            {
                var result = D3D11.D3D11CreateDevice(
                    adapter: null!,
                    DriverType.Hardware,
                    DeviceCreationFlags.BgraSupport,
                    featureLevels: new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 },
                    out _d3d11Device,
                    out _d3d11Context);

                if (result.Failure || _d3d11Device == null || _d3d11Context == null)
                {
                    // Fallback to WARP software rasterizer
                    D3D11.D3D11CreateDevice(
                        adapter: null!,
                        DriverType.Warp,
                        DeviceCreationFlags.BgraSupport,
                        featureLevels: new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 },
                        out _d3d11Device,
                        out _d3d11Context);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[WinUIVideoView] Direct3D 11 device initialization error: {ex.Message}");
            }
        }

        /// <summary>
        /// Associates this view with a WinUI 3 SwapChainPanel instance via COM native interop.
        /// </summary>
        /// <param name="panel">The WinUI 3 SwapChainPanel object or native COM pointer.</param>
        public void SetSwapChainPanel(object panel)
        {
            lock (_syncLock)
            {
                _swapChainPanelTarget = panel;
                BindSwapChainToPanel();
            }
        }

        private void BindSwapChainToPanel()
        {
            if (_swapChainPanelTarget == null || _swapChain == null)
                return;

            try
            {
                if (_swapChainPanelTarget is ISwapChainPanelNative panelNative)
                {
                    panelNative.SetSwapChain(_swapChain.NativePointer);
                }
                else if (Marshal.IsComObject(_swapChainPanelTarget))
                {
                    var panelPtr = Marshal.GetIUnknownForObject(_swapChainPanelTarget);
                    try
                    {
                        var panelGuid = typeof(ISwapChainPanelNative).GUID;
                        if (Marshal.QueryInterface(panelPtr, ref panelGuid, out var nativeInterfacePtr) == 0)
                        {
                            try
                            {
                                var nativeObj = (ISwapChainPanelNative)Marshal.GetObjectForIUnknown(nativeInterfacePtr);
                                nativeObj.SetSwapChain(_swapChain.NativePointer);
                            }
                            finally
                            {
                                Marshal.Release(nativeInterfacePtr);
                            }
                        }
                    }
                    finally
                    {
                        Marshal.Release(panelPtr);
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[WinUIVideoView] Failed to bind SwapChain to panel: {ex.Message}");
            }
        }

        /// <summary>
        /// Attaches the view to a shared D3D11 texture.
        /// </summary>
        /// <param name="sharedTextureHandle">NT handle to the DXGI shared texture.</param>
        /// <param name="width">Texture width in pixels.</param>
        /// <param name="height">Texture height in pixels.</param>
        public void Attach(IntPtr sharedTextureHandle, int width, int height)
        {
            Attach(sharedTextureHandle, IntPtr.Zero, width, height);
        }

        /// <summary>
        /// Attaches the view to a double-buffered shared D3D11 texture pair.
        /// </summary>
        /// <param name="sharedTextureHandle0">NT handle to the first DXGI shared texture.</param>
        /// <param name="sharedTextureHandle1">NT handle to the second DXGI shared texture.</param>
        /// <param name="width">Texture width in pixels.</param>
        /// <param name="height">Texture height in pixels.</param>
        public void Attach(IntPtr sharedTextureHandle0, IntPtr sharedTextureHandle1, int width, int height)
        {
            lock (_syncLock)
            {
                _sharedTextureHandle = sharedTextureHandle0;
                _sharedTextureHandle1 = sharedTextureHandle1;
                _width = width > 0 ? width : 1920;
                _height = height > 0 ? height : 1080;
                _isAttached = true;

                OpenSharedTextureResources();
                CreateSwapChainForComposition();
                BindSwapChainToPanel();

                Trace.WriteLine($"[WinUIVideoView] Attached shared texture: {_width}x{_height}");
            }
        }

        private void OpenSharedTextureResources()
        {
            _sharedTexture?.Dispose();
            _sharedTexture = null;

            if (_d3d11Device == null || _sharedTextureHandle == IntPtr.Zero)
                return;

            try
            {
                // First attempt NT shared handle (D3D11_RESOURCE_MISC_SHARED_NTHANDLE)
                using var device1 = _d3d11Device.QueryInterfaceOrNull<ID3D11Device1>();
                if (device1 != null)
                {
                    try
                    {
                        _sharedTexture = device1.OpenSharedResource1<ID3D11Texture2D>(_sharedTextureHandle);
                    }
                    catch
                    {
                        _sharedTexture = null;
                    }
                }

                // Fallback to legacy KMT shared resource handle
                if (_sharedTexture == null)
                {
                    _sharedTexture = _d3d11Device.OpenSharedResource<ID3D11Texture2D>(_sharedTextureHandle);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[WinUIVideoView] OpenSharedTextureResources error: {ex.Message}");
            }
        }

        private void CreateSwapChainForComposition()
        {
            if (_d3d11Device == null) return;

            try
            {
                _backBuffer?.Dispose();
                _backBuffer = null;
                _swapChain?.Dispose();
                _swapChain = null;

                using var dxgiDevice = _d3d11Device.QueryInterface<IDXGIDevice>();
                using var adapter = dxgiDevice.GetAdapter();
                using var factory = adapter.GetParent<IDXGIFactory2>();

                var desc = new SwapChainDescription1
                {
                    Width = (uint)_width,
                    Height = (uint)_height,
                    Format = Format.B8G8R8A8_UNorm,
                    Stereo = false,
                    SampleDescription = new SampleDescription(1, 0),
                    BufferUsage = Usage.RenderTargetOutput,
                    BufferCount = 2,
                    Scaling = Scaling.Stretch,
                    SwapEffect = SwapEffect.FlipSequential,
                    AlphaMode = AlphaMode.Premultiplied,
                    Flags = SwapChainFlags.None
                };

                _swapChain = factory.CreateSwapChainForComposition(_d3d11Device, desc);
                _backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[WinUIVideoView] CreateSwapChainForComposition error: {ex.Message}");
            }
        }

        /// <summary>
        /// Copies the shared texture data to the DXGI swap chain backbuffer and presents to WinUI 3.
        /// </summary>
        /// <returns>True if frame presentation succeeded; otherwise false.</returns>
        public bool RenderFrame()
        {
            lock (_syncLock)
            {
                if (!_isAttached || _d3d11Context == null || _swapChain == null)
                    return false;

                try
                {
                    if (_sharedTexture != null && _backBuffer != null)
                    {
                        _d3d11Context.CopyResource(_backBuffer, _sharedTexture);
                    }

                    _swapChain.Present(1, PresentFlags.None);
                    _renderedFramesCount++;
                    return true;
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[WinUIVideoView] RenderFrame exception: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// Detaches the view, releasing the shared texture reference and DXGI swapchain resources.
        /// </summary>
        public void Detach()
        {
            lock (_syncLock)
            {
                _sharedTextureHandle = IntPtr.Zero;
                _sharedTextureHandle1 = IntPtr.Zero;
                _isAttached = false;

                _sharedTexture?.Dispose();
                _sharedTexture = null;

                _backBuffer?.Dispose();
                _backBuffer = null;

                _swapChain?.Dispose();
                _swapChain = null;

                Trace.WriteLine("[WinUIVideoView] Detached.");
            }
        }

        /// <summary>
        /// Notifies the view that the source texture has been resized.
        /// </summary>
        public void Resize(int width, int height)
        {
            lock (_syncLock)
            {
                if (width <= 0 || height <= 0 || (width == _width && height == _height))
                    return;

                _width = width;
                _height = height;

                if (_swapChain != null)
                {
                    _backBuffer?.Dispose();
                    _backBuffer = null;

                    try
                    {
                        _swapChain.ResizeBuffers(2, (uint)_width, (uint)_height, Format.B8G8R8A8_UNorm, SwapChainFlags.None);
                        _backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"[WinUIVideoView] SwapChain ResizeBuffers error: {ex.Message}");
                    }
                }

                Trace.WriteLine($"[WinUIVideoView] Resized to {width}x{height}");
            }
        }

        /// <summary>
        /// Disposes native Direct3D and DXGI resources.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Detach();

            _d3d11Context?.Dispose();
            _d3d11Context = null;

            _d3d11Device?.Dispose();
            _d3d11Device = null;
        }
    }
}
