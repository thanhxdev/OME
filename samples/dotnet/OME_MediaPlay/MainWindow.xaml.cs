using Microsoft.Win32;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace OME_MediaPlay
{
    public partial class MainWindow : Window
    {
        private OME_MediaPlayController _controller;
        private DispatcherTimer _progressTimer;
        private bool _isPlaying = false;
        private double _totalDurationSeconds = 0;
        private bool _isMuted = false;

        public MainWindow()
        {
            InitializeComponent();
            _controller = new OME_MediaPlayController();
            
            _progressTimer = new DispatcherTimer();
            _progressTimer.Interval = TimeSpan.FromMilliseconds(500);
            _progressTimer.Tick += ProgressTimer_Tick;

            this.Loaded += MainWindow_Loaded;
            this.Closed += MainWindow_Closed;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Auto connect to server on load
            bool connected = await _controller.ConnectServerAsync();
            if (!connected)
            {
                MessageBox.Show("Failed to connect to OpenMediaServer. Ensure SDK Engine and OpenMediaServer are configured correctly.", "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else
            {
                var args = Environment.GetCommandLineArgs();
                if (args.Length > 1)
                {
                    // Skip the executable name, handle spaces in paths if passed as separate args just in case
                    string filePath = args[1];
                    await LoadVideoInternal(filePath);
                }
            }
        }

        private async void MainWindow_Closed(object sender, EventArgs e)
        {
            if (_controller != null)
            {
                await _controller.DisconnectAsync();
            }
        }

        private async void BtnLoad_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Video Files (*.mp4;*.mkv)|*.mp4;*.mkv|All files (*.*)|*.*";
            if (openFileDialog.ShowDialog() == true)
            {
                await LoadVideoInternal(openFileDialog.FileName);
            }
        }

        private async Task LoadVideoInternal(string filePath)
        {
            bool success = await _controller.LoadVideoAsync(filePath);
            if (success)
            {
                // Get real source info
                var sourceInfo = await _controller.GetSourceInfoAsync();
                if (sourceInfo != null)
                {
                    _totalDurationSeconds = sourceInfo.Value.DurationMs / 1000.0;
                    SeekSlider.Maximum = _totalDurationSeconds;
                    
                    // Update UI to match actual video format (if you want)
                    CmbResolution.Text = $"{sourceInfo.Value.Width}x{sourceInfo.Value.Height}";
                    CmbFps.Text = $"{sourceInfo.Value.FrameRate:F1} FPS";

                    // Sync pipeline FPS with actual video FPS to prevent audio distortion
                    await _controller.ChangeFPSAsync((float)sourceInfo.Value.FrameRate);
                }
                else
                {
                    _totalDurationSeconds = 0;
                    SeekSlider.Maximum = 0;
                }

                // Request shared texture from server
                var texPayload = await _controller.RequestSharedTextureAsync();
                if (texPayload != null && texPayload.Value.NtHandle0 != 0)
                {
                    // Pass handles to D3D11VideoPlayer
                    VideoPlayer.SetSharedHandles(texPayload.Value.NtHandle0, texPayload.Value.NtHandle1, texPayload.Value.Width, texPayload.Value.Height);
                }

                await _controller.PlayAsync();
                _isPlaying = true;
                BtnPlayPause.Content = "⏸";
                _progressTimer.Start();
            }
            else
            {
                MessageBox.Show("Failed to load video.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnPlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (!_controller.IsConnected || !_controller.IsVideoLoaded) return;

            if (_isPlaying)
            {
                await _controller.PauseAsync();
                BtnPlayPause.Content = "▶";
                _progressTimer.Stop();
            }
            else
            {
                await _controller.PlayAsync();
                BtnPlayPause.Content = "⏸";
                _progressTimer.Start();
            }
            _isPlaying = !_isPlaying;
        }

        private async void SeekSlider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            if (!_controller.IsConnected) return;
            await _controller.SeekToSecondsAsync(SeekSlider.Value);
        }

        private async void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_controller == null || !_controller.IsConnected) return;
            
            float volume = (float)VolumeSlider.Value;
            if (_isMuted) volume = 0f;
            
            await _controller.SetVolumeAsync(volume);
        }

        private async void BtnMute_Click(object sender, RoutedEventArgs e)
        {
            if (!_controller.IsConnected) return;

            _isMuted = !_isMuted;
            if (_isMuted)
            {
                BtnMute.Content = "🔇";
                await _controller.SetVolumeAsync(0f);
            }
            else
            {
                BtnMute.Content = "🔊";
                await _controller.SetVolumeAsync((float)VolumeSlider.Value);
            }
        }

        private async void CmbResolution_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_controller == null || !_controller.IsConnected) return;
            
            var selectedItem = CmbResolution.SelectedItem as ComboBoxItem;
            if (selectedItem != null)
            {
                string tag = selectedItem.Tag.ToString();
                string[] parts = tag.Split('x');
                if (parts.Length == 2 && int.TryParse(parts[0], out int width) && int.TryParse(parts[1], out int height))
                {
                    await _controller.ChangeResolutionAsync(width, height);
                }
            }
        }

        private async void CmbFps_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_controller == null || !_controller.IsConnected) return;

            var selectedItem = CmbFps.SelectedItem as ComboBoxItem;
            if (selectedItem != null)
            {
                if (float.TryParse(selectedItem.Tag.ToString(), out float fps))
                {
                    await _controller.ChangeFPSAsync(fps);
                }
            }
        }

        private void ProgressTimer_Tick(object sender, EventArgs e)
        {
            // Simulate progression
            if (_isPlaying && SeekSlider.Value < SeekSlider.Maximum)
            {
                SeekSlider.Value += 0.5;
                
                TimeSpan current = TimeSpan.FromSeconds(SeekSlider.Value);
                TimeSpan total = TimeSpan.FromSeconds(_totalDurationSeconds);
                TxtTime.Text = $"{current.ToString(@"mm\:ss")} / {total.ToString(@"mm\:ss")}";
            }
        }
    }
}