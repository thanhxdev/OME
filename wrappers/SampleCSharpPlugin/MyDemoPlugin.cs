using System;
using OpenMedia.SDK;

namespace SampleCSharpPlugin
{
    public class MyDemoPlugin : IOpenMediaPlugin
    {
        public string Name => "MyDemoPlugin";
        public string Version => "1.0.0";

        public void Initialize()
        {
            Console.WriteLine("[MyDemoPlugin] Plugin initialized successfully from C#!");
            // In a real application, the plugin could register new features, UI panels, etc.
        }

        public void Shutdown()
        {
            Console.WriteLine("[MyDemoPlugin] Plugin shutting down.");
        }
    }
}
