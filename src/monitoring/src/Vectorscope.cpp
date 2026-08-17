#include "openmedia/monitoring/Vectorscope.h"
#include <cstring>
#include <algorithm>

namespace openmedia {
namespace monitoring {

Vectorscope::Vectorscope(int size) : size_(size) {
    buffer_.resize(size_ * size_, 0);
}

Vectorscope::~Vectorscope() {
}

void Vectorscope::ProcessFrame(const uint8_t* u_data, const uint8_t* v_data, int stride, int width, int height) {
    std::memset(buffer_.data(), 0, buffer_.size());

    // Basic CPU implementation: Map U to X axis, V to Y axis
    for (int y = 0; y < height; ++y) {
        for (int x = 0; x < width; ++x) {
            uint8_t u = u_data[y * stride + x];
            uint8_t v = v_data[y * stride + x];
            
            // Map 0-255 to 0-size_
            int plot_x = (u * size_) / 256;
            int plot_y = ((255 - v) * size_) / 256; // invert V
            
            plot_x = std::clamp(plot_x, 0, size_ - 1);
            plot_y = std::clamp(plot_y, 0, size_ - 1);
            
            buffer_[plot_y * size_ + plot_x] = 255;
        }
    }
}

const std::vector<uint8_t>& Vectorscope::GetBuffer() const {
    return buffer_;
}

} // namespace monitoring
} // namespace openmedia
