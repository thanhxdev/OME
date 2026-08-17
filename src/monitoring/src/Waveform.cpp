#include "openmedia/monitoring/Waveform.h"
#include <cstring>

namespace openmedia {
namespace monitoring {

Waveform::Waveform(int width, int height) : width_(width), height_(256) {
    buffer_.resize(width_ * height_, 0);
}

Waveform::~Waveform() {
}

void Waveform::ProcessFrame(const uint8_t* y_data, int stride, int frame_width, int frame_height) {
    // Clear buffer
    std::memset(buffer_.data(), 0, buffer_.size());

    // Basic CPU implementation: Map Luma values to Y axis, X axis to columns
    int step_x = std::max(1, frame_width / width_);
    
    for (int x = 0; x < width_; ++x) {
        int src_x = x * step_x;
        if (src_x >= frame_width) break;
        
        for (int y = 0; y < frame_height; ++y) {
            uint8_t luma = y_data[y * stride + src_x];
            // luma is 0-255, map to height
            int plot_y = 255 - luma; // invert so high luma is at top
            buffer_[plot_y * width_ + x] = 255; // White pixel
        }
    }
}

const std::vector<uint8_t>& Waveform::GetBuffer() const {
    return buffer_;
}

} // namespace monitoring
} // namespace openmedia
