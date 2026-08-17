#include <openmedia/mixer/LumaKey.h>

namespace openmedia::mixer {

LumaKey::LumaKey() : m_threshold(0.1f), m_softness(0.1f), m_invert(false) {}
LumaKey::~LumaKey() {}

void LumaKey::SetThreshold(float threshold) { m_threshold = threshold; }
void LumaKey::SetSoftness(float softness) { m_softness = softness; }
void LumaKey::SetInvert(bool invert) { m_invert = invert; }

core::Result<std::shared_ptr<core::MediaFrame>> LumaKey::Process(const std::shared_ptr<core::MediaFrame>& input) {
    if (!input) return std::unexpected(core::Error::Make(core::ErrorCode::InvalidArgument, "Input frame is null"));
    
    if (input->GetPixelFormat() != core::PixelFormat::BGRA) {
        return input; // Fallback, no modification for non-BGRA right now
    }

    auto output = input->Clone(); 
    
    uint8_t* pixels = output->GetVideoPlane(0);
    int32_t stride = output->GetLineSize(0);
    int32_t height = output->GetHeight();
    size_t totalPixels = (stride * height) / 4;
    
    float threshY = m_threshold * 255.0f;
    float smoothY = (m_threshold + m_softness) * 255.0f;
    
    for (size_t i = 0; i < totalPixels; ++i) {
        uint8_t b = pixels[i * 4 + 0];
        uint8_t g = pixels[i * 4 + 1];
        uint8_t r = pixels[i * 4 + 2];
        uint8_t& a = pixels[i * 4 + 3];
        
        // Compute Luma (Y)
        float y = 0.299f * r + 0.587f * g + 0.114f * b;
        
        float dist = m_invert ? (255.0f - y) : y;
        
        if (dist <= threshY) {
            a = 0; // Transparent
        } else if (dist < smoothY) {
            float range = smoothY - threshY;
            float factor = (dist - threshY) / range;
            a = static_cast<uint8_t>(a * factor);
        }
    }
    
    return output;
}

core::Result<std::shared_ptr<core::MediaFrame>> LumaKey::ProcessGPU(const std::shared_ptr<core::MediaFrame>& input, std::shared_ptr<gpu::IGPUContext> gpuContext) {
    if (!input) return std::unexpected(core::Error::Make(core::ErrorCode::InvalidArgument, "Input frame is null"));
    if (!gpuContext) return Process(input); // Fallback to CPU

    // TODO: Implement actual GPU LumaKey (e.g. D3D11 Pixel Shader or CUDA kernel)
    // For now, simulate by falling back to CPU
    return Process(input);
}

} // namespace openmedia::mixer
