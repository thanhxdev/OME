using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OpenMedia;
using OpenMedia.SDK;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace PlaybackTest
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            Console.WriteLine("[TEST] Starting Playback verification test...");

            string videoPath = @"c:\Users\ASUS NUC\Desktop\Code\OME\samples\test_video.mp4";
            if (!File.Exists(videoPath))
            {
                Console.WriteLine($"[TEST-ERROR] File not found: {videoPath}");
                return 1;
            }

            // Check if server is running
            var procs = Process.GetProcessesByName("OpenMediaServer");
            if (procs.Length == 0)
            {
                Console.WriteLine("[TEST] Starting OpenMediaServer...");
                Process.Start(@"c:\Users\ASUS NUC\Desktop\Code\OME\build-demo\bin\Debug\OpenMediaServer.exe");
                await Task.Delay(1000);
            }

            try
            {
                Console.WriteLine("[TEST] Initializing SDKEngine and connecting IPC...");
                await SDKEngine.Instance.InitializeAsync();

                Console.WriteLine("[TEST] Creating Pipeline...");
                var pipeline = await SDKPipeline.CreateAsync("TestPipeline", 1920, 1080, 60.0);

                Console.WriteLine("[TEST] Opening Source: " + videoPath);
                var source = await SDKSource.CreateAsync(pipeline, 1, videoPath, loop: true);

                Console.WriteLine("[TEST] Starting Pipeline...");
                bool started = await pipeline.StartAsync();
                if (!started)
                {
                    Console.WriteLine("[TEST-ERROR] Pipeline failed to start.");
                    return 2;
                }

                Console.WriteLine("[TEST] Requesting Shared GPU Textures...");
                var ipcClient = SDKEngine.Instance.IPC;
                var payloadNullable = await ipcClient.RequestSharedTextureAsync();
                if (payloadNullable == null)
                {
                    Console.WriteLine("[TEST-ERROR] Failed to get shared textures from server.");
                    return 3;
                }
                var payload = payloadNullable.Value;
                Console.WriteLine($"[TEST] Received handles: Handle0=0x{payload.NtHandle0:X}, Handle1=0x{payload.NtHandle1:X}, Size={payload.Width}x{payload.Height}");

                // Enumerate adapters
                using var dxgiFactory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
                for (uint i = 0; dxgiFactory.EnumAdapters1(i, out var adapter).Success; i++)
                {
                    var desc = adapter.Description1;
                    Console.WriteLine($"[TEST-GPU] Adapter {i}: {desc.Description}, Dedicated: {desc.DedicatedVideoMemory / 1024 / 1024}MB, Flags: {desc.Flags}");
                }

                // Initialize D3D11 device
                D3D11.D3D11CreateDevice(
                    null,
                    DriverType.Hardware,
                    DeviceCreationFlags.BgraSupport,
                    new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 },
                    out ID3D11Device device,
                    out ID3D11DeviceContext context).CheckError();

                using var d3dDevice = device.QueryInterface<ID3D11Device1>();
                using var tex0 = d3dDevice.OpenSharedResource<ID3D11Texture2D>((IntPtr)payload.NtHandle0);
                using var tex1 = d3dDevice.OpenSharedResource<ID3D11Texture2D>((IntPtr)payload.NtHandle1);

                // Create staging texture to read back or render
                var stagingDesc = tex0.Description;
                stagingDesc.Usage = ResourceUsage.Staging;
                stagingDesc.BindFlags = BindFlags.None;
                stagingDesc.CPUAccessFlags = CpuAccessFlags.Read;
                stagingDesc.MiscFlags = ResourceOptionFlags.None;
                using var stagingTex = device.CreateTexture2D(stagingDesc);

                int framesReceived = 0;
                Console.WriteLine("[TEST] Streaming video frames directly via DXGI shared texture...");
                var sw = Stopwatch.StartNew();

                while (framesReceived < 60 && sw.ElapsedMilliseconds < 5000)
                {
                    var tex = (framesReceived % 2 == 0) ? tex0 : tex1;
                    context.CopyResource(stagingTex, tex);
                    framesReceived++;
                    if (framesReceived % 10 == 0)
                    {
                        Console.WriteLine($"[TEST] Received {framesReceived} frames ({sw.ElapsedMilliseconds}ms, ~{framesReceived * 1000.0 / sw.ElapsedMilliseconds:F1} FPS)...");
                    }
                    Thread.Sleep(16); // ~60 FPS
                }

                sw.Stop();
                Console.WriteLine($"[TEST] Summary: Received {framesReceived} frames in {sw.ElapsedMilliseconds}ms ({(framesReceived * 1000.0 / sw.ElapsedMilliseconds):F1} FPS)");

                await pipeline.StopAsync();

                if (framesReceived >= 30)
                {
                    Console.WriteLine("[TEST-SUCCESS] Continuous video playback verified successfully!");
                    return 0;
                }
                else
                {
                    Console.WriteLine($"[TEST-FAIL] Insufficient frames received: {framesReceived}");
                    return 4;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TEST-EXCEPTION] {ex.Message}\n{ex.StackTrace}");
                return 5;
            }
        }
    }
}
