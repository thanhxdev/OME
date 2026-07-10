/// @file MediaPipeline.cpp
/// @brief Pipeline builder and state machine implementation

#include <openmedia/core/MediaPipeline.h>
#include <openmedia/core/Logger.h>

#include <atomic>
#include <mutex>

namespace openmedia::core {

static std::atomic<uint64_t> s_nextPipelineId{1};

struct MediaPipeline::Impl {
    std::string name;
    uint64_t id;
    PipelineState state = PipelineState::Idle;
    std::mutex mutex;

    std::shared_ptr<IMediaObject> source;
    std::vector<std::shared_ptr<IMediaObject>> filters;
    std::shared_ptr<IMediaObject> mixer;
    std::shared_ptr<IMediaObject> encoder;
    std::vector<std::shared_ptr<IMediaObject>> outputs;

    StateChangeCallback stateCallback;
    ErrorCallback errorCallback;

    void SetState(PipelineState newState) {
        auto oldState = state;
        state = newState;
        if (stateCallback) {
            stateCallback(oldState, newState);
        }
    }
};

MediaPipeline::MediaPipeline() : m_impl(std::make_unique<Impl>()) {
    m_impl->id = s_nextPipelineId.fetch_add(1);
}

MediaPipeline::~MediaPipeline() {
    if (m_impl && m_impl->state == PipelineState::Running) {
        Stop();
    }
}

MediaPipeline::MediaPipeline(MediaPipeline&&) noexcept = default;
MediaPipeline& MediaPipeline::operator=(MediaPipeline&&) noexcept = default;

std::unique_ptr<MediaPipeline> MediaPipeline::Create(std::string_view name) {
    auto pipeline = std::unique_ptr<MediaPipeline>(new MediaPipeline());
    pipeline->m_impl->name = std::string(name);
    return pipeline;
}

MediaPipeline& MediaPipeline::SetSource(std::shared_ptr<IMediaObject> source) {
    std::lock_guard lock(m_impl->mutex);
    m_impl->source = std::move(source);
    return *this;
}

MediaPipeline& MediaPipeline::AddFilter(std::shared_ptr<IMediaObject> filter) {
    std::lock_guard lock(m_impl->mutex);
    m_impl->filters.push_back(std::move(filter));
    return *this;
}

MediaPipeline& MediaPipeline::SetMixer(std::shared_ptr<IMediaObject> mixer) {
    std::lock_guard lock(m_impl->mutex);
    m_impl->mixer = std::move(mixer);
    return *this;
}

MediaPipeline& MediaPipeline::SetEncoder(std::shared_ptr<IMediaObject> encoder) {
    std::lock_guard lock(m_impl->mutex);
    m_impl->encoder = std::move(encoder);
    return *this;
}

MediaPipeline& MediaPipeline::AddOutput(std::shared_ptr<IMediaObject> output) {
    std::lock_guard lock(m_impl->mutex);
    m_impl->outputs.push_back(std::move(output));
    return *this;
}

VoidResult MediaPipeline::Build() {
    std::lock_guard lock(m_impl->mutex);

    if (m_impl->state != PipelineState::Idle &&
        m_impl->state != PipelineState::Stopped) {
        return std::unexpected(
            OME_ERROR(ErrorCode::InvalidState, "Pipeline must be Idle or Stopped to build"));
    }

    m_impl->SetState(PipelineState::Building);

    // Validate: need at least a source
    if (!m_impl->source) {
        m_impl->SetState(PipelineState::Error);
        return std::unexpected(
            OME_ERROR(ErrorCode::PipelineBuildFailed, "Pipeline requires a source"));
    }

    // Connect the graph: source → filters → mixer/encoder → outputs
    // For now, just validate that we have minimum components
    auto& log = Logger::Get("pipeline");
    log.Info("Pipeline '{}' (id={}) built with {} filters, {} outputs",
        m_impl->name, m_impl->id,
        m_impl->filters.size(), m_impl->outputs.size());

    m_impl->SetState(PipelineState::Ready);
    return {};
}

VoidResult MediaPipeline::Start() {
    std::lock_guard lock(m_impl->mutex);

    if (m_impl->state != PipelineState::Ready &&
        m_impl->state != PipelineState::Paused) {
        return std::unexpected(
            OME_ERROR(ErrorCode::InvalidState, "Pipeline must be Ready or Paused to start"));
    }

    auto& log = Logger::Get("pipeline");
    log.Info("Pipeline '{}' starting", m_impl->name);

    // Initialize all nodes
    if (auto result = m_impl->source->Initialize(); !result) {
        m_impl->SetState(PipelineState::Error);
        return result;
    }

    // Start source
    if (auto result = m_impl->source->Start(); !result) {
        m_impl->SetState(PipelineState::Error);
        return result;
    }

    m_impl->SetState(PipelineState::Running);
    log.Info("Pipeline '{}' running", m_impl->name);
    return {};
}

VoidResult MediaPipeline::Stop() {
    std::lock_guard lock(m_impl->mutex);

    if (m_impl->state != PipelineState::Running &&
        m_impl->state != PipelineState::Paused) {
        return {};  // Already stopped
    }

    m_impl->SetState(PipelineState::Stopping);

    auto& log = Logger::Get("pipeline");
    log.Info("Pipeline '{}' stopping", m_impl->name);

    // Stop all nodes in reverse order
    if (m_impl->source) m_impl->source->Stop();
    for (auto& filter : m_impl->filters) filter->Stop();
    if (m_impl->encoder) m_impl->encoder->Stop();
    for (auto& output : m_impl->outputs) output->Stop();

    m_impl->SetState(PipelineState::Stopped);
    log.Info("Pipeline '{}' stopped", m_impl->name);
    return {};
}

VoidResult MediaPipeline::Pause() {
    std::lock_guard lock(m_impl->mutex);

    if (m_impl->state != PipelineState::Running) {
        return std::unexpected(
            OME_ERROR(ErrorCode::InvalidState, "Pipeline must be Running to pause"));
    }

    m_impl->SetState(PipelineState::Paused);
    return {};
}

VoidResult MediaPipeline::Resume() {
    std::lock_guard lock(m_impl->mutex);

    if (m_impl->state != PipelineState::Paused) {
        return std::unexpected(
            OME_ERROR(ErrorCode::InvalidState, "Pipeline must be Paused to resume"));
    }

    m_impl->SetState(PipelineState::Running);
    return {};
}

PipelineState MediaPipeline::GetState() const {
    return m_impl->state;
}

std::string MediaPipeline::GetName() const {
    return m_impl->name;
}

uint64_t MediaPipeline::GetId() const {
    return m_impl->id;
}

void MediaPipeline::OnStateChange(StateChangeCallback callback) {
    m_impl->stateCallback = std::move(callback);
}

void MediaPipeline::OnError(ErrorCallback callback) {
    m_impl->errorCallback = std::move(callback);
}

std::vector<std::shared_ptr<IMediaObject>> MediaPipeline::GetNodes() const {
    std::vector<std::shared_ptr<IMediaObject>> nodes;
    if (m_impl->source) nodes.push_back(m_impl->source);
    nodes.insert(nodes.end(), m_impl->filters.begin(), m_impl->filters.end());
    if (m_impl->mixer) nodes.push_back(m_impl->mixer);
    if (m_impl->encoder) nodes.push_back(m_impl->encoder);
    nodes.insert(nodes.end(), m_impl->outputs.begin(), m_impl->outputs.end());
    return nodes;
}

size_t MediaPipeline::GetNodeCount() const {
    return GetNodes().size();
}

} // namespace openmedia::core
