/// @file NVENCEncoder.cpp
/// @brief Native NVIDIA Video Codec SDK encoder implementation
///
/// Directly uses nvEncodeAPI.h for hardware-accelerated H.264 / HEVC encoding.
/// Zero-copy path when input frames are already on GPU (CUDA device pointers).

#include <openmedia/codecs/NVENCEncoder.h>
#include <openmedia/core/Logger.h>

#ifdef HAS_NVCODEC

#include <cuda.h>
#include <nvEncodeAPI.h>

#include <mutex>
#include <queue>
#include <vector>
#include <cstring>
#include <algorithm>

namespace openmedia::codecs {

// ---------------------------------------------------------------------------
// Helper: load NVENC API function table
// ---------------------------------------------------------------------------
namespace {

using NvEncCreateInstance_t = NVENCSTATUS(NVENCAPI*)(NV_ENCODE_API_FUNCTION_LIST*);

#ifdef _WIN32
#include <Windows.h>
static HMODULE g_nvencModule = nullptr;

NvEncCreateInstance_t LoadNvEncAPI() {
    if (!g_nvencModule) {
        g_nvencModule = LoadLibraryA("nvEncodeAPI64.dll");
        if (!g_nvencModule) g_nvencModule = LoadLibraryA("nvEncodeAPI.dll");
    }
    if (!g_nvencModule) return nullptr;
    return reinterpret_cast<NvEncCreateInstance_t>(GetProcAddress(g_nvencModule, "NvEncodeAPICreateInstance"));
}
#else
#include <dlfcn.h>
static void* g_nvencModule = nullptr;

NvEncCreateInstance_t LoadNvEncAPI() {
    if (!g_nvencModule) {
        g_nvencModule = dlopen("libnvidia-encode.so.1", RTLD_LAZY);
        if (!g_nvencModule) g_nvencModule = dlopen("libnvidia-encode.so", RTLD_LAZY);
    }
    if (!g_nvencModule) return nullptr;
    return reinterpret_cast<NvEncCreateInstance_t>(dlsym(g_nvencModule, "NvEncodeAPICreateInstance"));
}
#endif

std::string NvEncStatusString(NVENCSTATUS status) {
    switch (status) {
        case NV_ENC_SUCCESS:                     return "NV_ENC_SUCCESS";
        case NV_ENC_ERR_NO_ENCODE_DEVICE:        return "NV_ENC_ERR_NO_ENCODE_DEVICE";
        case NV_ENC_ERR_UNSUPPORTED_DEVICE:      return "NV_ENC_ERR_UNSUPPORTED_DEVICE";
        case NV_ENC_ERR_INVALID_ENCODERDEVICE:   return "NV_ENC_ERR_INVALID_ENCODERDEVICE";
        case NV_ENC_ERR_INVALID_DEVICE:          return "NV_ENC_ERR_INVALID_DEVICE";
        case NV_ENC_ERR_DEVICE_NOT_EXIST:        return "NV_ENC_ERR_DEVICE_NOT_EXIST";
        case NV_ENC_ERR_INVALID_PTR:             return "NV_ENC_ERR_INVALID_PTR";
        case NV_ENC_ERR_INVALID_EVENT:           return "NV_ENC_ERR_INVALID_EVENT";
        case NV_ENC_ERR_INVALID_PARAM:           return "NV_ENC_ERR_INVALID_PARAM";
        case NV_ENC_ERR_INVALID_CALL:            return "NV_ENC_ERR_INVALID_CALL";
        case NV_ENC_ERR_OUT_OF_MEMORY:           return "NV_ENC_ERR_OUT_OF_MEMORY";
        case NV_ENC_ERR_ENCODER_NOT_INITIALIZED: return "NV_ENC_ERR_ENCODER_NOT_INITIALIZED";
        case NV_ENC_ERR_UNSUPPORTED_PARAM:       return "NV_ENC_ERR_UNSUPPORTED_PARAM";
        case NV_ENC_ERR_LOCK_BUSY:               return "NV_ENC_ERR_LOCK_BUSY";
        case NV_ENC_ERR_ENCODER_BUSY:            return "NV_ENC_ERR_ENCODER_BUSY";
        case NV_ENC_ERR_GENERIC:                 return "NV_ENC_ERR_GENERIC";
        default:                                 return "UNKNOWN_NVENC_STATUS(" + std::to_string(static_cast<int>(status)) + ")";
    }
}

} // anonymous namespace

// ---------------------------------------------------------------------------
// Impl
// ---------------------------------------------------------------------------
struct NVENCEncoder::Impl {
    NVENCCodec codec;
    int deviceId = 0;

    // State
    std::mutex mutex;
    core::PipelineState state{core::PipelineState::Stopped};
    std::shared_ptr<core::IMediaObject> downstream;
    core::StateChangeCallback onStateChange;
    core::ErrorCallback onError;
    EncoderConfig config;

    // Tuning
    NVENCTuning tuning = NVENCTuning::LowLatency;
    int bFrames = 0;
    int lookahead = 0;

    // CUDA
    CUcontext cuCtx = nullptr;
    bool ownCuCtx = false; // true if we created the context ourselves

    // NVENC
    NV_ENCODE_API_FUNCTION_LIST nvenc{};
    void* encoder = nullptr;

    // Buffers
    static constexpr int kBufferCount = 4;
    std::vector<NV_ENC_INPUT_PTR> inputBuffers;
    std::vector<NV_ENC_OUTPUT_PTR> outputBuffers;
    int nextInputIdx = 0;

    // Encoded frame queue
    std::queue<std::shared_ptr<core::MediaFrame>> encodedQueue;

    int64_t frameCount = 0;

    // --- helpers ---

    void SetState(core::PipelineState newState) {
        auto old = state;
        state = newState;
        if (onStateChange) onStateChange(old, newState);
    }

    void ReportError(core::ErrorCode code, const std::string& msg) {
        if (onError) onError(core::Error::Make(code, msg));
    }

    GUID CodecGUID() const {
        return codec == NVENCCodec::HEVC ? NV_ENC_CODEC_HEVC_GUID : NV_ENC_CODEC_H264_GUID;
    }

    NV_ENC_TUNING_INFO TuningInfo() const {
        switch (tuning) {
            case NVENCTuning::HighQuality:      return NV_ENC_TUNING_INFO_HIGH_QUALITY;
            case NVENCTuning::LowLatency:       return NV_ENC_TUNING_INFO_LOW_LATENCY;
            case NVENCTuning::UltraLowLatency:  return NV_ENC_TUNING_INFO_ULTRA_LOW_LATENCY;
            case NVENCTuning::Lossless:         return NV_ENC_TUNING_INFO_LOSSLESS;
            default:                            return NV_ENC_TUNING_INFO_LOW_LATENCY;
        }
    }

    GUID PresetGUID() const {
        // Map EncoderConfig preset string to NVENC preset GUID
        // NVENC SDK 12+ uses P1-P7 presets
        const auto& p = config.preset;
        if (p == "p1" || p == "fastest")  return NV_ENC_PRESET_P1_GUID;
        if (p == "p2")                    return NV_ENC_PRESET_P2_GUID;
        if (p == "p3")                    return NV_ENC_PRESET_P3_GUID;
        if (p == "p5")                    return NV_ENC_PRESET_P5_GUID;
        if (p == "p6")                    return NV_ENC_PRESET_P6_GUID;
        if (p == "p7" || p == "best")     return NV_ENC_PRESET_P7_GUID;
        // Default: P4 (balanced)
        return NV_ENC_PRESET_P4_GUID;
    }

    core::VoidResult InitCUDA() {
        CUresult res = cuInit(0);
        if (res != CUDA_SUCCESS) {
            return std::unexpected(core::Error::Make(core::ErrorCode::GPUNotAvailable, "cuInit failed"));
        }

        CUdevice dev;
        res = cuDeviceGet(&dev, deviceId);
        if (res != CUDA_SUCCESS) {
            return std::unexpected(core::Error::Make(core::ErrorCode::GPUContextFailed, "cuDeviceGet failed for device " + std::to_string(deviceId)));
        }

#if CUDA_VERSION >= 13000
        res = cuCtxCreate(&cuCtx, nullptr, 0, dev);
#else
        res = cuCtxCreate(&cuCtx, 0, dev);
#endif
        if (res != CUDA_SUCCESS) {
            return std::unexpected(core::Error::Make(core::ErrorCode::GPUContextFailed, "cuCtxCreate failed"));
        }
        ownCuCtx = true;
        return {};
    }

    core::VoidResult InitEncoder() {
        auto createInstance = LoadNvEncAPI();
        if (!createInstance) {
            return std::unexpected(core::Error::Make(core::ErrorCode::CodecNotFound, "Failed to load nvEncodeAPI library"));
        }

        nvenc = {};
        nvenc.version = NV_ENCODE_API_FUNCTION_LIST_VER;
        NVENCSTATUS status = createInstance(&nvenc);
        if (status != NV_ENC_SUCCESS) {
            return std::unexpected(core::Error::Make(core::ErrorCode::CodecOpenFailed,
                "NvEncodeAPICreateInstance failed: " + NvEncStatusString(status)));
        }

        // Open encode session
        NV_ENC_OPEN_ENCODE_SESSION_EX_PARAMS sessionParams = {};
        sessionParams.version = NV_ENC_OPEN_ENCODE_SESSION_EX_PARAMS_VER;
        sessionParams.deviceType = NV_ENC_DEVICE_TYPE_CUDA;
        sessionParams.device = cuCtx;
        sessionParams.apiVersion = NVENCAPI_VERSION;

        status = nvenc.nvEncOpenEncodeSessionEx(&sessionParams, &encoder);
        if (status != NV_ENC_SUCCESS) {
            return std::unexpected(core::Error::Make(core::ErrorCode::CodecOpenFailed,
                "nvEncOpenEncodeSessionEx failed: " + NvEncStatusString(status)));
        }

        // Get preset config
        NV_ENC_PRESET_CONFIG presetConfig = {};
        presetConfig.version = NV_ENC_PRESET_CONFIG_VER;
        presetConfig.presetCfg.version = NV_ENC_CONFIG_VER;

        status = nvenc.nvEncGetEncodePresetConfigEx(encoder, CodecGUID(), PresetGUID(), TuningInfo(), &presetConfig);
        if (status != NV_ENC_SUCCESS) {
            return std::unexpected(core::Error::Make(core::ErrorCode::CodecConfigInvalid,
                "nvEncGetEncodePresetConfigEx failed: " + NvEncStatusString(status)));
        }

        NV_ENC_CONFIG encConfig = presetConfig.presetCfg;

        // Rate control
        auto& rc = encConfig.rcParams;
        if (config.rcMode == RateControlMode::CBR) {
            rc.rateControlMode = NV_ENC_PARAMS_RC_CBR;
            rc.averageBitRate  = static_cast<uint32_t>(config.bitrate);
            rc.maxBitRate      = static_cast<uint32_t>(config.bitrate);
        } else if (config.rcMode == RateControlMode::VBR) {
            rc.rateControlMode = NV_ENC_PARAMS_RC_VBR;
            rc.averageBitRate  = static_cast<uint32_t>(config.bitrate);
            rc.maxBitRate      = static_cast<uint32_t>(config.bitrate * 2);
        } else { // CQ
            rc.rateControlMode = NV_ENC_PARAMS_RC_VBR;
            rc.targetQuality   = static_cast<uint8_t>(config.quality);
        }

        // Temporal AQ for quality
        if (tuning == NVENCTuning::HighQuality) {
            rc.enableTemporalAQ = 1;
        }

        // Lookahead
        if (lookahead > 0) {
            rc.enableLookahead  = 1;
            rc.lookaheadDepth   = static_cast<uint16_t>(lookahead);
        }

        // GOP / B-frames
        encConfig.gopLength = NVENC_INFINITE_GOPLENGTH;
        if (config.fps > 0) {
            encConfig.gopLength = static_cast<uint32_t>(config.fps * 2); // 2-second GOP
        }
        encConfig.frameIntervalP = bFrames + 1; // 1 = no B-frames

        bool is10Bit = (config.pixelFormat == core::PixelFormat::P010LE || config.pixelFormat == core::PixelFormat::YUV420P10LE);

        // Codec-specific
        if (codec == NVENCCodec::H264) {
            auto& h264 = encConfig.encodeCodecConfig.h264Config;
            h264.idrPeriod = encConfig.gopLength;
            if (!config.profile.empty()) {
                if (config.profile == "baseline") h264.h264VUIParameters.colourPrimaries = NV_ENC_VUI_COLOR_PRIMARIES_BT709; // simplified
                if (config.profile == "main") { /* default */ }
                if (config.profile == "high") h264.chromaFormatIDC = 1;
            }
            h264.repeatSPSPPS = 1; // Include SPS/PPS with each IDR
        } else {
            auto& hevc = encConfig.encodeCodecConfig.hevcConfig;
            hevc.idrPeriod = encConfig.gopLength;
            hevc.repeatSPSPPS = 1;
            if (is10Bit) {
                encConfig.profileGUID = NV_ENC_HEVC_PROFILE_MAIN10_GUID;
                hevc.inputBitDepth = NV_ENC_BIT_DEPTH_10;
                hevc.outputBitDepth = NV_ENC_BIT_DEPTH_10;
            }
        }

        // Initialize encoder
        NV_ENC_INITIALIZE_PARAMS initParams = {};
        initParams.version = NV_ENC_INITIALIZE_PARAMS_VER;
        initParams.encodeGUID  = CodecGUID();
        initParams.presetGUID  = PresetGUID();
        initParams.tuningInfo  = TuningInfo();
        initParams.encodeWidth = static_cast<uint32_t>(config.width);
        initParams.encodeHeight = static_cast<uint32_t>(config.height);
        initParams.darWidth    = static_cast<uint32_t>(config.width);
        initParams.darHeight   = static_cast<uint32_t>(config.height);
        initParams.frameRateNum = static_cast<uint32_t>(config.fps);
        initParams.frameRateDen = 1;
        initParams.enablePTD   = 1; // Picture Type Decision by NVENC
        initParams.encodeConfig = &encConfig;

        status = nvenc.nvEncInitializeEncoder(encoder, &initParams);
        if (status != NV_ENC_SUCCESS) {
            return std::unexpected(core::Error::Make(core::ErrorCode::CodecOpenFailed,
                "nvEncInitializeEncoder failed: " + NvEncStatusString(status)));
        }

        return {};
    }

    core::VoidResult AllocateBuffers() {
        inputBuffers.resize(kBufferCount);
        outputBuffers.resize(kBufferCount);

        for (int i = 0; i < kBufferCount; ++i) {
            // Input buffer
            NV_ENC_CREATE_INPUT_BUFFER createIn = {};
            createIn.version = NV_ENC_CREATE_INPUT_BUFFER_VER;
            createIn.width   = static_cast<uint32_t>(config.width);
            createIn.height  = static_cast<uint32_t>(config.height);
            bool is10Bit = (config.pixelFormat == core::PixelFormat::P010LE || config.pixelFormat == core::PixelFormat::YUV420P10LE);
            createIn.bufferFmt = is10Bit ? NV_ENC_BUFFER_FORMAT_YUV420_10BIT : NV_ENC_BUFFER_FORMAT_NV12;

            NVENCSTATUS status = nvenc.nvEncCreateInputBuffer(encoder, &createIn);
            if (status != NV_ENC_SUCCESS) {
                return std::unexpected(core::Error::Make(core::ErrorCode::GPUMemoryFailed,
                    "nvEncCreateInputBuffer failed: " + NvEncStatusString(status)));
            }
            inputBuffers[i] = createIn.inputBuffer;

            // Output bitstream buffer
            NV_ENC_CREATE_BITSTREAM_BUFFER createOut = {};
            createOut.version = NV_ENC_CREATE_BITSTREAM_BUFFER_VER;

            status = nvenc.nvEncCreateBitstreamBuffer(encoder, &createOut);
            if (status != NV_ENC_SUCCESS) {
                return std::unexpected(core::Error::Make(core::ErrorCode::GPUMemoryFailed,
                    "nvEncCreateBitstreamBuffer failed: " + NvEncStatusString(status)));
            }
            outputBuffers[i] = createOut.bitstreamBuffer;
        }

        return {};
    }

    void DestroyBuffers() {
        if (!encoder || !nvenc.nvEncDestroyInputBuffer) return;

        for (auto& buf : inputBuffers) {
            if (buf) nvenc.nvEncDestroyInputBuffer(encoder, buf);
        }
        inputBuffers.clear();

        for (auto& buf : outputBuffers) {
            if (buf) nvenc.nvEncDestroyBitstreamBuffer(encoder, buf);
        }
        outputBuffers.clear();
    }

    void DestroyEncoder() {
        DestroyBuffers();
        if (encoder && nvenc.nvEncDestroyEncoder) {
            nvenc.nvEncDestroyEncoder(encoder);
            encoder = nullptr;
        }
        if (ownCuCtx && cuCtx) {
            cuCtxDestroy(cuCtx);
            cuCtx = nullptr;
            ownCuCtx = false;
        }
    }
};

// ---------------------------------------------------------------------------
// NVENCEncoder public API
// ---------------------------------------------------------------------------

NVENCEncoder::NVENCEncoder(NVENCCodec codec, int deviceId)
    : m_impl(std::make_unique<Impl>())
{
    m_impl->codec = codec;
    m_impl->deviceId = deviceId;
}

NVENCEncoder::~NVENCEncoder() {
    (void)Stop();
    m_impl->DestroyEncoder();
}

std::string NVENCEncoder::GetName() const {
    return m_impl->codec == NVENCCodec::HEVC ? "NVENCEncoder(HEVC)" : "NVENCEncoder(H264)";
}

core::PipelineState NVENCEncoder::GetState() const {
    return m_impl->state;
}

core::VoidResult NVENCEncoder::Configure(const EncoderConfig& config) {
    std::lock_guard lock(m_impl->mutex);
    m_impl->config = config;
    return {};
}

void NVENCEncoder::SetTuning(NVENCTuning tuning) {
    m_impl->tuning = tuning;
}

void NVENCEncoder::SetBFrames(int count) {
    m_impl->bFrames = std::clamp(count, 0, 4);
}

void NVENCEncoder::SetLookahead(int frames) {
    m_impl->lookahead = std::clamp(frames, 0, 32);
}

core::VoidResult NVENCEncoder::Initialize() {
    std::lock_guard lock(m_impl->mutex);
    auto& log = core::Logger::Get("codecs.nvenc");

    log.Info("Initializing NVENCEncoder: codec={} {}x{} @ {}fps, bitrate={}",
        m_impl->codec == NVENCCodec::HEVC ? "HEVC" : "H264",
        m_impl->config.width, m_impl->config.height,
        m_impl->config.fps, m_impl->config.bitrate);

    // 1. Create CUDA context
    auto cudaResult = m_impl->InitCUDA();
    if (!cudaResult) {
        log.Error("CUDA init failed: {}", cudaResult.error().message);
        return cudaResult;
    }

    // 2. Open encoder session & configure
    auto encResult = m_impl->InitEncoder();
    if (!encResult) {
        log.Error("NVENC init failed: {}", encResult.error().message);
        m_impl->DestroyEncoder();
        return encResult;
    }

    // 3. Allocate I/O buffers
    auto bufResult = m_impl->AllocateBuffers();
    if (!bufResult) {
        log.Error("Buffer allocation failed: {}", bufResult.error().message);
        m_impl->DestroyEncoder();
        return bufResult;
    }

    m_impl->SetState(core::PipelineState::Ready);
    log.Info("NVENCEncoder initialized successfully (buffers={})", Impl::kBufferCount);
    return {};
}

core::VoidResult NVENCEncoder::Start() {
    std::lock_guard lock(m_impl->mutex);
    if (m_impl->state == core::PipelineState::Running) return {};
    if (m_impl->state != core::PipelineState::Ready && m_impl->state != core::PipelineState::Stopped) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "Encoder not ready"));
    }
    m_impl->frameCount = 0;
    m_impl->SetState(core::PipelineState::Running);
    return {};
}

core::VoidResult NVENCEncoder::Stop() {
    std::lock_guard lock(m_impl->mutex);
    if (m_impl->state != core::PipelineState::Running) return {};
    m_impl->SetState(core::PipelineState::Stopped);
    return {};
}

core::VoidResult NVENCEncoder::PushFrame(std::shared_ptr<core::MediaFrame> frame) {
    std::lock_guard lock(m_impl->mutex);

    if (m_impl->state != core::PipelineState::Running) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "Encoder is not running"));
    }
    if (!frame) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidArgument, "Null frame"));
    }

    auto& nvenc = m_impl->nvenc;
    int idx = m_impl->nextInputIdx;
    m_impl->nextInputIdx = (idx + 1) % Impl::kBufferCount;

    // Lock input buffer and copy NV12 data from CPU frame
    NV_ENC_LOCK_INPUT_BUFFER lockParams = {};
    lockParams.version = NV_ENC_LOCK_INPUT_BUFFER_VER;
    lockParams.inputBuffer = m_impl->inputBuffers[idx];

    NVENCSTATUS status = nvenc.nvEncLockInputBuffer(m_impl->encoder, &lockParams);
    if (status != NV_ENC_SUCCESS) {
        return std::unexpected(core::Error::Make(core::ErrorCode::EncodeFailed,
            "nvEncLockInputBuffer failed: " + NvEncStatusString(status)));
    }

    // Copy NV12/P010 planes: Y plane then UV plane
    uint8_t* dst = static_cast<uint8_t*>(lockParams.bufferDataPtr);
    uint32_t dstPitch = lockParams.pitch;
    uint32_t w = static_cast<uint32_t>(m_impl->config.width);
    uint32_t h = static_cast<uint32_t>(m_impl->config.height);

    bool is10Bit = (m_impl->config.pixelFormat == core::PixelFormat::P010LE || m_impl->config.pixelFormat == core::PixelFormat::YUV420P10LE);
    uint32_t bytesPerPixel = is10Bit ? 2 : 1;
    uint32_t copyWidthBytes = w * bytesPerPixel;

    // Y plane
    const uint8_t* srcY = frame->GetVideoPlane(0);
    int srcStrideY = frame->GetLineSize(0);
    if (srcY && srcStrideY > 0) {
        for (uint32_t row = 0; row < h; ++row) {
            std::memcpy(dst + row * dstPitch, srcY + row * srcStrideY, copyWidthBytes);
        }
    }

    // UV plane (NV12/P010: interleaved U/V at half height)
    const uint8_t* srcUV = frame->GetVideoPlane(1);
    int srcStrideUV = frame->GetLineSize(1);
    uint8_t* dstUV = dst + dstPitch * h;
    if (srcUV && srcStrideUV > 0) {
        for (uint32_t row = 0; row < h / 2; ++row) {
            std::memcpy(dstUV + row * dstPitch, srcUV + row * srcStrideUV, copyWidthBytes);
        }
    }

    nvenc.nvEncUnlockInputBuffer(m_impl->encoder, m_impl->inputBuffers[idx]);

    // Encode
    NV_ENC_PIC_PARAMS picParams = {};
    picParams.version = NV_ENC_PIC_PARAMS_VER;
    picParams.inputBuffer  = m_impl->inputBuffers[idx];
    picParams.outputBitstream = m_impl->outputBuffers[idx];
    picParams.bufferFmt    = is10Bit ? NV_ENC_BUFFER_FORMAT_YUV420_10BIT : NV_ENC_BUFFER_FORMAT_NV12;
    picParams.inputWidth   = w;
    picParams.inputHeight  = h;
    picParams.pictureStruct = NV_ENC_PIC_STRUCT_FRAME;
    picParams.inputTimeStamp = frame->GetPts();

    status = nvenc.nvEncEncodePicture(m_impl->encoder, &picParams);
    if (status != NV_ENC_SUCCESS && status != NV_ENC_ERR_NEED_MORE_INPUT) {
        return std::unexpected(core::Error::Make(core::ErrorCode::EncodeFailed,
            "nvEncEncodePicture failed: " + NvEncStatusString(status)));
    }

    // If encoder produced output, lock bitstream and create output packet
    if (status == NV_ENC_SUCCESS) {
        NV_ENC_LOCK_BITSTREAM lockBs = {};
        lockBs.version = NV_ENC_LOCK_BITSTREAM_VER;
        lockBs.outputBitstream = m_impl->outputBuffers[idx];

        status = nvenc.nvEncLockBitstream(m_impl->encoder, &lockBs);
        if (status == NV_ENC_SUCCESS) {
            auto pkt = core::MediaFrame::CreatePacket(lockBs.bitstreamSizeInBytes);
            std::memcpy(pkt->GetPacketData(), lockBs.bitstreamBufferPtr, lockBs.bitstreamSizeInBytes);
            pkt->SetPts(static_cast<int64_t>(lockBs.outputTimeStamp));
            pkt->SetDts(static_cast<int64_t>(lockBs.outputTimeStamp));
            pkt->SetTimeBase({1, m_impl->config.fps});

            nvenc.nvEncUnlockBitstream(m_impl->encoder, m_impl->outputBuffers[idx]);

            m_impl->encodedQueue.push(pkt);
            ++m_impl->frameCount;

            if (m_impl->downstream) {
                (void)m_impl->downstream->PushFrame(pkt);
            }
        }
    }

    return {};
}

core::Result<std::shared_ptr<core::MediaFrame>> NVENCEncoder::PullFrame() {
    std::lock_guard lock(m_impl->mutex);
    if (m_impl->encodedQueue.empty()) {
        return std::unexpected(core::Error::Make(core::ErrorCode::WouldBlock, "No encoded frames available"));
    }
    auto f = m_impl->encodedQueue.front();
    m_impl->encodedQueue.pop();
    return f;
}

void NVENCEncoder::Flush() {
    std::lock_guard lock(m_impl->mutex);
    if (!m_impl->encoder) return;

    auto& nvenc = m_impl->nvenc;

    // Send EOS signal
    NV_ENC_PIC_PARAMS eosParams = {};
    eosParams.version = NV_ENC_PIC_PARAMS_VER;
    eosParams.encodePicFlags = NV_ENC_PIC_FLAG_EOS;

    nvenc.nvEncEncodePicture(m_impl->encoder, &eosParams);

    // Drain remaining output
    for (int i = 0; i < Impl::kBufferCount; ++i) {
        NV_ENC_LOCK_BITSTREAM lockBs = {};
        lockBs.version = NV_ENC_LOCK_BITSTREAM_VER;
        lockBs.outputBitstream = m_impl->outputBuffers[i];

        NVENCSTATUS status = nvenc.nvEncLockBitstream(m_impl->encoder, &lockBs);
        if (status == NV_ENC_SUCCESS && lockBs.bitstreamSizeInBytes > 0) {
            auto pkt = core::MediaFrame::CreatePacket(lockBs.bitstreamSizeInBytes);
            std::memcpy(pkt->GetPacketData(), lockBs.bitstreamBufferPtr, lockBs.bitstreamSizeInBytes);
            pkt->SetPts(static_cast<int64_t>(lockBs.outputTimeStamp));
            m_impl->encodedQueue.push(pkt);

            nvenc.nvEncUnlockBitstream(m_impl->encoder, m_impl->outputBuffers[i]);
        }
    }
}

core::VoidResult NVENCEncoder::Connect(std::shared_ptr<core::IMediaObject> downstream) {
    std::lock_guard lock(m_impl->mutex);
    m_impl->downstream = downstream;
    return {};
}

core::VoidResult NVENCEncoder::Disconnect() {
    std::lock_guard lock(m_impl->mutex);
    m_impl->downstream.reset();
    return {};
}

void NVENCEncoder::OnStateChange(core::StateChangeCallback callback) {
    m_impl->onStateChange = callback;
}

void NVENCEncoder::OnError(core::ErrorCallback callback) {
    m_impl->onError = callback;
}

bool NVENCEncoder::IsAvailable(NVENCCodec codec, int deviceId) {
    // Quick probe: try opening a session then immediately close it
    CUresult res = cuInit(0);
    if (res != CUDA_SUCCESS) return false;

    CUdevice dev;
    if (cuDeviceGet(&dev, deviceId) != CUDA_SUCCESS) return false;

    CUcontext ctx;
#if CUDA_VERSION >= 13000
    if (cuCtxCreate(&ctx, nullptr, 0, dev) != CUDA_SUCCESS) return false;
#else
    if (cuCtxCreate(&ctx, 0, dev) != CUDA_SUCCESS) return false;
#endif

    auto createInstance = LoadNvEncAPI();
    bool available = false;

    if (createInstance) {
        NV_ENCODE_API_FUNCTION_LIST fn = {};
        fn.version = NV_ENCODE_API_FUNCTION_LIST_VER;
        if (createInstance(&fn) == NV_ENC_SUCCESS) {
            NV_ENC_OPEN_ENCODE_SESSION_EX_PARAMS sp = {};
            sp.version = NV_ENC_OPEN_ENCODE_SESSION_EX_PARAMS_VER;
            sp.deviceType = NV_ENC_DEVICE_TYPE_CUDA;
            sp.device = ctx;
            sp.apiVersion = NVENCAPI_VERSION;

            void* enc = nullptr;
            if (fn.nvEncOpenEncodeSessionEx(&sp, &enc) == NV_ENC_SUCCESS) {
                // Check codec support
                GUID guid = (codec == NVENCCodec::HEVC) ? NV_ENC_CODEC_HEVC_GUID : NV_ENC_CODEC_H264_GUID;
                uint32_t count = 0;
                fn.nvEncGetEncodeGUIDCount(enc, &count);
                std::vector<GUID> guids(count);
                fn.nvEncGetEncodeGUIDs(enc, guids.data(), count, &count);

                for (uint32_t i = 0; i < count; ++i) {
                    if (memcmp(&guids[i], &guid, sizeof(GUID)) == 0) {
                        available = true;
                        break;
                    }
                }
                fn.nvEncDestroyEncoder(enc);
            }
        }
    }

    cuCtxDestroy(ctx);
    return available;
}

// ---------------------------------------------------------------------------
// NVENCCapabilities query
// ---------------------------------------------------------------------------
core::Result<NVENCCaps> QueryNVENCCapabilities(CUcontext cuCtx) {
    auto createInstance = LoadNvEncAPI();
    if (!createInstance) {
        return std::unexpected(core::Error::Make(core::ErrorCode::CodecNotFound, "nvEncodeAPI not available"));
    }

    NV_ENCODE_API_FUNCTION_LIST fn = {};
    fn.version = NV_ENCODE_API_FUNCTION_LIST_VER;
    if (createInstance(&fn) != NV_ENC_SUCCESS) {
        return std::unexpected(core::Error::Make(core::ErrorCode::CodecOpenFailed, "NvEncodeAPICreateInstance failed"));
    }

    NV_ENC_OPEN_ENCODE_SESSION_EX_PARAMS sp = {};
    sp.version = NV_ENC_OPEN_ENCODE_SESSION_EX_PARAMS_VER;
    sp.deviceType = NV_ENC_DEVICE_TYPE_CUDA;
    sp.device = cuCtx;
    sp.apiVersion = NVENCAPI_VERSION;

    void* enc = nullptr;
    if (fn.nvEncOpenEncodeSessionEx(&sp, &enc) != NV_ENC_SUCCESS) {
        return std::unexpected(core::Error::Make(core::ErrorCode::CodecOpenFailed, "Cannot open NVENC session"));
    }

    NVENCCaps caps;

    // Check which codecs are supported
    uint32_t guidCount = 0;
    fn.nvEncGetEncodeGUIDCount(enc, &guidCount);
    std::vector<GUID> guids(guidCount);
    fn.nvEncGetEncodeGUIDs(enc, guids.data(), guidCount, &guidCount);

    for (uint32_t i = 0; i < guidCount; ++i) {
        if (memcmp(&guids[i], &NV_ENC_CODEC_H264_GUID, sizeof(GUID)) == 0) caps.supportsH264 = true;
        if (memcmp(&guids[i], &NV_ENC_CODEC_HEVC_GUID, sizeof(GUID)) == 0) caps.supportsHEVC = true;
        // AV1 GUID check would go here for Ada Lovelace+
    }

    // Query H.264 capabilities as representative
    auto queryIntCap = [&](GUID codecGuid, NV_ENC_CAPS capType) -> int {
        NV_ENC_CAPS_PARAM capParam = {};
        capParam.version = NV_ENC_CAPS_PARAM_VER;
        capParam.capsToQuery = capType;
        int val = 0;
        fn.nvEncGetEncodeCaps(enc, codecGuid, &capParam, &val);
        return val;
    };

    GUID probeCodec = caps.supportsH264 ? NV_ENC_CODEC_H264_GUID : NV_ENC_CODEC_HEVC_GUID;

    caps.maxWidth             = queryIntCap(probeCodec, NV_ENC_CAPS_WIDTH_MAX);
    caps.maxHeight            = queryIntCap(probeCodec, NV_ENC_CAPS_HEIGHT_MAX);
    caps.maxConcurrentSessions = queryIntCap(probeCodec, NV_ENC_CAPS_NUM_MAX_BFRAMES);
    caps.supportsBFrames      = queryIntCap(probeCodec, NV_ENC_CAPS_NUM_MAX_BFRAMES) > 0;
    caps.supportsLookahead    = queryIntCap(probeCodec, NV_ENC_CAPS_SUPPORT_LOOKAHEAD) != 0;
    caps.supportsTemporalAQ   = queryIntCap(probeCodec, NV_ENC_CAPS_SUPPORT_TEMPORAL_AQ) != 0;
    caps.supports10Bit        = queryIntCap(probeCodec, NV_ENC_CAPS_SUPPORT_10BIT_ENCODE) != 0;
    caps.supportsLossless     = queryIntCap(probeCodec, NV_ENC_CAPS_SUPPORT_LOSSLESS_ENCODE) != 0;
    caps.supportsWeightedPred = queryIntCap(probeCodec, NV_ENC_CAPS_SUPPORT_WEIGHTED_PREDICTION) != 0;

    fn.nvEncDestroyEncoder(enc);
    return caps;
}

} // namespace openmedia::codecs

#else // !HAS_NVCODEC — stub when SDK is not available

namespace openmedia::codecs {

struct NVENCEncoder::Impl {
    NVENCCodec codec;
    core::PipelineState state{core::PipelineState::Stopped};
    EncoderConfig config;
    std::shared_ptr<core::IMediaObject> downstream;
    core::StateChangeCallback onStateChange;
    core::ErrorCallback onError;
    NVENCTuning tuning = NVENCTuning::LowLatency;
    int bFrames = 0;
    int lookahead = 0;
    std::mutex mutex;
};

NVENCEncoder::NVENCEncoder(NVENCCodec codec, int) : m_impl(std::make_unique<Impl>()) { m_impl->codec = codec; }
NVENCEncoder::~NVENCEncoder() = default;
std::string NVENCEncoder::GetName() const { return "NVENCEncoder(STUB)"; }
core::PipelineState NVENCEncoder::GetState() const { return m_impl->state; }
core::VoidResult NVENCEncoder::Configure(const EncoderConfig& c) { m_impl->config = c; return {}; }
void NVENCEncoder::SetTuning(NVENCTuning t) { m_impl->tuning = t; }
void NVENCEncoder::SetBFrames(int c) { m_impl->bFrames = c; }
void NVENCEncoder::SetLookahead(int f) { m_impl->lookahead = f; }

core::VoidResult NVENCEncoder::Initialize() {
    return std::unexpected(core::Error::Make(core::ErrorCode::NotSupported, "NVENC not available (HAS_NVCODEC not defined)"));
}
core::VoidResult NVENCEncoder::Start() { return std::unexpected(core::Error::Make(core::ErrorCode::NotSupported, "No NVENC")); }
core::VoidResult NVENCEncoder::Stop()  { return {}; }
core::VoidResult NVENCEncoder::PushFrame(std::shared_ptr<core::MediaFrame>) { return std::unexpected(core::Error::Make(core::ErrorCode::NotSupported, "No NVENC")); }
core::Result<std::shared_ptr<core::MediaFrame>> NVENCEncoder::PullFrame() { return std::unexpected(core::Error::Make(core::ErrorCode::NotSupported, "No NVENC")); }
core::VoidResult NVENCEncoder::Connect(std::shared_ptr<core::IMediaObject> d) { m_impl->downstream = d; return {}; }
core::VoidResult NVENCEncoder::Disconnect() { m_impl->downstream.reset(); return {}; }
void NVENCEncoder::OnStateChange(core::StateChangeCallback cb) { m_impl->onStateChange = cb; }
void NVENCEncoder::OnError(core::ErrorCallback cb) { m_impl->onError = cb; }
void NVENCEncoder::Flush() {}
bool NVENCEncoder::IsAvailable(NVENCCodec, int) { return false; }

} // namespace openmedia::codecs

#endif // HAS_NVCODEC
