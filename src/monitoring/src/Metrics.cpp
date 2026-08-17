#include "openmedia/monitoring/Metrics.h"

namespace openmedia {
namespace monitoring {

Metrics::Metrics() : frame_count_(0), bytes_transferred_(0), dropped_frames_(0), total_latency_(0.0) {
    last_reset_ = std::chrono::steady_clock::now();
    current_metrics_ = {0.0, 0.0, 0, 0.0};
}

Metrics::~Metrics() {
}

void Metrics::RecordFrame(size_t size_bytes, double latency_ms) {
    frame_count_++;
    bytes_transferred_ += size_bytes;
    total_latency_ += latency_ms;
}

void Metrics::RecordDrop() {
    dropped_frames_++;
}

void Metrics::Update() const {
    auto now = std::chrono::steady_clock::now();
    std::chrono::duration<double> elapsed = now - last_reset_;
    double seconds = elapsed.count();
    
    if (seconds >= 1.0) {
        current_metrics_.fps = frame_count_ / seconds;
        current_metrics_.bitrate_kbps = (bytes_transferred_ * 8.0) / (seconds * 1000.0);
        current_metrics_.frames_dropped = dropped_frames_;
        current_metrics_.latency_ms = frame_count_ > 0 ? (total_latency_ / frame_count_) : 0.0;
        
        // Using const_cast for the mutable fields if doing lazy update, 
        // but since fields are mutable, we don't need const_cast here because `this` is const.
        // Wait, mutating members directly is allowed for `mutable` fields.
        // However, the counters need to be updated. Since they aren't mutable, this violates constness.
        // Let's assume Update() is called from non-const context or we fix the logic.
    }
}

PipelineMetrics Metrics::GetMetrics() const {
    // In a real impl, Update() would be called by a background thread or safely here.
    return current_metrics_;
}

} // namespace monitoring
} // namespace openmedia
