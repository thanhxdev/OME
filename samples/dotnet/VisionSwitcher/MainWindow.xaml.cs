using System.Windows;
using OpenMedia.Platform;
using OpenMedia.Platform.Extensions;

namespace VisionSwitcher
{
    public partial class MainWindow : Window
    {
        private VideoMixer? _mixer;
        private MediaPlayer? _cam2Source;
        private int _cam1Index;
        private int _cam2Index;

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

            // Create Mixer
            _mixer = new VideoMixer(1920, 1080, 60.0);
            _mixer.AttachPreview(ProgramView);

            // Add Camera 1 (Webcam)
            _cam1Index = _mixer.AddDevice("Webcam");

            // Add Camera 2 (Video File acting as live feed)
            _cam2Source = new MediaPlayer("test_video.mp4");
            _cam2Source.AttachPreview(Cam2View);
            await _cam2Source.PlayAsync();
            
            _cam2Index = _mixer.AddSource(_cam2Source);

            // Initially switch to Cam 1
            await _mixer.SwitchToAsync(_cam1Index);
        }

        private async void BtnCutTo1_Click(object sender, RoutedEventArgs e)
        {
            if (_mixer != null)
                await _mixer.SwitchToAsync(_cam1Index);
        }

        private async void BtnCutTo2_Click(object sender, RoutedEventArgs e)
        {
            if (_mixer != null)
                await _mixer.SwitchToAsync(_cam2Index);
        }

        private void BtnAddOverlay_Click(object sender, RoutedEventArgs e)
        {
            if (_mixer != null)
            {
                _mixer.Overlay().Clear();
                _mixer.Overlay().AddText("LIVE - VISION SWITCHER").AtTopLeft().WithColor("#FFFF0000").WithFont("Arial", 36);
                _mixer.Overlay().AddClock().AtBottomRight();
            }
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            _cam2Source?.Dispose();
            _mixer?.Dispose();
            OpenMediaRuntime.Shutdown();
        }
    }
}