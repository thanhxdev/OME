using System.Windows;
using OpenMedia.Platform;
using System.IO;

namespace PlaylistPlayout
{
    public partial class MainWindow : Window
    {
        private MediaPlaylist? _playlist;
        private MediaPlayer? _previewPlayer;

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
                TxtStatus.Text = "⚠ Server not found";
                MessageBox.Show("Could not connect to OpenMediaServer.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _playlist = new MediaPlaylist();
            _previewPlayer = new MediaPlayer();
            
            // Note: In a real playout app, you might route the playlist directly to a Mixer,
            // but for simple preview, we attach it to our UI player.
            // (Our SDK supports passing MediaPlaylist items or having the playlist manage the player)
            
            // For now, we will just use the MediaPlaylist logic to feed our preview player.
            // Wait, MediaPlaylist in our SDK has its own playback engine!
            _playlist.AttachPreview(VideoView);

            _playlist.PlaylistCompleted += (_, _) =>
            {
                Dispatcher.Invoke(() => TxtStatus.Text = "Playlist completed.");
            };
            
            _playlist.ItemChanged += (_, args) =>
            {
                Dispatcher.Invoke(() => LstPlaylist.SelectedIndex = args.current?.Index ?? -1);
            };

            TxtStatus.Text = "Ready. Drop media files here.";
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                foreach (var file in files)
                {
                    if (File.Exists(file))
                    {
                        _playlist?.Add(file);
                        LstPlaylist.Items.Add(Path.GetFileName(file));
                    }
                }
            }
        }

        private void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            _ = _playlist?.PlayAsync();
        }

        private void BtnPause_Click(object sender, RoutedEventArgs e)
        {
            // _ = _playlist?.PauseAsync(); // Not implemented in MediaPlaylist
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            _ = _playlist?.StopAsync();
        }

        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            _ = _playlist?.NextAsync();
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            _playlist?.Dispose();
            _previewPlayer?.Dispose();
            OpenMediaRuntime.Shutdown();
        }
    }
}