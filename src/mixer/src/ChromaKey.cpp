#include <openmedia/mixer/ChromaKey.h>
#include <openmedia/core/ErrorCodes.h>

namespace openmedia::mixer {

ChromaKey::ChromaKey() : m_r(0), m_g(255), m_b(0), m_tolerance(0.1f), m_smoothing(0.1f) {}

ChromaKey::~ChromaKey() {}

void ChromaKey::SetKeyColor(int r, int g, int b) {
    m_r = r;
    m_g = g;
    m_b = b;
}

void ChromaKey::SetTolerance(float tolerance) { m_tolerance = tolerance; }
void ChromaKey::SetSmoothing(float smoothing) { m_smoothing = smoothing; }

core::Result<std::shared_ptr<core::MediaFrame>> ChromaKey::Process(const std::shared_ptr<core::MediaFrame>& input) {
    if (!input) return std::unexpected(openmedia::core::Error::Make(core::ErrorCode::InvalidArgument, "Input frame is null"));
    
    if (input->GetPixelFormat() != core::PixelFormat::BGRA) {
        return input; // Fallback, no modification for non-BGRA right now
    }

    auto output = input->Clone(); // Clone the frame to modify it
    
    uint8_t* pixels = output->GetVideoPlane(0);
    int32_t stride = output->GetLineSize(0);
    int32_t height = output->GetHeight();
    size_t totalPixels = (stride * height) / 4;
    
    float tolSq = m_tolerance * m_tolerance * 255 * 255 * 3; // roughly scaling tolerance
    float smoothTolSq = (m_tolerance + m_smoothing) * (m_tolerance + m_smoothing) * 255 * 255 * 3;
    
    for (size_t i = 0; i < totalPixels; ++i) {
        uint8_t b = pixels[i * 4 + 0];
        uint8_t g = pixels[i * 4 + 1];
        uint8_t r = pixels[i * 4 + 2];
        uint8_t& a = pixels[i * 4 + 3];
        
        float dr = r - m_r;
        float dg = g - m_g;
        float db = b - m_b;
        
        float distSq = (dr*dr) + (dg*dg) + (db*db);
        
        if (distSq < tolSq) {
            a = 0; // Transparent
        } else if (distSq < smoothTolSq) {
            float range = smoothTolSq - tolSq;
            float factor = (distSq - tolSq) / range;
            a = static_cast<uint8_t>(a * factor);
        }
    }
    
    return output;
}

} // namespace openmedia::mixer
