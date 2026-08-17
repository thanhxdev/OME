using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace WpfDemo
{
    public class D3D11VideoPlayer : HwndHost
    {
        private const int WS_CHILD = 0x40000000;
        private const int WS_VISIBLE = 0x10000000;
        private const int LBS_NOTIFY = 0x00000001;
        private const int HOST_ID = 0x00000002;
        private const int LISTBOX_ID = 0x00000001;
        private const int WS_VSCROLL = 0x00200000;
        private const int WS_BORDER = 0x00800000;

        private IntPtr hwndHost;

        private ID3D11Device1? d3dDevice;
        private ID3D11DeviceContext? d3dContext;
        private IDXGISwapChain1? swapChain;
        private ID3D11RenderTargetView? renderTargetView;

        // Double Buffering
        private ID3D11Texture2D[]? sharedTextures;
        private IDXGIKeyedMutex[]? keyedMutexes;
        
        private int currentBufferIndex = 0;

        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            hwndHost = CreateWindowEx(
                0, "static", "",
                WS_CHILD | WS_VISIBLE,
                0, 0,
                (int)Width, (int)Height,
                hwndParent.Handle,
                (IntPtr)HOST_ID,
                IntPtr.Zero,
                0);

            InitializeD3D11();

            // Hook up rendering loop
            System.Windows.Media.CompositionTarget.Rendering += OnRendering;

            return new HandleRef(this, hwndHost);
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            System.Windows.Media.CompositionTarget.Rendering -= OnRendering;
            
            CleanupD3D11();

            DestroyWindow(hwnd.Handle);
        }

        private void InitializeD3D11()
        {
            D3D11.D3D11CreateDevice(
                null,
                Vortice.Direct3D.DriverType.Hardware,
                DeviceCreationFlags.BgraSupport,
                new[] { Vortice.Direct3D.FeatureLevel.Level_11_1, Vortice.Direct3D.FeatureLevel.Level_11_0 },
                out ID3D11Device device,
                out ID3D11DeviceContext context).CheckError();

            d3dDevice = device.QueryInterface<ID3D11Device1>();
            d3dContext = context;

            using var dxgiDevice = d3dDevice.QueryInterface<IDXGIDevice2>();
            using var dxgiAdapter = dxgiDevice.GetAdapter();
            using var dxgiFactory = dxgiAdapter.GetParent<IDXGIFactory2>();

            var swapChainDesc = new SwapChainDescription1
            {
                Width = 1920,
                Height = 1080,
                Format = Format.B8G8R8A8_UNorm,
                BufferCount = 2,
                BufferUsage = Usage.RenderTargetOutput,
                SampleDescription = new SampleDescription(1, 0),
                Scaling = Scaling.Stretch,
                SwapEffect = SwapEffect.FlipDiscard,
                AlphaMode = AlphaMode.Ignore,
            };

            swapChain = dxgiFactory.CreateSwapChainForHwnd(d3dDevice, hwndHost, swapChainDesc);

            using var backBuffer = swapChain.GetBuffer<ID3D11Texture2D>(0);
            renderTargetView = d3dDevice.CreateRenderTargetView(backBuffer);
        }

        private void CleanupD3D11()
        {
            if (sharedTextures != null)
            {
                foreach (var tex in sharedTextures) tex?.Dispose();
                sharedTextures = null;
            }
            if (keyedMutexes != null)
            {
                foreach (var mutex in keyedMutexes) mutex?.Dispose();
                keyedMutexes = null;
            }

            renderTargetView?.Dispose();
            swapChain?.Dispose();
            d3dContext?.Dispose();
            d3dDevice?.Dispose();
        }

        public void SetSharedHandles(ulong handle0, ulong handle1)
        {
            if (d3dDevice == null) return;

            // Cleanup previous if any
            if (sharedTextures != null)
            {
                foreach (var tex in sharedTextures) tex?.Dispose();
            }
            if (keyedMutexes != null)
            {
                foreach (var mutex in keyedMutexes) mutex?.Dispose();
            }

            sharedTextures = new ID3D11Texture2D[2];
            keyedMutexes = new IDXGIKeyedMutex[2];

            // Open NT handles
            sharedTextures[0] = d3dDevice.OpenSharedResource1<ID3D11Texture2D>((IntPtr)handle0);
            keyedMutexes[0] = sharedTextures[0].QueryInterface<IDXGIKeyedMutex>();

            sharedTextures[1] = d3dDevice.OpenSharedResource1<ID3D11Texture2D>((IntPtr)handle1);
            keyedMutexes[1] = sharedTextures[1].QueryInterface<IDXGIKeyedMutex>();
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            if (d3dContext == null || swapChain == null || renderTargetView == null) return;
            if (sharedTextures == null || keyedMutexes == null) return;

            // Try to acquire the current buffer lock (Key = 1, meaning Server has released it for reading)
            var mutex = keyedMutexes[currentBufferIndex];
            if (mutex == null) return;

            // Use 0 timeout - if server hasn't produced frame yet, we just skip drawing
            try
            {
                mutex.AcquireSync(1, 0);

                var tex = sharedTextures[currentBufferIndex];
                
                // Draw/Copy the shared texture to the backbuffer
                using var backBuffer = swapChain.GetBuffer<ID3D11Texture2D>(0);
                
                // Assuming same size/format, we can just CopyResource
                d3dContext.CopyResource(backBuffer, tex);
                
                // Release with Key = 0 so Server can write again
                mutex.ReleaseSync(0);

                // Present
                swapChain.Present(0, PresentFlags.None);

                // Toggle buffer index
                currentBufferIndex = (currentBufferIndex + 1) % 2;
            }
            catch
            {
                // AcquireSync throws if it times out
            }
        }

        // P/Invoke for Win32 window creation
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
