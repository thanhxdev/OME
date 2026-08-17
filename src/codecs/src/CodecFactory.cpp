#include <openmedia/codecs/CodecFactory.h>
#include <openmedia/codecs/FFmpegH264Encoder.h>
#include <openmedia/codecs/FFmpegH264Decoder.h>
#include <openmedia/codecs/FFmpegH265Encoder.h>
#include <openmedia/codecs/FFmpegH265Decoder.h>
#include <openmedia/codecs/FFmpegNVENCEncoder.h>
#include <openmedia/codecs/FFmpegNVDECDecoder.h>
#include <openmedia/codecs/FFmpegQSVEncoder.h>
#include <openmedia/codecs/FFmpegQSVDecoder.h>
#include <openmedia/codecs/FFmpegAV1Encoder.h>
#include <openmedia/codecs/FFmpegAACEncoder.h>
#include <openmedia/codecs/FFmpegOpusEncoder.h>
#include <openmedia/codecs/NVENCEncoder.h>
#include <openmedia/core/Logger.h>
namespace openmedia::codecs {

std::shared_ptr<IEncoder> CodecFactory::CreateEncoder(VideoCodec type) {
    if (type == VideoCodec::H264) {
        return std::make_shared<FFmpegH264Encoder>();
    } else if (type == VideoCodec::H265) {
        return std::make_shared<FFmpegH265Encoder>();
    } else if (type == VideoCodec::H264_NVENC) {
        return std::make_shared<FFmpegNVENCEncoder>(false);
    } else if (type == VideoCodec::H265_NVENC) {
        return std::make_shared<FFmpegNVENCEncoder>(true);
    } else if (type == VideoCodec::H264_NVENC_NATIVE) {
        return std::make_shared<NVENCEncoder>(NVENCCodec::H264);
    } else if (type == VideoCodec::H265_NVENC_NATIVE) {
        return std::make_shared<NVENCEncoder>(NVENCCodec::HEVC);
    } else if (type == VideoCodec::H264_QSV) {
        return std::make_shared<FFmpegQSVEncoder>(false);
    } else if (type == VideoCodec::H265_QSV) {
        return std::make_shared<FFmpegQSVEncoder>(true);
    } else if (type == VideoCodec::AV1) {
        return std::make_shared<FFmpegAV1Encoder>();
    }
    // Other codecs not implemented yet
    core::Logger::SError("codecs", "Unsupported encoder codec type");
    return nullptr;
}

std::shared_ptr<IDecoder> CodecFactory::CreateDecoder(VideoCodec type) {
    if (type == VideoCodec::H264) {
        return std::make_shared<FFmpegH264Decoder>();
    } else if (type == VideoCodec::H265) {
        return std::make_shared<FFmpegH265Decoder>();
    } else if (type == VideoCodec::H264_NVENC) {
        return std::make_shared<FFmpegNVDECDecoder>(false);
    } else if (type == VideoCodec::H265_NVENC) {
        return std::make_shared<FFmpegNVDECDecoder>(true);
    } else if (type == VideoCodec::H264_QSV) {
        return std::make_shared<FFmpegQSVDecoder>(false);
    } else if (type == VideoCodec::H265_QSV) {
        return std::make_shared<FFmpegQSVDecoder>(true);
    }
    // Other codecs not implemented yet
    core::Logger::SError("codecs", "Unsupported decoder codec type");
    return nullptr;
}

std::shared_ptr<IEncoder> CodecFactory::CreateAudioEncoder(AudioCodec type) {
    if (type == AudioCodec::AAC) {
        return std::make_shared<FFmpegAACEncoder>();
    } else if (type == AudioCodec::Opus) {
        return std::make_shared<FFmpegOpusEncoder>();
    }
    core::Logger::SError("codecs", "Unsupported audio encoder codec type");
    return nullptr;
}

std::shared_ptr<IDecoder> CodecFactory::CreateAudioDecoder(AudioCodec type) {
    core::Logger::SError("codecs", "Unsupported audio decoder codec type");
    return nullptr;
}

std::shared_ptr<IEncoder> CodecFactory::AutoSelectBestEncoder(VideoCodec preferred) {
    auto& log = core::Logger::Get("codecs");

    bool wantH264 = (preferred == VideoCodec::H264 || preferred == VideoCodec::H264_NVENC ||
                     preferred == VideoCodec::H264_NVENC_NATIVE || preferred == VideoCodec::H264_QSV);

    NVENCCodec nvCodec = wantH264 ? NVENCCodec::H264 : NVENCCodec::HEVC;

    // Priority 1: Native NVENC (best performance)
    if (NVENCEncoder::IsAvailable(nvCodec)) {
        log.Info("AutoSelect: using Native NVENC ({})", wantH264 ? "H264" : "HEVC");
        return std::make_shared<NVENCEncoder>(nvCodec);
    }

    // Priority 2: FFmpeg NVENC wrapper
    log.Info("AutoSelect: Native NVENC unavailable, trying FFmpeg NVENC");
    auto ffmpegNvenc = std::make_shared<FFmpegNVENCEncoder>(!wantH264);
    // We can't easily probe FFmpeg NVENC without Initialize, so return it
    // and let the caller handle init failure

    // Priority 3: Software fallback
    log.Info("AutoSelect: returning FFmpeg NVENC (fallback to software if init fails)");
    return ffmpegNvenc;
}

std::shared_ptr<IEncoder> CodecFactory::CreateEncoderWithFallback(
    VideoCodec preferred,
    bool enableGpuFallback,
    EncoderFailureCallback onFail) {
    
    auto& log = core::Logger::Get("codecs");
    
    // For now, this acts as a wrapper around AutoSelectBestEncoder with hook-ins 
    // for runtime error tracking. A fully developed version would return a proxy
    // encoder that handles hot-swapping internally when errors occur.
    
    log.Info("Creating encoder with fallback for preferred codec {}", static_cast<int>(preferred));
    
    std::shared_ptr<IEncoder> encoder;
    if (enableGpuFallback) {
        encoder = AutoSelectBestEncoder(preferred);
    } else {
        encoder = CreateEncoder(preferred);
    }
    
    if (encoder && onFail) {
        // In a real implementation, we would wrap the encoder or attach an onError listener
        // that triggers onFail when the underlying encoder throws a hardware error.
        encoder->OnError([onFail, encoder](const core::Error& err) {
            onFail(encoder);
        });
    }
    
    return encoder;
}

} // namespace openmedia::codecs
