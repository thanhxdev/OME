#pragma once

#include <vector>
#include <cstdint>

namespace openmedia {
namespace monitoring {

class Waveform {
public:
    Waveform(int width, int height);
    ~Waveform();

    // Pass Y plane data (Luma)
    void ProcessFrame(const uint8_t* y_data, int stride, int frame_width, int frame_height);
    
    // Get the generated waveform image buffer (width x 256)
    const std::vector<uint8_t>& GetBuffer() const;

private:
    int width_;
    int height_;
    std::vector<uint8_t> buffer_;
};

} // namespace monitoring
} // namespace openmedia
