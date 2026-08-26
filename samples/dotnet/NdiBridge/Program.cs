using System;
using System.Threading.Tasks;
using OpenMedia.Platform;

namespace NdiBridge
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== OpenMedia NDI Bridge ===");
            
            bool ready = await OpenMediaRuntime.InitializeAsync();
            if (!ready)
            {
                Console.WriteLine("Error: Could not connect to OpenMediaServer.");
                return;
            }
            
            Console.WriteLine("Server connected. Creating NDI to SRT bridge...");
            
            // 1. Create a headless mixer (no UI needed)
            using var mixer = new VideoMixer(1920, 1080, 60.0);
            
            // 2. We use MediaPlayer to receive an NDI source (as "ndi://Source Name" - assuming our device model supports it)
            // Wait, for this demo, let's just use a local video file if NDI receiver isn't fully implemented in the dummy code,
            // or we just assume `MediaPlayer` supports "ndi://SourceName".
            Console.WriteLine("Adding NDI Source: 'ndi://My NDI Camera'");
            using var player = new MediaPlayer("ndi://My NDI Camera");
            await player.PlayAsync();
            
            int sourceIndex = mixer.AddSource(player);
            await mixer.SwitchToAsync(sourceIndex);
            
            // 3. Output to SRT Caller
            string srtIp = "127.0.0.1";
            int srtPort = 9000;
            Console.WriteLine($"Streaming to SRT: {srtIp}:{srtPort}");
            
            using var srtOutput = StreamOutput.SRT(srtIp, srtPort, SRTMode.Caller);
            mixer.AddOutput(srtOutput);
            
            Console.WriteLine("Bridge active. Press ENTER to stop.");
            Console.ReadLine();
            
            Console.WriteLine("Shutting down...");
            mixer.RemoveOutput(srtOutput);
            
            OpenMediaRuntime.Shutdown();
        }
    }
}
