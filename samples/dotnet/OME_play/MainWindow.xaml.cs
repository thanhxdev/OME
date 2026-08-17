using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using OpenMedia.SDK;

namespace OME_play
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private Process? _serverProcess;
        private SDKPipeline? _pipeline;
        private SDKSource? _source;
        private IPCClient? _ipcClient;
        private bool _isPlaying = false;
        private readonly string _pipeName;

        public MainWindow()
        {
            InitializeComponent();
            _pipeName = $"OpenMediaSDK_{Process.GetCurrentProcess().Id}";
            
            // Listen to ScaleMode changes
            VideoPlayer.ScaleModeChanged += OnScaleModeChanged;
            
            // Register Keyboard shortcuts
            this.KeyDown += MainWindow_KeyDown;

            Log("Ready. Select video file and click Play to start Server pipeline.");
        }

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.S)
            {
                BtnScaleMode_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.Space && !TxtVideoPath.IsFocused)
            {
                if (_isPlaying)
                {
                    BtnStop_Click(this, new RoutedEventArgs());
                }
                else
                {
                    BtnPlay_Click(this, new RoutedEventArgs());
                }
                e.Handled = true;
            }
        }

        private void OnScaleModeChanged(ScaleMode mode)
        {
            LblScaleMode.Text = mode.ToString();
            BtnScaleMode.Content = mode == ScaleMode.AspectRatioFit 
                ? "📐 Mode: AspectFit [S]" 
                : "📐 Mode: Stretch [S]";
            Log($"Scale Mode changed to: {mode}");
            UpdateVideoPlayerLayout();
        }

        private void VideoContainer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateVideoPlayerLayout();
        }

        private void UpdateVideoPlayerLayout()
        {
            if (VideoPlayer.CurrentScaleMode == ScaleMode.Stretch)
            {
                VideoPlayer.Width = double.NaN;
                VideoPlayer.Height = double.NaN;
            }
            else
            {
                // AspectRatioFit
                if (VideoPlayer.VideoWidth > 0 && VideoPlayer.VideoHeight > 0)
                {
                    double containerWidth = VideoContainer.ActualWidth;
                    double containerHeight = VideoContainer.ActualHeight;
                    if (containerWidth == 0 || containerHeight == 0) return;

                    double videoAspect = (double)VideoPlayer.VideoWidth / VideoPlayer.VideoHeight;
                    double containerAspect = containerWidth / containerHeight;

                    if (videoAspect > containerAspect)
                    {
                        // Video is wider than container, fit to width
                        VideoPlayer.Width = containerWidth;
                        VideoPlayer.Height = containerWidth / videoAspect;
                    }
                    else
                    {
                        // Video is taller than container, fit to height
                        VideoPlayer.Height = containerHeight;
                        VideoPlayer.Width = containerHeight * videoAspect;
                    }
                }
            }
        }

        private void BtnScaleMode_Click(object sender, RoutedEventArgs e)
        {
            VideoPlayer.ToggleScaleMode();
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "Select Video File",
                Filter = "Video Files (*.mp4;*.mkv;*.mov;*.avi;*.ts)|*.mp4;*.mkv;*.mov;*.avi;*.ts|All Files (*.*)|*.*"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                TxtVideoPath.Text = openFileDialog.FileName;
                Log($"Selected video file: {openFileDialog.FileName}");
            }
        }

        private async void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            string videoPath = TxtVideoPath.Text.Trim();
            if (string.IsNullOrEmpty(videoPath))
            {
                MessageBox.Show("Vui lòng chọn đường dẫn tệp tin video hợp lệ.", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!File.Exists(videoPath))
            {
                Log($"[ERROR] File not found: {videoPath}");
                MessageBox.Show($"Tệp tin video không tồn tại trên ổ đĩa:\n\n{videoPath}\n\nVui lòng nhấn nút '📁 Browse...' để chọn file video thực tế trên máy tính (.mp4, .mkv, .mov...).", "Không Tìm Thấy Tệp", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                BtnPlay.IsEnabled = false;
                BtnBrowse.IsEnabled = false;

                Log("Starting playback sequence...");

                // 1. Launch OpenMediaServer if not running
                await EnsureServerRunningAsync();

                // 2. Initialize Engine / IPC Client via SDKEngine.Instance
                Log("Connecting and handshaking with OpenMediaServer via SDKEngine...");
                bool connected = await SDKEngine.Instance.InitializeAsync(_pipeName);
                if (!connected)
                {
                    throw new Exception("Could not initialize SDKEngine or connect to OpenMediaServer via Named Pipe (OpenMediaSDK). Make sure OpenMediaServer.exe is compiled and running.");
                }

                _ipcClient = SDKEngine.Instance.IPC;
                Log("Connected to OpenMediaServer IPC successfully.");
                LblServerStatus.Text = "Connected";
                LblServerStatus.Foreground = System.Windows.Media.Brushes.LightGreen;

                // 3. Create Pipeline Graph
                // Resolution/FPS passed here are hints; the actual shared texture
                // dimensions are determined by the server from the opened video file
                Log("Creating Pipeline on Server...");
                _pipeline = await SDKPipeline.CreateAsync("VideoPlayerPipeline");
                Log($"Pipeline created successfully (ID: {_pipeline.Id}).");

                // 4. Open Media Source File
                Log($"Opening media source on Server: {videoPath}");
                _source = await SDKSource.CreateAsync(_pipeline, 1, videoPath);
                Log("Media source loaded.");

                // 5. Start Pipeline Processing
                Log("Starting Pipeline stream...");
                bool started = await _pipeline.StartAsync();
                if (!started)
                {
                    throw new Exception("Server rejected StartPipeline command.");
                }

                LblPipelineStatus.Text = "Running";
                LblPipelineStatus.Foreground = System.Windows.Media.Brushes.LightGreen;
                _isPlaying = true;
                OverlayPlaceholder.Visibility = Visibility.Collapsed;

                // 6. Request Shared DXGI Textures (NT Handles)
                Log("Requesting GPU Shared Textures from Server...");
                var payload = await _ipcClient.RequestSharedTextureAsync();
                if (payload != null && payload.Value.NtHandle0 != 0)
                {
                    ulong h0 = payload.Value.NtHandle0;
                    ulong h1 = payload.Value.NtHandle1;
                    uint width = payload.Value.Width;
                    uint height = payload.Value.Height;

                    Log($"Received Shared Textures from GPU: {width}x{height}, Buffers: {payload.Value.BufferCount}");
                    Log($"NT Handles: [Handle0: 0x{h0:X}, Handle1: 0x{h1:X}]");

                    LblNtHandles.Text = $"0x{h0:X} / {width}x{height}";

                    // Assign to D3D11 Video Player
                    VideoPlayer.SetSharedHandles(h0, h1, width, height);
                    Log("Direct3D 11 Zero-Copy presentation loop active.");
                    UpdateVideoPlayerLayout();
                }
                else
                {
                    Log("Warning: Could not retrieve NT Handles from Server.");
                }

                BtnStop.IsEnabled = true;
            }
            catch (Exception ex)
            {
                Log($"[ERROR] Playback failed: {ex.Message}");
                MessageBox.Show($"Lỗi khi phát video: {ex.Message}", "Lỗi Phát Video", MessageBoxButton.OK, MessageBoxImage.Error);
                await StopPipelineInternalAsync();
                BtnPlay.IsEnabled = true;
                BtnBrowse.IsEnabled = true;
            }
        }

        private async void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            await StopPipelineInternalAsync();
        }

        private async Task StopPipelineInternalAsync()
        {
            try
            {
                Log("Stopping pipeline...");
                if (_pipeline != null)
                {
                    try
                    {
                        await _pipeline.StopAsync();
                    }
                    catch { }
                    _pipeline = null;
                }

                _source = null;
                _isPlaying = false;

                LblPipelineStatus.Text = "Stopped";
                LblPipelineStatus.Foreground = System.Windows.Media.Brushes.Orange;
                LblNtHandles.Text = "None";
                OverlayPlaceholder.Visibility = Visibility.Visible;

                Log("Pipeline stopped.");
            }
            catch (Exception ex)
            {
                Log($"Error during stop: {ex.Message}");
            }
            finally
            {
                BtnPlay.IsEnabled = true;
                BtnBrowse.IsEnabled = true;
                BtnStop.IsEnabled = false;
            }
        }

        private async Task EnsureServerRunningAsync()
        {
            // Find OpenMediaServer.exe
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] possiblePaths = new[]
            {
                Path.Combine(baseDir, "OpenMediaServer.exe"),
                Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\..\build-demo\bin\Debug\OpenMediaServer.exe")),
                Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\..\build\bin\Debug\OpenMediaServer.exe")),
                Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\..\build-production\bin\Release\OpenMediaServer.exe")),
                @"c:\Users\ASUS NUC\Desktop\Code\OME\build-demo\bin\Debug\OpenMediaServer.exe"
            };

            string? serverExe = null;
            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    serverExe = path;
                    break;
                }
            }

            if (serverExe == null)
            {
                Log("Warning: OpenMediaServer.exe not found on disk. Please ensure OpenMediaServer is built.");
                return;
            }

            Log($"Launching OpenMediaServer: {serverExe}");
            _serverProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = serverExe,
                    Arguments = $"--pipe-name \\\\.\\pipe\\{_pipeName}",
                    WorkingDirectory = Path.GetDirectoryName(serverExe),
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            _serverProcess.Start();
            Log($"OpenMediaServer started with PID: {_serverProcess.Id}");

            // Give server 1 second to start named pipe
            await Task.Delay(1000);
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            TxtLog.Clear();
        }

        private void Log(string message)
        {
            Dispatcher.Invoke(() =>
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                TxtLog.AppendText($"[{timestamp}] {message}\n");
                TxtLog.ScrollToEnd();
            });
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                StopPipelineInternalAsync().Wait(2000);
                SDKEngine.Instance.Shutdown();
            }
            catch { }

            if (_serverProcess != null && !_serverProcess.HasExited)
            {
                try
                {
                    _serverProcess.Kill();
                    _serverProcess.Dispose();
                }
                catch { }
            }

            base.OnClosed(e);
        }
    }
}
