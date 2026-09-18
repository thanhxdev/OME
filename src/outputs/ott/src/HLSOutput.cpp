#include "openmedia/outputs/ott/HLSOutput.h"
#include <sstream>
#include <deque>
#include <cmath>
#include <iomanip>

namespace openmedia {
namespace outputs {
namespace ott {

struct HLSOutput::Impl {
    HLSConfig config;
    bool started = false;
    mutable std::mutex mutex;

    uint64_t currentSequence = 0;
    uint64_t startSequence = 0;
    int64_t currentSegmentStartPts = -1;
    size_t currentSegmentBytes = 0;
    std::deque<HLSSegmentInfo> segments;

    explicit Impl(const HLSConfig& cfg) : config(cfg) {}

    bool Push(int64_t pts, std::span<const uint8_t> payload, bool isKeyframe) {
        std::lock_guard<std::mutex> lock(mutex);
        if (!started) return false;

        if (currentSegmentStartPts < 0) {
            currentSegmentStartPts = pts;
        }

        currentSegmentBytes += payload.size();

        // 90kHz timescale: duration in seconds = (pts - startPts) / 90000.0
        double segmentElapsedSec = (pts >= currentSegmentStartPts)
            ? (static_cast<double>(pts - currentSegmentStartPts) / 90000.0)
            : 0.0;

        if (isKeyframe && segmentElapsedSec >= config.segmentDurationSec) {
            // Cut segment
            std::ostringstream fn;
            fn << "segment_" << std::setfill('0') << std::setw(5) << currentSequence 
               << (config.useFmp4 ? ".m4s" : ".ts");

            HLSSegmentInfo seg;
            seg.filename = fn.str();
            seg.durationSec = segmentElapsedSec;
            seg.sequenceNumber = currentSequence;

            segments.push_back(seg);
            currentSequence++;

            if (config.mode == HLSMode::Live && segments.size() > config.liveWindowSegments) {
                segments.pop_front();
                startSequence++;
            }

            currentSegmentStartPts = pts;
            currentSegmentBytes = 0;
        }

        return true;
    }

    std::string BuildPlaylist() const {
        std::lock_guard<std::mutex> lock(mutex);
        int targetDuration = static_cast<int>(std::ceil(config.segmentDurationSec)) + 1;

        std::ostringstream ss;
        ss << "#EXTM3U\n"
           << "#EXT-X-VERSION:3\n"
           << "#EXT-X-TARGETDURATION:" << targetDuration << "\n";

        if (config.mode == HLSMode::Live) {
            ss << "#EXT-X-MEDIA-SEQUENCE:" << startSequence << "\n";
        } else {
            ss << "#EXT-X-PLAYLIST-TYPE:EVENT\n";
        }

        for (const auto& seg : segments) {
            ss << "#EXTINF:" << std::fixed << std::setprecision(3) << seg.durationSec << ",\n"
               << seg.filename << "\n";
        }

        if (!started && config.mode == HLSMode::Event) {
            ss << "#EXT-X-ENDLIST\n";
        }

        return ss.str();
    }
};

HLSOutput::HLSOutput(const HLSConfig& config)
    : m_impl(std::make_unique<Impl>(config)) {
}

HLSOutput::~HLSOutput() {
    Stop();
}

bool HLSOutput::Start(const std::string& output_dir, const std::string& manifest_name) {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    m_impl->config.outputDir = output_dir;
    m_impl->config.manifestName = manifest_name;
    m_impl->started = true;
    m_impl->currentSegmentStartPts = -1;
    m_impl->currentSegmentBytes = 0;
    return true;
}

void HLSOutput::Stop() {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    m_impl->started = false;
}

bool HLSOutput::IsStarted() const {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    return m_impl->started;
}

void HLSOutput::Configure(const HLSConfig& config) {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    m_impl->config = config;
}

HLSConfig HLSOutput::GetConfig() const {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    return m_impl->config;
}

bool HLSOutput::PushPacket(int64_t pts, std::span<const uint8_t> payload, bool isKeyframe) {
    return m_impl->Push(pts, payload, isKeyframe);
}

std::string HLSOutput::GeneratePlaylist() const {
    return m_impl->BuildPlaylist();
}

std::vector<HLSSegmentInfo> HLSOutput::GetSegments() const {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    return std::vector<HLSSegmentInfo>(m_impl->segments.begin(), m_impl->segments.end());
}

} // namespace ott
} // namespace outputs
} // namespace openmedia
