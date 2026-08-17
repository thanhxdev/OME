/// @file H264Decoder_NV.h
#pragma once

#include <openmedia/codecs/IDecoder.h>
#include <openmedia/core/MediaFrame.h>
#include <openmedia/core/Config.h>
#include <queue>
#include <mutex>

#ifdef HAS_NVCODEC
#include <cuda.h>
#include <nvcuvid.h>
#else
typedef struct CUstream_st* CUstream;
typedef struct CUctx_st* CUcontext;
typedef struct CUvideoparser_st* CUvideoparser;
typedef struct CUvideodecoder_st* CUvideodecoder;
#endif

namespace openmedia::codecs {

class H264Decoder_NV : public IDecoder {
public:
    H264Decoder_NV();
    ~H264Decoder_NV() override;

    // IMediaObject overrides
    std::string GetName() const override { return "H264Decoder_NV"; }
    core::PipelineState GetState() const override { return m_initialized ? core::PipelineState::Running : core::PipelineState::Stopped; }
    
    core::VoidResult Initialize() override;
    core::VoidResult Start() override;
    core::VoidResult Stop() override;
    core::VoidResult PushFrame(std::shared_ptr<core::MediaFrame> frame) override;
    [[nodiscard]] core::Result<std::shared_ptr<core::MediaFrame>> PullFrame() override;
    
    core::VoidResult Connect(std::shared_ptr<core::IMediaObject> /*downstream*/) override { return {}; }
    core::VoidResult Disconnect() override { return {}; }
    void OnStateChange(core::StateChangeCallback /*callback*/) override {}
    void OnError(core::ErrorCallback /*callback*/) override {}

private:
    bool m_initialized = false;
    uint32_t m_width = 0;
    uint32_t m_height = 0;
    CUcontext m_cuContext = nullptr;
    CUvideoparser m_parser = nullptr;
    CUvideodecoder m_decoder = nullptr;
    
    std::mutex m_queueMutex;
    std::queue<CUVIDPARSERDISPINFO> m_displayQueue;
    
#ifdef HAS_NVCODEC
    static int CUDAAPI HandleVideoSequence(void* userData, CUVIDEOFORMAT* format);
    static int CUDAAPI HandlePictureDecode(void* userData, CUVIDPICPARAMS* picParams);
    static int CUDAAPI HandlePictureDisplay(void* userData, CUVIDPARSERDISPINFO* dispInfo);
#endif
};

} // namespace openmedia::codecs
