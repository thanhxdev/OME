#pragma once

#include <stdint.h>
#include <stdbool.h>
#include <stddef.h>

#ifdef __cplusplus
extern "C" {
#endif

// Mock NDI types
typedef void* NDIlib_send_instance_t;
typedef void* NDIlib_recv_instance_t;

typedef enum NDIlib_frame_type_e {
    NDIlib_frame_type_none = 0,
    NDIlib_frame_type_video = 1,
    NDIlib_frame_type_audio = 2,
    NDIlib_frame_type_metadata = 3,
    NDIlib_frame_type_error = 4,
    NDIlib_frame_type_status_change = 100
} NDIlib_frame_type_e;

typedef struct NDIlib_video_frame_v2_t {
    int xres;
    int yres;
    int FourCC;
    int frame_rate_N;
    int frame_rate_D;
    float picture_aspect_ratio;
    int frame_format_type;
    int timecode;
    uint8_t* p_data;
    int line_stride_in_bytes;
    const char* p_metadata;
    int64_t timestamp;
} NDIlib_video_frame_v2_t;

typedef struct NDIlib_audio_frame_v2_t {
    int sample_rate;
    int no_channels;
    int no_samples;
    int timecode;
    float* p_data;
    int channel_stride_in_bytes;
    const char* p_metadata;
    int64_t timestamp;
} NDIlib_audio_frame_v2_t;

typedef struct NDIlib_send_create_t {
    const char* p_ndi_name;
    const char* p_groups;
    bool clock_video;
    bool clock_audio;
} NDIlib_send_create_t;

typedef struct NDIlib_source_t {
    const char* p_ndi_name;
    const char* p_url_address;
} NDIlib_source_t;

typedef struct NDIlib_recv_create_v3_t {
    NDIlib_source_t source_to_connect_to;
    int color_format;
    int bandwidth;
    bool allow_video_fields;
    const char* p_ndi_recv_name;
} NDIlib_recv_create_v3_t;

// Mock NDI functions
bool NDIlib_initialize(void);
void NDIlib_destroy(void);

NDIlib_send_instance_t NDIlib_send_create(const NDIlib_send_create_t* p_create_settings);
void NDIlib_send_destroy(NDIlib_send_instance_t p_instance);
void NDIlib_send_send_video_v2(NDIlib_send_instance_t p_instance, const NDIlib_video_frame_v2_t* p_video_data);
void NDIlib_send_send_audio_v2(NDIlib_send_instance_t p_instance, const NDIlib_audio_frame_v2_t* p_audio_data);

NDIlib_recv_instance_t NDIlib_recv_create_v3(const NDIlib_recv_create_v3_t* p_create_settings);
void NDIlib_recv_destroy(NDIlib_recv_instance_t p_instance);
NDIlib_frame_type_e NDIlib_recv_capture_v2(
    NDIlib_recv_instance_t p_instance,
    NDIlib_video_frame_v2_t* p_video_data,
    NDIlib_audio_frame_v2_t* p_audio_data,
    void* p_metadata,
    uint32_t timeout_in_ms
);
void NDIlib_recv_free_video_v2(NDIlib_recv_instance_t p_instance, const NDIlib_video_frame_v2_t* p_video_data);
void NDIlib_recv_free_audio_v2(NDIlib_recv_instance_t p_instance, const NDIlib_audio_frame_v2_t* p_audio_data);

#ifdef __cplusplus
}
#endif
