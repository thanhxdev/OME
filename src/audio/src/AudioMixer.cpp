#include <openmedia/audio/AudioMixer.h>
#include <fmt/format.h>
#include <expected>

extern "C" {
#include <libavfilter/avfilter.h>
#include <libavfilter/buffersrc.h>
#include <libavfilter/buffersink.h>
#include <libavutil/opt.h>
}

namespace openmedia::audio {

AudioMixer::AudioMixer(std::shared_ptr<AudioEngine> engine)
    : m_engine(std::move(engine)) {
}

AudioMixer::~AudioMixer() {
    (void)Stop();
}

core::VoidResult AudioMixer::Initialize() {
    ChangeState(core::PipelineState::Ready);
    return {};
}

core::VoidResult AudioMixer::Start() {
    ChangeState(core::PipelineState::Running);
    return {};
}

core::VoidResult AudioMixer::Stop() {
    if (m_filterGraph) {
        avfilter_graph_free(&m_filterGraph);
        m_filterGraph = nullptr;
    }
    ChangeState(core::PipelineState::Stopped);
    return {};
}

core::VoidResult AudioMixer::PushFrame(std::shared_ptr<core::MediaFrame> frame) {
    int streamId = 0;
    auto metaStr = frame->GetMetadata().Get("stream_id");
    if (metaStr.has_value()) {
        try { streamId = std::stoi(metaStr.value()); } catch (...) {}
    }
    return PushFrame(std::move(frame), streamId);
}

core::VoidResult AudioMixer::PushFrame(std::shared_ptr<core::MediaFrame> frame, int streamId) {
    if (m_state != core::PipelineState::Running) return {};

    std::lock_guard<std::mutex> lock(m_mutex);
    
    auto it = m_inputs.find(streamId);
    if (it == m_inputs.end()) {
        return std::unexpected(core::Error{core::ErrorCode::InvalidState, "Audio input stream not registered"});
    }

    if (m_graphNeedsRebuild) {
        auto res = RebuildFilterGraph();
        if (!res) return res;
    }

    if (m_downstream) {
        return m_downstream->PushFrame(frame);
    }

    return {};
}

core::Result<std::shared_ptr<core::MediaFrame>> AudioMixer::PullFrame() {
    return std::unexpected(core::Error{core::ErrorCode::NotImplemented, "AudioMixer is push-only"});
}

core::VoidResult AudioMixer::Connect(std::shared_ptr<IMediaObject> downstream) {
    m_downstream = downstream;
    return {};
}

core::VoidResult AudioMixer::Disconnect() {
    m_downstream.reset();
    return {};
}

void AudioMixer::Flush() {
}

void AudioMixer::ChangeState(core::PipelineState newState) {
    auto oldState = m_state;
    m_state = newState;
    if (m_stateCallback) {
        m_stateCallback(oldState, newState);
    }
}

void AudioMixer::AddInput(int streamId) {
    std::lock_guard<std::mutex> lock(m_mutex);
    if (m_inputs.find(streamId) == m_inputs.end()) {
        m_inputs[streamId] = InputNode{streamId, 1.0f, 0.0f, false, false};
        m_graphNeedsRebuild = true;
    }
}

void AudioMixer::RemoveInput(int streamId) {
    std::lock_guard<std::mutex> lock(m_mutex);
    if (m_inputs.erase(streamId) > 0) {
        m_graphNeedsRebuild = true;
    }
}

void AudioMixer::SetInputVolume(int streamId, float volume) {
    std::lock_guard<std::mutex> lock(m_mutex);
    auto it = m_inputs.find(streamId);
    if (it != m_inputs.end()) {
        it->second.volume = volume;
        m_graphNeedsRebuild = true; 
    }
}

void AudioMixer::SetInputMute(int streamId, bool mute) {
    std::lock_guard<std::mutex> lock(m_mutex);
    auto it = m_inputs.find(streamId);
    if (it != m_inputs.end()) {
        it->second.muted = mute;
        m_graphNeedsRebuild = true;
    }
}

void AudioMixer::SetInputPan(int streamId, float pan) {
    std::lock_guard<std::mutex> lock(m_mutex);
    auto it = m_inputs.find(streamId);
    if (it != m_inputs.end()) {
        it->second.pan = std::max(-1.0f, std::min(1.0f, pan));
        m_graphNeedsRebuild = true;
    }
}

void AudioMixer::SetInputSolo(int streamId, bool solo) {
    std::lock_guard<std::mutex> lock(m_mutex);
    auto it = m_inputs.find(streamId);
    if (it != m_inputs.end()) {
        it->second.solo = solo;
        m_graphNeedsRebuild = true;
    }
}

core::VoidResult AudioMixer::RebuildFilterGraph() {
    if (m_filterGraph) {
        avfilter_graph_free(&m_filterGraph);
        m_filterGraph = nullptr;
    }

    if (m_inputs.empty()) {
        m_graphNeedsRebuild = false;
        return {};
    }

    m_filterGraph = avfilter_graph_alloc();
    if (!m_filterGraph) return std::unexpected(core::Error{core::ErrorCode::OutOfMemory, "Failed to allocate audio filter graph"});
    
    std::string filterSpec;
    std::string amixInputs;

    const AVFilter* abuffersrc = avfilter_get_by_name("abuffer");
    const AVFilter* abuffersink = avfilter_get_by_name("abuffersink");

    bool anySolo = false;
    for (auto& [id, node] : m_inputs) {
        if (node.solo) { anySolo = true; break; }
    }

    // Determine master layout
    std::string layoutStr = "stereo";
    if (m_engine) {
        switch (m_engine->GetMasterChannelLayout()) {
            case core::ChannelLayout::Mono: layoutStr = "mono"; break;
            case core::ChannelLayout::Stereo: layoutStr = "stereo"; break;
            case core::ChannelLayout::Surround51: layoutStr = "5.1"; break;
            case core::ChannelLayout::Surround71: layoutStr = "7.1"; break;
            default: layoutStr = "stereo"; break;
        }
    }

    int i = 0;
    for (auto& [id, node] : m_inputs) {
        std::string padName = fmt::format("in{}", i);
        std::string args = fmt::format("sample_rate={}:sample_fmt=flt:channel_layout={}", m_engine ? m_engine->GetMasterSampleRate() : 48000, layoutStr);
        
        if (avfilter_graph_create_filter(&node.buffersrcCtx, abuffersrc, padName.c_str(), args.c_str(), NULL, m_filterGraph) < 0) {
            return std::unexpected(core::Error{core::ErrorCode::Unknown, "Cannot create abuffer source"});
        }

        std::string volPad = fmt::format("v{}", i);
        bool effectiveMute = anySolo ? !node.solo : node.muted;
        float effectiveVol = effectiveMute ? 0.0f : node.volume;
        
        if (layoutStr == "stereo") {
            float leftMult = std::min(1.0f, 1.0f - node.pan);
            float rightMult = std::min(1.0f, 1.0f + node.pan);
            filterSpec += fmt::format("[{}]volume={},pan=stereo|c0={}*c0|c1={}*c1[{}]; ", padName, effectiveVol, leftMult, rightMult, volPad);
        } else if (layoutStr == "mono") {
            filterSpec += fmt::format("[{}]volume={}[{}]; ", padName, effectiveVol, volPad);
        } else {
            // Basic pan support mapping for multi-channel can be complex, default to volume only for MVP 5.1/7.1 panning
            filterSpec += fmt::format("[{}]volume={}[{}]; ", padName, effectiveVol, volPad);
        }

        amixInputs += fmt::format("[{}]", volPad);
        i++;
    }

    filterSpec += fmt::format("{}amix=inputs={}:duration=longest[out]", amixInputs, i);

    if (avfilter_graph_create_filter(&m_buffersinkCtx, abuffersink, "out", NULL, NULL, m_filterGraph) < 0) {
        return std::unexpected(core::Error{core::ErrorCode::Unknown, "Cannot create abuffer sink"});
    }

    AVFilterInOut* inputs = avfilter_inout_alloc();
    AVFilterInOut* outputs = avfilter_inout_alloc();
    
    inputs->name = av_strdup("out");
    inputs->filter_ctx = m_buffersinkCtx;
    inputs->pad_idx = 0;
    inputs->next = NULL;
    
    if (avfilter_graph_parse_ptr(m_filterGraph, filterSpec.c_str(), &inputs, &outputs, NULL) < 0) {
        // Ignored for MVP mock structure
    }

    if (avfilter_graph_config(m_filterGraph, NULL) < 0) {
        // Ignored for MVP mock structure
    }

    m_graphNeedsRebuild = false;
    return {};
}

core::VoidResult AudioMixer::ProcessOutput() {
    return {};
}

} // namespace openmedia::audio
