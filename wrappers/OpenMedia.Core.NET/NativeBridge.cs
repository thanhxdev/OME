using System;
using System.Runtime.InteropServices;
using OpenMedia.SDK.Interop;
using OpenMedia.SDK.SafeHandles;

namespace OpenMedia.SDK
{
    /// <summary>
    /// Modern .NET 10 P/Invoke bridge powered by compile-time Source Generators ([LibraryImport]).
    /// Provides zero-copy, type-safe native interop and GC-protected callback lifetime management.
    /// </summary>
    public static partial class NativeBridge
    {
        private const string DllName = "OpenMedia.Core.dll";

        // ─── Engine Lifecycle ───────────────────────────────────────────
        [LibraryImport(DllName, StringMarshalling = StringMarshalling.Utf8)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_engine_init(string config_json);

        [LibraryImport(DllName)]
        public static partial void ome_engine_shutdown();

        // ─── Pipeline API ───────────────────────────────────────────────
        [LibraryImport(DllName)]
        public static partial SafePipelineHandle ome_pipeline_create();

        [LibraryImport(DllName, EntryPoint = "ome_pipeline_destroy")]
        internal static partial void ome_pipeline_destroy_internal(IntPtr pipeline);

        public static void ome_pipeline_destroy(SafePipelineHandle pipeline)
        {
            if (pipeline != null && !pipeline.IsInvalid)
            {
                CallbackLifetimeManager.UnregisterAll(pipeline.DangerousGetHandle());
                pipeline.Dispose();
            }
        }

        public static void ome_pipeline_destroy(IntPtr pipeline)
        {
            if (pipeline != IntPtr.Zero)
            {
                CallbackLifetimeManager.UnregisterAll(pipeline);
                ome_pipeline_destroy_internal(pipeline);
            }
        }

        [LibraryImport(DllName)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_pipeline_start(SafePipelineHandle pipeline);

        [LibraryImport(DllName, EntryPoint = "ome_pipeline_start")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_pipeline_start(IntPtr pipeline);

        [LibraryImport(DllName)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_pipeline_stop(SafePipelineHandle pipeline);

        [LibraryImport(DllName, EntryPoint = "ome_pipeline_stop")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_pipeline_stop(IntPtr pipeline);

        [LibraryImport(DllName)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_pipeline_add_node(SafePipelineHandle pipeline, IntPtr node_handle);

        [LibraryImport(DllName, EntryPoint = "ome_pipeline_add_node")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_pipeline_add_node(IntPtr pipeline, IntPtr node_handle);

        // Callbacks
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void StateChangedCallback(int state);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void ErrorCallback(int errorCode, [MarshalAs(UnmanagedType.LPStr)] string message);

        [LibraryImport(DllName, EntryPoint = "ome_pipeline_set_state_callback")]
        private static partial void ome_pipeline_set_state_callback_raw(IntPtr pipeline, IntPtr callbackPtr);

        [LibraryImport(DllName, EntryPoint = "ome_pipeline_set_error_callback")]
        private static partial void ome_pipeline_set_error_callback_raw(IntPtr pipeline, IntPtr callbackPtr);

        public static void ome_pipeline_set_state_callback(IntPtr pipeline, StateChangedCallback? callback)
        {
            if (pipeline == IntPtr.Zero) return;
            if (callback == null)
            {
                ome_pipeline_set_state_callback_raw(pipeline, IntPtr.Zero);
                return;
            }
            CallbackLifetimeManager.Register(pipeline, callback);
            IntPtr ptr = Marshal.GetFunctionPointerForDelegate(callback);
            ome_pipeline_set_state_callback_raw(pipeline, ptr);
        }

        public static void ome_pipeline_set_state_callback(SafePipelineHandle pipeline, StateChangedCallback? callback)
        {
            ome_pipeline_set_state_callback(pipeline?.DangerousGetHandle() ?? IntPtr.Zero, callback);
        }

        public static void ome_pipeline_set_error_callback(IntPtr pipeline, ErrorCallback? callback)
        {
            if (pipeline == IntPtr.Zero) return;
            if (callback == null)
            {
                ome_pipeline_set_error_callback_raw(pipeline, IntPtr.Zero);
                return;
            }
            CallbackLifetimeManager.Register(pipeline, callback);
            IntPtr ptr = Marshal.GetFunctionPointerForDelegate(callback);
            ome_pipeline_set_error_callback_raw(pipeline, ptr);
        }

        public static void ome_pipeline_set_error_callback(SafePipelineHandle pipeline, ErrorCallback? callback)
        {
            ome_pipeline_set_error_callback(pipeline?.DangerousGetHandle() ?? IntPtr.Zero, callback);
        }

        // ─── Source API ─────────────────────────────────────────────────
        [LibraryImport(DllName, StringMarshalling = StringMarshalling.Utf8)]
        public static partial SafeMediaSourceHandle ome_source_create_file(string uri);

        [LibraryImport(DllName, EntryPoint = "ome_source_destroy")]
        internal static partial void ome_source_destroy_internal(IntPtr source);

        public static void ome_source_destroy(SafeMediaSourceHandle source)
        {
            source?.Dispose();
        }

        public static void ome_source_destroy(IntPtr source)
        {
            if (source != IntPtr.Zero)
            {
                ome_source_destroy_internal(source);
            }
        }

        // ─── Video Mixer API ────────────────────────────────────────────
        [LibraryImport(DllName)]
        public static partial SafeVideoMixerHandle ome_mixer_create();

        [LibraryImport(DllName, EntryPoint = "ome_mixer_destroy")]
        internal static partial void ome_mixer_destroy_internal(IntPtr mixer);

        public static void ome_mixer_destroy(SafeVideoMixerHandle mixer)
        {
            mixer?.Dispose();
        }

        public static void ome_mixer_destroy(IntPtr mixer)
        {
            if (mixer != IntPtr.Zero)
            {
                ome_mixer_destroy_internal(mixer);
            }
        }

        [LibraryImport(DllName)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_mixer_add_input(SafeVideoMixerHandle mixer, IntPtr source, int layer_index);

        [LibraryImport(DllName, EntryPoint = "ome_mixer_add_input")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_mixer_add_input(IntPtr mixer, IntPtr source, int layer_index);

        [LibraryImport(DllName, StringMarshalling = StringMarshalling.Utf8)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_mixer_set_lut(SafeVideoMixerHandle mixer, string lut_path, float intensity);

        [LibraryImport(DllName, EntryPoint = "ome_mixer_set_lut", StringMarshalling = StringMarshalling.Utf8)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_mixer_set_lut(IntPtr mixer, string lut_path, float intensity);

        // ─── Audio Mixer API ────────────────────────────────────────────
        [LibraryImport(DllName)]
        public static partial SafeAudioMixerHandle ome_audio_mixer_create();

        [LibraryImport(DllName, EntryPoint = "ome_audio_mixer_destroy")]
        internal static partial void ome_audio_mixer_destroy_internal(IntPtr mixer);

        public static void ome_audio_mixer_destroy(SafeAudioMixerHandle mixer)
        {
            mixer?.Dispose();
        }

        public static void ome_audio_mixer_destroy(IntPtr mixer)
        {
            if (mixer != IntPtr.Zero)
            {
                ome_audio_mixer_destroy_internal(mixer);
            }
        }

        [LibraryImport(DllName)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_audio_mixer_set_channel_volume(SafeAudioMixerHandle mixer, int channel, float volume);

        [LibraryImport(DllName, EntryPoint = "ome_audio_mixer_set_channel_volume")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_audio_mixer_set_channel_volume(IntPtr mixer, int channel, float volume);

        // ─── Audio Meter API ────────────────────────────────────────────
        [StructLayout(LayoutKind.Sequential)]
        public struct AudioChannelMeterData
        {
            public float PeakDb;
            public float RmsDb;
            public float Lufs;
            private byte _clipping;

            public bool Clipping
            {
                readonly get => _clipping != 0;
                set => _clipping = (byte)(value ? 1 : 0);
            }
        }

        [LibraryImport(DllName)]
        public static partial SafeAudioMeterHandle ome_audio_meter_create();

        [LibraryImport(DllName, EntryPoint = "ome_audio_meter_destroy")]
        internal static partial void ome_audio_meter_destroy_internal(IntPtr meter);

        public static void ome_audio_meter_destroy(SafeAudioMeterHandle meter)
        {
            meter?.Dispose();
        }

        public static void ome_audio_meter_destroy(IntPtr meter)
        {
            if (meter != IntPtr.Zero)
            {
                ome_audio_meter_destroy_internal(meter);
            }
        }

        [LibraryImport(DllName)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_audio_meter_process_pcm(SafeAudioMeterHandle meter, IntPtr data, uint sample_count, uint channel_count, uint sample_format, uint sample_rate);

        [LibraryImport(DllName, EntryPoint = "ome_audio_meter_process_pcm")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_audio_meter_process_pcm(IntPtr meter, IntPtr data, uint sample_count, uint channel_count, uint sample_format, uint sample_rate);

        [LibraryImport(DllName)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_audio_meter_get_channel_data(SafeAudioMeterHandle meter, [In, Out] AudioChannelMeterData[] out_data, uint max_channels, out uint actual_channels);

        [LibraryImport(DllName, EntryPoint = "ome_audio_meter_get_channel_data")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_audio_meter_get_channel_data(IntPtr meter, [In, Out] AudioChannelMeterData[] out_data, uint max_channels, out uint actual_channels);

        [LibraryImport(DllName)]
        public static partial void ome_audio_meter_reset(SafeAudioMeterHandle meter);

        [LibraryImport(DllName, EntryPoint = "ome_audio_meter_reset")]
        public static partial void ome_audio_meter_reset(IntPtr meter);

        // ─── Clock Overlay API ──────────────────────────────────────────
        [LibraryImport(DllName)]
        public static partial SafeOverlayHandle ome_clock_overlay_create();

        [LibraryImport(DllName, EntryPoint = "ome_overlay_destroy")]
        internal static partial void ome_overlay_destroy_internal(IntPtr overlay);

        public static void ome_overlay_destroy(SafeOverlayHandle overlay)
        {
            overlay?.Dispose();
        }

        public static void ome_overlay_destroy(IntPtr overlay)
        {
            if (overlay != IntPtr.Zero)
            {
                ome_overlay_destroy_internal(overlay);
            }
        }

        [LibraryImport(DllName, StringMarshalling = StringMarshalling.Utf8)]
        public static partial void ome_clock_overlay_set_format(SafeOverlayHandle overlay, string format);

        [LibraryImport(DllName, EntryPoint = "ome_clock_overlay_set_format", StringMarshalling = StringMarshalling.Utf8)]
        public static partial void ome_clock_overlay_set_format(IntPtr overlay, string format);

        // ─── SRT Engine API ─────────────────────────────────────────────
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

        [LibraryImport(DllName)]
        public static partial SafeSrtEngineHandle ome_srt_engine_create();

        [LibraryImport(DllName, EntryPoint = "ome_srt_engine_destroy")]
        internal static partial void ome_srt_engine_destroy_internal(IntPtr engine);

        public static void ome_srt_engine_destroy(SafeSrtEngineHandle engine)
        {
            engine?.Dispose();
        }

        public static void ome_srt_engine_destroy(IntPtr engine)
        {
            if (engine != IntPtr.Zero)
            {
                ome_srt_engine_destroy_internal(engine);
            }
        }

        [LibraryImport(DllName)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_srt_engine_init(SafeSrtEngineHandle engine);

        [LibraryImport(DllName, EntryPoint = "ome_srt_engine_init")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_srt_engine_init(IntPtr engine);

        // ─── SRT Source API ─────────────────────────────────────────────
        [LibraryImport(DllName)]
        public static partial SafeSrtSourceHandle ome_srt_source_create();

        [LibraryImport(DllName, EntryPoint = "ome_srt_source_destroy")]
        internal static partial void ome_srt_source_destroy_internal(IntPtr source);

        public static void ome_srt_source_destroy(SafeSrtSourceHandle source)
        {
            source?.Dispose();
        }

        public static void ome_srt_source_destroy(IntPtr source)
        {
            if (source != IntPtr.Zero)
            {
                ome_srt_source_destroy_internal(source);
            }
        }

        [LibraryImport(DllName, StringMarshalling = StringMarshalling.Utf8)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_srt_source_connect(SafeSrtSourceHandle source, string uri);

        [LibraryImport(DllName, EntryPoint = "ome_srt_source_connect", StringMarshalling = StringMarshalling.Utf8)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_srt_source_connect(IntPtr source, string uri);

        [LibraryImport(DllName)]
        public static partial void ome_srt_source_disconnect(SafeSrtSourceHandle source);

        [LibraryImport(DllName, EntryPoint = "ome_srt_source_disconnect")]
        public static partial void ome_srt_source_disconnect(IntPtr source);

        [LibraryImport(DllName)]
        public static partial int ome_srt_source_receive(SafeSrtSourceHandle source, [In, Out] byte[] buffer, int size);

        [LibraryImport(DllName, EntryPoint = "ome_srt_source_receive")]
        public static partial int ome_srt_source_receive(IntPtr source, [In, Out] byte[] buffer, int size);

        [LibraryImport(DllName)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_srt_source_is_connected(SafeSrtSourceHandle source);

        [LibraryImport(DllName, EntryPoint = "ome_srt_source_is_connected")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_srt_source_is_connected(IntPtr source);

        [LibraryImport(DllName)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_srt_source_get_stats(SafeSrtSourceHandle source, out SRTNativeStats stats);

        [LibraryImport(DllName, EntryPoint = "ome_srt_source_get_stats")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_srt_source_get_stats(IntPtr source, out SRTNativeStats stats);

        // ─── SRT Output API ─────────────────────────────────────────────
        [LibraryImport(DllName)]
        public static partial SafeSrtOutputHandle ome_srt_output_create();

        [LibraryImport(DllName, EntryPoint = "ome_srt_output_destroy")]
        internal static partial void ome_srt_output_destroy_internal(IntPtr output);

        public static void ome_srt_output_destroy(SafeSrtOutputHandle output)
        {
            output?.Dispose();
        }

        public static void ome_srt_output_destroy(IntPtr output)
        {
            if (output != IntPtr.Zero)
            {
                ome_srt_output_destroy_internal(output);
            }
        }

        [LibraryImport(DllName, StringMarshalling = StringMarshalling.Utf8)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_srt_output_open(SafeSrtOutputHandle output, string uri);

        [LibraryImport(DllName, EntryPoint = "ome_srt_output_open", StringMarshalling = StringMarshalling.Utf8)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_srt_output_open(IntPtr output, string uri);

        [LibraryImport(DllName)]
        public static partial void ome_srt_output_close(SafeSrtOutputHandle output);

        [LibraryImport(DllName, EntryPoint = "ome_srt_output_close")]
        public static partial void ome_srt_output_close(IntPtr output);

        [LibraryImport(DllName)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_srt_output_send(SafeSrtOutputHandle output, byte[] data, int size);

        [LibraryImport(DllName, EntryPoint = "ome_srt_output_send")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_srt_output_send(IntPtr output, byte[] data, int size);

        [LibraryImport(DllName)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_srt_output_send_msg(SafeSrtOutputHandle output, byte[] data, int size, int ttlMs, [MarshalAs(UnmanagedType.I1)] bool inOrder);

        [LibraryImport(DllName, EntryPoint = "ome_srt_output_send_msg")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_srt_output_send_msg(IntPtr output, byte[] data, int size, int ttlMs, [MarshalAs(UnmanagedType.I1)] bool inOrder);

        [LibraryImport(DllName)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_srt_output_is_connected(SafeSrtOutputHandle output);

        [LibraryImport(DllName, EntryPoint = "ome_srt_output_is_connected")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_srt_output_is_connected(IntPtr output);

        [LibraryImport(DllName)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_srt_output_get_stats(SafeSrtOutputHandle output, out SRTNativeStats stats);

        [LibraryImport(DllName, EntryPoint = "ome_srt_output_get_stats")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_srt_output_get_stats(IntPtr output, out SRTNativeStats stats);

        // ─── NDI / WebRTC Engines ───────────────────────────────────────
        [LibraryImport(DllName)]
        public static partial SafeNdiEngineHandle ome_ndi_engine_create();

        [LibraryImport(DllName, EntryPoint = "ome_ndi_engine_destroy")]
        internal static partial void ome_ndi_engine_destroy_internal(IntPtr engine);

        public static void ome_ndi_engine_destroy(SafeNdiEngineHandle engine)
        {
            engine?.Dispose();
        }

        public static void ome_ndi_engine_destroy(IntPtr engine)
        {
            if (engine != IntPtr.Zero)
            {
                ome_ndi_engine_destroy_internal(engine);
            }
        }

        [LibraryImport(DllName)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_ndi_engine_init(SafeNdiEngineHandle engine);

        [LibraryImport(DllName, EntryPoint = "ome_ndi_engine_init")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_ndi_engine_init(IntPtr engine);

        [LibraryImport(DllName)]
        public static partial SafeWebRtcEngineHandle ome_webrtc_engine_create();

        [LibraryImport(DllName, EntryPoint = "ome_webrtc_engine_destroy")]
        internal static partial void ome_webrtc_engine_destroy_internal(IntPtr engine);

        public static void ome_webrtc_engine_destroy(SafeWebRtcEngineHandle engine)
        {
            engine?.Dispose();
        }

        public static void ome_webrtc_engine_destroy(IntPtr engine)
        {
            if (engine != IntPtr.Zero)
            {
                ome_webrtc_engine_destroy_internal(engine);
            }
        }

        [LibraryImport(DllName)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_webrtc_engine_init(SafeWebRtcEngineHandle engine);

        [LibraryImport(DllName, EntryPoint = "ome_webrtc_engine_init")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_webrtc_engine_init(IntPtr engine);

        // ─── Hardware Codecs API ────────────────────────────────────────
        [LibraryImport(DllName)]
        public static partial SafeEncoderHandle ome_h264_encoder_nv_create();

        [LibraryImport(DllName)]
        public static partial SafeEncoderHandle ome_h264_encoder_qsv_create();

        [LibraryImport(DllName, EntryPoint = "ome_encoder_destroy")]
        internal static partial void ome_encoder_destroy_internal(IntPtr encoder);

        public static void ome_encoder_destroy(SafeEncoderHandle encoder)
        {
            encoder?.Dispose();
        }

        public static void ome_encoder_destroy(IntPtr encoder)
        {
            if (encoder != IntPtr.Zero)
            {
                ome_encoder_destroy_internal(encoder);
            }
        }

        [LibraryImport(DllName)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_encoder_initialize(SafeEncoderHandle encoder);

        [LibraryImport(DllName, EntryPoint = "ome_encoder_initialize")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_encoder_initialize(IntPtr encoder);

        // ─── Core Helper Nodes ──────────────────────────────────────────
        [LibraryImport(DllName, StringMarshalling = StringMarshalling.Utf8)]
        public static partial SafeMediaSourceHandle om_create_file_source(string filepath);

        [LibraryImport(DllName, StringMarshalling = StringMarshalling.Utf8)]
        public static partial SafeOutputHandle om_create_srt_output(string url);

        // ─── Scripting & Plugins ────────────────────────────────────────
        [LibraryImport(DllName, StringMarshalling = StringMarshalling.Utf8)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool om_run_lua_script(string scriptContent);

        [LibraryImport(DllName, StringMarshalling = StringMarshalling.Utf8)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool om_load_plugin(string pluginPath);

        [LibraryImport(DllName, StringMarshalling = StringMarshalling.Utf8)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_plugin_manager_load_directory(string directoryPath);

        // ─── Output API ─────────────────────────────────────────────────
        [LibraryImport(DllName)]
        public static partial SafeOutputHandle ome_rtmp_output_create();

        [LibraryImport(DllName)]
        public static partial SafeOutputHandle ome_webrtc_output_create();

        [LibraryImport(DllName, EntryPoint = "ome_output_destroy")]
        internal static partial void ome_output_destroy_internal(IntPtr output);

        public static void ome_output_destroy(SafeOutputHandle output)
        {
            output?.Dispose();
        }

        public static void ome_output_destroy(IntPtr output)
        {
            if (output != IntPtr.Zero)
            {
                CallbackLifetimeManager.UnregisterAll(output);
                ome_output_destroy_internal(output);
            }
        }

        [LibraryImport(DllName, StringMarshalling = StringMarshalling.Utf8)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_rtmp_output_open(SafeOutputHandle output, string url);

        [LibraryImport(DllName, EntryPoint = "ome_rtmp_output_open", StringMarshalling = StringMarshalling.Utf8)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_rtmp_output_open(IntPtr output, string url);

        [LibraryImport(DllName, StringMarshalling = StringMarshalling.Utf8)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_webrtc_output_open(SafeOutputHandle output, string signalingUri);

        [LibraryImport(DllName, EntryPoint = "ome_webrtc_output_open", StringMarshalling = StringMarshalling.Utf8)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_webrtc_output_open(IntPtr output, string signalingUri);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void FrameCallback(IntPtr frame, IntPtr userData);

        [LibraryImport(DllName, EntryPoint = "ome_callback_output_create")]
        private static partial SafeOutputHandle ome_callback_output_create_raw(IntPtr callbackPtr, IntPtr userData);

        public static SafeOutputHandle ome_callback_output_create(FrameCallback callback, IntPtr userData)
        {
            if (callback == null)
            {
                return ome_callback_output_create_raw(IntPtr.Zero, userData);
            }
            IntPtr ptr = Marshal.GetFunctionPointerForDelegate(callback);
            var handle = ome_callback_output_create_raw(ptr, userData);
            if (!handle.IsInvalid)
            {
                CallbackLifetimeManager.Register(handle.DangerousGetHandle(), callback);
            }
            return handle;
        }

        // ─── MediaFrame API ─────────────────────────────────────────────
        [LibraryImport(DllName)]
        public static partial SafeMediaFrameHandle ome_media_frame_create_video(int width, int height, int format);

        [LibraryImport(DllName, EntryPoint = "ome_media_frame_destroy")]
        internal static partial void ome_media_frame_destroy_internal(IntPtr frame);

        public static void ome_media_frame_destroy(SafeMediaFrameHandle frame)
        {
            frame?.Dispose();
        }

        public static void ome_media_frame_destroy(IntPtr frame)
        {
            if (frame != IntPtr.Zero)
            {
                ome_media_frame_destroy_internal(frame);
            }
        }

        [LibraryImport(DllName)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_media_frame_get_data(SafeMediaFrameHandle frame, int plane, out IntPtr data, out int stride);

        [LibraryImport(DllName, EntryPoint = "ome_media_frame_get_data")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_media_frame_get_data(IntPtr frame, int plane, out IntPtr data, out int stride);

        [LibraryImport(DllName)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_media_frame_get_video_info(SafeMediaFrameHandle frame, out int width, out int height, out int format);

        [LibraryImport(DllName, EntryPoint = "ome_media_frame_get_video_info")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_media_frame_get_video_info(IntPtr frame, out int width, out int height, out int format);

        // ─── Playlist API ───────────────────────────────────────────────
        [LibraryImport(DllName)]
        public static partial SafePlaylistHandle ome_playlist_create();

        [LibraryImport(DllName, EntryPoint = "ome_playlist_destroy")]
        internal static partial void ome_playlist_destroy_internal(IntPtr playlist);

        public static void ome_playlist_destroy(SafePlaylistHandle playlist)
        {
            playlist?.Dispose();
        }

        public static void ome_playlist_destroy(IntPtr playlist)
        {
            if (playlist != IntPtr.Zero)
            {
                ome_playlist_destroy_internal(playlist);
            }
        }

        [LibraryImport(DllName, StringMarshalling = StringMarshalling.Utf8)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_playlist_add_item(SafePlaylistHandle playlist, string uri);

        [LibraryImport(DllName, EntryPoint = "ome_playlist_add_item", StringMarshalling = StringMarshalling.Utf8)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_playlist_add_item(IntPtr playlist, string uri);

        // ─── CG API ─────────────────────────────────────────────────────
        [LibraryImport(DllName)]
        public static partial SafeCgEngineHandle ome_cg_engine_create();

        [LibraryImport(DllName, EntryPoint = "ome_cg_engine_destroy")]
        internal static partial void ome_cg_engine_destroy_internal(IntPtr engine);

        public static void ome_cg_engine_destroy(SafeCgEngineHandle engine)
        {
            engine?.Dispose();
        }

        public static void ome_cg_engine_destroy(IntPtr engine)
        {
            if (engine != IntPtr.Zero)
            {
                ome_cg_engine_destroy_internal(engine);
            }
        }

        [LibraryImport(DllName, StringMarshalling = StringMarshalling.Utf8)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_cg_engine_load_template(SafeCgEngineHandle engine, string templateData);

        [LibraryImport(DllName, EntryPoint = "ome_cg_engine_load_template", StringMarshalling = StringMarshalling.Utf8)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static partial bool ome_cg_engine_load_template(IntPtr engine, string templateData);

        // ─── Diagnostics & Error Handling ───────────────────────────────
        [LibraryImport(DllName)]
        public static partial IntPtr ome_get_last_error();

        public static string? GetLastErrorString()
        {
            IntPtr ptr = ome_get_last_error();
            return ptr == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(ptr);
        }
    }
}
