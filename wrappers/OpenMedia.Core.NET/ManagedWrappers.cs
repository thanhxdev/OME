using System;
using System.Runtime.InteropServices;

namespace OpenMedia.SDK
{
    public class OpenMediaException : Exception
    {
        public OpenMediaException(string message) : base(message) { }
    }

    internal static class NativeHelper
    {
        public static void CheckError(bool success, string fallbackMessage)
        {
            if (!success)
            {
                IntPtr ptr = NativeBridge.ome_get_last_error();
                if (ptr != IntPtr.Zero)
                {
                    string msg = Marshal.PtrToStringAnsi(ptr);
                    throw new OpenMediaException(string.IsNullOrEmpty(msg) ? fallbackMessage : msg);
                }
                throw new OpenMediaException(fallbackMessage);
            }
        }
    }

    public enum PixelFormat
    {
        Unknown = 0,
        NV12,
        YUV420P,
        BGRA,
        RGBA,
        ARGB
    }

    public static class PluginManager
    {
        public static bool LoadDirectory(string directoryPath)
        {
            return NativeBridge.ome_plugin_manager_load_directory(directoryPath);
        }

        public static bool LoadPlugin(string pluginPath)
        {
            return NativeBridge.om_load_plugin(pluginPath);
        }
    }
}
