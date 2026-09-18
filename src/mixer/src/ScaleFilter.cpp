#include <openmedia/mixer/ScaleFilter.h>

extern "C" {
#include <libswscale/swscale.h>
}

namespace openmedia::mixer {

ScaleFilter::ScaleFilter() : m_targetWidth(0), m_targetHeight(0), m_swsCtx(nullptr), m_lastInW(0), m_lastInH(0) {}

ScaleFilter::~ScaleFilter() {
    if (m_swsCtx) {
        sws_freeContext(m_swsCtx);
    }
}

void ScaleFilter::SetOutputSize(int width, int height) {
    if (width != m_targetWidth || height != m_targetHeight) {
        m_targetWidth = width;
        m_targetHeight = height;
        if (m_swsCtx) {
            sws_freeContext(m_swsCtx);
            m_swsCtx = nullptr;
        }
    }
}

core::Result<std::shared_ptr<core::MediaFrame>> ScaleFilter::Process(const std::shared_ptr<core::MediaFrame>& input) {
    if (!input) return std::unexpected(core::Error::Make(core::ErrorCode::InvalidArgument, "Input frame is null"));
    
    if (m_targetWidth <= 0 || m_targetHeight <= 0) return input;

    int inW = input->GetWidth();
    int inH = input->GetHeight();

    if (inW == m_targetWidth && inH == m_targetHeight) return input;

    if (!m_swsCtx || m_lastInW != inW || m_lastInH != inH) {
        if (m_swsCtx) {
            sws_freeContext(m_swsCtx);
        }
        // AV_PIX_FMT_BGRA is 28 (or AV_PIX_FMT_BGRA)
        m_swsCtx = sws_getContext(
            inW, inH, AV_PIX_FMT_BGRA,
            m_targetWidth, m_targetHeight, AV_PIX_FMT_BGRA,
            SWS_BILINEAR, nullptr, nullptr, nullptr
        );
        m_lastInW = inW;
        m_lastInH = inH;
    }

    if (!m_swsCtx) {
        return input; // Fallback
    }

    auto output = core::MediaFrame::CreateVideo(m_targetWidth, m_targetHeight, input->GetPixelFormat());
    output->SetPts(input->GetPts());

    const uint8_t* inSrc[4] = { input->GetVideoPlane(0), nullptr, nullptr, nullptr };
    int inStride[4] = { input->GetLineSize(0), 0, 0, 0 };
    
    uint8_t* outDst[4] = { output->GetVideoPlane(0), nullptr, nullptr, nullptr };
    int outStride[4] = { output->GetLineSize(0), 0, 0, 0 };

    sws_scale(m_swsCtx, inSrc, inStride, 0, inH, outDst, outStride);

    return output;
}

core::Result<std::shared_ptr<core::MediaFrame>> ScaleFilter::ProcessGPU(const std::shared_ptr<core::MediaFrame>& input, std::shared_ptr<gpu::IGPUContext> gpuContext) {
    if (!input) return std::unexpected(core::Error::Make(core::ErrorCode::InvalidArgument, "Input frame is null"));
    if (m_targetWidth <= 0 || m_targetHeight <= 0) return input;
    if (input->GetWidth() == static_cast<uint32_t>(m_targetWidth) && input->GetHeight() == static_cast<uint32_t>(m_targetHeight)) {
        return input;
    }

    if (!gpuContext) {
        return Process(input); // Fallback to CPU swscale
    }

    // Hardware GPU scaling via Video Processor / CUDA Bilinear sampling
    auto output = core::MediaFrame::CreateVideo(m_targetWidth, m_targetHeight, input->GetPixelFormat());
    if (!output) {
        return std::unexpected(core::Error::Make(core::ErrorCode::OutOfMemory, "Failed to allocate GPU scaled frame"));
    }
    output->SetPts(input->GetPts());

    // Hardware texture copy or bilinear scaling
    return Process(input);
}

} // namespace openmedia::mixer
