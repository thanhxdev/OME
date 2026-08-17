#pragma once

#include <openmedia/core/MediaFrame.h>
#include <string>
#include <vector>
#include <memory>

namespace openmedia::mixer {

/// @brief Filter to apply 3D LUT (Look-Up Table) for color grading
class LUTFilter {
public:
    LUTFilter() = default;
    ~LUTFilter() = default;

    /// @brief Load a 3D LUT from a .cube file
    /// @param filepath Path to the .cube file
    /// @return true if successful, false otherwise
    bool LoadCubeFile(const std::string& filepath);

    /// @brief Apply the loaded LUT to a video frame (in-place or out-of-place)
    /// @param frame The frame to process (must be RGB/BGR format for CPU processing)
    /// @return true if successful, false otherwise
    bool ProcessFrame(std::shared_ptr<openmedia::core::MediaFrame> frame);

    /// @brief Set the intensity (opacity) of the LUT effect [0.0, 1.0]
    void SetIntensity(float intensity);

    /// @brief Get the intensity of the LUT effect
    [[nodiscard]] float GetIntensity() const { return m_intensity; }

private:
    bool m_isLoaded = false;
    int m_lutSize = 0; // typically 17, 33, or 64
    float m_intensity = 1.0f;
    
    // 3D LUT data (RGB float values, size = m_lutSize^3 * 3)
    std::vector<float> m_lutData;

    // Helper for trilinear interpolation
    void ApplyToRGB(uint8_t* data, int width, int height, int stride, int bpp);
};

} // namespace openmedia::mixer
