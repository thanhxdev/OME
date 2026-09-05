using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using OpenMedia.Platform;
using OpenMedia.Platform.Controls.Wpf;
using PlatformMediaPlayer = OpenMedia.Platform.MediaPlayer;

namespace PlaybackTest
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));

            Task.Run(async () =>
            {
                try
                {
                    await RunAsync(dispatcher);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[TEST-FATAL] {ex}");
                }
                finally
                {
                    dispatcher.InvokeShutdown();
                }
            });

            Dispatcher.Run();
        }

        static async Task RunAsync(Dispatcher dispatcher)
        {
            Console.WriteLine("[TEST] Starting PlatformMediaPlayer test with test_video.mp4...");

            string videoPath = @"c:\Users\ASUS NUC\Desktop\Code\OME\samples\test_video.mp4";
            if (!File.Exists(videoPath))
            {
                Console.WriteLine($"[TEST-ERROR] File not found: {videoPath}");
                return;
            }

            string serverPath = @"c:\Users\ASUS NUC\Desktop\Code\OME\build-demo\bin\Debug\OpenMediaServer.exe";
            var options = new RuntimeOptions
            {
                ServerPath = serverPath,
                AutoLaunch = true
            };

            Console.WriteLine("[TEST] Initializing OpenMediaRuntime...");
            bool initialized = await OpenMediaRuntime.InitializeAsync(options);
            Console.WriteLine($"[TEST] OpenMediaRuntime.InitializeAsync result: {initialized}, EngineVersion: {OpenMediaRuntime.EngineVersion}");

            if (!initialized)
            {
                Console.WriteLine("[TEST-ERROR] Runtime failed to initialize.");
                return;
            }

            OpenMediaVideoView? view = null;
            dispatcher.Invoke(() =>
            {
                view = new OpenMediaVideoView();
                Console.WriteLine("[TEST] OpenMediaVideoView instantiated successfully on UI thread.");
            });

            using var player = new PlatformMediaPlayer();
            player.IsLooping = true;
            player.AttachPreview(view!);

            player.StateChanged += (s, state) => Console.WriteLine($"[TEST-EVENT] StateChanged: {state}");
            player.PositionChanged += (s, pos) => Console.WriteLine($"[TEST-EVENT] PositionChanged: {pos}");
            player.ErrorOccurred += (s, err) => Console.WriteLine($"[TEST-EVENT] Error: {err.Message}");

            Console.WriteLine($"[TEST] Opening file: {videoPath}");
            await player.OpenAsync(videoPath);
            Console.WriteLine($"[TEST] Open completed. State={player.State}, Duration={player.Duration}, Info={player.Information?.Width}x{player.Information?.Height} @ {player.Information?.FrameRate} fps");

            Console.WriteLine("[TEST] Calling PlayAsync()...");
            await player.PlayAsync();
            Console.WriteLine($"[TEST] PlayAsync completed. State={player.State}");

            Console.WriteLine("[TEST] Monitoring playback for 3 seconds...");
            for (int i = 0; i < 12; i++)
            {
                await Task.Delay(250);
                dispatcher.Invoke(() =>
                {
                    var bmp = view?.VideoImageControl?.Source as System.Windows.Media.Imaging.WriteableBitmap;
                    Console.WriteLine($"[TEST-MONITOR] {i * 250}ms: Position={player.Position}, State={player.State}, IsPlaying={view?.IsPlaying}, Bmp={bmp?.PixelWidth}x{bmp?.PixelHeight}");
                });
            }

            await player.StopAsync();
            OpenMediaRuntime.Shutdown();
        }
    }
}
