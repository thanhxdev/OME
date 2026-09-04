#ifndef OPENMEDIA_C_API_H
#define OPENMEDIA_C_API_H

#include <stdint.h>
#include <stdbool.h>

#ifdef _WIN32
#  define OME_API __declspec(dllexport)
#else
#  define OME_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

// Engine API
OME_API bool ome_engine_init(const char* config_json);
OME_API void ome_engine_shutdown();

// Shared Types
typedef void* ome_media_frame_t;

// Pipeline API
typedef void* ome_pipeline_t;
OME_API ome_pipeline_t ome_pipeline_create();
OME_API void ome_pipeline_destroy(ome_pipeline_t pipeline);
OME_API bool ome_pipeline_start(ome_pipeline_t pipeline);
OME_API bool ome_pipeline_stop(ome_pipeline_t pipeline);
OME_API bool ome_pipeline_add_node(ome_pipeline_t pipeline, void* node_handle);

// Pipeline Callbacks
typedef void (*ome_state_callback_t)(int new_state);
typedef void (*ome_error_callback_t)(int error_code, const char* message);
OME_API void ome_pipeline_set_state_callback(ome_pipeline_t pipeline, ome_state_callback_t callback);
OME_API void ome_pipeline_set_error_callback(ome_pipeline_t pipeline, ome_error_callback_t callback);

// Source API
typedef void* ome_source_t;
OME_API ome_source_t ome_source_create_file(const char* uri);
OME_API void ome_source_destroy(ome_source_t source);

// Mixer API
typedef void* ome_mixer_t;
OME_API ome_mixer_t ome_mixer_create();
OME_API void ome_mixer_destroy(ome_mixer_t mixer);
OME_API bool ome_mixer_add_input(ome_mixer_t mixer, ome_source_t source, int layer_index);
OME_API bool ome_mixer_set_lut(ome_mixer_t mixer, const char* lut_path, float intensity);

// Audio Mixer API
typedef void* ome_audio_mixer_t;
OME_API ome_audio_mixer_t ome_audio_mixer_create();
OME_API void ome_audio_mixer_destroy(ome_audio_mixer_t mixer);
OME_API bool ome_audio_mixer_set_channel_volume(ome_audio_mixer_t mixer, int channel, float volume);

// Audio Meter API
typedef void* ome_audio_meter_t;

typedef struct {
    float peak_db;
    float rms_db;
    float lufs;
    bool clipping;
} ome_audio_channel_meter_t;

OME_API ome_audio_meter_t ome_audio_meter_create();
OME_API void ome_audio_meter_destroy(ome_audio_meter_t meter);
OME_API bool ome_audio_meter_process_pcm(ome_audio_meter_t meter, const void* data, uint32_t sample_count, uint32_t channel_count, uint32_t sample_format, uint32_t sample_rate);
OME_API bool ome_audio_meter_get_channel_data(ome_audio_meter_t meter, ome_audio_channel_meter_t* out_data, uint32_t max_channels, uint32_t* actual_channels);
OME_API void ome_audio_meter_reset(ome_audio_meter_t meter);

// Overlay API
typedef void* ome_overlay_t;
OME_API ome_overlay_t ome_clock_overlay_create();
OME_API void ome_overlay_destroy(ome_overlay_t overlay);
OME_API void ome_clock_overlay_set_format(ome_overlay_t overlay, const char* format);

// Protocols API (SRT, NDI, WebRTC)
typedef struct {
    int64_t ms_rtt;
    int32_t pkt_loss_total;
    int32_t mbps_bandwidth;
    int32_t pkt_retransmit_total;
    int32_t pkt_sent_total;
    int32_t pkt_recv_total;
    int32_t pkt_drop_total;
    uint64_t bytes_sent_total;
    uint64_t bytes_recv_total;
} ome_srt_stats_t;

typedef void* ome_srt_engine_t;
OME_API ome_srt_engine_t ome_srt_engine_create();
OME_API void ome_srt_engine_destroy(ome_srt_engine_t engine);
OME_API bool ome_srt_engine_init(ome_srt_engine_t engine);

// SRT Source API
typedef void* ome_srt_source_t;
OME_API ome_srt_source_t ome_srt_source_create();
OME_API void ome_srt_source_destroy(ome_srt_source_t source);
OME_API bool ome_srt_source_connect(ome_srt_source_t source, const char* uri);
OME_API void ome_srt_source_disconnect(ome_srt_source_t source);
OME_API int ome_srt_source_receive(ome_srt_source_t source, uint8_t* buffer, int size);
OME_API bool ome_srt_source_is_connected(ome_srt_source_t source);
OME_API bool ome_srt_source_get_stats(ome_srt_source_t source, ome_srt_stats_t* stats);

// Shared Output Type
typedef void* ome_output_t;

// SRT Output API
OME_API ome_output_t ome_srt_output_create();
OME_API void ome_srt_output_destroy(ome_output_t output);
OME_API bool ome_srt_output_open(ome_output_t output, const char* uri);
OME_API void ome_srt_output_close(ome_output_t output);
OME_API bool ome_srt_output_send(ome_output_t output, const uint8_t* data, int size);
OME_API bool ome_srt_output_is_connected(ome_output_t output);
OME_API bool ome_srt_output_get_stats(ome_output_t output, ome_srt_stats_t* stats);

typedef void* ome_ndi_engine_t;
OME_API ome_ndi_engine_t ome_ndi_engine_create();
OME_API void ome_ndi_engine_destroy(ome_ndi_engine_t engine);
OME_API bool ome_ndi_engine_init(ome_ndi_engine_t engine);

typedef void* ome_webrtc_engine_t;
OME_API ome_webrtc_engine_t ome_webrtc_engine_create();
OME_API void ome_webrtc_engine_destroy(ome_webrtc_engine_t engine);
OME_API bool ome_webrtc_engine_init(ome_webrtc_engine_t engine);

// Codecs API
typedef void* ome_encoder_t;
OME_API ome_encoder_t ome_h264_encoder_nv_create();
OME_API ome_encoder_t ome_h264_encoder_qsv_create();
OME_API void ome_encoder_destroy(ome_encoder_t encoder);
OME_API bool ome_encoder_initialize(ome_encoder_t encoder);

// Output API
OME_API ome_output_t ome_rtmp_output_create();
OME_API ome_output_t ome_webrtc_output_create();
OME_API void ome_output_destroy(ome_output_t output);
OME_API bool ome_rtmp_output_open(ome_output_t output, const char* url);
OME_API bool ome_webrtc_output_open(ome_output_t output, const char* signaling_uri);

typedef void (*ome_frame_callback_t)(ome_media_frame_t frame, void* user_data);
OME_API ome_output_t ome_callback_output_create(ome_frame_callback_t callback, void* user_data);

// Plugin API
OME_API bool ome_plugin_manager_load_directory(const char* directory_path);

// MediaFrame API
OME_API ome_media_frame_t ome_media_frame_create_video(int width, int height, int format);
OME_API void ome_media_frame_destroy(ome_media_frame_t frame);
OME_API bool ome_media_frame_get_data(ome_media_frame_t frame, int plane, uint8_t** data, int* stride);
OME_API bool ome_media_frame_get_video_info(ome_media_frame_t frame, int* width, int* height, int* format);

// Error Handling
OME_API const char* ome_get_last_error();

#ifdef __cplusplus
}
#endif

#endif // OPENMEDIA_C_API_H
