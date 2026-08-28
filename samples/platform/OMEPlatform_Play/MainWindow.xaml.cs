using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using OpenMedia.Platform;
using OpenMedia.Platform.Controls.Wpf;
using OpenMedia.Platform.Models;
using PlatformMediaPlayer = OpenMedia.Platform.MediaPlayer;

namespace OMEPlatform_Play
{
    public partial class MainWindow : Window
    {
        private PlatformMediaPlayer? _player;
        private bool _isDraggingSlider = false;
        private double _lastNonMuteVolume = 1.0;
        private string? _currentFilePath;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                TxtStatus.Text = "Đang khởi tạo kết nối với OpenMedia.Platform Engine...";
                TxtEngineState.Text = "Engine: Connecting...";

                string? discoveredServer = FindServerExecutable();
                var options = new RuntimeOptions
                {
                    ServerPath = discoveredServer
                };

                if (!string.IsNullOrEmpty(discoveredServer))
                {
                    Debug.WriteLine($"[OMEPlatform_Play] Target OpenMediaServer: {discoveredServer}");
                }

                bool ready = await OpenMediaRuntime.InitializeAsync(options);
                if (!ready)
                {
                    TxtStatus.Text = "⚠ Không thể kết nối tới OpenMediaServer.";
                    TxtEngineState.Text = "Engine: Disconnected";
                    MessageBox.Show(
                        "Không thể khởi động hoặc kết nối tới OpenMediaServer.\n" +
                        "Vui lòng đảm bảo các dịch vụ nền đã sẵn sàng.",
                        "Lỗi Khởi Tạo Runtime",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                TxtEngineState.Text = $"Engine: Connected (v{OpenMediaRuntime.EngineVersion})";
                TxtStatus.Text = "Đã sẵn sàng. Chọn tập tin video hoặc kéo thả vào ứng dụng.";

                // Initialize MediaPlayer
                InitializePlayer();

                // Auto-load and play video if passed via command line arguments
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 1; i < args.Length; i++)
                {
                    string arg = args[i].Trim('"');
                    if (File.Exists(arg))
                    {
                        try
                        {
                            await LoadVideoFileAsync(arg);
                            await _player!.PlayAsync();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[OMEPlatform_Play AutoPlay Error]: {ex.Message}");
                        }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Lỗi khởi tạo: {ex.Message}";
                TxtEngineState.Text = "Engine: Error";
            }
        }

        private void InitializePlayer()
        {
            _player?.Dispose();

            _player = new PlatformMediaPlayer();
            _player.AttachPreview(VideoView);

            _player.StateChanged += Player_StateChanged;
            _player.PositionChanged += Player_PositionChanged;
            _player.ErrorOccurred += Player_ErrorOccurred;
            _player.EndOfMedia += Player_EndOfMedia;

            UpdateVolumeState();
        }

        #region Open & Load File

        private async void BtnOpenFile_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "Chọn File Video",
                Filter = "Video Files (*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.flv;*.ts;*.m2ts)|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.flv;*.ts;*.m2ts|All Files (*.*)|*.*",
                Multiselect = false
            };

            if (openFileDialog.ShowDialog() == true)
            {
                await LoadVideoFileAsync(openFileDialog.FileName);
            }
        }

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0 && File.Exists(files[0]))
                {
                    await LoadVideoFileAsync(files[0]);
                }
            }
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private async Task LoadVideoFileAsync(string filePath)
        {
            if (!File.Exists(filePath))
            {
                MessageBox.Show("Tập tin video không tồn tại!", "Lỗi Tập Tin", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _currentFilePath = filePath;
            TxtFilePath.Text = filePath;
            TxtFileName.Text = Path.GetFileName(filePath);
            TxtStatus.Text = $"Đang tải video: {Path.GetFileName(filePath)}...";

            if (_player == null)
            {
                InitializePlayer();
            }

            // Open video source in player
            await _player!.OpenAsync(filePath);

            // Populate Metadata
            PopulateMediaInformation(_player.Information, filePath);

            // Configure Seek Bar
            var duration = _player.Duration;
            SldPosition.Minimum = 0;
            SldPosition.Maximum = duration.TotalSeconds > 0 ? duration.TotalSeconds : 100;
            SldPosition.Value = 0;
            TxtDuration.Text = FormatTimeSpan(duration);
            TxtPosition.Text = "00:00:00";

            TxtStatus.Text = $"Đã tải xong: {Path.GetFileName(filePath)}. Sẵn sàng phát.";
        }

        private void PopulateMediaInformation(MediaInfo? info, string filePath)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);

                if (info != null)
                {
                    TxtParamDuration.Text = FormatTimeSpan(info.Duration);
                    TxtResolution.Text = (info.Width > 0 && info.Height > 0) ? $"{info.Width} x {info.Height}" : "Chưa xác định";
                    TxtFrameRate.Text = info.FrameRate > 0 ? $"{info.FrameRate:F2} fps" : "Chưa xác định";
                    TxtVideoCodec.Text = !string.IsNullOrEmpty(info.VideoCodec) ? info.VideoCodec.ToUpper() : "N/A";
                    TxtAudioCodec.Text = !string.IsNullOrEmpty(info.AudioCodec) ? info.AudioCodec.ToUpper() : "N/A";
                    TxtAudioChannels.Text = info.AudioChannels > 0 ? $"{info.AudioChannels} ch" : "N/A";
                    TxtSampleRate.Text = info.AudioSampleRate > 0 ? $"{info.AudioSampleRate} Hz" : "N/A";

                    long bitrate = info.BitrateKbps > 0 ? info.BitrateKbps : (fileInfo.Length * 8 / 1024 / Math.Max(1, (long)info.Duration.TotalSeconds));
                    TxtBitrate.Text = bitrate > 0 ? $"{bitrate} kbps" : "N/A";
                }
                else
                {
                    TxtParamDuration.Text = "00:00:00";
                    TxtResolution.Text = "N/A";
                    TxtFrameRate.Text = "N/A";
                    TxtVideoCodec.Text = "N/A";
                    TxtAudioCodec.Text = "N/A";
                    TxtAudioChannels.Text = "N/A";
                    TxtSampleRate.Text = "N/A";
                    TxtBitrate.Text = $"{fileInfo.Length / 1024 / 1024} MB";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Lỗi đọc metadata: {ex.Message}");
            }
        }

        #endregion

        #region Player Events

        private void Player_StateChanged(object? sender, PlaybackState state)
        {
            Dispatcher.Invoke(() =>
            {
                TxtStatus.Text = $"Trạng thái phát: {state}";
                BtnPlay.IsEnabled = state != PlaybackState.Playing;
                BtnPause.IsEnabled = state == PlaybackState.Playing || state == PlaybackState.Paused;
                BtnStop.IsEnabled = state == PlaybackState.Playing || state == PlaybackState.Paused;

                if (state == PlaybackState.Paused)
                {
                    TxtPauseIcon.Text = "▶ ";
                    TxtPauseText.Text = "Resume";
                    BtnPause.ToolTip = "Tiếp tục phát video (Resume)";
                    BtnPause.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A6E3A1"));
                }
                else
                {
                    TxtPauseIcon.Text = "⏸ ";
                    TxtPauseText.Text = "Pause";
                    BtnPause.ToolTip = "Tạm dừng phát video (Pause)";
                    BtnPause.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F9E2AF"));
                }
            });
        }

        private async void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space && !TxtJumpTime.IsFocused)
            {
                e.Handled = true;
                if (_player != null)
                {
                    if (_player.State == PlaybackState.Playing)
                    {
                        await _player.PauseAsync();
                    }
                    else if (_player.State == PlaybackState.Paused || _player.State == PlaybackState.Ready || _player.State == PlaybackState.Stopped)
                    {
                        await _player.PlayAsync();
                    }
                }
            }
        }

        private void Player_PositionChanged(object? sender, TimeSpan position)
        {
            Dispatcher.Invoke(() =>
            {
                if (!_isDraggingSlider)
                {
                    SldPosition.Value = position.TotalSeconds;
                    TxtPosition.Text = FormatTimeSpan(position);
                }
            });
        }

        private void Player_ErrorOccurred(object? sender, MediaErrorEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                TxtStatus.Text = $"Lỗi phát: {e.Message}";
                MessageBox.Show($"Đã xảy ra lỗi khi phát video:\n{e.Message}", "Lỗi Phát Video", MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }

        private void Player_EndOfMedia(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                TxtStatus.Text = "Đã phát xong toàn bộ video.";
                SldPosition.Value = 0;
                TxtPosition.Text = "00:00:00";
            });
        }

        #endregion

        #region Transport & Seek Controls

        private async void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            if (_player != null)
            {
                await _player.PlayAsync();
            }
        }

        private async void BtnPause_Click(object sender, RoutedEventArgs e)
        {
            if (_player != null)
            {
                if (_player.State == PlaybackState.Playing)
                {
                    await _player.PauseAsync();
                }
                else if (_player.State == PlaybackState.Paused)
                {
                    await _player.PlayAsync();
                }
            }
        }

        private async void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            if (_player != null)
            {
                await _player.StopAsync();
                SldPosition.Value = 0;
                TxtPosition.Text = "00:00:00";
            }
        }

        private async void BtnRewind10_Click(object sender, RoutedEventArgs e)
        {
            if (_player != null)
            {
                var newPos = _player.Position - TimeSpan.FromSeconds(10);
                if (newPos < TimeSpan.Zero) newPos = TimeSpan.Zero;
                await _player.SeekAsync(newPos);
            }
        }

        private async void BtnForward10_Click(object sender, RoutedEventArgs e)
        {
            if (_player != null)
            {
                var newPos = _player.Position + TimeSpan.FromSeconds(10);
                if (newPos > _player.Duration) newPos = _player.Duration;
                await _player.SeekAsync(newPos);
            }
        }

        private void SldPosition_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
        {
            _isDraggingSlider = true;
        }

        private async void SldPosition_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            _isDraggingSlider = false;
            if (_player != null)
            {
                var targetSeconds = SldPosition.Value;
                var targetPos = TimeSpan.FromSeconds(targetSeconds);
                await _player.SeekAsync(targetPos);
                TxtPosition.Text = FormatTimeSpan(targetPos);
            }
        }

        private void SldPosition_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isDraggingSlider)
            {
                TxtPosition.Text = FormatTimeSpan(TimeSpan.FromSeconds(e.NewValue));
            }
        }

        private async void BtnJumpTime_Click(object sender, RoutedEventArgs e)
        {
            if (_player == null) return;

            string text = TxtJumpTime.Text.Trim();
            if (TryParseTimeSpan(text, out TimeSpan targetPos))
            {
                if (targetPos > _player.Duration)
                {
                    targetPos = _player.Duration;
                }
                if (targetPos < TimeSpan.Zero)
                {
                    targetPos = TimeSpan.Zero;
                }

                await _player.SeekAsync(targetPos);
                SldPosition.Value = targetPos.TotalSeconds;
                TxtPosition.Text = FormatTimeSpan(targetPos);
                TxtStatus.Text = $"Đã tua đến thời điểm: {FormatTimeSpan(targetPos)}";
            }
            else
            {
                MessageBox.Show(
                    "Định dạng thời gian không hợp lệ!\n" +
                    "Vui lòng nhập định dạng HH:mm:ss (ví dụ 00:01:30) hoặc mm:ss (ví dụ 01:30) hoặc số giây.",
                    "Lỗi Định Dạng Time",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        #endregion

        #region Audio & View Options

        private void ChkEnableVideo_CheckedChanged(object sender, RoutedEventArgs e)
        {
            bool enable = ChkEnableVideo.IsChecked == true;
            if (VideoView != null && PnlVideoDisabled != null)
            {
                VideoView.Visibility = enable ? Visibility.Visible : Visibility.Collapsed;
                PnlVideoDisabled.Visibility = enable ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        private void ChkAudio_CheckedChanged(object sender, RoutedEventArgs e)
        {
            UpdateVolumeState();
        }

        private void SldVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtVolumePercent != null)
            {
                TxtVolumePercent.Text = $"{(int)(e.NewValue * 100)}%";
            }

            if (_player != null)
            {
                _player.Volume = e.NewValue;
            }
        }

        private void UpdateVolumeState()
        {
            if (_player == null) return;

            bool isAudioEnabled = ChkAudio.IsChecked == true;
            _player.IsMuted = !isAudioEnabled;
            _player.Volume = SldVolume.Value;
        }

        private void CmbStretchMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (VideoView == null) return;

            var image = FindVisualChild<Image>(VideoView);
            if (image == null) return;

            switch (CmbStretchMode.SelectedIndex)
            {
                case 0:
                    image.Stretch = Stretch.Uniform; // Fit
                    break;
                case 1:
                    image.Stretch = Stretch.UniformToFill; // Fill
                    break;
                case 2:
                    image.Stretch = Stretch.Fill; // Stretch
                    break;
                case 3:
                    image.Stretch = Stretch.None; // Center
                    break;
                default:
                    image.Stretch = Stretch.Uniform;
                    break;
            }
        }

        #endregion

        #region Helpers

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;

            int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childrenCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild)
                {
                    return typedChild;
                }

                var descendant = FindVisualChild<T>(child);
                if (descendant != null)
                {
                    return descendant;
                }
            }
            return null;
        }

        private static string FormatTimeSpan(TimeSpan ts)
        {
            return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        }

        private static bool TryParseTimeSpan(string input, out TimeSpan result)
        {
            result = TimeSpan.Zero;
            if (string.IsNullOrWhiteSpace(input)) return false;

            // Try standard TimeSpan parse
            if (TimeSpan.TryParse(input, out result)) return true;

            // Try mm:ss
            var parts = input.Split(':');
            if (parts.Length == 2 && int.TryParse(parts[0], out int m) && int.TryParse(parts[1], out int s))
            {
                result = new TimeSpan(0, m, s);
                return true;
            }

            // Try total seconds
            if (double.TryParse(input, out double totalSeconds))
            {
                result = TimeSpan.FromSeconds(totalSeconds);
                return true;
            }

            return false;
        }

        private static string? FindServerExecutable()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string current = baseDir;

            for (int i = 0; i < 6; i++)
            {
                if (string.IsNullOrEmpty(current)) break;

                var candidates = new[]
                {
                    Path.Combine(current, "build", "bin", "Debug", "OpenMediaServer.exe"),
                    Path.Combine(current, "build", "bin", "Release", "OpenMediaServer.exe"),
                    Path.Combine(current, "build-demo", "bin", "Debug", "OpenMediaServer.exe"),
                    Path.Combine(current, "build-demo", "bin", "Release", "OpenMediaServer.exe"),
                    Path.Combine(current, "build-production", "bin", "Release", "OpenMediaServer.exe"),
                    Path.Combine(current, "dist", "production", "bin", "OpenMediaServer.exe"),
                    Path.Combine(baseDir, "OpenMediaServer", "OpenMediaServer.exe"),
                    Path.Combine(baseDir, "OpenMediaServer.exe")
                };

                foreach (var candidate in candidates)
                {
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }

                var parent = Directory.GetParent(current);
                if (parent == null) break;
                current = parent.FullName;
            }

            return null;
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                _player?.Dispose();
                _player = null;

                OpenMediaRuntime.Shutdown();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow_Closing] Exception during exit: {ex.Message}");
            }
        }

        #endregion
    }
}
