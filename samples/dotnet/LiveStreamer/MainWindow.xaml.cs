using System.Windows;
using OpenMedia.Platform;

namespace LiveStreamer
{
    public partial class MainWindow : Window
    {
        private VideoMixer? _mixer;
        private StreamOutput? _rtmpOutput;
        private int _camIndex;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            bool ready = await OpenMediaRuntime.InitializeAsync();
            if (!ready)
            {
                MessageBox.Show("Could not connect to OpenMediaServer.");
                return;
            }

            _mixer = new VideoMixer();
            _mixer.AttachPreview(VideoView);
            
            _camIndex = _mixer.AddDevice("Webcam");
            await _mixer.SwitchToAsync(_camIndex);
        }

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            if (_mixer == null) return;
            
            try
            {
                _rtmpOutput = StreamOutput.RTMP(TxtRtmpUrl.Text);
                _mixer.AddOutput(_rtmpOutput);
                
                BtnStart.IsEnabled = false;
                BtnStop.IsEnabled = true;
                TxtRtmpUrl.IsEnabled = false;
                TxtStatus.Text = "Streaming (RTMP)";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start stream: {ex.Message}");
            }
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            if (_mixer != null && _rtmpOutput != null)
            {
                _mixer.RemoveOutput(_rtmpOutput);
                _rtmpOutput.Dispose();
                _rtmpOutput = null;
                
                BtnStart.IsEnabled = true;
                BtnStop.IsEnabled = false;
                TxtRtmpUrl.IsEnabled = true;
                TxtStatus.Text = "Stopped";
            }
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            _rtmpOutput?.Dispose();
            _mixer?.Dispose();
            OpenMediaRuntime.Shutdown();
        }
    }
}