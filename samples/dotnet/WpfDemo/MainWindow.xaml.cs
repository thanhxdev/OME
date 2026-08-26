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

using OpenMedia.SDK;
using System.Diagnostics;

namespace WpfDemo;

public partial class MainWindow : Window
{
    private Pipeline? _pipeline;
    private MediaEncoder? _encoder;
    private RTMPOutput? _output;
    private OpenMedia.SDK.IPCClient? _ipcClient;

    public MainWindow()
    {
        InitializeComponent();
        
        try
        {
            Engine.Initialize("{}");
            Log("Engine initialized successfully.");
        }
        catch (Exception ex)
        {
            Log($"Failed to initialize Engine: {ex.Message}");
        }
    }

    private async void BtnStart_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Local pipeline disabled for PoC
            /*
            _pipeline = new Pipeline();
            Log("Pipeline created.");

            var encoderType = RbNvenc.IsChecked == true ? EncoderType.NVENC : EncoderType.QuickSync;
            _encoder = new MediaEncoder(encoderType);
            _encoder.Initialize();
            Log($"{encoderType} Encoder initialized.");

            _output = new RTMPOutput();
            _output.Open("rtmp://localhost/live/test");
            Log("RTMP Output initialized.");

            _pipeline.Start();
            Log("Pipeline started.");
            */

            // Start IPC connection to get shared texture handles
            _ipcClient = new OpenMedia.SDK.IPCClient();
            if (await _ipcClient.ConnectAsync("OpenMediaIPC", 5000))
            {
                Log("IPC connected, requesting shared textures...");
                var payload = await _ipcClient.RequestSharedTextureAsync();
                if (payload != null)
                {
                    Log($"Received NT Handles: {payload.Value.NtHandle0:X}, {payload.Value.NtHandle1:X}");
                    VideoPlayer.SetSharedHandles(payload.Value.NtHandle0, payload.Value.NtHandle1);
                }
                else
                {
                    Log("Failed to get shared texture payload.");
                }
            }
            else
            {
                Log("Failed to connect to IPC.");
            }

            BtnStart.IsEnabled = false;
            BtnStop.IsEnabled = true;
        }
        catch (Exception ex)
        {
            Log($"Error starting pipeline: {ex.Message}");
        }
    }

    private void BtnStop_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            /*
            _pipeline?.Stop();
            _pipeline?.Dispose();
            _pipeline = null;

            _encoder?.Dispose();
            _encoder = null;

            _output?.Dispose();
            _output = null;
            */

            _ipcClient?.Dispose();
            _ipcClient = null;

            Log("Pipeline stopped and resources released.");

            BtnStart.IsEnabled = true;
            BtnStop.IsEnabled = false;
        }
        catch (Exception ex)
        {
            Log($"Error stopping pipeline: {ex.Message}");
        }
    }

    private void Log(string message)
    {
        TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
        TxtLog.ScrollToEnd();
    }

    protected override void OnClosed(EventArgs e)
    {
        BtnStop_Click(this, new RoutedEventArgs());
        Engine.Shutdown();
        base.OnClosed(e);
    }
}