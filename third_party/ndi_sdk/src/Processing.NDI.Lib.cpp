#include "Processing.NDI.Lib.h"
#include <iostream>

bool NDIlib_initialize(void) {
    std::cout << "[Mock NDI] Initialized\n";
    return true;
}

void NDIlib_destroy(void) {
    std::cout << "[Mock NDI] Destroyed\n";
}

NDIlib_send_instance_t NDIlib_send_create(const NDIlib_send_create_t* p_create_settings) {
    std::cout << "[Mock NDI] Send instance created for: " 
              << (p_create_settings && p_create_settings->p_ndi_name ? p_create_settings->p_ndi_name : "unknown") << "\n";
    return (NDIlib_send_instance_t)0xDEADBEEF;
}

void NDIlib_send_destroy(NDIlib_send_instance_t p_instance) {
    std::cout << "[Mock NDI] Send instance destroyed\n";
}

void NDIlib_send_send_video_v2(NDIlib_send_instance_t p_instance, const NDIlib_video_frame_v2_t* p_video_data) {
    // std::cout << "[Mock NDI] Sending video frame\n";
}

void NDIlib_send_send_audio_v2(NDIlib_send_instance_t p_instance, const NDIlib_audio_frame_v2_t* p_audio_data) {
    // std::cout << "[Mock NDI] Sending audio frame\n";
}

NDIlib_recv_instance_t NDIlib_recv_create_v3(const NDIlib_recv_create_v3_t* p_create_settings) {
    std::cout << "[Mock NDI] Recv instance created to connect to: " 
              << (p_create_settings && p_create_settings->source_to_connect_to.p_ndi_name ? p_create_settings->source_to_connect_to.p_ndi_name : "unknown") << "\n";
    return (NDIlib_recv_instance_t)0xCAFEBABE;
}

void NDIlib_recv_destroy(NDIlib_recv_instance_t p_instance) {
    std::cout << "[Mock NDI] Recv instance destroyed\n";
}

NDIlib_frame_type_e NDIlib_recv_capture_v2(
    NDIlib_recv_instance_t p_instance,
    NDIlib_video_frame_v2_t* p_video_data,
    NDIlib_audio_frame_v2_t* p_audio_data,
    void* p_metadata,
    uint32_t timeout_in_ms
) {
    return NDIlib_frame_type_none; // Mock always returns none (timeout) for now
}

void NDIlib_recv_free_video_v2(NDIlib_recv_instance_t p_instance, const NDIlib_video_frame_v2_t* p_video_data) {
}

void NDIlib_recv_free_audio_v2(NDIlib_recv_instance_t p_instance, const NDIlib_audio_frame_v2_t* p_audio_data) {
}
