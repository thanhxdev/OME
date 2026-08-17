#include <openmedia/overlay/OverlayEngine.h>
#include <openmedia/overlay/IOverlay.h>
#include <fmt/format.h>
#include <algorithm>
#include <expected>

extern "C" {
#include <libavfilter/avfilter.h>
#include <libavfilter/buffersrc.h>
#include <libavfilter/buffersink.h>
#include <libavutil/opt.h>
}

namespace openmedia::overlay {

OverlayEngine::OverlayEngine() {
}

OverlayEngine::~OverlayEngine() {
    (void)Stop();
}

core::VoidResult OverlayEngine::Initialize() {
    ChangeState(core::PipelineState::Ready);
    return {};
}

core::VoidResult OverlayEngine::Start() {
    ChangeState(core::PipelineState::Running);
    return {};
}

core::VoidResult OverlayEngine::Stop() {
    if (m_filterGraph) {
        avfilter_graph_free(&m_filterGraph);
        m_filterGraph = nullptr;
    }
    ChangeState(core::PipelineState::Stopped);
    return {};
}

core::VoidResult OverlayEngine::PushFrame(std::shared_ptr<core::MediaFrame> frame) {
    if (m_state != core::PipelineState::Running) return {};
    
    std::lock_guard<std::mutex> lock(m_mutex);
    return ProcessFrame(frame);
}

core::Result<std::shared_ptr<core::MediaFrame>> OverlayEngine::PullFrame() {
    return std::unexpected(core::Error{core::ErrorCode::NotImplemented, "OverlayEngine is push-only"});
}

core::VoidResult OverlayEngine::Connect(std::shared_ptr<IMediaObject> downstream) {
    m_downstream = downstream;
    return {};
}

core::VoidResult OverlayEngine::Disconnect() {
    m_downstream.reset();
    return {};
}

void OverlayEngine::Flush() {
}

void OverlayEngine::ChangeState(core::PipelineState newState) {
    auto oldState = m_state;
    m_state = newState;
    if (m_stateCallback) {
        m_stateCallback(oldState, newState);
    }
}

void OverlayEngine::AddOverlay(std::shared_ptr<IOverlay> overlay) {
    std::lock_guard<std::mutex> lock(m_mutex);
    m_overlays.push_back(overlay);
    m_graphNeedsRebuild = true;
}

void OverlayEngine::RemoveOverlay(const std::string& id) {
    std::lock_guard<std::mutex> lock(m_mutex);
    m_overlays.erase(std::remove_if(m_overlays.begin(), m_overlays.end(), 
        [&](const auto& o) { return o->GetId() == id; }), m_overlays.end());
    m_graphNeedsRebuild = true;
}

void OverlayEngine::ClearOverlays() {
    std::lock_guard<std::mutex> lock(m_mutex);
    m_overlays.clear();
    m_graphNeedsRebuild = true;
}

std::shared_ptr<IOverlay> OverlayEngine::GetOverlay(const std::string& id) {
    std::lock_guard<std::mutex> lock(m_mutex);
    auto it = std::find_if(m_overlays.begin(), m_overlays.end(), [&](const auto& o) { return o->GetId() == id; });
    return it != m_overlays.end() ? *it : nullptr;
}

core::VoidResult OverlayEngine::RebuildFilterGraph(uint32_t width, uint32_t height, int format, int timebaseNum, int timebaseDen) {
    if (m_filterGraph) {
        avfilter_graph_free(&m_filterGraph);
        m_filterGraph = nullptr;
    }

    m_filterGraph = avfilter_graph_alloc();
    if (!m_filterGraph) return std::unexpected(core::Error{core::ErrorCode::OutOfMemory, "Failed to allocate filter graph"});

    std::string args = fmt::format("video_size={}x{}:pix_fmt={}:time_base={}/{}", width, height, format, timebaseNum, timebaseDen);
    const AVFilter* buffersrc = avfilter_get_by_name("buffer");
    const AVFilter* buffersink = avfilter_get_by_name("buffersink");

    if (avfilter_graph_create_filter(&m_buffersrcCtx, buffersrc, "in", args.c_str(), NULL, m_filterGraph) < 0)
        return std::unexpected(core::Error{core::ErrorCode::Unknown, "Cannot create buffer source"});

    if (avfilter_graph_create_filter(&m_buffersinkCtx, buffersink, "out", NULL, NULL, m_filterGraph) < 0)
        return std::unexpected(core::Error{core::ErrorCode::Unknown, "Cannot create buffer sink"});

    auto sortedOverlays = m_overlays;
    std::sort(sortedOverlays.begin(), sortedOverlays.end(), [](const auto& a, const auto& b) {
        return a->GetZOrder() < b->GetZOrder();
    });

    std::string filterSpec;
    std::string currentPad = "in";
    
    for (size_t i = 0; i < sortedOverlays.size(); ++i) {
        if (!sortedOverlays[i]->IsVisible()) continue;
        std::string outPad = (i == sortedOverlays.size() - 1) ? "out" : fmt::format("pad_{}", i);
        if (i > 0 && !filterSpec.empty()) filterSpec += "; ";
        filterSpec += sortedOverlays[i]->GetFilterString(currentPad, outPad);
        currentPad = outPad;
    }

    if (filterSpec.empty()) filterSpec = "[in]copy[out]";

    AVFilterInOut* inputs = avfilter_inout_alloc();
    AVFilterInOut* outputs = avfilter_inout_alloc();
    
    outputs->name = av_strdup("in");
    outputs->filter_ctx = m_buffersrcCtx;
    outputs->pad_idx = 0;
    outputs->next = NULL;

    inputs->name = av_strdup("out");
    inputs->filter_ctx = m_buffersinkCtx;
    inputs->pad_idx = 0;
    inputs->next = NULL;

    if (avfilter_graph_parse_ptr(m_filterGraph, filterSpec.c_str(), &inputs, &outputs, NULL) < 0) {
        avfilter_inout_free(&inputs);
        avfilter_inout_free(&outputs);
        return std::unexpected(core::Error{core::ErrorCode::Unknown, "Failed to parse filter string"});
    }

    if (avfilter_graph_config(m_filterGraph, NULL) < 0)
        return std::unexpected(core::Error{core::ErrorCode::Unknown, "Failed to configure filter graph"});

    avfilter_inout_free(&inputs);
    avfilter_inout_free(&outputs);
    
    m_currentWidth = width; m_currentHeight = height; m_currentFormat = format; m_graphNeedsRebuild = false;
    return {};
}

core::VoidResult OverlayEngine::ProcessFrame(std::shared_ptr<core::MediaFrame> frame) {
    if (m_downstream) return m_downstream->PushFrame(frame);
    return {};
}

} // namespace openmedia::overlay
