#include "openmedia/core/TeeNode.h"
#include <openmedia/core/Logger.h>
#include <algorithm>
#include <spdlog/spdlog.h>

namespace openmedia::core {

struct TeeNode::Impl {
    std::mutex mutex;
    PipelineState state{PipelineState::Stopped};
    std::vector<std::shared_ptr<IMediaObject>> downstreams;
    StateChangeCallback onStateChange;
    ErrorCallback onError;
    Logger* logger = nullptr;
};

TeeNode::TeeNode() : m_impl(std::make_unique<Impl>()) {
    m_impl->logger = &Logger::Get("TeeNode");
}

TeeNode::~TeeNode() {
    Stop();
}

PipelineState TeeNode::GetState() const {
    return m_impl->state;
}

VoidResult TeeNode::Initialize() {
    auto old = m_impl->state;
    m_impl->state = PipelineState::Ready;
    if (m_impl->onStateChange) m_impl->onStateChange(old, m_impl->state);
    return {};
}

VoidResult TeeNode::Start() {
    auto old = m_impl->state;
    m_impl->state = PipelineState::Running;
    if (m_impl->onStateChange) m_impl->onStateChange(old, m_impl->state);
    return {};
}

VoidResult TeeNode::Stop() {
    auto old = m_impl->state;
    m_impl->state = PipelineState::Stopped;
    if (m_impl->onStateChange) m_impl->onStateChange(old, m_impl->state);
    return {};
}

VoidResult TeeNode::PushFrame(std::shared_ptr<MediaFrame> frame) {
    std::lock_guard lock(m_impl->mutex);
    if (m_impl->state != PipelineState::Running) {
        return std::unexpected(Error::Make(ErrorCode::InvalidState, "TeeNode is not running"));
    }

    if (m_impl->downstreams.empty()) {
        OME_LOG_WARN(*m_impl->logger, "TeeNode received frame but has no downstream connections");
        return {};
    }

    // Fan-out: send the same frame (shared_ptr) to all connected downstreams.
    // If the frame requires deep copying for certain downstreams (e.g., if one modifies it),
    // that should be handled by the downstream or a specific deep-copy node. 
    // Usually, MediaFrames are immutable or have their own ref-counting for GPU resources.
    VoidResult firstError;
    bool hasError = false;

    for (auto& downstream : m_impl->downstreams) {
        if (auto res = downstream->PushFrame(frame); !res) {
            if (!hasError) {
                firstError = res;
                hasError = true;
            }
            if (m_impl->onError) {
                m_impl->onError(res.error());
            }
        }
    }

    return hasError ? firstError : VoidResult{};
}

Result<std::shared_ptr<MediaFrame>> TeeNode::PullFrame() {
    return std::unexpected(Error::Make(ErrorCode::NotImplemented, "TeeNode is a push-only node"));
}

VoidResult TeeNode::Connect(std::shared_ptr<IMediaObject> downstream) {
    std::lock_guard lock(m_impl->mutex);
    if (!downstream) return std::unexpected(Error::Make(ErrorCode::InvalidArgument, "Downstream is null"));
    
    // Prevent duplicate connections
    if (std::find(m_impl->downstreams.begin(), m_impl->downstreams.end(), downstream) != m_impl->downstreams.end()) {
        return std::unexpected(Error::Make(ErrorCode::AlreadyExists, "Downstream already connected"));
    }
    
    m_impl->downstreams.push_back(downstream);
    OME_LOG_INFO(*m_impl->logger, "Connected downstream node, total connections: {}", m_impl->downstreams.size());
    return {};
}

VoidResult TeeNode::Disconnect(std::shared_ptr<IMediaObject> downstream) {
    std::lock_guard lock(m_impl->mutex);
    auto it = std::find(m_impl->downstreams.begin(), m_impl->downstreams.end(), downstream);
    if (it != m_impl->downstreams.end()) {
        m_impl->downstreams.erase(it);
        OME_LOG_INFO(*m_impl->logger, "Disconnected downstream node, remaining: {}", m_impl->downstreams.size());
        return {};
    }
    return std::unexpected(Error::Make(ErrorCode::NotFound, "Downstream not found"));
}

VoidResult TeeNode::Disconnect() {
    std::lock_guard lock(m_impl->mutex);
    m_impl->downstreams.clear();
    OME_LOG_INFO(*m_impl->logger, "Disconnected all downstreams");
    return {};
}

void TeeNode::OnStateChange(StateChangeCallback callback) {
    m_impl->onStateChange = callback;
}

void TeeNode::OnError(ErrorCallback callback) {
    m_impl->onError = callback;
}

} // namespace openmedia::core
