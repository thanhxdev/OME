using System;

namespace OpenMedia.SDK
{
    public interface IOpenMediaPlugin
    {
        string Name { get; }
        string Version { get; }
        void Initialize();
        void Shutdown();
    }
}
