#pragma once

#include <vector>
#include <cstdint>

namespace openmedia {
namespace monitoring {

class Vectorscope {
public:
    Vectorscope(int size); // Usually 256x256
    ~Vectorscope();

    // Pass U and V planes data (Chroma)
    void ProcessFrame(const uint8_t* u_data, const uint8_t* v_data, int stride, int width, int height);
    
    // Get the generated vectorscope image buffer (size x size)
    const std::vector<uint8_t>& GetBuffer() const;

private:
    int size_;
    std::vector<uint8_t> buffer_;
};

} // namespace monitoring
} // namespace openmedia
