#include <openmedia/mixer/Transition.h>
#include <openmedia/core/ErrorCodes.h>

namespace openmedia::mixer {

Transition::Transition(TransitionType type, int durationMs) 
    : m_type(type), m_durationMs(durationMs) {}

Transition::~Transition() {}

void Transition::SetType(TransitionType type) { m_type = type; }
void Transition::SetDuration(int durationMs) { m_durationMs = durationMs; }
int Transition::GetDurationMs() const { return m_durationMs; }

core::Result<std::shared_ptr<core::MediaFrame>> Transition::Process(
    const std::shared_ptr<core::MediaFrame>& frameA,
    const std::shared_ptr<core::MediaFrame>& frameB,
    float progress) 
{
    if (m_type == TransitionType::Cut) {
        return progress < 0.5f ? frameA : frameB;
    }
    
    // Validate inputs for blending
    if (!frameA || !frameB) {
        return progress < 0.5f ? frameA : frameB;
    }

    if (frameA->GetPixelFormat() != core::PixelFormat::BGRA || frameB->GetPixelFormat() != core::PixelFormat::BGRA) {
        // Fallback for non-BGRA formats right now
        return progress < 0.5f ? frameA : frameB;
    }

    if (frameA->GetWidth() != frameB->GetWidth() || frameA->GetHeight() != frameB->GetHeight()) {
        // Fallback for different dimensions
        return progress < 0.5f ? frameA : frameB;
    }

    if (m_type == TransitionType::Dissolve) {
        auto outFrame = core::MediaFrame::CreateVideo(frameA->GetWidth(), frameA->GetHeight(), core::PixelFormat::BGRA);
        outFrame->SetPts(frameA->GetPts());
        
        const uint8_t* pA = frameA->GetVideoPlane(0);
        const uint8_t* pB = frameB->GetVideoPlane(0);
        uint8_t* pOut = outFrame->GetVideoPlane(0);
        
        int32_t stride = frameA->GetLineSize(0);
        int32_t height = frameA->GetHeight();
        size_t totalBytes = stride * height;
        
        float wA = 1.0f - progress;
        float wB = progress;
        
        for (size_t i = 0; i < totalBytes; ++i) {
            pOut[i] = static_cast<uint8_t>((pA[i] * wA) + (pB[i] * wB));
        }
        
        return outFrame;
    }

    if (m_type == TransitionType::Wipe || m_type == TransitionType::Push || m_type == TransitionType::Slide) {
        auto outFrame = core::MediaFrame::CreateVideo(frameA->GetWidth(), frameA->GetHeight(), core::PixelFormat::BGRA);
        outFrame->SetPts(frameA->GetPts());

        const uint8_t* pA = frameA->GetVideoPlane(0);
        const uint8_t* pB = frameB->GetVideoPlane(0);
        uint8_t* pOut = outFrame->GetVideoPlane(0);

        int32_t stride = frameA->GetLineSize(0);
        int32_t width = frameA->GetWidth();
        int32_t height = frameA->GetHeight();
        
        int32_t boundaryX = static_cast<int32_t>(width * progress);

        for (int32_t y = 0; y < height; ++y) {
            for (int32_t x = 0; x < width; ++x) {
                int pixelIdx = y * stride + x * 4;
                
                if (m_type == TransitionType::Wipe) {
                    // Left to right wipe
                    if (x < boundaryX) {
                        std::memcpy(&pOut[pixelIdx], &pB[pixelIdx], 4);
                    } else {
                        std::memcpy(&pOut[pixelIdx], &pA[pixelIdx], 4);
                    }
                } else if (m_type == TransitionType::Push) {
                    // Left to right push
                    int aX = x + boundaryX;
                    int bX = x - (width - boundaryX);
                    
                    if (bX >= 0) {
                        int bIdx = y * stride + bX * 4;
                        std::memcpy(&pOut[pixelIdx], &pB[bIdx], 4);
                    } else {
                        if (aX < width) {
                            int aIdx = y * stride + aX * 4;
                            std::memcpy(&pOut[pixelIdx], &pA[aIdx], 4);
                        } else {
                            std::memset(&pOut[pixelIdx], 0, 4); // Edge case
                        }
                    }
                } else if (m_type == TransitionType::Slide) {
                    // Slide B over A (Left to right)
                    int bX = x - (width - boundaryX);
                    if (bX >= 0) {
                        int bIdx = y * stride + bX * 4;
                        std::memcpy(&pOut[pixelIdx], &pB[bIdx], 4);
                    } else {
                        std::memcpy(&pOut[pixelIdx], &pA[pixelIdx], 4);
                    }
                }
            }
        }
        return outFrame;
    }
    
    // Fallback to cut
    return progress >= 1.0f ? frameB : frameA;
}

} // namespace openmedia::mixer
