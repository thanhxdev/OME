#include <openmedia/mixer/LUTFilter.h>
#include <fstream>
#include <sstream>
#include <cmath>
#include <algorithm>

namespace openmedia::mixer {

bool LUTFilter::LoadCubeFile(const std::string& filepath) {
    std::ifstream file(filepath);
    if (!file.is_open()) return false;

    std::string line;
    std::vector<float> data;
    int size = 0;

    while (std::getline(file, line)) {
        if (line.empty() || line[0] == '#') continue;

        if (line.rfind("LUT_3D_SIZE", 0) == 0) {
            std::istringstream iss(line);
            std::string token;
            iss >> token >> size;
        } else {
            std::istringstream iss(line);
            float r, g, b;
            if (iss >> r >> g >> b) {
                data.push_back(r);
                data.push_back(g);
                data.push_back(b);
            }
        }
    }

    if (size > 0 && data.size() == static_cast<size_t>(size * size * size * 3)) {
        m_lutSize = size;
        m_lutData = std::move(data);
        m_isLoaded = true;
        return true;
    }
    return false;
}

void LUTFilter::SetIntensity(float intensity) {
    m_intensity = std::clamp(intensity, 0.0f, 1.0f);
}

bool LUTFilter::ProcessFrame(std::shared_ptr<openmedia::core::MediaFrame> frame) {
    if (!m_isLoaded || !frame || frame->GetVideoPlaneCount() == 0 || m_intensity <= 0.0f) {
        return false;
    }

    auto fmt = frame->GetPixelFormat();
    int bpp = 0;
    if (fmt == openmedia::core::PixelFormat::RGB24 || fmt == openmedia::core::PixelFormat::BGR24) {
        bpp = 3;
    } else if (fmt == openmedia::core::PixelFormat::RGBA || fmt == openmedia::core::PixelFormat::BGRA) {
        bpp = 4;
    } else {
        // Only RGB/BGR packed formats are supported for CPU LUT right now
        return false;
    }

    uint8_t* data = frame->GetVideoPlane(0);
    int width = frame->GetWidth();
    int height = frame->GetHeight();
    int stride = frame->GetLineSize(0);

    ApplyToRGB(data, width, height, stride, bpp);
    return true;
}

void LUTFilter::ApplyToRGB(uint8_t* data, int width, int height, int stride, int bpp) {
    // Simplified Nearest-Neighbor or Trilinear application. 
    // This is a basic CPU stub for LUT application.
    for (int y = 0; y < height; ++y) {
        uint8_t* row = data + y * stride;
        for (int x = 0; x < width; ++x) {
            uint8_t* pixel = row + x * bpp;
            
            // Assume BGR or RGB depending on bpp
            // For now, assume R is pixel[2], G is pixel[1], B is pixel[0] (BGRA/BGR24)
            // A more complete implementation would check exact format.
            float r = pixel[2] / 255.0f;
            float g = pixel[1] / 255.0f;
            float b = pixel[0] / 255.0f;

            // Nearest-neighbor index calculation
            int ri = std::clamp(static_cast<int>(r * (m_lutSize - 1)), 0, m_lutSize - 1);
            int gi = std::clamp(static_cast<int>(g * (m_lutSize - 1)), 0, m_lutSize - 1);
            int bi = std::clamp(static_cast<int>(b * (m_lutSize - 1)), 0, m_lutSize - 1);

            int idx = (bi * m_lutSize * m_lutSize + gi * m_lutSize + ri) * 3;
            
            float lutR = m_lutData[idx];
            float lutG = m_lutData[idx + 1];
            float lutB = m_lutData[idx + 2];

            // Mix with original based on intensity
            r = r + (lutR - r) * m_intensity;
            g = g + (lutG - g) * m_intensity;
            b = b + (lutB - b) * m_intensity;

            pixel[2] = static_cast<uint8_t>(std::clamp(r * 255.0f, 0.0f, 255.0f));
            pixel[1] = static_cast<uint8_t>(std::clamp(g * 255.0f, 0.0f, 255.0f));
            pixel[0] = static_cast<uint8_t>(std::clamp(b * 255.0f, 0.0f, 255.0f));
        }
    }
}

} // namespace openmedia::mixer
