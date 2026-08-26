using System.Windows;
using OpenMedia.Platform;
using System.IO;

namespace WpfQuickPlayer
{
    /// <summary>
    /// Demonstrates the OpenMedia.Platform "3-line" developer experience.
    /// </summary>
    public partial class MainWindow : Window
    {
        private MediaPlayer? _player;
        private bool _isDraggingSlider;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Initialize the runtime (server discovery + IPC connect)
            bool ready = await OpenMediaRuntime.InitializeAsync();
            if (!ready)
            {
                TxtStatus.Text = "⚠ Server not found";
                MessageBox.Show("Could not connect to OpenMediaServer. Ensure the server is built and available.", "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Start with a default or empty player
            _player = new MediaPlayer();
            _player.AttachPreview(VideoView);
            WirePlayerEvents();
            TxtStatus.Text = "Ready. Drag & Drop a video file here.";
        }
        
        private void WirePlayerEvents()
        {
            if (_player == null) return;
            
            _player.StateChanged += (_, state) => 
            {
                Dispatcher.Invoke(() => 
                {
                    TxtStatus.Text = state.ToString();
                    if (state == PlaybackState.Playing || state == PlaybackState.Ready)
                    {
                        SldPosition.Maximum = _player.Duration.TotalSeconds;
                        TxtDuration.Text = _player.Duration.ToString(@"hh\:mm\:ss");
                    }
                });
            };
            
            _player.ErrorOccurred += (_, err) => 
            {
                Dispatcher.Invoke(() => 
                {
                    TxtStatus.Text = $"❌ {err.Message}";
                    MessageBox.Show(err.Message, "Playback Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            };
            
            _player.PositionChanged += (_, pos) =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (!_isDraggingSlider)
                    {
                        SldPosition.Value = pos.TotalSeconds;
                        TxtPosition.Text = pos.ToString(@"hh\:mm\:ss");
                    }
                });
            };
        }

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0 && File.Exists(files[0]))
                {
                    if (_player != null)
                    {
                        await _player.StopAsync();
                        await _player.OpenAsync(files[0]);
                        await _player.PlayAsync();
                    }
                }
            }
        }

        private void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            _ = _player?.PlayAsync();
        }

        private void BtnPause_Click(object sender, RoutedEventArgs e)
        {
            _ = _player?.PauseAsync();
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            _ = _player?.StopAsync();
        }

        private void SldVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_player != null)
            {
                _player.Volume = SldVolume.Value;
            }
        }

        private void SldPosition_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
        {
            _isDraggingSlider = true;
        }

        private async void SldPosition_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            if (_player != null)
            {
                await _player.SeekAsync(TimeSpan.FromSeconds(SldPosition.Value));
            }
            _isDraggingSlider = false;
        }
        
        private async void SldPosition_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // If the user clicks on the slider track (not dragging the thumb)
            if (Math.Abs(e.NewValue - e.OldValue) > 1.0 && !_isDraggingSlider && _player != null)
            {
                await _player.SeekAsync(TimeSpan.FromSeconds(e.NewValue));
            }
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            _player?.Dispose();
            OpenMediaRuntime.Shutdown();
        }
    }
}
