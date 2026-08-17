#include <openmedia/mixer/MixerLayer.h>

namespace openmedia::mixer {

MixerLayer::MixerLayer(int id, const std::string& name) 
    : m_id(id), m_name(name), m_x(0), m_y(0), m_width(1920), m_height(1080), 
      m_zIndex(0), m_opacity(1.0f), m_visible(true), m_state(core::PipelineState::Stopped) {}

MixerLayer::~MixerLayer() {
    (void)Stop();
}

void MixerLayer::SetPosition(int x, int y) { std::lock_guard lock(m_mutex); m_x = x; m_y = y; }
void MixerLayer::SetSize(int width, int height) { std::lock_guard lock(m_mutex); m_width = width; m_height = height; }
void MixerLayer::SetOpacity(float opacity) { std::lock_guard lock(m_mutex); m_opacity = opacity; }
void MixerLayer::SetVisible(bool visible) { std::lock_guard lock(m_mutex); m_visible = visible; }
void MixerLayer::SetZIndex(int zIndex) { std::lock_guard lock(m_mutex); m_zIndex = zIndex; }

std::shared_ptr<core::MediaFrame> MixerLayer::GetCurrentFrame() const {
    std::lock_guard lock(m_mutex);
    return m_currentFrame;
}

std::string MixerLayer::GetName() const {
    return m_name;
}

core::PipelineState MixerLayer::GetState() const {
    std::lock_guard lock(m_mutex);
    return m_state;
}

core::VoidResult MixerLayer::Initialize() {
    std::lock_guard lock(m_mutex);
    m_currentFrame.reset();
    return {};
}

core::VoidResult MixerLayer::Start() {
    std::lock_guard lock(m_mutex);
    if (m_state == core::PipelineState::Running) return {};
    auto oldState = m_state;
    m_state = core::PipelineState::Running;
    if (m_onStateChange) m_onStateChange(oldState, m_state);
    return {};
}

core::VoidResult MixerLayer::Stop() {
    std::lock_guard lock(m_mutex);
    if (m_state == core::PipelineState::Stopped) return {};
    auto oldState = m_state;
    m_state = core::PipelineState::Stopped;
    if (m_onStateChange) m_onStateChange(oldState, m_state);
    return {};
}

core::VoidResult MixerLayer::PushFrame(std::shared_ptr<core::MediaFrame> frame) {
    std::lock_guard lock(m_mutex);
    if (m_state != core::PipelineState::Running) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "MixerLayer is not running"));
    }
    m_currentFrame = frame;
    return {};
}

core::Result<std::shared_ptr<core::MediaFrame>> MixerLayer::PullFrame() {
    return std::unexpected(core::Error::Make(core::ErrorCode::NotImplemented, "MixerLayer does not implement PullFrame (it exposes GetCurrentFrame)"));
}

core::VoidResult MixerLayer::Connect(std::shared_ptr<core::IMediaObject> /*downstream*/) {
    // MixerLayer typically doesn't connect downstream directly. It is polled by the Mixer.
    return std::unexpected(core::Error::Make(core::ErrorCode::NotImplemented, "MixerLayer cannot connect downstream"));
}

core::VoidResult MixerLayer::Disconnect() {
    return {};
}

void MixerLayer::OnStateChange(core::StateChangeCallback callback) {
    std::lock_guard lock(m_mutex);
    m_onStateChange = std::move(callback);
}

void MixerLayer::OnError(core::ErrorCallback callback) {
    std::lock_guard lock(m_mutex);
    m_onError = std::move(callback);
}

} // namespace openmedia::mixer
