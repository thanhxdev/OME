using System;
using System.Diagnostics;
using System.Windows;
using OpenMedia.Platform;

namespace SimplePlayer
{
    public partial class MainWindow : Window
    {
        private MediaPlayer? _player;

        public MainWindow()
        {
            InitializeComponent();
            this.Loaded += MainWindow_Loaded;
            this.Closed += MainWindow_Closed;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Khởi tạo runtime
                bool connected = await OpenMediaRuntime.InitializeAsync();
                if (!connected)
                {
                    MessageBox.Show("Không thể khởi tạo OpenMediaRuntime.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Khởi tạo MediaPlayer và Attach UI
                _player = new MediaPlayer();
                _player.AttachPreview(VideoView);

                Debug.WriteLine("MediaPlayer initialized and preview attached.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khởi tạo: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            _player?.Dispose();
            OpenMediaRuntime.Shutdown();
        }

        private async void BtnOpen_Click(object sender, RoutedEventArgs e)
        {
            if (_player == null) return;
            try
            {
                await _player.OpenAsync("test_video.mp4");
                await _player.PlayAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi mở file: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            if (_player == null) return;
            await _player.PlayAsync();
        }

        private async void BtnPause_Click(object sender, RoutedEventArgs e)
        {
            if (_player == null) return;
            await _player.PauseAsync();
        }

        private async void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            if (_player == null) return;
            await _player.StopAsync();
        }

        private void SliderDelay_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_player == null) return;

            // Đọc giá trị từ UI (đã bind từ Slider)
            int videoDelay = (int)(SliderVideoDelay?.Value ?? 0);
            int audioDelay = (int)(SliderAudioDelay?.Value ?? 0);
            int masterDelay = (int)(SliderMasterDelay?.Value ?? 0);

            // Cập nhật thuộc tính của MediaPlayer
            _player.VideoDelayMs = videoDelay;
            _player.AudioDelayMs = audioDelay;
            _player.MasterDelayMs = masterDelay;

            // Cập nhật TextBlock hiển thị
            if (TxtEffectiveOffset != null)
            {
                TxtEffectiveOffset.Text = $"📊 Effective Offset = {_player.EffectiveVideoOffsetMs} ms";
            }
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            if (SliderVideoDelay != null) SliderVideoDelay.Value = 0;
            if (SliderAudioDelay != null) SliderAudioDelay.Value = 0;
            if (SliderMasterDelay != null) SliderMasterDelay.Value = 0;
        }
    }
}