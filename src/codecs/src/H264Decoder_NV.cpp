/// @file H264Decoder_NV.cpp
#include <openmedia/codecs/H264Decoder_NV.h>
#include <openmedia/core/Logger.h>

// Note: In real implementation, include nvcuvid.h and cuda.h here.

namespace openmedia::codecs {

H264Decoder_NV::H264Decoder_NV() {
}

H264Decoder_NV::~H264Decoder_NV() {
    (void)Stop();
}

core::VoidResult H264Decoder_NV::Initialize() {
    if (m_initialized) {
        return {};
    }
    
#ifdef HAS_NVCODEC
    // Initialize CUDA
    if (cuInit(0) != CUDA_SUCCESS) {
        return std::unexpected(core::Error::Make(core::ErrorCode::Unknown, "Failed to initialize CUDA"));
    }
    
    // Create Context
    if (cuCtxCreate(&m_cuContext, nullptr, 0, (CUdevice)0) != CUDA_SUCCESS) {
        return std::unexpected(core::Error::Make(core::ErrorCode::Unknown, "Failed to create CUDA context"));
    }
    
    // Create Parser
    CUVIDPARSERPARAMS parserParams = {};
    parserParams.CodecType = cudaVideoCodec_H264;
    parserParams.ulMaxNumDecodeSurfaces = 20;
    parserParams.ulMaxDisplayDelay = 2;
    parserParams.pUserData = this;
    parserParams.pfnSequenceCallback = HandleVideoSequence;
    parserParams.pfnDecodePicture = HandlePictureDecode;
    parserParams.pfnDisplayPicture = HandlePictureDisplay;
    
    if (cuvidCreateVideoParser(&m_parser, &parserParams) != CUDA_SUCCESS) {
        return std::unexpected(core::Error::Make(core::ErrorCode::Unknown, "Failed to create NVDEC parser"));
    }
#endif
    
    m_initialized = true;

    return {};
}

core::VoidResult H264Decoder_NV::Start() {
    if (!m_initialized) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "Decoder not initialized"));
    }
    return {};
}

core::VoidResult H264Decoder_NV::Stop() {
    if (m_initialized) {
#ifdef HAS_NVCODEC
        if (m_decoder) {
            cuvidDestroyDecoder(m_decoder);
            m_decoder = nullptr;
        }
        if (m_parser) {
            cuvidDestroyVideoParser(m_parser);
            m_parser = nullptr;
        }
        if (m_cuContext) {
            cuCtxDestroy(m_cuContext);
            m_cuContext = nullptr;
        }
#endif
        m_initialized = false;

    }
    return {};
}

core::VoidResult H264Decoder_NV::PushFrame(std::shared_ptr<core::MediaFrame> frame) {
    if (!m_initialized || !frame) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidArgument, "Invalid frame or not initialized"));
    }
    
#ifdef HAS_NVCODEC
    CUVIDSOURCEDATAPACKET packet = {};
    packet.payload = frame->GetPacketData(); // Assuming GetPacketData() contains the compressed NALU data
    packet.payload_size = static_cast<unsigned long>(frame->GetPacketSize()); // Assuming GetPacketSize() is the buffer size
    packet.flags = CUVID_PKT_ENDOFPICTURE;
    // packet.timestamp = frame->GetPts();
    
    if (packet.payload && packet.payload_size > 0) {
        CUresult res = cuvidParseVideoData(m_parser, &packet);
        if (res != CUDA_SUCCESS) {
            return std::unexpected(core::Error::Make(core::ErrorCode::Unknown, "cuvidParseVideoData failed"));
        }
    }
#endif
    
    return {};
}

core::Result<std::shared_ptr<core::MediaFrame>> H264Decoder_NV::PullFrame() {
    if (!m_initialized) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "Not initialized"));
    }
    
#ifdef HAS_NVCODEC
    CUVIDPARSERDISPINFO dispInfo;
    bool hasFrame = false;
    {
        std::lock_guard<std::mutex> lock(m_queueMutex);
        if (!m_displayQueue.empty()) {
            dispInfo = m_displayQueue.front();
            m_displayQueue.pop();
            hasFrame = true;
        }
    }

    if (hasFrame) {
        CUVIDPROCPARAMS videoProcessingParameters = {};
        videoProcessingParameters.progressive_frame = dispInfo.progressive_frame;
        videoProcessingParameters.second_field = dispInfo.repeat_first_field + 1;
        videoProcessingParameters.top_field_first = dispInfo.top_field_first;
        videoProcessingParameters.unpaired_field = dispInfo.repeat_first_field < 0;
        
        CUdeviceptr dptr = 0;
        unsigned int pitch = 0;
        CUresult res = cuvidMapVideoFrame(m_decoder, dispInfo.picture_index, &dptr, &pitch, &videoProcessingParameters);
        if (res == CUDA_SUCCESS) {

            
            // Use actual dimensions if known (or default if not)
            uint32_t width = m_width > 0 ? m_width : 1920;
            uint32_t height = m_height > 0 ? m_height : 1080;
            
            auto outFrame = core::MediaFrame::CreateVideo(width, height, core::PixelFormat::NV12);
            
            // NV12 mapping: 
            // - Luma (Y) is width x height
            // - Chroma (UV) is width x (height/2)
            // Total size = width x height x 1.5
            // Note: pitch is the line stride for Luma and Chroma.
            
            uint8_t* dst_y = outFrame->GetVideoPlane(0);
            uint8_t* dst_uv = outFrame->GetVideoPlane(1);
            
            if (dst_y && dst_uv) {
                // Copy Luma (Y)
                CUDA_MEMCPY2D m = {};
                m.srcMemoryType = CU_MEMORYTYPE_DEVICE;
                m.srcDevice = dptr;
                m.srcPitch = pitch;
                m.dstMemoryType = CU_MEMORYTYPE_HOST;
                m.dstHost = dst_y;
                m.dstPitch = width; // Host pitch is exactly width for NV12
                m.WidthInBytes = width;
                m.Height = height;
                cuMemcpy2D(&m);
                
                // Copy Chroma (UV)
                m.srcDevice = dptr + (height * pitch); // UV starts after Y (height lines of 'pitch' length)
                m.dstHost = dst_uv;
                m.dstPitch = width; // UV interleaved has the same pitch as Y (width)
                m.Height = height / 2;
                cuMemcpy2D(&m);
            }
            
            cuvidUnmapVideoFrame(m_decoder, dptr);
            return outFrame;
        } else {
            return std::unexpected(core::Error::Make(core::ErrorCode::Unknown, "Failed to map video frame"));
        }
    }
#endif
    
    // If no frame is ready, we return null or Wait (not implemented)
    return std::shared_ptr<core::MediaFrame>(nullptr);
}

#ifdef HAS_NVCODEC
int CUDAAPI H264Decoder_NV::HandleVideoSequence(void* userData, CUVIDEOFORMAT* format) {
    H264Decoder_NV* decoder = static_cast<H264Decoder_NV*>(userData);
    
    // Create decoder if not already created or if format changes (re-create not fully handled here for simplicity)
    if (!decoder->m_decoder) {
        CUVIDDECODECREATEINFO videoDecodeCreateInfo = {};
        videoDecodeCreateInfo.CodecType = format->codec;
        videoDecodeCreateInfo.ChromaFormat = format->chroma_format;
        videoDecodeCreateInfo.OutputFormat = cudaVideoSurfaceFormat_NV12; // Typical output format for NVDEC
        videoDecodeCreateInfo.ulTargetWidth = format->coded_width;
        videoDecodeCreateInfo.ulTargetHeight = format->coded_height;
        videoDecodeCreateInfo.ulWidth = format->coded_width;
        videoDecodeCreateInfo.ulHeight = format->coded_height;
        videoDecodeCreateInfo.ulNumDecodeSurfaces = format->min_num_decode_surfaces;
        videoDecodeCreateInfo.bitDepthMinus8 = format->bit_depth_luma_minus8;
        videoDecodeCreateInfo.DeinterlaceMode = cudaVideoDeinterlaceMode_Weave;
        videoDecodeCreateInfo.ulNumOutputSurfaces = 2; // For display queue
        videoDecodeCreateInfo.ulCreationFlags = cudaVideoCreate_PreferCUVID;
        videoDecodeCreateInfo.ulNumDecodeSurfaces = format->min_num_decode_surfaces + 4; // Safety margin
        videoDecodeCreateInfo.vidLock = nullptr;
        videoDecodeCreateInfo.ulWidth = format->coded_width;
        videoDecodeCreateInfo.ulHeight = format->coded_height;
        videoDecodeCreateInfo.ulMaxWidth = format->coded_width;
        videoDecodeCreateInfo.ulMaxHeight = format->coded_height;
        videoDecodeCreateInfo.ulTargetWidth = format->display_area.right - format->display_area.left;
        videoDecodeCreateInfo.ulTargetHeight = format->display_area.bottom - format->display_area.top;
        
        // Ensure valid target dimensions
        if (videoDecodeCreateInfo.ulTargetWidth == 0 || videoDecodeCreateInfo.ulTargetHeight == 0) {
            videoDecodeCreateInfo.ulTargetWidth = format->coded_width;
            videoDecodeCreateInfo.ulTargetHeight = format->coded_height;
        }

        CUresult res = cuvidCreateDecoder(&decoder->m_decoder, &videoDecodeCreateInfo);
        if (res != CUDA_SUCCESS) {
            // Error creating decoder
            return 0;
        }
    }
    
    decoder->m_width = format->coded_width;
    decoder->m_height = format->coded_height;
    
    // Return the number of decode surfaces needed
    return format->min_num_decode_surfaces + 4;
}

int CUDAAPI H264Decoder_NV::HandlePictureDecode(void* userData, CUVIDPICPARAMS* picParams) {
    H264Decoder_NV* decoder = static_cast<H264Decoder_NV*>(userData);
    if (decoder->m_decoder) {
        cuvidDecodePicture(decoder->m_decoder, picParams);
    }
    return 1;
}

int CUDAAPI H264Decoder_NV::HandlePictureDisplay(void* userData, CUVIDPARSERDISPINFO* dispInfo) {
    H264Decoder_NV* decoder = static_cast<H264Decoder_NV*>(userData);
    if (decoder && dispInfo) {
        std::lock_guard<std::mutex> lock(decoder->m_queueMutex);
        decoder->m_displayQueue.push(*dispInfo);
    }
    return 1;
}
#endif

} // namespace openmedia::codecs
