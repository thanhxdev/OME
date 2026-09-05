using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace OpenMedia.Platform.Controls.Wpf
{
    /// <summary>
    /// Manages D3D11 shared texture reception and rendering into a WPF <see cref="WriteableBitmap"/>.
    /// Maps D3D11 staging memory directly into WPF UI Image bitmap backbuffer for zero-copy 60 FPS rendering.
    /// </summary>
    internal sealed class WpfD3D11Renderer : IDisposable
    {
        private ID3D11Device? _d3d11Device;
        private ID3D11DeviceContext? _d3d11Context;
        private ID3D11Texture2D?[] _sharedTextures = new ID3D11Texture2D?[2];
        private int _currentBufferIndex = 0;
        private ID3D11Texture2D? _stagingTexture;

        private WriteableBitmap? _bitmap;
        private int _width;
        private int _height;
        private bool _disposed;

        /// <summary>
        /// The WPF <see cref="WriteableBitmap"/> that receives rendered frames.
        /// </summary>
        internal WriteableBitmap? Bitmap => _bitmap;

        /// <summary>
        /// Initializes the D3D11 device.
        /// Must be called on the UI thread.
        /// </summary>
        internal bool Initialize()
        {
            try
            {
                // Create D3D11 device (Hardware first, fallback to WARP if needed)
                var result = D3D11.D3D11CreateDevice(
                    adapter: null!,
                    DriverType.Hardware,
                    DeviceCreationFlags.BgraSupport,
                    featureLevels: null,
                    out _d3d11Device,
                    out _d3d11Context);

                if (result.Failure || _d3d11Device == null || _d3d11Context == null)
                {
                    D3D11.D3D11CreateDevice(
                        adapter: null!,
                        DriverType.Warp,
                        DeviceCreationFlags.BgraSupport,
                        featureLevels: null,
                        out _d3d11Device,
                        out _d3d11Context);
                }

                return _d3d11Device != null && _d3d11Context != null;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[WpfD3D11Renderer] Initialize failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Opens a single DXGI shared texture by its NT handle and allocates WPF WriteableBitmap.
        /// </summary>
        internal bool OpenSharedTexture(IntPtr ntHandle, int width, int height)
        {
            return OpenSharedTextures(ntHandle, IntPtr.Zero, width, height);
        }

        /// <summary>
        /// Opens a double-buffered DXGI shared texture pair by NT handles and allocates WPF WriteableBitmap.
        /// </summary>
        internal bool OpenSharedTextures(IntPtr ntHandle0, IntPtr ntHandle1, int width, int height)
        {
            if (_d3d11Device == null) return false;

            _stagingTexture?.Dispose();
            _stagingTexture = null;

            if (_sharedTextures != null)
            {
                for (int i = 0; i < _sharedTextures.Length; i++)
                {
                    _sharedTextures[i]?.Dispose();
                    _sharedTextures[i] = null;
                }
            }
            _sharedTextures = new ID3D11Texture2D?[2];
            _currentBufferIndex = 0;
            _bitmap = null;

            _width = width;
            _height = height;

            try
            {
                _sharedTextures[0] = OpenTextureResource(ntHandle0);
                if (_sharedTextures[0] == null) return false;

                if (ntHandle1 != IntPtr.Zero)
                {
                    _sharedTextures[1] = OpenTextureResource(ntHandle1);
                }

                // Create a staging texture for CPU readback (D3D11 → WriteableBitmap bridge)
                var stagingDesc = new Texture2DDescription
                {
                    Width = (uint)width,
                    Height = (uint)height,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Vortice.DXGI.Format.B8G8R8A8_UNorm,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Staging,
                    BindFlags = BindFlags.None,
                    CPUAccessFlags = CpuAccessFlags.Read
                };
                _stagingTexture = _d3d11Device.CreateTexture2D(stagingDesc);

                _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);

                Trace.WriteLine($"[WpfD3D11Renderer] Shared texture opened: {width}x{height} (Double-buffered: {_sharedTextures[1] != null})");
                return true;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[WpfD3D11Renderer] OpenSharedTextures failed: {ex.Message}");
                return false;
            }
        }

        private ID3D11Texture2D? OpenTextureResource(IntPtr ntHandle)
        {
            if (_d3d11Device == null || ntHandle == IntPtr.Zero)
            {
                Console.WriteLine($"[DEBUG-RENDERER] OpenTextureResource: device={_d3d11Device != null}, handle=0x{ntHandle:X}");
                return null;
            }

            try
            {
                // Try opening as legacy KMT shared resource handle (D3D11_RESOURCE_MISC_SHARED)
                var tex = _d3d11Device.OpenSharedResource<ID3D11Texture2D>(ntHandle);
                Console.WriteLine($"[DEBUG-RENDERER] OpenSharedResource (KMT) returned: {tex != null}");
                return tex;
            }
            catch (Exception ex1)
            {
                Console.WriteLine($"[DEBUG-RENDERER] OpenSharedResource (KMT) threw: {ex1.Message}");
                try
                {
                    // Fall back to NT shared resource handle (D3D11_RESOURCE_MISC_SHARED_NTHANDLE)
                    using var device1 = _d3d11Device.QueryInterface<ID3D11Device1>();
                    var tex1 = device1.OpenSharedResource1<ID3D11Texture2D>(ntHandle);
                    Console.WriteLine($"[DEBUG-RENDERER] OpenSharedResource1 (NT) returned: {tex1 != null}");
                    return tex1;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DEBUG-RENDERER] OpenTextureResource failed: {ex.Message}");
                    return null;
                }
            }
        }

        /// <summary>
        /// Renders the current frame from the shared texture into the WriteableBitmap.
        /// Must be called on the WPF Dispatcher thread.
        /// </summary>
        internal bool RenderFrame()
        {
            var textureToRead = _sharedTextures?[0];
            if (_bitmap == null || textureToRead == null || _d3d11Context == null || _stagingTexture == null)
                return false;

            try
            {
                // Copy shared texture → staging texture
                _d3d11Context.CopyResource(_stagingTexture, textureToRead);

                var mapped = _d3d11Context.Map(_stagingTexture, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                if (mapped.DataPointer == IntPtr.Zero) return false;

                _bitmap.Lock();

                int sourcePitch = (int)mapped.RowPitch;
                int destPitch = _bitmap.BackBufferStride;
                IntPtr pSource = mapped.DataPointer;
                IntPtr pDest = _bitmap.BackBuffer;

                if (sourcePitch == destPitch)
                {
                    CopyMemory(pDest, pSource, (uint)(destPitch * _height));
                }
                else
                {
                    int bytesToCopy = Math.Min(sourcePitch, destPitch);
                    for (int y = 0; y < _height; y++)
                    {
                        CopyMemory(IntPtr.Add(pDest, y * destPitch), IntPtr.Add(pSource, y * sourcePitch), (uint)bytesToCopy);
                    }
                }

                _bitmap.AddDirtyRect(new Int32Rect(0, 0, _width, _height));
                _bitmap.Unlock();

                _d3d11Context.Unmap(_stagingTexture, 0);

                return true;
            }
            catch
            {
                return false;
            }
        }

        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory", SetLastError = false)]
        private static extern void CopyMemory(IntPtr dest, IntPtr src, uint count);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _stagingTexture?.Dispose();
            if (_sharedTextures != null)
            {
                for (int i = 0; i < _sharedTextures.Length; i++)
                {
                    _sharedTextures[i]?.Dispose();
                    _sharedTextures[i] = null;
                }
            }
            _d3d11Context?.Dispose();
            _d3d11Device?.Dispose();
        }
    }
}
