using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using OpenMedia.Platform;

namespace OMEPlatform_AVDelay
{
    public partial class MainWindow : Window
    {
        private MediaPlayer? _player;
        private bool _isDraggingSeek = false;
        private bool _isUpdatingDelaysFromCode = false;

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                StatusOverlay.Text = "Status: Initializing SDK...";
                
                // Khởi tạo Runtime để kết nối với OpenMediaServer
                await OpenMediaRuntime.InitializeAsync();

                _player = new MediaPlayer();
                _player.AttachPreview(VideoView);

                // Đăng ký sự kiện
                _player.StateChanged += Player_StateChanged;
                _player.PositionChanged += Player_PositionChanged;
                _player.ErrorOccurred += Player_ErrorOccurred;
                _player.EndOfMedia += Player_EndOfMedia;

                StatusOverlay.Text = "Status: Ready";
                StatusOverlay.Foreground = System.Windows.Media.Brushes.LightGreen;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize SDK: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusOverlay.Text = "Status: Initialization Failed";
                StatusOverlay.Foreground = System.Windows.Media.Brushes.Red;
            }
        }

        private void Window_Unloaded(object sender, RoutedEventArgs e)
        {
            Cleanup();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            Cleanup();
        }

        private void Cleanup()
        {
            if (_player != null)
            {
                _player.Dispose();
                _player = null;
            }
            OpenMediaRuntime.Shutdown();
        }

        // --- PLAYBACK CONTROLS ---

        private async void BtnOpen_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Media Files|*.mp4;*.mkv;*.ts;*.mov;*.avi|All Files|*.*"
            };

            if (dlg.ShowDialog() == true && _player != null)
            {
                await _player.OpenAsync(dlg.FileName);
                await _player.PlayAsync();
            }
        }

        private async void BtnOpenUrl_Click(object sender, RoutedEventArgs e)
        {
            // For simplicity, hardcoded or use a prompt. Let's just ask user via input box if needed, or open a test stream.
            // Using a simple input dialog trick or just hardcoding for now.
            // In a real app we'd have a dialog.
            if (_player != null)
            {
                // await _player.OpenAsync("srt://localhost:9000");
                // await _player.PlayAsync();
                MessageBox.Show("Open URL requires a dialog. For now, use Open File.");
            }
        }

        private async void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            if (_player != null) await _player.PlayAsync();
        }

        private async void BtnPause_Click(object sender, RoutedEventArgs e)
        {
            if (_player != null) await _player.PauseAsync();
        }

        private async void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            if (_player != null) await _player.StopAsync();
        }

        private void BtnMute_Click(object sender, RoutedEventArgs e)
        {
            if (_player != null)
            {
                _player.IsMuted = !_player.IsMuted;
                BtnMute.Content = _player.IsMuted ? "🔇" : "🔊";
            }
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_player != null)
            {
                _player.Volume = e.NewValue;
            }
        }

        // --- SEEK BAR LOGIC ---

        private void SeekSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDraggingSeek = true;
        }

        private async void SeekSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDraggingSeek = false;
            if (_player != null && _player.Information != null)
            {
                double percentage = SeekSlider.Value / 100.0;
                var target = TimeSpan.FromMilliseconds(_player.Duration.TotalMilliseconds * percentage);
                await _player.SeekAsync(target);
            }
        }

        // --- PLAYER EVENTS ---

        private void Player_StateChanged(object sender, PlaybackState state)
        {
            Dispatcher.Invoke(() =>
            {
                if (_player?.Information != null)
                {
                    StatusOverlay.Text = $"Status: {state} | {_player.Information.Width}x{_player.Information.Height} @ {_player.Information.FrameRate:F2}fps | Delay: {_player.MasterDelayMs}ms";
                }
                else
                {
                    StatusOverlay.Text = $"Status: {state}";
                }
            });
        }

        private void Player_PositionChanged(object sender, TimeSpan position)
        {
            if (!_isDraggingSeek)
            {
                Dispatcher.Invoke(() =>
                {
                    CurrentTimeText.Text = position.ToString(@"hh\:mm\:ss\.fff");
                    if (_player != null && _player.Duration.TotalMilliseconds > 0)
                    {
                        DurationText.Text = _player.Duration.ToString(@"hh\:mm\:ss\.fff");
                        SeekSlider.Value = (position.TotalMilliseconds / _player.Duration.TotalMilliseconds) * 100.0;
                    }

                    if (_player != null)
                    {
                        var audioPos = position.Add(TimeSpan.FromMilliseconds(_player.AudioDelayMs + _player.MasterDelayMs));
                        if (audioPos < TimeSpan.Zero) audioPos = TimeSpan.Zero;
                        AudioTimecodeOverlay.Text = $"Audio TC: {audioPos:hh\\:mm\\:ss\\.fff}";

                        var videoPos = position.Add(TimeSpan.FromMilliseconds(_player.VideoDelayMs + _player.MasterDelayMs));
                        if (videoPos < TimeSpan.Zero) videoPos = TimeSpan.Zero;
                        VideoTimecodeOverlay.Text = $"Video TC: {videoPos:hh\\:mm\\:ss\\.fff}";
                    }
                });
            }
        }

        private void Player_ErrorOccurred(object sender, MediaErrorEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                StatusOverlay.Text = $"Error: {e.Message}";
                StatusOverlay.Foreground = System.Windows.Media.Brushes.Red;
            });
        }

        private void Player_EndOfMedia(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                StatusOverlay.Text = "Status: Ended";
            });
        }

        // --- DELAY CONTROLS LOGIC ---

        private void VideoDelaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingDelaysFromCode) return;
            UpdateDelayValues((int)e.NewValue, null, null);
        }

        private void AudioDelaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingDelaysFromCode) return;
            UpdateDelayValues(null, (int)e.NewValue, null);
        }

        private void MasterDelaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingDelaysFromCode) return;
            UpdateDelayValues(null, null, (int)e.NewValue);
        }

        private int _lastVideoMs = 0;
        private int _lastAudioMs = 0;
        private int _lastMasterMs = 0;

        private void UpdateDelayValues(int? videoMs, int? audioMs, int? masterMs)
        {
            _isUpdatingDelaysFromCode = true;

            try
            {
                int newVideo = videoMs ?? (int)VideoDelaySlider.Value;
                int newAudio = audioMs ?? (int)AudioDelaySlider.Value;
                int newMaster = masterMs ?? (int)MasterDelaySlider.Value;

                // Xử lý Lock Ratio
                if (LockRatioCheck.IsChecked == true)
                {
                    if (videoMs.HasValue)
                    {
                        int diff = newVideo - _lastVideoMs;
                        newAudio += diff;
                    }
                    else if (audioMs.HasValue)
                    {
                        int diff = newAudio - _lastAudioMs;
                        newVideo += diff;
                    }
                }

                VideoDelaySlider.Value = newVideo;
                AudioDelaySlider.Value = newAudio;
                MasterDelaySlider.Value = newMaster;

                _lastVideoMs = newVideo;
                _lastAudioMs = newAudio;
                _lastMasterMs = newMaster;

                // Cập nhật TextBoxes
                VideoDelayText.Text = newVideo.ToString();
                AudioDelayText.Text = newAudio.ToString();
                MasterDelayText.Text = newMaster.ToString();

                // Gửi lệnh xuống Player SDK
                if (_player != null)
                {
                    _player.VideoDelayMs = newVideo;
                    _player.AudioDelayMs = newAudio;
                    _player.MasterDelayMs = newMaster;
                    Player_StateChanged(_player, _player.State); // update UI
                }
            }
            finally
            {
                _isUpdatingDelaysFromCode = false;
            }
        }

        // Quick Actions
        private void BtnResetDelays_Click(object sender, RoutedEventArgs e)
        {
            UpdateDelayValues(0, 0, 0);
        }

        // Video Buttons
        private void BtnVideoMinus_Click(object sender, RoutedEventArgs e) => UpdateDelayValues((int)VideoDelaySlider.Value - 50, null, null);
        private void BtnVideoPlus_Click(object sender, RoutedEventArgs e) => UpdateDelayValues((int)VideoDelaySlider.Value + 50, null, null);

        // Audio Buttons
        private void BtnAudioMinus_Click(object sender, RoutedEventArgs e) => UpdateDelayValues(null, (int)AudioDelaySlider.Value - 50, null);
        private void BtnAudioPlus_Click(object sender, RoutedEventArgs e) => UpdateDelayValues(null, (int)AudioDelaySlider.Value + 50, null);

        // Master Buttons
        private void BtnMasterMinus_Click(object sender, RoutedEventArgs e) => UpdateDelayValues(null, null, (int)MasterDelaySlider.Value - 50);
        private void BtnMasterPlus_Click(object sender, RoutedEventArgs e) => UpdateDelayValues(null, null, (int)MasterDelaySlider.Value + 50);

        // TextBoxes Input
        private void VideoDelayText_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && int.TryParse(VideoDelayText.Text, out int val)) UpdateDelayValues(val, null, null);
        }

        private void AudioDelayText_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && int.TryParse(AudioDelayText.Text, out int val)) UpdateDelayValues(null, val, null);
        }

        private void MasterDelayText_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && int.TryParse(MasterDelayText.Text, out int val)) UpdateDelayValues(null, null, val);
        }
    }
}
