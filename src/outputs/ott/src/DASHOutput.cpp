#include "openmedia/outputs/ott/DASHOutput.h"
#include <sstream>

namespace openmedia {
namespace outputs {
namespace ott {

struct DASHOutput::Impl {
    DASHConfig config;
    bool started = false;
    mutable std::mutex mutex;

    explicit Impl(const DASHConfig& cfg) : config(cfg) {
        if (config.representations.empty()) {
            config.representations.push_back({"video_1080p", 1920, 1080, 6000000, "avc1.640028", "60"});
            config.representations.push_back({"video_720p", 1280, 720, 3000000, "avc1.4d401f", "60"});
        }
    }

    std::string BuildManifest() const {
        std::lock_guard<std::mutex> lock(mutex);
        uint32_t segDurationMs = static_cast<uint32_t>(config.segmentDurationSec * 1000.0);

        std::ostringstream ss;
        ss << "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
           << "<MPD xmlns=\"urn:mpeg:dash:schema:mpd:2011\"\n"
           << "     profiles=\"urn:mpeg:dash:profile:isoff-live:2011\"\n"
           << "     type=\"" << (config.isLive ? "dynamic" : "static") << "\"\n"
           << "     minBufferTime=\"PT2.0S\"\n"
           << "     mediaPresentationDuration=\"PT1H\">\n"
           << "  <Period id=\"P0\" start=\"PT0S\">\n"
           << "    <AdaptationSet id=\"0\" contentType=\"video\" mimeType=\"video/mp4\" segmentAlignment=\"true\">\n"
           << "      <SegmentTemplate timescale=\"1000\"\n"
           << "                       duration=\"" << segDurationMs << "\"\n"
           << "                       initialization=\"init-$RepresentationID$.mp4\"\n"
           << "                       media=\"chunk-$RepresentationID$-$Number%05d$.m4s\"\n"
           << "                       startNumber=\"1\"/>\n";

        for (const auto& rep : config.representations) {
            ss << "      <Representation id=\"" << rep.id << "\"\n"
               << "                      bandwidth=\"" << rep.bandwidth << "\"\n"
               << "                      width=\"" << rep.width << "\"\n"
               << "                      height=\"" << rep.height << "\"\n"
               << "                      codecs=\"" << rep.codec << "\"\n"
               << "                      frameRate=\"" << rep.frameRate << "\"/>\n";
        }

        ss << "    </AdaptationSet>\n"
           << "  </Period>\n"
           << "</MPD>\n";

        return ss.str();
    }
};

DASHOutput::DASHOutput(const DASHConfig& config)
    : m_impl(std::make_unique<Impl>(config)) {
}

DASHOutput::~DASHOutput() {
    Stop();
}

bool DASHOutput::Start(const std::string& output_dir, const std::string& manifest_name) {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    m_impl->config.outputDir = output_dir;
    m_impl->config.manifestName = manifest_name;
    m_impl->started = true;
    return true;
}

void DASHOutput::Stop() {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    m_impl->started = false;
}

bool DASHOutput::IsStarted() const {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    return m_impl->started;
}

void DASHOutput::Configure(const DASHConfig& config) {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    m_impl->config = config;
}

DASHConfig DASHOutput::GetConfig() const {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    return m_impl->config;
}

void DASHOutput::AddRepresentation(const DASHRepresentation& rep) {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    m_impl->config.representations.push_back(rep);
}

bool DASHOutput::PushPacket(const std::string& /*repId*/, int64_t /*pts*/, std::span<const uint8_t> payload, bool /*isKeyframe*/) {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    if (!m_impl->started || payload.empty()) return false;
    return true;
}

std::string DASHOutput::GenerateManifest() const {
    return m_impl->BuildManifest();
}

} // namespace ott
} // namespace outputs
} // namespace openmedia
