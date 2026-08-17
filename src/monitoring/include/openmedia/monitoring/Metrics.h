#pragma once

#include <chrono>

namespace openmedia {
namespace monitoring {

struct PipelineMetrics {
    double fps;
    double bitrate_kbps;
    uint64_t frames_dropped;
    double latency_ms;
};

class Metrics {
public:
    Metrics();
    ~Metrics();

    void RecordFrame(size_t size_bytes, double latency_ms);
    void RecordDrop();
    PipelineMetrics GetMetrics() const;

private:
    uint64_t frame_count_;
    uint64_t bytes_transferred_;
    uint64_t dropped_frames_;
    double total_latency_;
    std::chrono::time_point<std::chrono::steady_clock> last_reset_;
    mutable PipelineMetrics current_metrics_;
    
    void Update() const;
};

} // namespace monitoring
} // namespace openmedia
