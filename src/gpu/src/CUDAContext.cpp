/// @file CUDAContext.cpp
/// @brief CUDA Driver API context implementation

#include "openmedia/gpu/CUDAContext.h"
#include <openmedia/core/Logger.h>

#ifdef OME_HAS_CUDA
#include <cuda.h>
#endif

namespace openmedia::gpu {

#ifdef OME_HAS_CUDA

namespace {

/// Map CUresult to a human-readable error string
std::string CudaErrorString(CUresult result) {
    const char* errName = nullptr;
    const char* errStr = nullptr;
    cuGetErrorName(result, &errName);
    cuGetErrorString(result, &errStr);
    return std::string(errName ? errName : "UNKNOWN") + ": " + (errStr ? errStr : "no description");
}

/// Compute byte size for an NV12 / BGRA allocation
size_t ComputeBufferSize(int width, int height, core::PixelFormat format) {
    switch (format) {
        case core::PixelFormat::NV12:       return static_cast<size_t>(width) * height * 3 / 2;
        case core::PixelFormat::YUV420P:    return static_cast<size_t>(width) * height * 3 / 2;
        case core::PixelFormat::BGRA:
        case core::PixelFormat::RGBA:       return static_cast<size_t>(width) * height * 4;
        case core::PixelFormat::P010LE:     return static_cast<size_t>(width) * height * 3; // 10-bit NV12
        default:                            return static_cast<size_t>(width) * height * 4;
    }
}

} // anonymous namespace

CUDAContext::CUDAContext() = default;

CUDAContext::~CUDAContext() {
    Shutdown();
}

core::VoidResult CUDAContext::Initialize() {
    if (m_initialized) return {};

    auto& log = core::Logger::Get("gpu.cuda");

    CUresult res = cuInit(0);
    if (res != CUDA_SUCCESS) {
        log.Error("cuInit failed: {}", CudaErrorString(res));
        return std::unexpected(core::Error::Make(core::ErrorCode::GPUNotAvailable, "cuInit failed: " + CudaErrorString(res)));
    }

    int deviceCount = 0;
    res = cuDeviceGetCount(&deviceCount);
    if (res != CUDA_SUCCESS || deviceCount == 0) {
        log.Error("No CUDA devices found");
        return std::unexpected(core::Error::Make(core::ErrorCode::GPUNotAvailable, "No CUDA devices found"));
    }

    res = cuDeviceGet(&m_cuDevice, m_deviceOrdinal);
    if (res != CUDA_SUCCESS) {
        log.Error("cuDeviceGet({}) failed: {}", m_deviceOrdinal, CudaErrorString(res));
        return std::unexpected(core::Error::Make(core::ErrorCode::GPUContextFailed, "cuDeviceGet failed"));
    }

    char name[256] = {};
    cuDeviceGetName(name, sizeof(name), m_cuDevice);
    m_deviceName = name;

#if CUDA_VERSION >= 13000
    res = cuCtxCreate(&m_cuContext, nullptr, 0, m_cuDevice);
#else
    res = cuCtxCreate(&m_cuContext, 0, m_cuDevice);
#endif
    if (res != CUDA_SUCCESS) {
        log.Error("cuCtxCreate failed: {}", CudaErrorString(res));
        return std::unexpected(core::Error::Make(core::ErrorCode::GPUContextFailed, "cuCtxCreate failed: " + CudaErrorString(res)));
    }

    m_initialized = true;
    log.Info("CUDA context initialized: device={} ({})", m_deviceOrdinal, m_deviceName);
    return {};
}

void CUDAContext::Shutdown() {
    if (!m_initialized) return;

    if (m_cuContext) {
        cuCtxDestroy(m_cuContext);
        m_cuContext = nullptr;
    }
    m_initialized = false;
}

void* CUDAContext::GetDeviceHandle() {
    return reinterpret_cast<void*>(m_cuContext);
}

std::string CUDAContext::GetDeviceName() const {
    return m_deviceName;
}

core::VoidResult CUDAContext::UploadTexture(const uint8_t* data, int width, int height, core::PixelFormat format, void** outTextureHandle) {
    if (!m_initialized || !data || !outTextureHandle) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidArgument, "Invalid args or context not initialized"));
    }

    size_t bufferSize = ComputeBufferSize(width, height, format);

    CUdeviceptr devPtr = 0;
    CUresult res = cuMemAlloc(&devPtr, bufferSize);
    if (res != CUDA_SUCCESS) {
        return std::unexpected(core::Error::Make(core::ErrorCode::GPUMemoryFailed, "cuMemAlloc failed: " + CudaErrorString(res)));
    }

    res = cuMemcpyHtoD(devPtr, data, bufferSize);
    if (res != CUDA_SUCCESS) {
        cuMemFree(devPtr);
        return std::unexpected(core::Error::Make(core::ErrorCode::GPUTransferFailed, "cuMemcpyHtoD failed: " + CudaErrorString(res)));
    }

    *outTextureHandle = reinterpret_cast<void*>(devPtr);
    return {};
}

core::VoidResult CUDAContext::DownloadTexture(void* textureHandle, uint8_t* outData, int width, int height, core::PixelFormat format) {
    if (!m_initialized || !textureHandle || !outData) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidArgument, "Invalid args or context not initialized"));
    }

    size_t bufferSize = ComputeBufferSize(width, height, format);
    CUdeviceptr devPtr = reinterpret_cast<CUdeviceptr>(textureHandle);

    CUresult res = cuMemcpyDtoH(outData, devPtr, bufferSize);
    if (res != CUDA_SUCCESS) {
        return std::unexpected(core::Error::Make(core::ErrorCode::GPUTransferFailed, "cuMemcpyDtoH failed: " + CudaErrorString(res)));
    }

    return {};
}

core::VoidResult CUDAContext::CopyTexture(void* srcHandle, void* dstHandle) {
    if (!m_initialized || !srcHandle || !dstHandle) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidArgument, "Invalid args"));
    }

    // NOTE: Caller must ensure both allocations have the same size.
    // For a production system, we'd track allocation sizes in a registry.
    return std::unexpected(core::Error::Make(core::ErrorCode::NotImplemented,
        "CopyTexture requires size tracking — use UploadTexture/DownloadTexture for now"));
}

void CUDAContext::FreeTexture(void* textureHandle) {
    if (!textureHandle) return;
    CUdeviceptr devPtr = reinterpret_cast<CUdeviceptr>(textureHandle);
    cuMemFree(devPtr);
}

#else // !OME_HAS_CUDA — Fallback stubs

CUDAContext::CUDAContext() = default;
CUDAContext::~CUDAContext() { Shutdown(); }

core::VoidResult CUDAContext::Initialize() {
    return std::unexpected(core::Error::Make(core::ErrorCode::NotSupported, "CUDA support not compiled (OME_HAS_CUDA not defined)"));
}

void CUDAContext::Shutdown() { m_initialized = false; }
void* CUDAContext::GetDeviceHandle() { return nullptr; }
std::string CUDAContext::GetDeviceName() const { return "N/A (no CUDA)"; }

core::VoidResult CUDAContext::UploadTexture(const uint8_t*, int, int, core::PixelFormat, void**) {
    return std::unexpected(core::Error::Make(core::ErrorCode::NotSupported, "No CUDA"));
}
core::VoidResult CUDAContext::DownloadTexture(void*, uint8_t*, int, int, core::PixelFormat) {
    return std::unexpected(core::Error::Make(core::ErrorCode::NotSupported, "No CUDA"));
}
core::VoidResult CUDAContext::CopyTexture(void*, void*) {
    return std::unexpected(core::Error::Make(core::ErrorCode::NotSupported, "No CUDA"));
}
void CUDAContext::FreeTexture(void*) {}

#endif // OME_HAS_CUDA

} // namespace openmedia::gpu
