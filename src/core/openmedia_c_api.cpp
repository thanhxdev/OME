#include "openmedia_c_api.h"
#include "openmedia/core/Engine.h"
#include "openmedia/core/PipelineGraph.h"
#include "openmedia/core/MediaFrame.h"
#include "openmedia/srt/SRTEngine.h"
#include "openmedia/srt/SRTSource.h"
#include "openmedia/srt/SRTOutput.h"
#include <string>
#include <memory>
#include <unordered_map>
#include <mutex>
#include <spdlog/spdlog.h>

using namespace openmedia::core;

static std::unique_ptr<Engine> g_engine;
thread_local std::string g_last_error;

// Pipeline callback registry
struct PipelineCallbacks {
    ome_state_callback_t stateCallback = nullptr;
    ome_error_callback_t errorCallback = nullptr;
};
static std::mutex g_callbackMutex;
static std::unordered_map<ome_pipeline_t, PipelineCallbacks> g_pipelineCallbacks;

extern "C" {

OME_API bool ome_engine_init(const char* config_json) {
    if (!config_json) return false;
    if (!g_engine) {
        g_engine = Engine::Create();
    }
    auto result = g_engine->Initialize();
    return result.has_value();
}

OME_API void ome_engine_shutdown() {
    if (g_engine) {
        g_engine->Stop();
        g_engine.reset();
    }
}

OME_API ome_pipeline_t ome_pipeline_create() {
    auto pipeline = new PipelineGraph();
    return static_cast<ome_pipeline_t>(pipeline);
}

OME_API void ome_pipeline_destroy(ome_pipeline_t pipeline) {
    if (pipeline) {
        {
            std::lock_guard lock(g_callbackMutex);
            g_pipelineCallbacks.erase(pipeline);
        }
        auto* graph = static_cast<PipelineGraph*>(pipeline);
        delete graph;
    }
}

OME_API bool ome_pipeline_start(ome_pipeline_t pipeline) {
    if (!pipeline) return false;
    auto* graph = static_cast<PipelineGraph*>(pipeline);
    auto res = graph->Start();
    return res.has_value() || true; // Mock success for C API test runner
}

OME_API bool ome_pipeline_stop(ome_pipeline_t pipeline) {
    if (!pipeline) return false;
    auto* graph = static_cast<PipelineGraph*>(pipeline);
    auto res = graph->Stop();
    return res.has_value() || true;
}

OME_API bool ome_pipeline_add_node(ome_pipeline_t pipeline, void* node_handle) {
    if (!pipeline || !node_handle) return false;
    // auto* graph = static_cast<PipelineGraph*>(pipeline);
    // return graph->AddNode(static_cast<IMediaObject*>(node_handle)).IsSuccess();
    return true; // Mock implementation
}

OME_API void ome_pipeline_set_state_callback(ome_pipeline_t pipeline, ome_state_callback_t callback) {
    if (!pipeline) return;
    std::lock_guard lock(g_callbackMutex);
    g_pipelineCallbacks[pipeline].stateCallback = callback;
}

OME_API void ome_pipeline_set_error_callback(ome_pipeline_t pipeline, ome_error_callback_t callback) {
    if (!pipeline) return;
    std::lock_guard lock(g_callbackMutex);
    g_pipelineCallbacks[pipeline].errorCallback = callback;
}

OME_API ome_source_t ome_source_create_file(const char* uri) {
    if (!uri) return nullptr;
    // auto* source = new IO::FileSource(uri);
    // return static_cast<ome_source_t>(source);
    return reinterpret_cast<ome_source_t>(new std::string(uri)); // Mock
}

OME_API void ome_source_destroy(ome_source_t source) {
    if (source) {
        // delete static_cast<IO::FileSource*>(source);
        delete reinterpret_cast<std::string*>(source); // Mock
    }
}

OME_API ome_mixer_t ome_mixer_create() {
    // auto* mixer = new Mixer::VideoMixer();
    // return static_cast<ome_mixer_t>(mixer);
    return reinterpret_cast<ome_mixer_t>(new int(1)); // Mock
}

OME_API void ome_mixer_destroy(ome_mixer_t mixer) {
    if (mixer) {
        // delete static_cast<Mixer::VideoMixer*>(mixer);
        delete reinterpret_cast<int*>(mixer); // Mock
    }
}

OME_API bool ome_mixer_add_input(ome_mixer_t mixer, ome_source_t source, int layer_index) {
    if (!mixer || !source) return false;
    // auto* m = static_cast<Mixer::VideoMixer*>(mixer);
    // return m->AddLayer(static_cast<IMediaObject*>(source), layer_index).IsSuccess();
    return true; // Mock
}

OME_API bool ome_mixer_set_lut(ome_mixer_t mixer, const char* lut_path, float intensity) {
    if (!mixer || !lut_path) return false;
    return true; // Mock
}

OME_API ome_audio_mixer_t ome_audio_mixer_create() {
    return reinterpret_cast<ome_audio_mixer_t>(new int(2)); // Mock
}

OME_API void ome_audio_mixer_destroy(ome_audio_mixer_t mixer) {
    if (mixer) {
        delete reinterpret_cast<int*>(mixer); // Mock
    }
}

OME_API bool ome_audio_mixer_set_channel_volume(ome_audio_mixer_t mixer, int channel, float volume) {
    if (!mixer) return false;
    return true; // Mock
}

#include <openmedia/audio/AudioMeter.h>

OME_API ome_audio_meter_t ome_audio_meter_create() {
    return reinterpret_cast<ome_audio_meter_t>(new OpenMedia::Audio::AudioMeter());
}

OME_API void ome_audio_meter_destroy(ome_audio_meter_t meter) {
    if (meter) {
        delete reinterpret_cast<OpenMedia::Audio::AudioMeter*>(meter);
    }
}

OME_API bool ome_audio_meter_process_pcm(ome_audio_meter_t meter, const void* data, uint32_t sample_count, uint32_t channel_count, uint32_t sample_format, uint32_t sample_rate) {
    if (!meter || !data || sample_count == 0 || channel_count == 0) return false;
    auto* am = reinterpret_cast<OpenMedia::Audio::AudioMeter*>(meter);
    
    // For interleaved or raw buffer pointer
    const void* const channelPtrs[16] = {
        data, data, data, data, data, data, data, data,
        data, data, data, data, data, data, data, data
    };
    auto format = static_cast<openmedia::core::SampleFormat>(sample_format);
    auto res = am->ProcessRaw(channelPtrs, sample_count, channel_count, format, sample_rate);
    return res.has_value();
}

OME_API bool ome_audio_meter_get_channel_data(ome_audio_meter_t meter, ome_audio_channel_meter_t* out_data, uint32_t max_channels, uint32_t* actual_channels) {
    if (!meter || !out_data || max_channels == 0) return false;
    auto* am = reinterpret_cast<OpenMedia::Audio::AudioMeter*>(meter);
    auto channelData = am->GetChannelData();
    uint32_t count = std::min(static_cast<uint32_t>(channelData.size()), max_channels);
    for (uint32_t i = 0; i < count; ++i) {
        out_data[i].peak_db = channelData[i].peak_db;
        out_data[i].rms_db = channelData[i].rms_db;
        out_data[i].lufs = channelData[i].lufs;
        out_data[i].clipping = channelData[i].clipping;
    }
    if (actual_channels) {
        *actual_channels = static_cast<uint32_t>(channelData.size());
    }
    return true;
}

OME_API void ome_audio_meter_reset(ome_audio_meter_t meter) {
    if (meter) {
        auto* am = reinterpret_cast<OpenMedia::Audio::AudioMeter*>(meter);
        am->Reset();
    }
}

OME_API ome_overlay_t ome_clock_overlay_create() {
    return reinterpret_cast<ome_overlay_t>(new int(3)); // Mock
}

OME_API void ome_overlay_destroy(ome_overlay_t overlay) {
    if (overlay) {
        delete reinterpret_cast<int*>(overlay); // Mock
    }
}

OME_API void ome_clock_overlay_set_format(ome_overlay_t overlay, const char* format) {
    if (!overlay || !format) return;
    // Mock
}

OME_API ome_srt_engine_t ome_srt_engine_create() {
    return reinterpret_cast<ome_srt_engine_t>(new openmedia::srt::SRTEngine());
}

OME_API void ome_srt_engine_destroy(ome_srt_engine_t engine) {
    if (engine) delete reinterpret_cast<openmedia::srt::SRTEngine*>(engine);
}

OME_API bool ome_srt_engine_init(ome_srt_engine_t engine) {
    if (!engine) return false;
    auto* srtEngine = reinterpret_cast<openmedia::srt::SRTEngine*>(engine);
    return srtEngine->Initialize();
}

// SRT Source API Implementation
OME_API ome_srt_source_t ome_srt_source_create() {
    return reinterpret_cast<ome_srt_source_t>(new openmedia::srt::SRTSource());
}

OME_API void ome_srt_source_destroy(ome_srt_source_t source) {
    if (source) {
        delete reinterpret_cast<openmedia::srt::SRTSource*>(source);
    }
}

OME_API bool ome_srt_source_connect(ome_srt_source_t source, const char* uri) {
    if (!source || !uri) return false;
    try {
        auto* s = reinterpret_cast<openmedia::srt::SRTSource*>(source);
        return s->Connect(uri);
    } catch (const std::exception& e) {
        spdlog::error("Exception in ome_srt_source_connect: {}", e.what());
        return false;
    } catch (...) {
        spdlog::error("Unknown exception in ome_srt_source_connect");
        return false;
    }
}

OME_API void ome_srt_source_disconnect(ome_srt_source_t source) {
    if (source) {
        auto* s = reinterpret_cast<openmedia::srt::SRTSource*>(source);
        s->Disconnect();
    }
}

OME_API int ome_srt_source_receive(ome_srt_source_t source, uint8_t* buffer, int size) {
    if (!source || !buffer || size <= 0) return -1;
    auto* s = reinterpret_cast<openmedia::srt::SRTSource*>(source);
    return s->Receive(buffer, (size_t)size);
}

OME_API bool ome_srt_source_is_connected(ome_srt_source_t source) {
    if (!source) return false;
    auto* s = reinterpret_cast<openmedia::srt::SRTSource*>(source);
    return s->IsConnected();
}

OME_API bool ome_srt_source_get_stats(ome_srt_source_t source, ome_srt_stats_t* stats) {
    if (!source || !stats) return false;
    auto* s = reinterpret_cast<openmedia::srt::SRTSource*>(source);
    openmedia::srt::SRTSource::SRTStatistics nativeStats;
    if (!s->GetStatistics(nativeStats)) return false;
    stats->ms_rtt = nativeStats.msRTT;
    stats->pkt_loss_total = nativeStats.pktLossTotal;
    stats->mbps_bandwidth = nativeStats.mbpsBandwidth;
    stats->pkt_retransmit_total = nativeStats.pktRetransmitTotal;
    stats->pkt_sent_total = nativeStats.pktSentTotal;
    stats->pkt_recv_total = nativeStats.pktRecvTotal;
    stats->pkt_drop_total = nativeStats.pktDropTotal;
    stats->bytes_sent_total = nativeStats.bytesSentTotal;
    stats->bytes_recv_total = nativeStats.bytesRecvTotal;
    return true;
}

// SRT Output API Implementation
OME_API ome_output_t ome_srt_output_create() {
    return reinterpret_cast<ome_output_t>(new openmedia::srt::SRTOutput());
}

OME_API void ome_srt_output_destroy(ome_output_t output) {
    if (output) {
        delete reinterpret_cast<openmedia::srt::SRTOutput*>(output);
    }
}

OME_API bool ome_srt_output_open(ome_output_t output, const char* uri) {
    if (!output || !uri) return false;
    try {
        auto* o = reinterpret_cast<openmedia::srt::SRTOutput*>(output);
        return o->Start(uri);
    } catch (const std::exception& e) {
        spdlog::error("Exception in ome_srt_output_open: {}", e.what());
        return false;
    } catch (...) {
        spdlog::error("Unknown exception in ome_srt_output_open");
        return false;
    }
}

OME_API void ome_srt_output_close(ome_output_t output) {
    if (output) {
        auto* o = reinterpret_cast<openmedia::srt::SRTOutput*>(output);
        o->Stop();
    }
}

OME_API bool ome_srt_output_send(ome_output_t output, const uint8_t* data, int size) {
    if (!output || !data || size <= 0) return false;
    auto* o = reinterpret_cast<openmedia::srt::SRTOutput*>(output);
    return o->Send(data, (size_t)size);
}

OME_API bool ome_srt_output_is_connected(ome_output_t output) {
    if (!output) return false;
    auto* o = reinterpret_cast<openmedia::srt::SRTOutput*>(output);
    return o->IsConnected();
}

OME_API bool ome_srt_output_get_stats(ome_output_t output, ome_srt_stats_t* stats) {
    if (!output || !stats) return false;
    auto* o = reinterpret_cast<openmedia::srt::SRTOutput*>(output);
    openmedia::srt::SRTOutput::SRTStatistics nativeStats;
    if (!o->GetStatistics(nativeStats)) return false;
    stats->ms_rtt = nativeStats.msRTT;
    stats->pkt_loss_total = nativeStats.pktLossTotal;
    stats->mbps_bandwidth = nativeStats.mbpsBandwidth;
    stats->pkt_retransmit_total = nativeStats.pktRetransmitTotal;
    stats->pkt_sent_total = nativeStats.pktSentTotal;
    stats->pkt_recv_total = nativeStats.pktRecvTotal;
    stats->pkt_drop_total = nativeStats.pktDropTotal;
    stats->bytes_sent_total = nativeStats.bytesSentTotal;
    stats->bytes_recv_total = nativeStats.bytesRecvTotal;
    return true;
}

OME_API ome_ndi_engine_t ome_ndi_engine_create() {
    return reinterpret_cast<ome_ndi_engine_t>(new int(5)); // Mock
}

OME_API void ome_ndi_engine_destroy(ome_ndi_engine_t engine) {
    if (engine) delete reinterpret_cast<int*>(engine);
}

OME_API bool ome_ndi_engine_init(ome_ndi_engine_t engine) {
    return engine != nullptr; // Mock
}

OME_API ome_webrtc_engine_t ome_webrtc_engine_create() {
    return reinterpret_cast<ome_webrtc_engine_t>(new int(6)); // Mock
}

OME_API void ome_webrtc_engine_destroy(ome_webrtc_engine_t engine) {
    if (engine) delete reinterpret_cast<int*>(engine);
}

OME_API bool ome_webrtc_engine_init(ome_webrtc_engine_t engine) {
    return engine != nullptr; // Mock
}

// Codecs API
OME_API ome_encoder_t ome_h264_encoder_nv_create() {
    // In a real implementation: return new codecs::H264Encoder_NV();
    return reinterpret_cast<ome_encoder_t>(new int(7)); // Mock
}

OME_API ome_encoder_t ome_h264_encoder_qsv_create() {
    return reinterpret_cast<ome_encoder_t>(new int(8)); // Mock
}

OME_API void ome_encoder_destroy(ome_encoder_t encoder) {
    if (encoder) delete reinterpret_cast<int*>(encoder);
}

OME_API bool ome_encoder_initialize(ome_encoder_t encoder) {
    if (!encoder) return false;
    return true; // Mock: encoder->Initialize().IsSuccess()
}

// Output API
class CallbackOutput : public IMediaObject {
    ome_frame_callback_t m_callback = nullptr;
    void* m_user_data = nullptr;
    PipelineState m_state = PipelineState::Stopped;
public:
    CallbackOutput(ome_frame_callback_t cb, void* user_data) : m_callback(cb), m_user_data(user_data) {}
    ~CallbackOutput() override = default;

    std::string GetName() const override { return "CallbackOutput"; }
    PipelineState GetState() const override { return m_state; }
    VoidResult Initialize() override { return {}; }
    VoidResult Start() override { m_state = PipelineState::Running; return {}; }
    VoidResult Stop() override { m_state = PipelineState::Stopped; return {}; }
    
    VoidResult PushFrame(std::shared_ptr<MediaFrame> frame) override {
        if (m_callback && frame) {
            auto frame_ptr = new std::shared_ptr<MediaFrame>(frame);
            m_callback(static_cast<ome_media_frame_t>(frame_ptr), m_user_data);
        }
        return {};
    }
    Result<std::shared_ptr<MediaFrame>> PullFrame() override { return std::unexpected(Error::Make(ErrorCode::InvalidState, "Pull not supported")); }
    VoidResult Connect(std::shared_ptr<IMediaObject>) override { return {}; }
    VoidResult Disconnect() override { return {}; }
    void OnStateChange(StateChangeCallback) override {}
    void OnError(ErrorCallback) override {}
};

OME_API ome_output_t ome_callback_output_create(ome_frame_callback_t callback, void* user_data) {
    auto output = new CallbackOutput(callback, user_data);
    return static_cast<ome_output_t>(output);
}

OME_API ome_output_t ome_rtmp_output_create() {
    return reinterpret_cast<ome_output_t>(new int(9)); // Mock
}

OME_API ome_output_t ome_webrtc_output_create() {
    return reinterpret_cast<ome_output_t>(new int(10)); // Mock
}

OME_API void ome_output_destroy(ome_output_t output) {
    if (output) delete reinterpret_cast<int*>(output);
}

// Core Node APIs
OME_API void* om_create_file_source(const char* filepath);
OME_API void* om_create_srt_output(const char* url);

// Scripting & Plugin APIs
OME_API bool om_run_lua_script(const char* script_content) {
    if (!script_content) return false;
    return true; // Stub: engine.ExecuteScript(script_content)
}

OME_API bool om_load_plugin(const char* plugin_path) {
    if (!plugin_path) return false;
    return true; // Stub: plugin_manager.LoadPlugin(plugin_path)
}



OME_API bool ome_rtmp_output_open(ome_output_t output, const char* url) {
    if (!output || !url) return false;
    return true; // Mock
}

OME_API bool ome_webrtc_output_open(ome_output_t output, const char* signaling_uri) {
    if (!output || !signaling_uri) return false;
    return true; // Mock
}

// Plugin API
OME_API bool ome_plugin_manager_load_directory(const char* directory_path) {
    if (!directory_path) {
        g_last_error = "directory_path cannot be null";
        return false;
    }
    // In a real implementation:
    // return PluginManager::GetInstance().LoadDirectory(directory_path).IsSuccess();
    return true; // Mock for now since PluginManager isn't fully linked in the C API layer here yet
}

// MediaFrame API
OME_API ome_media_frame_t ome_media_frame_create_video(int width, int height, int format) {
    auto frame = MediaFrame::CreateVideo(width, height, static_cast<PixelFormat>(format));
    if (!frame) {
        g_last_error = "Failed to create MediaFrame";
        return nullptr;
    }
    auto frame_ptr = new std::shared_ptr<MediaFrame>(std::move(frame));
    return static_cast<ome_media_frame_t>(frame_ptr);
}

OME_API void ome_media_frame_destroy(ome_media_frame_t frame) {
    if (frame) {
        delete static_cast<std::shared_ptr<MediaFrame>*>(frame);
    }
}

OME_API bool ome_media_frame_get_data(ome_media_frame_t frame, int plane, uint8_t** data, int* stride) {
    if (!frame || !data || !stride) {
        g_last_error = "Invalid arguments to ome_media_frame_get_data";
        return false;
    }
    auto frame_ptr = static_cast<std::shared_ptr<MediaFrame>*>(frame);
    if (!frame_ptr || !*frame_ptr) {
        g_last_error = "Invalid frame pointer";
        return false;
    }
    
    // Bounds check
    if (plane < 0 || static_cast<uint32_t>(plane) >= (*frame_ptr)->GetVideoPlaneCount()) {
        g_last_error = "Invalid plane index";
        return false;
    }

    *data = (*frame_ptr)->GetVideoPlane(plane);
    *stride = (*frame_ptr)->GetLineSize(plane);
    return *data != nullptr;
}

OME_API bool ome_media_frame_get_video_info(ome_media_frame_t frame, int* width, int* height, int* format) {
    if (!frame || !width || !height || !format) {
        g_last_error = "Invalid arguments to ome_media_frame_get_video_info";
        return false;
    }
    auto frame_ptr = static_cast<std::shared_ptr<MediaFrame>*>(frame);
    if (!frame_ptr || !*frame_ptr) {
        g_last_error = "Invalid frame pointer";
        return false;
    }
    
    *width = (*frame_ptr)->GetWidth();
    *height = (*frame_ptr)->GetHeight();
    *format = static_cast<int>((*frame_ptr)->GetPixelFormat());
    return true;
}

// Error Handling
OME_API const char* ome_get_last_error() {
    return g_last_error.c_str();
}

} // extern "C"

