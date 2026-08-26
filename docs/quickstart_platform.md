# Quick Start: OpenMedia.Platform

OpenMedia.Platform is the high-level API for broadcast and media applications. It provides a simple, modern "3-5 line" developer experience for common media tasks.

## Prerequisites

1. **OpenMediaServer**: Ensure you have `OpenMediaServer.exe` installed and accessible.
2. **.NET 10.0**: Ensure the .NET 10.0 SDK is installed.
3. **OpenMedia.Platform Package**: Add the `OpenMedia.Platform` NuGet package (or project reference) to your application.

## Từ số 0 đến Play Video trong 60 giây

To build a simple video player with hardware acceleration and IPC media pipeline, follow these steps:

### 1. Create a WPF Application
```bash
dotnet new wpf -n MyPlayer
cd MyPlayer
dotnet add reference /path/to/OpenMedia.Platform.csproj
```

### 2. Add the Video View to XAML
In your `MainWindow.xaml`, add the OpenMedia namespace and place the `OpenMediaVideoView` control:
```xml
<Window x:Class="MyPlayer.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:om="clr-namespace:OpenMedia.Platform.Controls.Wpf;assembly=OpenMedia.Platform"
        Title="MyPlayer" Height="450" Width="800">
    <Grid>
        <om:OpenMediaVideoView x:Name="VideoView" />
    </Grid>
</Window>
```

### 3. Write 3 Lines of Code
In your `MainWindow.xaml.cs`, initialize the runtime, create a `MediaPlayer`, attach the preview, and start playback:

```csharp
using System.Windows;
using OpenMedia.Platform;

namespace MyPlayer
{
    public partial class MainWindow : Window
    {
        private MediaPlayer? _player;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 0. Initialize the runtime connection to the server
            await OpenMediaRuntime.InitializeAsync();

            // 1. Create a player with a source (file, URL, or device://)
            _player = new MediaPlayer("C:\\Videos\\sample.mp4");

            // 2. Attach the WPF control for preview
            _player.AttachPreview(VideoView);

            // 3. Play!
            await _player.PlayAsync();
        }
    }
}
```

### 4. Run the Application
```bash
dotnet run
```
You should now see the video playing seamlessly inside your WPF application.

## Troubleshooting

- **Server not found**: Ensure `OpenMediaServer.exe` is in your `%ProgramFiles%\OpenMedia\bin` directory, or set the `OPENMEDIA_SERVER_PATH` environment variable.
- **No Video output**: Check if the video file path is correct. Listen to the `_player.ErrorOccurred` event to capture error messages.
- **DXGI Shared Texture Error**: If you're running on an older iGPU, verify that it supports D3D11 shared resources.
