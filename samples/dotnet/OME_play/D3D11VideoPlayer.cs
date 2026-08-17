using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Interop;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace OME_play
{
    public enum ScaleMode
    {
        AspectRatioFit,
        Stretch
    }

    public class D3D11VideoPlayer : HwndHost
    {
        private const int WS_CHILD = 0x40000000;
        private const int WS_VISIBLE = 0x10000000;
        private const int HOST_ID = 0x00000002;

        private IntPtr _hwndHost = IntPtr.Zero;

        private ID3D11Device1? _d3dDevice;
        private ID3D11DeviceContext? _d3dContext;
        private IDXGISwapChain1? _swapChain;
        private ID3D11RenderTargetView? _renderTargetView;

        // Double Buffering Shared Handles
        private ID3D11Texture2D[]? _sharedTextures;
        private IDXGIKeyedMutex[]? _keyedMutexes;
        private int _currentBufferIndex = 0;

        private uint _videoWidth = 1920;
        private uint _videoHeight = 1080;
        private int _clientWidth = 1280;
        private int _clientHeight = 720;

        private ScaleMode _scaleMode = ScaleMode.AspectRatioFit;
        private readonly object _lockObj = new object();

        // Dedicated High-Performance Background Render Thread
        private Thread? _renderThread;
        private volatile bool _renderRunning = false;

        public ScaleMode CurrentScaleMode => _scaleMode;
        public uint VideoWidth => _videoWidth;
        public uint VideoHeight => _videoHeight;

        public event Action<ScaleMode>? ScaleModeChanged;

        public void SetScaleMode(ScaleMode mode)
        {
            _scaleMode = mode;
            ScaleModeChanged?.Invoke(_scaleMode);
        }

        public void ToggleScaleMode()
        {
            SetScaleMode(_scaleMode == ScaleMode.AspectRatioFit ? ScaleMode.Stretch : ScaleMode.AspectRatioFit);
        }

        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            int w = (int)Math.Max(100, ActualWidth > 0 ? ActualWidth : 1280);
            int h = (int)Math.Max(100, ActualHeight > 0 ? ActualHeight : 720);
            _clientWidth = w;
            _clientHeight = h;

            _hwndHost = CreateWindowEx(
                0, "static", "",
                WS_CHILD | WS_VISIBLE,
                0, 0,
                w, h,
                hwndParent.Handle,
                (IntPtr)HOST_ID,
                IntPtr.Zero,
                0);

            InitializeD3D11();
            StartRenderLoop();

            return new HandleRef(this, _hwndHost);
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            StopRenderLoop();
            CleanupD3D11();
            if (hwnd.Handle != IntPtr.Zero)
            {
                DestroyWindow(hwnd.Handle);
            }
        }

        private void InitializeD3D11()
        {
            try
            {
                D3D11.D3D11CreateDevice(
                    null,
                    DriverType.Hardware,
                    DeviceCreationFlags.BgraSupport,
                    new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 },
                    out ID3D11Device device,
                    out ID3D11DeviceContext context).CheckError();

                _d3dDevice = device.QueryInterface<ID3D11Device1>();
                _d3dContext = context;

                using var dxgiDevice = _d3dDevice.QueryInterface<IDXGIDevice2>();
                using var dxgiAdapter = dxgiDevice.GetAdapter();
                using var dxgiFactory = dxgiAdapter.GetParent<IDXGIFactory2>();

                var swapChainDesc = new SwapChainDescription1
                {
                    Width = _videoWidth,
                    Height = _videoHeight,
                    Format = Format.B8G8R8A8_UNorm,
                    BufferCount = 2,
                    BufferUsage = Usage.RenderTargetOutput,
                    SampleDescription = new SampleDescription(1, 0),
                    Scaling = Scaling.Stretch,
                    SwapEffect = SwapEffect.FlipDiscard,
                    AlphaMode = AlphaMode.Ignore,
                };

                _swapChain = dxgiFactory.CreateSwapChainForHwnd(_d3dDevice, _hwndHost, swapChainDesc);

                CreateRenderTargetView();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[D3D11VideoPlayer] Initialize error: {ex.Message}");
            }
        }

        private void CreateRenderTargetView()
        {
            if (_swapChain == null || _d3dDevice == null) return;

            _renderTargetView?.Dispose();
            using var backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
            _renderTargetView = _d3dDevice.CreateRenderTargetView(backBuffer);
        }

        private void StartRenderLoop()
        {
            if (_renderRunning) return;
            _renderRunning = true;
            _renderThread = new Thread(RenderLoopWorker)
            {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal,
                Name = "D3D11VideoPlayer_RenderLoop"
            };
            _renderThread.Start();
        }

        private void StopRenderLoop()
        {
            _renderRunning = false;
            if (_renderThread != null && _renderThread.IsAlive)
            {
                _renderThread.Join(500);
                _renderThread = null;
            }
        }

        private void CleanupD3D11()
        {
            lock (_lockObj)
            {
                if (_sharedTextures != null)
                {
                    foreach (var tex in _sharedTextures) tex?.Dispose();
                    _sharedTextures = null;
                }
                if (_keyedMutexes != null)
                {
                    foreach (var mutex in _keyedMutexes) mutex?.Dispose();
                    _keyedMutexes = null;
                }

                _renderTargetView?.Dispose();
                _renderTargetView = null;
                _swapChain?.Dispose();
                _swapChain = null;
                _d3dContext?.Dispose();
                _d3dContext = null;
                _d3dDevice?.Dispose();
                _d3dDevice = null;
            }
        }

        public void SetSharedHandles(ulong handle0, ulong handle1, uint width = 1920, uint height = 1080)
        {
            lock (_lockObj)
            {
                if (_d3dDevice == null) return;

                _videoWidth = width > 0 ? width : 1920;
                _videoHeight = height > 0 ? height : 1080;

                // Adjust swapchain buffer size to match video dimensions
                if (_swapChain != null)
                {
                    _renderTargetView?.Dispose();
                    _renderTargetView = null;
                    try
                    {
                        _swapChain.ResizeBuffers(2, _videoWidth, _videoHeight, Format.B8G8R8A8_UNorm, SwapChainFlags.None);
                        CreateRenderTargetView();
                    }
                    catch { }
                }

                // Cleanup previous shared textures
                if (_sharedTextures != null)
                {
                    foreach (var tex in _sharedTextures) tex?.Dispose();
                }

                _sharedTextures = new ID3D11Texture2D[2];
                _sharedTextures[0] = OpenSharedTextureSafe(handle0);
                _sharedTextures[1] = OpenSharedTextureSafe(handle1);
                _currentBufferIndex = 0;
            }

            StartRenderLoop();
        }

        private ID3D11Texture2D OpenSharedTextureSafe(ulong handle)
        {
            if (_d3dDevice == null) throw new InvalidOperationException("D3D11 Device not initialized");

            IntPtr handlePtr = (IntPtr)handle;
            try
            {
                // Try legacy KMT shared resource first (from IDXGIResource::GetSharedHandle)
                return _d3dDevice.OpenSharedResource<ID3D11Texture2D>(handlePtr);
            }
            catch
            {
                // Fallback to NT handle (from IDXGIResource1::CreateSharedHandle)
                return _d3dDevice.OpenSharedResource1<ID3D11Texture2D>(handlePtr);
            }
        }

        protected override void OnRenderSizeChanged(System.Windows.SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);

            int newW = (int)sizeInfo.NewSize.Width;
            int newH = (int)sizeInfo.NewSize.Height;
            if (newW <= 0 || newH <= 0) return;

            _clientWidth = newW;
            _clientHeight = newH;
        }

        private void RenderLoopWorker()
        {
            while (_renderRunning)
            {
                bool frameRendered = false;

                if (_d3dContext != null && _swapChain != null && _sharedTextures != null)
                {
                    lock (_lockObj)
                    {
                        var sharedTex = _sharedTextures?[_currentBufferIndex];
                        if (sharedTex != null)
                        {
                            try
                            {
                                using var backBuffer = _swapChain?.GetBuffer<ID3D11Texture2D>(0);
                                if (backBuffer != null)
                                {
                                    _d3dContext?.CopyResource(backBuffer, sharedTex);
                                    _swapChain?.Present(1, PresentFlags.None);
                                    _currentBufferIndex = (_currentBufferIndex + 1) % 2;
                                    frameRendered = true;
                                }
                            }
                            catch
                            {
                            }
                        }
                    }
                }

                if (!frameRendered)
                {
                    Thread.Sleep(2);
                }
                else
                {
                    Thread.Sleep(10);
                }
            }
        }

        // P/Invoke for Win32 child window
        [DllImport("user32.dll", EntryPoint = "CreateWindowEx", CharSet = CharSet.Unicode)]
        internal static extern IntPtr CreateWindowEx(
            int dwExStyle,
            string lpszClassName,
            string lpszWindowName,
            int style,
            int x, int y,
            int width, int height,
            IntPtr hwndParent,
            IntPtr hMenu,
            IntPtr hInst,
            IntPtr pvParam);

        [DllImport("user32.dll", EntryPoint = "DestroyWindow", CharSet = CharSet.Unicode)]
        internal static extern bool DestroyWindow(IntPtr hwnd);
    }
}
