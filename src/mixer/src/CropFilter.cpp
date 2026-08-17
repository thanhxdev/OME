#include <openmedia/mixer/CropFilter.h>
#include <cstring>
#include <algorithm>

namespace openmedia::mixer {

CropFilter::CropFilter() : m_x(0), m_y(0), m_width(0), m_height(0) {}
CropFilter::~CropFilter() {}

void CropFilter::SetCrop(int x, int y, int width, int height) {
    m_x = x;
    m_y = y;
    m_width = width;
    m_height = height;
}

core::Result<std::shared_ptr<core::MediaFrame>> CropFilter::Process(const std::shared_ptr<core::MediaFrame>& input) {
    if (!input) return std::unexpected(core::Error::Make(core::ErrorCode::InvalidArgument, "Input frame is null"));
    
    if (m_width <= 0 || m_height <= 0) return input; // No crop applied

    int inWidth = input->GetWidth();
    int inHeight = input->GetHeight();

    int startX = std::clamp(m_x, 0, inWidth - 1);
    int startY = std::clamp(m_y, 0, inHeight - 1);
    int cropW = std::clamp(m_width, 1, inWidth - startX);
    int cropH = std::clamp(m_height, 1, inHeight - startY);

    if (startX == 0 && startY == 0 && cropW == inWidth && cropH == inHeight) {
        return input; // Full frame, no crop needed
    }

    auto output = core::MediaFrame::CreateVideo(cropW, cropH, input->GetPixelFormat());
    output->SetPts(input->GetPts());
    
    // For now support BGRA (4 bytes per pixel)
    if (input->GetPixelFormat() == core::PixelFormat::BGRA) {
        const uint8_t* inPixels = input->GetVideoPlane(0);
        uint8_t* outPixels = output->GetVideoPlane(0);
        int inStride = input->GetLineSize(0);
        int outStride = output->GetLineSize(0);
        
        for (int y = 0; y < cropH; ++y) {
            const uint8_t* inRow = inPixels + ((startY + y) * inStride) + (startX * 4);
            uint8_t* outRow = outPixels + (y * outStride);
            std::memcpy(outRow, inRow, cropW * 4);
        }
    } else {
        // Fallback for non-BGRA formats, returning uncropped for now
        return input;
    }

    return output;
}

} // namespace openmedia::mixer
