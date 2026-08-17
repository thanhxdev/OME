using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using OpenMedia.Core.NET;
using OpenMedia.SDK;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Interop;

namespace SimplePlayer;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private SDKPipeline _pipeline;
    private SDKSource _source;

    private ManagedPluginLoader _pluginLoader;
    private IPCClient _ipcClient;
    private D3D11Interop _d3dInterop;

    public MainWindow()
    {
        InitializeComponent();
        this.Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try 
        {
            // 0. Load Managed Plugins
            _pluginLoader = new ManagedPluginLoader();
            string pluginDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins");
            _pluginLoader.LoadDirectory(pluginDir);

            // 1. Initialize Engine (Launch OpenMediaServer.exe if needed)
            bool engineReady = await SDKEngine.Instance.InitializeAsync();
            if (!engineReady)
            {
                throw new OpenMediaException("Failed to connect to OpenMediaServer via IPC.");
            }
            Debug.WriteLine("OpenMedia Engine (IPC) Initialized.");

            // 2. Create Media Objects via IPC
            _pipeline = await SDKPipeline.CreateAsync("MainPipeline");
            _source = await SDKSource.CreateAsync(_pipeline, 1, "test_video.mp4");

            // 4. Start Processing
            await _pipeline.StartAsync();
            Debug.WriteLine("Pipeline Started Successfully via IPC.");

            // 5. Connect to IPC and request D3D11 Shared Texture
            await SetupSharedTextureIPC();
        }
        catch (OpenMediaException ex)
        {
            MessageBox.Show($"Lỗi từ OpenMedia SDK: {ex.Message}", "Lỗi SDK", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi hệ thống: {ex.Message}", "Lỗi Hệ Thống", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task SetupSharedTextureIPC()
    {
        _d3dInterop = new D3D11Interop();
        
        // We use the existing IPC connection from SDKEngine
        _ipcClient = SDKEngine.Instance.IPC;


        var payload = await _ipcClient.RequestSharedTextureAsync();
        if (payload.HasValue)
        {
            IntPtr ntHandle = (IntPtr)payload.Value.NtHandle;
            int width = (int)payload.Value.Width;
            int height = (int)payload.Value.Height;

            if (_d3dInterop.OpenSharedTexture(ntHandle, width, height))
            {
                // Setup WPF D3DImage
                D3DTarget.Lock();
                D3DTarget.SetBackBuffer(D3DResourceType.IDirect3DSurface9, _d3dInterop.D3D9SurfacePtr);
                D3DTarget.Unlock();

                // Hook into WPF rendering loop
                CompositionTarget.Rendering += CompositionTarget_Rendering;
                Debug.WriteLine("D3D11 Shared Texture connected successfully.");
            }
        }
    }

    private void CompositionTarget_Rendering(object sender, EventArgs e)
    {
        if (_d3dInterop == null) return;

        // Perform GPU copy under lock
        if (_d3dInterop.RenderFrame(50))
        {
            if (D3DTarget.IsFrontBufferAvailable)
            {
                D3DTarget.Lock();
                // Invalidate the whole surface
                D3DTarget.AddDirtyRect(new Int32Rect(0, 0, D3DTarget.PixelWidth, D3DTarget.PixelHeight));
                D3DTarget.Unlock();
            }
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        CompositionTarget.Rendering -= CompositionTarget_Rendering;

        _pipeline?.StopAsync().Wait();
        
        SDKEngine.Instance.Shutdown();
        _pluginLoader?.ShutdownAll();
        
        base.OnClosed(e);
    }
}