using System;

namespace OpenMedia.NDI
{
    public static class NDIEngine
    {
        public static bool Initialize()
        {
            return NativeInterop.ome_ndi_engine_init();
        }

        public static void Shutdown()
        {
            NativeInterop.ome_ndi_engine_shutdown();
        }
    }
}
