using System;
using System.Runtime.InteropServices;

namespace OpenMedia.NDI
{
    internal static class NativeInterop
    {
        private const string DllName = "OpenMedia_C";

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool ome_ndi_engine_init();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ome_ndi_engine_shutdown();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ome_ndi_source_create([MarshalAs(UnmanagedType.LPUTF8Str)] string sourceName);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ome_ndi_source_destroy(IntPtr source);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ome_ndi_source_start(IntPtr source);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ome_ndi_source_stop(IntPtr source);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ome_ndi_output_create([MarshalAs(UnmanagedType.LPUTF8Str)] string sourceName);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ome_ndi_output_destroy(IntPtr output);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool ome_ndi_output_send_frame(IntPtr output, IntPtr frameHandle);
    }
}
