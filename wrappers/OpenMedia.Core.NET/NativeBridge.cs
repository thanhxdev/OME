using System;
using System.Runtime.InteropServices;

namespace OpenMedia.SDK
{
    public static class NativeBridge
    {
        private const string DllName = "OpenMedia.Core.dll";

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern bool ome_engine_init(string config_json);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ome_engine_shutdown();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ome_pipeline_create();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ome_pipeline_destroy(IntPtr pipeline);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool ome_pipeline_start(IntPtr pipeline);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool ome_pipeline_stop(IntPtr pipeline);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool ome_pipeline_add_node(IntPtr pipeline, IntPtr node_handle);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void StateChangedCallback(int state);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void ErrorCallback(int errorCode, [MarshalAs(UnmanagedType.LPStr)] string message);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ome_pipeline_set_state_callback(IntPtr pipeline, StateChangedCallback callback);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ome_pipeline_set_error_callback(IntPtr pipeline, ErrorCallback callback);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern IntPtr ome_source_create_file(string uri);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ome_source_destroy(IntPtr source);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ome_mixer_create();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ome_mixer_destroy(IntPtr mixer);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool ome_mixer_add_input(IntPtr mixer, IntPtr source, int layer_index);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern bool ome_mixer_set_lut(IntPtr mixer, string lut_path, float intensity);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ome_audio_mixer_create();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ome_audio_mixer_destroy(IntPtr mixer);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool ome_audio_mixer_set_channel_volume(IntPtr mixer, int channel, float volume);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ome_clock_overlay_create();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ome_overlay_destroy(IntPtr overlay);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern void ome_clock_overlay_set_format(IntPtr overlay, string format);

        [StructLayout(LayoutKind.Sequential)]
        public struct SRTNativeStats
        {
            public long msRTT;
            public int pktLossTotal;
            public int mbpsBandwidth;
            public int pktRetransmitTotal;
            public int pktSentTotal;
            public int pktRecvTotal;
            public int pktDropTotal;
            public ulong bytesSentTotal;
            public ulong bytesRecvTotal;
        }

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ome_srt_engine_create();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ome_srt_engine_destroy(IntPtr engine);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool ome_srt_engine_init(IntPtr engine);

        // SRT Source API
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ome_srt_source_create();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ome_srt_source_destroy(IntPtr source);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern bool ome_srt_source_connect(IntPtr source, string uri);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ome_srt_source_disconnect(IntPtr source);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ome_srt_source_receive(IntPtr source, byte[] buffer, int size);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool ome_srt_source_is_connected(IntPtr source);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool ome_srt_source_get_stats(IntPtr source, out SRTNativeStats stats);

        // SRT Output API
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ome_srt_output_create();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern bool ome_srt_output_open(IntPtr output, string uri);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ome_srt_output_close(IntPtr output);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool ome_srt_output_send(IntPtr output, byte[] data, int size);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool ome_srt_output_is_connected(IntPtr output);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool ome_srt_output_get_stats(IntPtr output, out SRTNativeStats stats);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ome_ndi_engine_create();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ome_ndi_engine_destroy(IntPtr engine);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool ome_ndi_engine_init(IntPtr engine);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ome_webrtc_engine_create();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ome_webrtc_engine_destroy(IntPtr engine);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool ome_webrtc_engine_init(IntPtr engine);

        // Codecs API
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ome_h264_encoder_nv_create();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ome_h264_encoder_qsv_create();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ome_encoder_destroy(IntPtr encoder);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool ome_encoder_initialize(IntPtr encoder);

        // Core Nodes
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr om_create_file_source([MarshalAs(UnmanagedType.LPStr)] string filepath);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr om_create_srt_output([MarshalAs(UnmanagedType.LPStr)] string url);

        // Scripting & Plugins
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool om_run_lua_script([MarshalAs(UnmanagedType.LPStr)] string scriptContent);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool om_load_plugin([MarshalAs(UnmanagedType.LPStr)] string pluginPath);

        // Output API
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ome_rtmp_output_create();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ome_webrtc_output_create();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ome_output_destroy(IntPtr output);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern bool ome_rtmp_output_open(IntPtr output, string url);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern bool ome_webrtc_output_open(IntPtr output, string signalingUri);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void FrameCallback(IntPtr frame, IntPtr userData);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ome_callback_output_create(FrameCallback callback, IntPtr userData);

        // Plugin API
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern bool ome_plugin_manager_load_directory(string directoryPath);

        // MediaFrame API
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ome_media_frame_create_video(int width, int height, int format);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ome_media_frame_destroy(IntPtr frame);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool ome_media_frame_get_data(IntPtr frame, int plane, out IntPtr data, out int stride);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool ome_media_frame_get_video_info(IntPtr frame, out int width, out int height, out int format);

        // Playlist API
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ome_playlist_create();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ome_playlist_destroy(IntPtr playlist);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern bool ome_playlist_add_item(IntPtr playlist, string uri);

        // CG API
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ome_cg_engine_create();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ome_cg_engine_destroy(IntPtr engine);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern bool ome_cg_engine_load_template(IntPtr engine, string templateData);

        // Error Handling
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ome_get_last_error();
    }
}
